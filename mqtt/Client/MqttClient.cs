using Mqtt.Client.Network;
using Mqtt.Client.Packets;
using Mqtt.Client.Queue;
using Mqtt.Client.ReasonCode;
using Mqtt.Packets;
using Mqtt.Client.Validation;
using System.Diagnostics;
using System.Net.Sockets;

namespace Mqtt.Client
{
    public class MqttClient(MqttOption mqttOption, Action<string>? debug = null) : IMqttClient
    {
        public delegate void ConnectionEstablishedDelegate(bool sessionPresent, ConnectReturnCode returnCode);
        public event ConnectionEstablishedDelegate? OnConnectionEstablished;

        public delegate void ReconnectingDelegate();
        public event ReconnectingDelegate? OnReconnection;

        public delegate void ConnectionLostDelegate();
        public event ConnectionLostDelegate? OnConnectionLost;

        public delegate void ConnectionFailedDelegate(string reason);
        public event ConnectionFailedDelegate? OnConnectionFailed;

        public delegate void DisconnectedDelegate(DisconnectReasionCode reason = DisconnectReasionCode.NORMAL_DISCONNECTION);
        public event DisconnectedDelegate? OnDisconnected;

        public delegate void MessageReceivedDelegate(string topic, string message, QualityOfService qos, bool retain);
        public event MessageReceivedDelegate? OnMessageReceived;

        public delegate void SubscribedDelegate(string topic, QualityOfService qos);
        public event SubscribedDelegate? OnSubscribed;

        public delegate void UnsubscribedDelegate(string topic);
        public event UnsubscribedDelegate? OnUnsubscribed;

        public delegate void ErrorDelegate(string at, string message);
        public event ErrorDelegate? OnError;

        private delegate void PacketRelease(ushort id);
        private event PacketRelease? OnPacketRelease;

        private delegate void ConnectResultDelegate(bool success);
        private event ConnectResultDelegate? OnConnectResult;

        private readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

        private TcpClient? tcpClient;
        private NetworkStream? stream;
        private bool isConnecting = false;
        private CancellationTokenSource reconnectCts = new();

        private readonly MqttOption mqttOption = mqttOption;

        private readonly MqttMonitor mqttMonitor = new(debug);
        private readonly PendingPacketQueue pendingPacketQueue = new(debug);
        private OutgoingHandler? outgoingHandler;
        private IncomingHandler? incomingHandler;

        private string host = "";
        private int port = 1883;
        private string clientID = "";
        private string username = "";
        private string password = "";

        // Connect to the MQTT broker
        public async Task Connect(string host, int port, string clientID, string username = "", string password = "")
        {
            if (CheckConnect(host, port, clientID, username, password))
            {
                return;
            }

            this.host = host;
            this.port = port;
            this.clientID = clientID;
            this.username = username;
            this.password = password;

            var tcs = new TaskCompletionSource();
            void ConnectResult(bool success)
            {
                pendingPacketQueue.Start(mqttMonitor, outgoingHandler!);
                tcs.SetResult();
            }
            OnConnectResult += ConnectResult;

            await EstablishConnection(host, port, clientID, username, password);

            await tcs.Task;

            OnConnectResult -= ConnectResult;
        }

