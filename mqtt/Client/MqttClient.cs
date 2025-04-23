using Mqtt.Client;
using Mqtt.Client.Network;
using Mqtt.Client.Packets;
using Mqtt.Client.Queue;
using Mqtt.Client.ReasonCode;
using Mqtt.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client
{
    public class MqttClient(MqttOption mqttOption) : IMqttClient
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

        private TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

        private TcpClient? tcpClient;
        private NetworkStream? stream;
        private bool isConnecting = false;

        private readonly MqttOption mqttOption = mqttOption;

        private readonly MqttMonitor mqttMonitor = new();
        private readonly PacketQueueHandler incomingPacketQueueHandler = new("incoming");
        private readonly PacketQueueHandler outgoingPacketQueueHandler = new("outgoing");
        private OutgoingHandler? outgoingHandler;
        private IncomingHandler? incomingHandler;

        private string host = "";
        private int port = 1883;
        private string clientID = "";
        private string username = "";
        private string password = "";

        public async Task Connect(string host, int port, string clientID, string username = "", string password = "")
        {
            if (isConnecting || mqttMonitor.IsClientConnected || mqttMonitor.IsConnectionEstablished)
            {
                OnError?.Invoke("Connect", "Client is already connected!");
                return;
            }
            else
            {
                isConnecting = true;
                if (host.Equals(""))
                {
                    OnError?.Invoke("Connect", "Broker address is empty!");
                    return;
                }
                else if (port < 0 || port > 65535)
                {
                    OnError?.Invoke("Connect", "Port is invalid! (" + port + ")");
                    return;
                }
                if (clientID.Equals(""))
                {
                    OnError?.Invoke("Connect", "Client ID is empty!");
                    return;
                }
                else if (clientID.Length > 23)
                {
                    OnError?.Invoke("Connect", "Client ID is too long! (max. 23)");
                    return;
                }

                this.host = host;
                this.port = port;
                this.clientID = clientID;
                this.username = username;
                this.password = password;

                try
                {
                    await EstablishConnection(host, port, clientID, username, password);
                }
                catch (Exception ex)
                {
                    OnConnectionFailed?.Invoke(ex.Message);
                    Terminate(true);
                }
                finally
                {
                    isConnecting = false;
                }
            }
        }

        private async Task EstablishConnection(string host, int port, string clientID, string username, string password)
        {
            tcpClient = new TcpClient(host, port);
            // Grund verbindung war erfolgreich

            stream = tcpClient.GetStream();
            // AUfbau des Streams über die verbindung

            outgoingHandler = new OutgoingHandler(mqttOption, stream);
            // Initieren der ausgehenden handlers

            incomingHandler = new IncomingHandler();
            incomingHandler.OnConnAck += HandleConnAck;
            incomingHandler.OnPublish += HandlePublish;
            incomingHandler.OnPubAck += HandlePubAck;
            incomingHandler.OnPubRec += HandlePubRec;
            incomingHandler.OnPubRel += HandlePubRel;
            incomingHandler.OnPubComp += HandlePubComp;
            incomingHandler.OnSubAck += HandleSubAck;
            incomingHandler.OnUnSubAck += HandleUnsubAck;
            incomingHandler.OnDisconnect += HandleDisconnect;
            incomingHandler.Start(stream!, mqttMonitor, mqttOption);
            // Initieren des eingehenden handlers 

            mqttMonitor.Start(tcpClient, mqttOption.KeepAlive, outgoingHandler.SendPingReq);
            mqttMonitor.OnDisconnect += () => Terminate(false);
            mqttMonitor.OnConnectionLost += ConnectionLost;
            // Startet die Netzwerk kontroller um zu überprüfen, ob die verbindung besteht oder nicht

            outgoingHandler.SendConnect(clientID, username, password);

            DateTime currentTime = DateTime.UtcNow;
            while (!mqttMonitor.IsClientConnected && (DateTime.UtcNow - currentTime) < ConnectionTimeout)
            {
                await Task.Delay(50);
            }
            if (!mqttMonitor.IsConnectionClosed && mqttMonitor.IsConnectionEstablished && mqttMonitor.IsClientConnected)
            {
                Debug.WriteLine("Connected");
                incomingPacketQueueHandler.Start(mqttMonitor, outgoingHandler);
                outgoingPacketQueueHandler.Start(mqttMonitor, outgoingHandler);
            }
            else
            {
                OnConnectionFailed?.Invoke("Connection was not successful.");
            }
        }

        public void Publish(string topic, string message, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            Publish(new Topic(topic, qos), message);
        }

        public async Task PublishAsync(string topic, string message, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            await PublishAsync(new Topic(topic, qos), message);
        }

        public void Publish(Topic topic, string message)
        {
            if (CheckForError("Publish"))
            {
                return;
            }
            PublishPacket publishPacket = new(null, topic.Name, message, topic.QoS, mqttOption.WillRetain, false);
            outgoingPacketQueueHandler.Enqueue(
                new PendingPacket(
                    publishPacket.PacketID,
                    PacketType.PUBLISH,
                    publishPacket
                ));
        }

        public async Task PublishAsync(Topic topic, string message)
        {
            if (CheckForError("Publish"))
            {
                return;
            }
            PublishPacket publishPacket = new(null, topic.Name, message, topic.QoS, mqttOption.WillRetain, false);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id)
            {
                if (id == publishPacket.PacketID)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    tcs.SetResult(true);
                }
            }

            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Enqueue(
                new PendingPacket(
                    publishPacket.PacketID,
                    PacketType.PUBLISH,
                    publishPacket
                ));

            await tcs.Task;
        }

        public void Subscribe(string topic, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            Subscribe([new Topic(topic, qos)]);
        }

        public async Task SubscribeAsync(string topic, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            await SubscribeAsync([new Topic(topic, qos)]);
        }

        public void Subscribe(Topic topic)
        {
            Subscribe([topic]);
        }

        public async Task SubscribeAsync(Topic topic)
        {
            await SubscribeAsync([topic]);
        }

        public void Subscribe(Topic[] topics)
        {
            if (CheckForError("Subscribe"))
            {
                return;
            }
            SubscribePacket subscribePacket = new(null, topics);
            outgoingPacketQueueHandler.Enqueue(
                new PendingPacket(
                    subscribePacket.PacketId,
                    PacketType.SUBSCRIBE,
                    subscribePacket
                ));
        }

        public async Task SubscribeAsync(Topic[] topics)
        {
            if (CheckForError("Subscribe"))
            {
                return;
            }
            SubscribePacket subscribePacket = new(null, topics);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id)
            {
                if (id == subscribePacket.PacketId)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    tcs.SetResult(true);
                }
            }

            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Enqueue(
                new PendingPacket(
                    subscribePacket.PacketId,
                    PacketType.SUBSCRIBE,
                    subscribePacket
                ));

            await tcs.Task;
        }

        public void Unsubscribe(string topic)
        {
            Unsubscribe([new Topic(topic)]);
        }

        public async Task UnsubscribeAsync(string topic)
        {
            await UnsubscribeAsync([new Topic(topic)]);
        }

        public void Unsubscribe(Topic topic)
        {
            Unsubscribe([topic]);
        }

        public async Task UnsubscribeAsync(Topic topic)
        {
            await UnsubscribeAsync([topic]);
        }

        public void Unsubscribe(Topic[] topics)
        {
            if (CheckForError("Unsubscribe"))
            {
                return;
            }
            UnsubscribePacket unsubscribePacket = new(null, topics);
            outgoingPacketQueueHandler.Enqueue(
                new PendingPacket(
                    unsubscribePacket.PacketId,
                    PacketType.UNSUBSCRIBE,
                    unsubscribePacket
                ));
        }

        public async Task UnsubscribeAsync(Topic[] topics)
        {
            if (CheckForError("Unsubscribe"))
            {
                return;
            }
            UnsubscribePacket unsubscribePacket = new(null, topics);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id)
            {
                if (id == unsubscribePacket.PacketId)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    tcs.SetResult(true);
                }
            }
            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Enqueue(
                new PendingPacket(
                    unsubscribePacket.PacketId, 
                    PacketType.UNSUBSCRIBE, 
                    unsubscribePacket
                ));
            await tcs.Task;
        }

        public void Disconnect()
        {
            Terminate(false);
        }

        private void Terminate(bool isConnectionLost)
        {
            if (!mqttMonitor.IsClientConnected)
            {
                return;
            }
            else if (!isConnectionLost)
            {
                OnDisconnected?.Invoke();
            }

            mqttMonitor.Dispose();
            incomingPacketQueueHandler.Dispose(isConnectionLost);
            outgoingPacketQueueHandler.Dispose(isConnectionLost);

            // Close the stream
            if (stream != null)
            {
                outgoingHandler!.SendDisconnect();
                stream.Close();
                stream.Dispose();
            }

            outgoingHandler?.Dispose();
            incomingHandler?.Dispose();

            // Disconnect the client
            tcpClient?.Close();
            tcpClient?.Dispose();
        }

        private void ConnectionLost()
        {
            Terminate(true);
            OnConnectionLost?.Invoke();
            Reconnect();
        }

        private async void Reconnect()
        {
            int attempts = 0;
            int maxAttempts = 10;

            while (!mqttMonitor.IsConnectionEstablished && !mqttMonitor.IsClientConnected && attempts < maxAttempts)
            {
                attempts++;

                if (mqttOption.Debug)
                {
                    OnReconnection?.Invoke();
                }
                try
                {
                    await Connect(host, port, clientID, username, password);
                }
                catch { }
                await Task.Delay(ConnectionTimeout);
            }
            if (!mqttMonitor.IsConnectionEstablished && !mqttMonitor.IsClientConnected)
            {
                OnConnectionFailed?.Invoke("Failed to reconnect!");
            }
        }

        private void HandleConnAck(ConnAckPacket connAckPacket)
        {
            // Invoke the ConnectionSuccess event
            OnConnectionEstablished?.Invoke(connAckPacket.SessionPresent, connAckPacket.ReturnCode);
            mqttMonitor.IsClientConnected = true;
        }

        private void HandlePublish(PublishPacket pubPacket)
        {
            switch (pubPacket.QoS)
            {
                case QualityOfService.AT_MOST_ONCE: // QoS 0 - "At most once"
                    OnMessageReceived?.Invoke(pubPacket.Topic, pubPacket.Message, pubPacket.QoS, pubPacket.Retain);
                    break;
                case QualityOfService.AT_LEAST_ONCE: // QoS 1 - "At least once"
                    outgoingHandler?.SendPubAck(pubPacket.PacketID);
                    OnMessageReceived?.Invoke(pubPacket.Topic, pubPacket.Message, pubPacket.QoS, pubPacket.Retain);
                    break;
                case QualityOfService.EXACTLY_ONCE: // QoS 2 - "Exactly once"
                    incomingPacketQueueHandler.Enqueue(
                        new PendingPacket(
                            pubPacket.PacketID,
                            PacketType.PUBREC,
                            pubPacket,
                            false
                        )
                    );
                    break;
            }
        }

        private void HandlePubAck(PubAckPacket pubAckPacket)
        {
            outgoingPacketQueueHandler.Update(pubAckPacket.PacketID, PacketType.PUBACK);
        }

        private void HandlePubRec(PubRecPacket pubRecPacket)
        {
            outgoingPacketQueueHandler.Update(pubRecPacket.PacketID, PacketType.PUBREL);
        }

        private void HandlePubRel(PubRelPacket pubRelPacket)
        {
            PublishPacket publishPacket = (PublishPacket)incomingPacketQueueHandler.Update(pubRelPacket.PacketID, PacketType.PUBCOMP)!;
            OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message, publishPacket.QoS, publishPacket.Retain);
        }

        private void HandlePubComp(PubCompPacket pubCompPacket)
        {
            outgoingPacketQueueHandler.Update(pubCompPacket.PacketID, PacketType.PUBCOMP);
        }

        private void HandleSubAck(SubAckPacket subAckPacket)
        {
            SubscribePacket subscribePacket = (SubscribePacket)outgoingPacketQueueHandler.Update(subAckPacket.PacketID, PacketType.SUBACK)!;
            foreach (Topic topic in subscribePacket.Topics)
            {
                OnSubscribed?.Invoke(topic.Name, topic.QoS);
            }
        }

        private void HandleUnsubAck(UnSubAckPacket unsubAckPacket)
        {
            UnsubscribePacket unsubscribePacket = (UnsubscribePacket)outgoingPacketQueueHandler.Update(unsubAckPacket.PacketID, PacketType.UNSUBACK)!;
            foreach (Topic topic in unsubscribePacket.Topics)
            {
                OnUnsubscribed?.Invoke(topic.Name);
            }
        }

        private void HandleDisconnect(DisconnectPacket disconnectPacket)
        {
            OnDisconnected?.Invoke(disconnectPacket.ReasonCode ?? DisconnectReasionCode.NORMAL_DISCONNECTION);
        }

        private bool CheckForError(string at)
        {
            if (mqttMonitor.IsConnectionClosed)
            {
                OnError?.Invoke(at, "There is no existing connection!");
                return true;
            }
            else if (!mqttMonitor.IsConnectionEstablished)
            {
                OnError?.Invoke(at, "Connection is not established!");
                return true;
            }
            else if (!mqttMonitor.IsClientConnected)
            {
                OnError?.Invoke(at, "Client is not connected!");
                return true;
            }
            return false;
        }
    }
}
