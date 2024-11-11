using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using mqtt.Connection;
using mqtt.Options;
using mqtt.Packets;
using mqtt.ReasonCode;

namespace mqtt.Client
{
    public class Mqtt : IMqtt
    {
        /// <summary>
        /// Here we define the events for the MQTT client
        /// </summary>
        public delegate void ConnectionSuccessDelegate(bool sessionPresent, ConnectReturnCode returnCode);
        public event ConnectionSuccessDelegate? OnConnectionSuccess;

        public delegate void ConnectionLostDelegate();
        public event ConnectionLostDelegate? OnConnectionLost;

        public delegate void ConnectionFailedDelegate();
        public event ConnectionFailedDelegate? OnConnectionFailed;

        public delegate void DisconnectedDelegate();
        public event DisconnectedDelegate? OnDisconnected;

        public delegate void MessageReceivedDelegate(string topic, string message);
        public event MessageReceivedDelegate? OnMessageReceived;

        public delegate void SubscribedDelegate(string topic);
        public event SubscribedDelegate? OnSubscribed;

        public delegate void UnsubscribedDelegate(string topic);
        public event UnsubscribedDelegate? OnUnsubscribed;

        /// <summary>
        /// Variables for the connection state
        /// </summary>
        public MqttVersion Version { get; set; } = MqttVersion.MQTT_3_1_1;
        public bool WillRetain { get; set; } = false;
        public LastWill? LastWill { get; set; }
        public QualityOfService QoS { get; set; } = QualityOfService.EXACTLY_ONCE;
        public bool CleanSession { get; set; } = true;
        public int KeepAlive { get; set; } = 20;
        public int SessionExpiryInterval { get; set; } = 10;

        /// <summary>
        /// TcpClient for the connection and NetworkStream for the data transfer
        /// </summary>
        private TcpClient? tcpClient;
        private NetworkStream? stream;

        private readonly MqttMonitor mqttMonitor = new MqttMonitor();

        /// <summary>
        /// Timer for the ping messages
        /// </summary>
        CancellationTokenSource? cts = null;

        private string brokerAddress = "";
        private int port = 0;
        private string clientID = "";
        private string username = "";
        private string password = "";

        /// <summary>
        /// Connect to the MQTT broker
        /// </summary>
        /// <param name="brokerAddress"> The broker address </param>
        /// <param name="port"> The port </param>
        /// <param name="clientID"> The client ID </param>
        /// <returns> Task </returns>
        public async Task Connect(string brokerAddress, int port, string clientID, string username = "", string password = "")
        {
            try
            {
                if (mqttMonitor.IsConnected)
                {
                    return;
                }

                mqttMonitor.ResetVariables();

                if (brokerAddress.Equals(""))
                {
                    throw new Exception("Broker address is empty!");
                }
                else if (port < 0 || port > 65535)
                {
                    throw new Exception("Port is invalid!");
                }
                if (clientID.Equals(""))
                {
                    throw new Exception("Client ID is empty!");
                }
                else if (clientID.Length > 23)
                {
                    throw new Exception("Client ID is too long!");
                }

                this.brokerAddress = brokerAddress;
                this.port = port;
                this.clientID = clientID;
                this.username = username;
                this.password = password;

                // Create a new TCP client and connect to the broker
                tcpClient = new TcpClient(brokerAddress, port);

                // Get the stream
                stream = tcpClient.GetStream();

                cts = new CancellationTokenSource();

                // Starte den Listener für eingehende Pakete
                IncomingPacketListener();

                MqttQueueListener();

                Task.Delay(100).Wait();

                // Erstelle eine MQTT CONNECT Nachricht (simplifizierte Version)
                SendConnect(clientID, username, password);

                // Wait until the connection is established or the connection is closed
                int elapsed = 0;
                int timeout = 3000;
                while (!mqttMonitor.IsConnected && !mqttMonitor.IsConnectionClosed && elapsed < timeout)
                {
                    elapsed += 50;
                    await Task.Delay(50);
                }

                mqttMonitor.Start(tcpClient, cts, KeepAlive, SendPingReq);
                mqttMonitor.OnDisconnect += () => Disconnect();
                mqttMonitor.OnConnectionLost += ConnectionLost;

            }
            catch (Exception ex)
            {
                Console.WriteLine("Connection failed: " + ex.Message);
                OnConnectionFailed?.Invoke();
            }
        }

        private List<(int, PacketType, DateTime?, string, string?, string?)> mqttQueue = [];