        // Establish the connection with the MQTT broker
        private async Task EstablishConnection(string host, int port, string clientID, string username, string password)
        {
            try
            {
                isConnecting = true;

                debug?.Invoke("Connecting...");

                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port);
                // Grund verbindung war erfolgreich

                stream = tcpClient.GetStream();
                // AUfbau des Streams über die verbindung

                outgoingHandler = new OutgoingHandler(mqttOption, stream, debug);
                // Initieren der ausgehenden handlers

                incomingHandler = new IncomingHandler(debug);
                incomingHandler.OnConnAck += HandleConnAck;
                incomingHandler.OnPublish += HandlePublish;
                incomingHandler.OnPubAck += HandlePubAck;
                incomingHandler.OnPubRec += HandlePubRec;
                incomingHandler.OnPubRel += HandlePubRel;
                incomingHandler.OnPubComp += HandlePubComp;
                incomingHandler.OnSubAck += HandleSubAck;
                incomingHandler.OnUnSubAck += HandleUnsubAck;
                incomingHandler.OnDisconnect += HandleDisconnect;
                incomingHandler.Start(stream);
                // Initieren des eingehenden handlers 

                mqttMonitor.Start(tcpClient, mqttOption.KeepAlive, outgoingHandler);
                mqttMonitor.OnDisconnect += async () => await Terminate(false);
                mqttMonitor.OnConnectionLost += ConnectionLost;
                // Startet die Netzwerk kontroller um zu überprüfen, ob die verbindung besteht oder nicht

                debug?.Invoke("The initialization of the MQTT client was successful.");

                await outgoingHandler.SendConnect(clientID, username, password);
            }
            catch (SocketException)
            {

                OnConnectionFailed?.Invoke("A connection could not be established because the target computer refused the connection.");
                await Terminate(true);
                OnConnectResult?.Invoke(false);
            }
            catch (Exception ex)
            {
                OnConnectionFailed?.Invoke(ex.Message);
                OnConnectResult?.Invoke(false);
            }
            finally
            {
                isConnecting = false;
            }
        }

        // Publish a message to a topic with the specified quality of service
        public void Publish(string topic, string message, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            Publish(new Topic(topic, qos), message);
        }

        // Asynchronously publish a message to a topic with the specified quality of service
        public async Task PublishAsync(string topic, string message, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            await PublishAsync(new Topic(topic, qos), message);
        }

        // Publish a message to a topic with the specified quality of service
        public void Publish(Topic topic, string message)
        {
            if (CheckPublish(topic, message))
            {
                return;
            }
            PublishPacket publishPacket = new(null, topic.Name, message, topic.QoS, mqttOption.WillRetain, false);
            pendingPacketQueue.Enqueue(
                new PendingPacket(
                    publishPacket.PacketID,
                    PacketType.PUBLISH,
                    publishPacket,
                    PendingPacketType.CLIENT
                ));
        }

        // Asynchronously publish a message to a topic with the specified quality of service
        public async Task PublishAsync(Topic topic, string message)
        {
            if (CheckPublish(topic, message))
            {
                return;
            }
            PublishPacket publishPacket = new(null, topic.Name, message, topic.QoS, mqttOption.WillRetain, false);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(ushort id)
            {
                if (id == publishPacket.PacketID)
                {
                    OnPacketRelease -= OnPacketAcknowledged;
                    tcs.SetResult(true);
                }
            }
            if (topic.QoS != QualityOfService.AT_MOST_ONCE)
                OnPacketRelease += OnPacketAcknowledged;
            else
                tcs.SetResult(true);

            pendingPacketQueue.Enqueue(
                new PendingPacket(
                    publishPacket.PacketID,
                    PacketType.PUBLISH,
                    publishPacket,
                    PendingPacketType.CLIENT
                ));

            await tcs.Task;
        }

        // Subscribe to a topic with the specified quality of service
        public void Subscribe(string topic, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            Subscribe([new Topic(topic, qos)]);
        }

        // Asynchronously subscribe to a topic with the specified quality of service
        public async Task SubscribeAsync(string topic, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            await SubscribeAsync([new Topic(topic, qos)]);
        }

        // Subscribe to a topic
        public void Subscribe(Topic topic)
        {
            Subscribe([topic]);
        }

        // Asynchronously subscribe to a topic
        public async Task SubscribeAsync(Topic topic)
        {
            await SubscribeAsync([topic]);
        }

        // Subscribe to multiple topics
        public void Subscribe(Topic[] topics)
        {
            if (CheckSubscribe(topics))
            {
                return;
            }
            SubscribePacket subscribePacket = new(null, topics);
            pendingPacketQueue.Enqueue(
                new PendingPacket(
                    subscribePacket.PacketId,
                    PacketType.SUBSCRIBE,
                    subscribePacket,
                    PendingPacketType.CLIENT
                ));
        }

