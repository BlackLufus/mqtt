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
        public delegate void ConnectionSuccessDelegate(bool sessionPresent, ConnectReturnCode returnCode);
        public event ConnectionSuccessDelegate? OnConnectionSuccess;

        public delegate void ConnectionLostDelegate();
        public event ConnectionLostDelegate? OnConnectionLost;

        public delegate void ConnectionFailedDelegate();
        public event ConnectionFailedDelegate? OnConnectionFailed;

        public delegate void DisconnectedDelegate();
        public event DisconnectedDelegate? OnDisconnected;

        public delegate void MessageReceivedDelegate(string topic, string message, bool retain);
        public event MessageReceivedDelegate? OnMessageReceived;

        public delegate void SubscribedDelegate(string topic);
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
        private readonly PacketQueueHandler packetQueueHandler;
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
            packetQueueHandler = new PacketQueueHandler();
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
                    Console.WriteLine("Connection failed: " + ex.Message);
                    OnConnectionFailed?.Invoke();
                }
            }
        }

        public void Publish(string topic, string message, bool isDup = false)
        {
            packetQueueHandler.Add(new PacketQueue(PublishPacket.NextPacketID, PacketType.PUBLISH, null, topic, message, "PUBLISH"));
        }

        public void Subscribe(string topic)
        {
            packetQueueHandler.Add(new PacketQueue(SubscribePacket.NextPacketID, PacketType.SUBSCRIBE, null, topic, null, null));
        }

        public void Unsubscribe(string topic)
        {
            packetQueueHandler.Add(new PacketQueue(UnsubscribePacket.NextPacketID, PacketType.UNSUBSCRIBE, null, topic, null, null));
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
                packetQueueHandler.Clear();
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
            Disconnect(false);
            OnConnectionLost?.Invoke();
            Reconnect();
        }

        private async void Reconnect()
        {
            while (!mqttMonitor.IsConnected)
            {
                Console.WriteLine("Reconnecting...");
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
                // Invoke the ConnectionSuccess event
                OnConnectionSuccess?.Invoke(connAckPacket.SessionPresent, connAckPacket.ReturnCode);

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
                        OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message, publishPacket.Retain);
                        break;
                    case QualityOfService.AT_LEAST_ONCE: // QoS 1 - "At least once"
                        outgoingHandler.SendPubAck(publishPacket.PacketID);
                        OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message, publishPacket.Retain);
                        break;
                    case QualityOfService.EXACTLY_ONCE: // QoS 2 - "Exactly once"
                        PublishPacket.PendingPackets[publishPacket.PacketID] = publishPacket;
                        outgoingHandler.SendPubRec(publishPacket.PacketID);
                        break;
                }
            };
            incomingHandler.OnPubAck += (PubAckPacket pubAckPacket) =>
            {
                // Remove the message from the packetMap
                if (pubAckPacket.PacketID == packetQueueHandler.Get.Id)
                {
                    packetQueueHandler.Remove();
                }
            };

            incomingHandler.OnPubRec += (PubRecPacket pubRecPacket) =>
            {
                // Send PUBREL
                outgoingHandler.SendPubRel(pubRecPacket.PacketID);

                if (pubRecPacket.PacketID == packetQueueHandler.Get.Id)
                {
                    PacketQueue packetQueue = packetQueueHandler.Get;
                    packetQueueHandler.Add(new PacketQueue(packetQueue.Id, packetQueue.PacketType, packetQueue.Timestamp, packetQueue.Topic, packetQueue.Message, "PUBREC"));
                }
            };

            incomingHandler.OnPubRel += (PubRelPacket pubRelPacket) =>
            {
                // Send PUBCOMP
                outgoingHandler.SendPubComp(pubRelPacket.PacketID);

                // Invoke the MessageReceived event
                OnMessageReceived?.Invoke(PublishPacket.PendingPackets[pubRelPacket.PacketID].Topic, PublishPacket.PendingPackets[pubRelPacket.PacketID].Message, PublishPacket.PendingPackets[pubRelPacket.PacketID].Retain);

                // Remove the message from the packetMap
                PublishPacket.PendingPackets.Remove(pubRelPacket.PacketID);
            };

            incomingHandler.OnPubComp += (PubCompPacket pubCompPacket) =>
            {
                // Remove the message from the packetMap
                if (pubCompPacket.PacketID == packetQueueHandler.Get.Id)
                {
                    Console.WriteLine("Remove from queue: (publish)" + pubCompPacket.PacketID);
                    packetQueueHandler.Remove();
                }
            };
            incomingHandler.OnSubAck += (SubAckPacket subAckPacket) =>
            {
                // Invoke the Subscribed event
                if (subAckPacket.PacketID == packetQueueHandler.Get.Id)
                {
                    OnSubscribed?.Invoke(packetQueueHandler.Get.Topic);
                    Debug.WriteLine("Remove from queue: (subscribe)" + subAckPacket.PacketID);
                    packetQueueHandler.Remove();
                }
            };
            incomingHandler.OnUnsubAck += (UnsubscribePacket unsubscribePacket) =>
            {
                // Invoke the Unsubscribed event
                if (unsubscribePacket.PacketID == packetQueueHandler.Get.Id)
                {
                    OnUnsubscribed?.Invoke(packetQueueHandler.Get.Topic);
                    Debug.WriteLine("Remove from queue: (unsubscribe)" + unsubscribePacket.PacketID);
                    packetQueueHandler.Remove();
                }
            };
            incomingHandler.OnDisconnect += (DisconnectPacket disconnectPacket) =>
            {
                if (mqttOption.Version == MqttVersion.MQTT_5)
                {
                    Console.WriteLine("Disconnect Reason Code: " + disconnectPacket.ReasonCode);
                }
                OnDisconnected?.Invoke();
            };
        }

        private void StartListeners()
        {
            cts = new CancellationTokenSource();

            incomingHandler!.IncomingPacketListener(cts, stream!, mqttMonitor);

            packetQueueHandler.Start(cts, mqttMonitor, mqttOption, outgoingHandler!);
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