        /// <summary>
        /// Publish a message to a topic
        /// </summary>
        /// <param name="topic"> The topic to publish to </param>
        /// <param name="message"> The message to publish </param>
        public void Publish(string topic, string message)
        {
            mqttQueue.Add((PublishPacket.NextPacketID, PacketType.PUBLISH, null, topic, message, "PUBLISH"));
        }

        /// <summary>
        /// Subscribe to a topic
        /// </summary>
        /// <param name="topic"> The topic to subscribe to </param>
        public void Subscribe(string topic)
        {
            mqttQueue.Add((SubscribePacket.NextPacketID, PacketType.SUBSCRIBE, null, topic, null, null));
        }

        /// <summary>
        /// Unsubscribe from a topic
        /// </summary>
        /// <param name="topic"> The topic to unsubscribe from </param>
        public void Unsubscribe(string topic)
        {
            mqttQueue.Add((UnsubscribePacket.NextPacketID, PacketType.UNSUBSCRIBE, null, topic, null, null));
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
                await Connect(brokerAddress, port, clientID, username, password);
            }
        }

        /// <summary>
        /// Close the stream and the connection to the broker
        /// </summary>
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
                OnDisconnected?.Invoke();
            }

            cts?.Cancel();
            cts?.Dispose();

            // Close the stream
            if (stream != null)
            {
                SendDisconnect();
                stream.Close();
            }

            // Disconnect the client
            tcpClient?.Close();
        }

        private void MqttQueueListener()
        {
            CancellationToken token = cts!.Token;
            Task.Run(async () =>
            {
                while (!mqttMonitor.IsConnectionClosed)
                {
                    if (mqttMonitor.IsConnected && mqttQueue.Count > 0)
                    {
                        (int id, PacketType packetType, DateTime? timestamp, string topic, string? message, string state) = mqttQueue[0];

                        if (timestamp == null || DateTime.Now - timestamp > TimeSpan.FromSeconds(3))
                        {
                            timestamp = DateTime.Now;
                            mqttQueue[0] = (id, packetType, timestamp, topic, message, state);

                            switch (packetType)
                            {
                                case PacketType.PUBLISH:
                                    if (QoS == QualityOfService.AT_MOST_ONCE)
                                    {
                                        SendPublish(id, topic, message!);
                                        mqttQueue.RemoveAt(0);
                                    }
                                    else if (QoS == QualityOfService.AT_LEAST_ONCE)
                                    {
                                        SendPublish(id, topic, message!);
                                    }
                                    else if (QoS == QualityOfService.EXACTLY_ONCE)
                                    {
                                        switch (state)
                                        {
                                            case "PUBLISH":
                                                SendPublish(id, topic, message!);
                                                break;
                                            case "PUBREC":
                                                SendPubRel(id);
                                                break;
                                        }
                                    }
                                    break;
                                case PacketType.SUBSCRIBE:
                                    SendSubscribe(id, topic);
                                    break;
                                case PacketType.UNSUBSCRIBE:
                                    SendUnsubscribe(id, topic);
                                    break;
                            }
                        }
                    }
                    await Task.Delay(50);
                }
            }, token);
        }

        /// <summary>
        /// Incoming Packet Listener
        /// </summary>
        /// <returns> Task </returns>
        private void IncomingPacketListener()
        {
            CancellationToken token = cts!.Token;
            Task.Run(async () =>
            {
                // Check if the stream is null
                if (stream == null)
                {
                    return;
                }

                // Buffer for incoming data (1 KB) and number of bytes read
                byte[] buffer = new byte[1024 * 1024 * 256];
                int bytesRead;

                // Read incoming data from the stream and handle the packet accordingly until the connection is closed or an exception occurs
                do
                {
                    // Read the incoming data
                    bytesRead = await stream.ReadAsync(buffer);

                    // Handle the incoming packet
                    Debug.WriteLine("Bytes read: " + bytesRead);
                    HandleIncomingPacket(buffer);
                } while (bytesRead > 0);
            }, token);
        }

        /// <summary>
        /// Handle incoming packet
        /// </summary>
        /// <param name="packet"></param>
        private void HandleIncomingPacket(byte[] packet)
        {
            try
            {
                // 1. Byte: Packet Type
                PacketType packetType = (PacketType)(packet[0] & 0b_1111_0000);

                Console.WriteLine("Incoming Packet: " + packetType);

                // Handle the packet based on the packet type
                switch (packetType)
                {
                    case PacketType.CONNACK:
                        HandleConnAck(ConnAckPacket.Decode(packet));
                        break;
                    case PacketType.PUBLISH:
                        HandlePublish(PublishPacket.Decode(packet));
                        break;
                    case PacketType.PUBACK:
                        HandlePubAck(PubAckPacket.Decode(packet));
                        break;
                    case PacketType.PUBREC:
                        HandlePubRec(PubRecPacket.Decode(packet));
                        break;
                    case PacketType.PUBREL:
                        HandlePubRel(PubRelPacket.Decode(packet));
                        break;
                    case PacketType.PUBCOMP:
                        HandlePubComp(PubCompPacket.Decode(packet));
                        break;
                    case PacketType.SUBACK:
                        HandleSubAck(SubAckPacket.Decode(packet));
                        break;
                    case PacketType.UNSUBACK:
                        HandleUnsubAck(UnsubscribePacket.Decode(packet));
                        break;
                    case PacketType.PINGREQ:
                        HandlePingReq(PingReqPacket.Decode(packet));
                        break;
                    case PacketType.PINGRESP:
                        HandlePingResp(PingRespPacket.Decode(packet));
                        break;
                    case PacketType.DISCONNECT:
                        HandleDisconnect(DisconnectPacket.Decode(packet));
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while handling incoming packet: " + ex.Message);
                Console.WriteLine("Stack Trace: " + ex.StackTrace);
            }
        }

        /// <summary>
        /// Handle Publish (PUBLISH) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePublish(PublishPacket publishPacket)
        {
            // Verarbeite je nach QoS-Stufe
            switch (publishPacket.QoS)
            {
                case QualityOfService.AT_MOST_ONCE: // QoS 0 - "At most once"
                    OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message);
                    break;
                case QualityOfService.AT_LEAST_ONCE: // QoS 1 - "At least once"
                    SendPubAck(publishPacket.PacketID);
                    OnMessageReceived?.Invoke(publishPacket.Topic, publishPacket.Message);
                    break;
                case QualityOfService.EXACTLY_ONCE: // QoS 2 - "Exactly once"
                    PublishPacket.PendingPackets[publishPacket.PacketID] = publishPacket;
                    SendPubRec(publishPacket.PacketID);
                    break;
            }
        }

        /// <summary>
        /// Handle Connect Acknowledge (CONNACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleConnAck(ConnAckPacket connAckPacket)
        {
            // Invoke the ConnectionSuccess event
            OnConnectionSuccess?.Invoke(connAckPacket.SessionPresent, connAckPacket.ReturnCode);

            // Set the connection state
            mqttMonitor.IsConnected = true;
        }

        /// <summary>
        /// Handle Publish acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubAck(PubAckPacket pubAckPacket)
        {
            // Remove the message from the packetMap
            if (pubAckPacket.PacketID == mqttQueue[0].Item1)
            {
                mqttQueue.RemoveAt(0);
            }
        }

        /// <summary>
        /// Handle Publish received (PUBREC) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubRec(PubRecPacket pubRecPacket)
        {
            // Send PUBREL
            SendPubRel(pubRecPacket.PacketID);

            if (pubRecPacket.PacketID == mqttQueue[0].Item1)
            {
                (int id, PacketType packetType, DateTime? timestamp, string topic, string? message, string state) = mqttQueue[0];
                mqttQueue[0] = (id, packetType, timestamp, topic, message, "PUBREC");
            }
        }

        /// <summary>
        /// Handle Publish Release (PUBREL) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubRel(PubRelPacket pubRecPacket)
        {
            // Send PUBCOMP
            SendPubComp(pubRecPacket.PacketID);

            // Invoke the MessageReceived event
            OnMessageReceived?.Invoke(PublishPacket.PendingPackets[pubRecPacket.PacketID].Topic, PublishPacket.PendingPackets[pubRecPacket.PacketID].Message);

            // Remove the message from the packetMap
            PublishPacket.PendingPackets.Remove(pubRecPacket.PacketID);
        }

        /// <summary>
        /// Handle Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubComp(PubCompPacket pubCompPacket)
        {
            // Remove the message from the packetMap
            if (pubCompPacket.PacketID == mqttQueue[0].Item1)
            {
                Console.WriteLine("Remove from queue: (publish)" + pubCompPacket.PacketID);
                mqttQueue.RemoveAt(0);
            }
        }

        /// <summary>
        /// Handle Subscribe acknowledgement (SUBACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleSubAck(SubAckPacket subAckPacket)
        {
            // Invoke the Subscribed event
            if (subAckPacket.PacketID == mqttQueue[0].Item1)
            {
                OnSubscribed?.Invoke(mqttQueue[0].Item4);
                Debug.WriteLine("Remove from queue: (subscribe)" + subAckPacket.PacketID);
                mqttQueue.RemoveAt(0);
            }
        }

        /// <summary>
        /// Handle Unsubscribe acknowledgement (UNSUBACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleUnsubAck(UnsubscribePacket unsubscribePacket)
        {
            // Invoke the Unsubscribed event
            if (unsubscribePacket.PacketID == mqttQueue[0].Item1)
            {
                OnUnsubscribed?.Invoke(mqttQueue[0].Item4);
                Debug.WriteLine("Remove from queue: (unsubscribe)" + unsubscribePacket.PacketID);
                mqttQueue.RemoveAt(0);
            }
        }

        /// <summary>
        /// Handle Ping Request (PINGREQ) message
        /// </summary>
        /// <param name="packet"></param>
        private void HandlePingReq(PingReqPacket pingReqPacket)
        {
            // ToDo: Implement
            // No Implementation needed [Server specific]
        }

        /// <summary>
        /// Handle Ping Response (PINGRESP) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePingResp(PingRespPacket pingRespPacket)
        {
            Console.WriteLine("Ping Response");
        }

        /// <summary>
        /// Handle Disconnect (DISCONNECT) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleDisconnect(DisconnectPacket disconnectPacket)
        {
            if (Version == MqttVersion.MQTT_5)
            {
                Console.WriteLine("Disconnect Reason Code: " + disconnectPacket.ReasonCode);
            }
            Disconnect();
        }

        // ToDo: Implement to Connect Packet in a separate class (ConnectPacket.cs)
        private void SendConnect(string clientId, string username, string password)
        {
            // Connect Packet
            ConnectPacket connectPacket = new ConnectPacket(clientId, username, password, LastWill, WillRetain, QoS, CleanSession, Version, (ushort)KeepAlive, (uint)SessionExpiryInterval);

            // Send the message and flush the stream
            Send(connectPacket.Encode());
        }

        private void SendPublish(int id, string topic, string message, bool isDup = false)
        {
            // Publish Packet
            PublishPacket publishPacket = new PublishPacket(topic, message, QoS, WillRetain, isDup, id);

            // Send the message and flush the stream
            Send(publishPacket.Encode());
        }

        /// <summary>
        /// Send Publish Acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubAck(int packetId)
        {
            // PubAck Packet
            PubAckPacket pubAckPacket = new PubAckPacket(packetId);

            // Send the message and flush the stream
            Send(pubAckPacket.Encode());
        }

        /// <summary>
        /// Send Publish Received (PUBREC) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubRec(int packetId)
        {
            // PubRec Packet
            PubRecPacket pubRecPacket = new PubRecPacket(packetId);

            // Send the message and flush the stream
            Send(pubRecPacket.Encode());
        }

        /// <summary>
        /// Send Publish Release (PUBREL) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubRel(int packetId)
        {
            // PubRel Packet
            PubRelPacket pubRelPacket = new PubRelPacket(packetId);

            // Send the message and flush the stream
            Send(pubRelPacket.Encode());
        }

        /// <summary>
        /// Send Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubComp(int packetId)
        {
            // PubComp Packet
            PubCompPacket pubCompPacket = new PubCompPacket(packetId);

            // Send the message and flush the stream
            Send(pubCompPacket.Encode());
        }

        private void SendSubscribe(int id, string topic)
        {
            // Subscribe Packet
            SubscribePacket subscribe = new SubscribePacket(id, [topic], QoS);

            // Send the message and flush the stream
            Send(subscribe.Encode());
        }

        private void SendUnsubscribe(int id, string topic)
        {
            // Unsubscribe Packet
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(id, [topic]);

            // Send the message and flush the stream
            Send(unsubscribePacket.Encode());
        }

        /// <summary>
        /// Send Ping Request (PINGREQ) message
        /// </summary>
        private void SendPingReq()
        {
            // Ping Request Packet
            PingReqPacket pingReqPacket = new PingReqPacket();

            // Send the message and flush the stream
            Send(pingReqPacket.Encode());
        }

        /// <summary>
        /// Send Disconnect (DISCONNECT) message
        /// </summary>
        private void SendDisconnect()
        {
            // Disconnect Packet
            DisconnectPacket disconnectPacket = new DisconnectPacket();

            // Send the message and flush the stream
            Send(disconnectPacket.Encode());
        }

        private async void Send(byte[] data)
        {
            try
            {
                await stream!.WriteAsync(data);
                stream!.Flush();
            }
            catch { }
        }
    }
}