        // Asynchronously subscribe to multiple topics
        public async Task SubscribeAsync(Topic[] topics)
        {
            if (CheckSubscribe(topics))
            {
                return;
            }
            SubscribePacket subscribePacket = new(null, topics);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(ushort id)
            {
                if (id == subscribePacket.PacketId)
                {
                    OnPacketRelease -= OnPacketAcknowledged;
                    tcs.SetResult(true);
                }
            }
            OnPacketRelease += OnPacketAcknowledged;

            pendingPacketQueue.Enqueue(
                new PendingPacket(
                    subscribePacket.PacketId,
                    PacketType.SUBSCRIBE,
                    subscribePacket,
                    PendingPacketType.CLIENT
                ));

            await tcs.Task;
        }

        // Unsubscribe from a topic
        public void Unsubscribe(string topic)
        {
            Unsubscribe([new Topic(topic)]);
        }

        // Asynchronously unsubscribe from a topic
        public async Task UnsubscribeAsync(string topic)
        {
            await UnsubscribeAsync([new Topic(topic)]);
        }

        // Unsubscribe from a topic
        public void Unsubscribe(Topic topic)
        {
            Unsubscribe([topic]);
        }

        // Asynchronously unsubscribe from a topic
        public async Task UnsubscribeAsync(Topic topic)
        {
            await UnsubscribeAsync([topic]);
        }

        // Unsubscribe from multiple topics
        public void Unsubscribe(Topic[] topics)
        {
            if (CheckSubscribe(topics))
            {
                return;
            }

            UnsubscribePacket unsubscribePacket = new(null, topics);
            pendingPacketQueue.Enqueue(
                new PendingPacket(
                    unsubscribePacket.PacketId,
                    PacketType.UNSUBSCRIBE,
                    unsubscribePacket,
                    PendingPacketType.CLIENT
                ));
        }

        // Asynchronously unsubscribe from multiple topics
        public async Task UnsubscribeAsync(Topic[] topics)
        {
            if (CheckSubscribe(topics))
            {
                return;
            }

            UnsubscribePacket unsubscribePacket = new(null, topics);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(ushort id)
            {
                if (id == unsubscribePacket.PacketId)
                {
                    OnPacketRelease -= OnPacketAcknowledged;
                    tcs.SetResult(true);
                }
            }
            OnPacketRelease += OnPacketAcknowledged;

            pendingPacketQueue.Enqueue(
                new PendingPacket(
                    unsubscribePacket.PacketId, 
                    PacketType.UNSUBSCRIBE, 
                    unsubscribePacket,
                    PendingPacketType.CLIENT
                ));
            await tcs.Task;
        }

        // Disconnect from the MQTT broker
        public async Task Disconnect()
        {
            reconnectCts.Cancel();
            await Terminate(false);
        }

        // Terminate the connection
        private async Task Terminate(bool isConnectionLost)
        {
            // Check if connection is already closed
            if (mqttMonitor.IsConnectionClosed)
                return;

            // Check if connection is not lost
            if (!isConnectionLost)
                OnDisconnected?.Invoke();

            // Dispose mqtt monitor and pending packet queue
            mqttMonitor.Dispose(!isConnectionLost);
            pendingPacketQueue.Dispose(isConnectionLost);

            // Check if connection is lost
            if (isConnectionLost)
                OnPacketRelease = null;

            // Close the stream
            if (stream != null)
            {
                // Send DISCONNECT packet to broker
                await outgoingHandler!.SendDisconnect();

                debug?.Invoke("Stream has been closed!");
                stream.Close();
                stream.Dispose();
            }

            // Dispose outgooing and incoming handler
            outgoingHandler?.Dispose();
            incomingHandler?.Dispose();

            // Close and dispose tcp connection
            debug?.Invoke("TCP connection has been closed!");
            tcpClient?.Close();
            tcpClient?.Dispose();

            debug?.Invoke("Terminated! " +
                "{" + "\n" +
                "    IsClientConnected: " + mqttMonitor.IsClientConnected + "\n" +
                "    IsConnectionEstablished: " + mqttMonitor.IsConnectionEstablished + "\n" +
                "    IsConnectionClosed: " + mqttMonitor.IsConnectionClosed + "\n" +
                "}");
        }

