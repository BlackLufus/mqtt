using Mqtt.Client;
using Mqtt.Client.Network;
using Mqtt.Client.Packets;
using Mqtt.Client.ReasonCode;
using Mqtt.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client
{
    public class MqttClient : IMqttClient
    {
        public delegate void ConnectionEstablishedDelegate(bool sessionPresent, ConnectReturnCode returnCode);
        public event ConnectionEstablishedDelegate? OnConnectionEstablished;

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

        private readonly string host;
        private readonly int port;

        private TcpClient? tcpClient;
        private NetworkStream? stream;
        private CancellationTokenSource? cts;

        private readonly MqttOption mqttOption;

        private readonly MqttMonitor mqttMonitor;
        private readonly PacketQueueHandler incomingPacketQueueHandler;
        private readonly PacketQueueHandler outgoingPacketQueueHandler;
        private OutgoingHandler? outgoingHandler;
        private IncomingHandler? incomingHandler;

        private string clientID = "";
        private string username = "";
        private string password = "";

        public MqttClient(string host, int port, MqttOption mqttOption)
        {
            if (host.Equals(""))
            {
                throw new Exception("Broker address is empty!");
            }
            else if (port < 0 || port > 65535)
            {
                throw new Exception("Port is invalid!");
            }

            this.host = host;
            this.port = port;
            this.mqttOption = mqttOption;
            mqttMonitor = new MqttMonitor();
            incomingPacketQueueHandler = new PacketQueueHandler();
            outgoingPacketQueueHandler = new PacketQueueHandler();
        }

        public async Task Connect(string clientID, string username = "", string password = "")
        {
            if (mqttMonitor.IsConnected)
            {
                return;
            }
            else
            {
                mqttMonitor.ResetVariables();

                if (clientID.Equals(""))
                {
                    throw new Exception("Client ID is empty!");
                }
                else if (clientID.Length > 23)
                {
                    throw new Exception("Client ID is too long!");
                }

                this.clientID = clientID;
                this.username = username;
                this.password = password;

                try
                {
                    Init();

                    StartListeners();

                    await HandleConnect();
                }
                catch (Exception ex)
                {
                    OnConnectionFailed?.Invoke(ex.Message);
                }
            }
        }

        public void Publish(string topic, string message, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            PublishPacket publishPacket = new PublishPacket(PublishPacket.NextPacketId, topic, message, qos, mqttOption.WillRetain, false);
            outgoingPacketQueueHandler.Add(new QueuePacket(publishPacket.PacketID, PacketType.PUBLISH, publishPacket));
        }

        public async Task PublishAsync(string topic, string message, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            PublishPacket publishPacket = new PublishPacket(PublishPacket.NextPacketId, topic, message, qos, mqttOption.WillRetain, false);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == publishPacket.PacketID && packetType == PacketType.PUBLISH)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }

            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(publishPacket.PacketID, PacketType.PUBLISH, publishPacket));

            await tcs.Task;
        }

        public void Publish(Topic topic, string message)
        {
            PublishPacket publishPacket = new PublishPacket(PublishPacket.NextPacketId, topic.Name, message, topic.QoS, mqttOption.WillRetain, false);
            outgoingPacketQueueHandler.Add(new QueuePacket(publishPacket.PacketID, PacketType.PUBLISH, publishPacket));
        }

        public async Task PublishAsync(Topic topic, string message)
        {
            PublishPacket publishPacket = new PublishPacket(PublishPacket.NextPacketId, topic.Name, message, topic.QoS, mqttOption.WillRetain, false);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == publishPacket.PacketID && packetType == PacketType.PUBLISH)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }

            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(publishPacket.PacketID, PacketType.PUBLISH, publishPacket));

            await tcs.Task;
        }

        public void Subscribe(string topic, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            SubscribePacket subscribePacket = new SubscribePacket(SubscribePacket.NextPacketId, [new Topic(topic, qos)]);
            outgoingPacketQueueHandler.Add(new QueuePacket(subscribePacket.PacketId, PacketType.SUBSCRIBE, subscribePacket));
        }

        public async Task SubscribeAsync(string topic, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
        {
            SubscribePacket subscribePacket = new SubscribePacket(SubscribePacket.NextPacketId, [new Topic(topic, qos)]);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == subscribePacket.PacketId && packetType == PacketType.SUBSCRIBE)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }

            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(subscribePacket.PacketId, PacketType.SUBSCRIBE, subscribePacket));

            await tcs.Task;
        }

        public void Subscribe(Topic topic)
        {
            SubscribePacket subscribePacket = new SubscribePacket(SubscribePacket.NextPacketId, [topic]);
            outgoingPacketQueueHandler.Add(new QueuePacket(subscribePacket.PacketId, PacketType.SUBSCRIBE, subscribePacket));
        }

        public async Task SubscribeAsync(Topic topic)
        {
            SubscribePacket subscribePacket = new SubscribePacket(SubscribePacket.NextPacketId, [topic]);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == subscribePacket.PacketId && packetType == PacketType.SUBSCRIBE)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }

            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(subscribePacket.PacketId, PacketType.SUBSCRIBE, subscribePacket));

            await tcs.Task;
        }

        public void Subscribe(Topic[] topics)
        {
            SubscribePacket subscribePacket = new SubscribePacket(SubscribePacket.NextPacketId, topics);
            outgoingPacketQueueHandler.Add(new QueuePacket(subscribePacket.PacketId, PacketType.SUBSCRIBE, subscribePacket));
        }

        public async Task SubscribeAsync(Topic[] topics)
        {
            SubscribePacket subscribePacket = new SubscribePacket(SubscribePacket.NextPacketId, topics);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == subscribePacket.PacketId && packetType == PacketType.SUBSCRIBE)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }

            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(subscribePacket.PacketId, PacketType.SUBSCRIBE, subscribePacket));

            await tcs.Task;
        }

        public void Unsubscribe(string topic)
        {
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(UnsubscribePacket.NextPacketId, [new Topic(topic)]);
            outgoingPacketQueueHandler.Add(new QueuePacket(unsubscribePacket.PacketId, PacketType.UNSUBSCRIBE, unsubscribePacket));
        }

        public async Task UnsubscribeAsync(string topic)
        {
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(UnsubscribePacket.NextPacketId, [new Topic(topic)]);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == unsubscribePacket.PacketId && packetType == PacketType.UNSUBSCRIBE)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }
            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(unsubscribePacket.PacketId, PacketType.UNSUBSCRIBE, unsubscribePacket));
            await tcs.Task;
        }

        public void Unsubscribe(Topic topic)
        {
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(UnsubscribePacket.NextPacketId, [topic]);
            outgoingPacketQueueHandler.Add(new QueuePacket(unsubscribePacket.PacketId, PacketType.UNSUBSCRIBE, unsubscribePacket));
        }

        public async Task UnsubscribeAsync(Topic topic)
        {
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(UnsubscribePacket.NextPacketId, [topic]);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == unsubscribePacket.PacketId && packetType == PacketType.UNSUBSCRIBE)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }
            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(unsubscribePacket.PacketId, PacketType.UNSUBSCRIBE, unsubscribePacket));
            await tcs.Task;
        }

        public void Unsubscribe(Topic[] topics)
        {
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(UnsubscribePacket.NextPacketId, topics);
            outgoingPacketQueueHandler.Add(new QueuePacket(unsubscribePacket.PacketId, PacketType.UNSUBSCRIBE, unsubscribePacket));
        }

        public async Task UnsubscribeAsync(Topic[] topics)
        {
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(UnsubscribePacket.NextPacketId, topics);

            var tcs = new TaskCompletionSource<bool>();

            void OnPacketAcknowledged(int id, PacketType packetType)
            {
                if (id == unsubscribePacket.PacketId && packetType == PacketType.UNSUBSCRIBE)
                {
                    outgoingPacketQueueHandler.OnPacketProcessed -= OnPacketAcknowledged;
                    Task.Run(() => tcs.SetResult(true)).ConfigureAwait(false);
                }
            }
            outgoingPacketQueueHandler.OnPacketProcessed += OnPacketAcknowledged;
            outgoingPacketQueueHandler.Add(new QueuePacket(unsubscribePacket.PacketId, PacketType.UNSUBSCRIBE, unsubscribePacket));
            await tcs.Task;
        }

        public void Disconnect(bool triggerEvent = true)
        {
            if (!mqttMonitor.IsConnected && mqttMonitor.IsConnectionClosed)
            {
                return;
            }

            mqttMonitor.IsConnectionClosed = true;
            mqttMonitor.IsConnected = false;

            if (triggerEvent)
            {
                incomingPacketQueueHandler.Clear();
                outgoingPacketQueueHandler.Clear();
                OnDisconnected?.Invoke();
            }

            cts?.Cancel();
            cts?.Dispose();

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
            Debug.WriteLine("Connection lost!");
            Disconnect(false);
            OnConnectionLost?.Invoke();
            Reconnect();
        }

        private async void Reconnect()
        {
            while (!mqttMonitor.IsConnected)
            {
                if (mqttOption.Debug)
                {
                    Console.WriteLine("Reconnecting...");
                }
                await Connect(clientID, username, password);
                await Task.Delay(5000);
            }
        }

        private void Init()
        {
            tcpClient = new TcpClient(host, port);
            stream = tcpClient.GetStream();

            outgoingHandler = new OutgoingHandler(mqttOption, stream);
            incomingHandler = new IncomingHandler();
            incomingHandler.OnConnAck += (connAckPacket) =>
            {
                if (mqttMonitor.IsConnected)
                {
                    OnConnectionLost?.Invoke();
                    OnConnectionEstablished?.Invoke(connAckPacket.SessionPresent, connAckPacket.ReturnCode);
                    return;
                }

                // Invoke the ConnectionSuccess event
                OnConnectionEstablished?.Invoke(connAckPacket.SessionPresent, connAckPacket.ReturnCode);

                // Set the connection state
                mqttMonitor.IsConnected = true;

                OnConnected();
            };
            incomingHandler.OnPublish += (PublishPacket publishPacket) =>
            {
                // Verarbeite je nach QoS-Stufe
                switch (publishPacket.QoS)
                {
                    case QualityOfService.AT_MOST_ONCE: // QoS 0 - "At most once"
                        OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message, publishPacket.QoS, publishPacket.Retain);
                        break;
                    case QualityOfService.AT_LEAST_ONCE: // QoS 1 - "At least once"
                        outgoingHandler.SendPubAck(publishPacket.PacketID);
                        OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message, publishPacket.QoS, publishPacket.Retain);
                        break;
                    case QualityOfService.EXACTLY_ONCE: // QoS 2 - "Exactly once"
                        incomingPacketQueueHandler.Add(new QueuePacket(
                            publishPacket.PacketID,
                            PacketType.PUBREC,
                            publishPacket
                        ));
                        break;
                }
            };
            incomingHandler.OnPubAck += (PubAckPacket pubAckPacket) =>
            {
                outgoingPacketQueueHandler.PacketReceived(pubAckPacket.PacketID, PacketType.PUBLISH, PacketType.PUBACK);
            };

            incomingHandler.OnPubRec += (PubRecPacket pubRecPacket) =>
            {
                outgoingPacketQueueHandler.PacketReceived(pubRecPacket.PacketID, PacketType.PUBLISH, PacketType.PUBREL);
            };

            incomingHandler.OnPubRel += (PubRelPacket pubRelPacket) =>
            {
                PublishPacket publishPacket = (PublishPacket)incomingPacketQueueHandler.Get.Packet!;
                incomingPacketQueueHandler.PacketReceived(pubRelPacket.PacketID, PacketType.PUBREC, PacketType.PUBCOMP);
                OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message, publishPacket.QoS, publishPacket.Retain);
            };

            incomingHandler.OnPubComp += (PubCompPacket pubCompPacket) =>
            {
                outgoingPacketQueueHandler.PacketReceived(pubCompPacket.PacketID, PacketType.PUBREL, PacketType.PUBCOMP);
            };
            incomingHandler.OnSubAck += (SubAckPacket subAckPacket) =>
            {
                outgoingPacketQueueHandler.PacketReceived(subAckPacket.PacketID, PacketType.SUBSCRIBE, PacketType.SUBACK);
                SubscribePacket subscribePacket = (SubscribePacket)outgoingPacketQueueHandler.Get.Packet!;
                foreach (Topic topic in subscribePacket.Topics)
                {
                    OnSubscribed?.Invoke(topic.Name, topic.QoS);
                }
            };
            incomingHandler.OnUnSubAck += (UnSubAckPacket unSubAckPacket) =>
            {
                UnsubscribePacket unsubscribePacket = (UnsubscribePacket)outgoingPacketQueueHandler.Get.Packet!;
                outgoingPacketQueueHandler.PacketReceived(unSubAckPacket.PacketID, PacketType.UNSUBSCRIBE, PacketType.UNSUBACK);
                foreach (Topic topic in unsubscribePacket.Topics)
                {
                    OnUnsubscribed?.Invoke(topic.Name);
                }
            };
            incomingHandler.OnDisconnect += (DisconnectPacket disconnectPacket) =>
            {
                OnDisconnected?.Invoke(disconnectPacket.ReasonCode ?? DisconnectReasionCode.NORMAL_DISCONNECTION);
            };
        }

        private void StartListeners()
        {
            cts = new CancellationTokenSource();

            incomingHandler!.IncomingPacketListener(cts, stream!, mqttMonitor, mqttOption);

            incomingPacketQueueHandler.Start(cts, mqttMonitor, outgoingHandler!);
            outgoingPacketQueueHandler.Start(cts, mqttMonitor, outgoingHandler!);
        }

        private async Task HandleConnect()
        {
            outgoingHandler!.SendConnect(clientID, username, password);

            int elapsed = 0;
            int timeout = 3000;
            while (!mqttMonitor.IsConnected && !mqttMonitor.IsConnectionClosed && elapsed < timeout)
            {
                elapsed += 50;
                await Task.Delay(50);
            }
        }

        private void OnConnected()
        {
            mqttMonitor.Start(tcpClient!, cts!, mqttOption.KeepAlive, outgoingHandler!.SendPingReq);
            mqttMonitor.OnDisconnect += () => Disconnect();
            mqttMonitor.OnConnectionLost += ConnectionLost;
        }
    }
}