        // Handle the event when the connection is lost
        private async Task ConnectionLost()
        {
            debug?.Invoke("Connection lost...");
            await Terminate(true);
            OnConnectionLost?.Invoke();
            await Reconnect();
        }

        // Reconnect to the MQTT broker
        private async Task Reconnect()
        {
            if (isConnecting) return;

            int attempts = 0;
            int maxAttempts = 10;

            do
            {
                debug?.Invoke("Reconnecting...");

                attempts++;

                var tcs = new TaskCompletionSource<bool>();
                void ConnectResult(bool success)
                {
                    pendingPacketQueue.Start(mqttMonitor, outgoingHandler!);
                    tcs.SetResult(success);
                }
                OnConnectResult += ConnectResult;

                await EstablishConnection(host, port, clientID, username, password);

                await tcs.Task;

                OnConnectResult -= ConnectResult;
                
                if (tcs.Task.Result)
                {
                    debug?.Invoke("Reconnected!");
                    break;
                }
                else if (attempts >= maxAttempts)
                {
                    debug?.Invoke("Reconnect failed! (Reached max iterations)");
                    await Terminate(false);
                }
                else
                {
                    debug?.Invoke("Reconnect failed!");
                }

            } while (attempts < maxAttempts);
        }

        // Handle the CONNACK packet received from the broker
        private void HandleConnAck(ConnAckPacket connAckPacket)
        {
            // Invoke the ConnectionSuccess event
            OnConnectResult?.Invoke(true);
            OnConnectionEstablished?.Invoke(connAckPacket.SessionPresent, connAckPacket.ReturnCode);
            mqttMonitor.IsClientConnected = true;
        }

        // Handle the PUBLISH packet received from the broker
        private async Task HandlePublish(PublishPacket pubPacket)
        {
            switch (pubPacket.QoS)
            {
                case QualityOfService.AT_MOST_ONCE: // QoS 0 - "At most once"
                    OnMessageReceived?.Invoke(pubPacket.Topic, pubPacket.Message, pubPacket.QoS, pubPacket.Retain);
                    break;
                case QualityOfService.AT_LEAST_ONCE: // QoS 1 - "At least once"
                    OnMessageReceived?.Invoke(pubPacket.Topic, pubPacket.Message, pubPacket.QoS, pubPacket.Retain);
                    await outgoingHandler!.SendPubAck(pubPacket.PacketID);
                    break;
                case QualityOfService.EXACTLY_ONCE: // QoS 2 - "Exactly once"
                    pendingPacketQueue.Enqueue(
                        new PendingPacket(
                            pubPacket.PacketID,
                            PacketType.PUBREC,
                            pubPacket,
                            PendingPacketType.SERVER
                        )
                    );
                    break;
            }
        }


        // Handle the PUBACK packet received from the broker
        private void HandlePubAck(PubAckPacket pubAckPacket)
        {
            pendingPacketQueue.Dequeue(PendingPacketType.CLIENT, pubAckPacket.PacketID);
            OnPacketRelease?.Invoke(pubAckPacket.PacketID);
        }

        // Handle the PUBREC packet received from the broker
        private void HandlePubRec(PubRecPacket pubRecPacket)
        {
            pendingPacketQueue.UpdatePacketTypeStatus(PendingPacketType.CLIENT, pubRecPacket.PacketID, PacketType.PUBREL);
        }

        // Handle the PUBREL packet received from the broker
        private void HandlePubRel(PubRelPacket pubRelPacket)
        {
            PublishPacket publishPacket = (PublishPacket)pendingPacketQueue.UpdatePacketTypeStatus(PendingPacketType.SERVER, pubRelPacket.PacketID, PacketType.PUBCOMP)!;
            OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message, publishPacket.QoS, publishPacket.Retain);
        }

        // Handle the PUBCOMP packet received from the broker
        private void HandlePubComp(PubCompPacket pubCompPacket)
        {
            pendingPacketQueue.Dequeue(PendingPacketType.CLIENT, pubCompPacket.PacketID);
            OnPacketRelease?.Invoke(pubCompPacket.PacketID);
        }

        // Handle the SUBACK packet received from the broker
        private void HandleSubAck(SubAckPacket subAckPacket)
        {
            SubscribePacket subscribePacket = (SubscribePacket)pendingPacketQueue.Dequeue(PendingPacketType.CLIENT, subAckPacket.PacketID)!;
            foreach (Topic topic in subscribePacket.Topics)
            {
                OnSubscribed?.Invoke(topic.Name, topic.QoS);
            }
            OnPacketRelease?.Invoke(subAckPacket.PacketID);
        }

        // Check for any error during the execution of a function
        private void HandleUnsubAck(UnSubAckPacket unsubAckPacket)
        {
            UnsubscribePacket unsubscribePacket = (UnsubscribePacket)pendingPacketQueue.Dequeue(PendingPacketType.CLIENT, unsubAckPacket.PacketID)!;
            foreach (Topic topic in unsubscribePacket.Topics)
            {
                OnUnsubscribed?.Invoke(topic.Name);
            }
            OnPacketRelease?.Invoke(unsubAckPacket.PacketID);
        }

        // Check for any error during the execution of a function
        private void HandleDisconnect(DisconnectPacket disconnectPacket)
        {
            OnDisconnected?.Invoke(disconnectPacket.ReasonCode ?? DisconnectReasionCode.NORMAL_DISCONNECTION);
        }

        // Check for any error during the execution of a function
        private bool CheckConnect(string host, int port, string clientID, string username, string password)
        {
            (string, string)? error;
            if ((error = MQTTValidator.CheckHost(host)) != null ||
                (error = MQTTValidator.CheckPort(port)) != null ||
                (error = MQTTValidator.CheckClientID(clientID)) != null ||
                (error = MQTTValidator.CheckUsername(username)) != null ||
                (error = MQTTValidator.CheckPassword(password)) != null ||
                (error = MQTTValidator.CheckIsConnected(isConnecting, mqttMonitor)) != null)
            {
                OnError?.Invoke(error.Value.Item1, error.Value.Item2);
                return true;
            }
            return false;
        }

        // Check for any error during the execution of a function
        private bool CheckPublish(Topic topic, string message)
        {
            (string, string)? error;
            if ((error = MQTTValidator.CheckTopic(true, topic.Name)) != null ||
                (error = MQTTValidator.CheckMessage(message)) != null ||
                (error = MQTTValidator.CheckConnectionClosed(mqttMonitor)) != null ||
                (error = MQTTValidator.CheckConnectionEstablished(mqttMonitor)) != null ||
                (error = MQTTValidator.CheckClientConnected(mqttMonitor)) != null)
            {
                OnError?.Invoke(error.Value.Item1, error.Value.Item2);
                return true;
            }
            return false;
        }

        // Check for any error during the execution of a function
        private bool CheckSubscribe(Topic[] topics)
        {
            (string, string)? error;
            foreach (var topic in topics)
            {
                error = MQTTValidator.CheckTopic(false, topic.Name);
                if (error != null)
                {
                    OnError?.Invoke(error.Value.Item1, error.Value.Item2);
                    return true;
                }
            }
            if ((error = MQTTValidator.CheckConnectionClosed(mqttMonitor)) != null ||
                (error = MQTTValidator.CheckConnectionEstablished(mqttMonitor)) != null ||
                (error = MQTTValidator.CheckClientConnected(mqttMonitor)) != null)
            {
                OnError?.Invoke(error.Value.Item1, error.Value.Item2);
                return true;
            }
            return false;
        }
    }
}
