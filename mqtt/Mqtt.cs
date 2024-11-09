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

namespace mqtt
{
    public class Mqtt : IMqtt
    {
        /// <summary>
        /// Enum for the MQTT version
        /// </summary>
        public enum Version
        {
            MQTT_3_1_1 = 4,
            MQTT_5 = 5 // Coming soon
        }
        /// <summary>
        /// Enum for the Packet Type
        /// </summary>
        private enum PacketType
        {
            CONNECT = 0x10,
            CONNACK = 0x20,
            PUBLISH = 0x30,
            PUBACK = 0x40,
            PUBREC = 0x50,
            PUBREL = 0x60,
            PUBCOMP = 0x70,
            SUBSCRIBE = 0x80,
            SUBACK = 0x90,
            UNSUBSCRIBE = 0xA0,
            UNSUBACK = 0xB0,
            PINGREQ = 0xC0,
            PINGRESP = 0xD0,
            DISCONNECT = 0xE0
        }

        /// <summary>
        /// Enum for the Quality of Service (QoS)
        /// </summary>
        public enum QualityOfService
        {
            AT_MOST_ONCE = 0,
            AT_LEAST_ONCE = 1,
            EXACTLY_ONCE = 2
        }

        /// <summary>
        /// Enum for the Connect Return Code
        /// </summary>
        private enum ConnectReturnCode
        {
            SUCCESS = 0x00,

            UNSPECIFIED_ERROR = 0x80,
            MALFORMED_PACKET = 0x81,
            PROTOCOL_ERROR = 0x82,
            IMPLEMENTATION_SPECIFIC_ERROR = 0x83,
            UNSUPPORTED_PROTOCOL_VERSION = 0x84,
            CLIENT_IDENTIFIER_NOT_VALID = 0x85,
            BAD_USER_NAME_OR_PASSWORD = 0x86,
            NOT_AUTHORIZED = 0x87,
            SERVER_UNAVAILABLE = 0x88,
            SERVER_BUSY = 0x89,
            BANNED = 0x8A,
            BAD_AUTHENTICATION_METHOD = 0x8C,
            TOPIC_NAME_INVALID = 0x90,
            PACKET_TOO_LARGE = 0x95,
            QUOTA_EXCEEDED = 0x97,
            PAYLOAD_FORMAT_INVALID = 0x99,
            RETAIN_NOT_SUPPORTED = 0x9A,
            QoS_NOT_SUPPORTED = 0x9B,
            USE_ANOTHER_SERVER = 0x9C,
            SERVER_MOVED = 0x9D,
            CONNECTION_RATE_EXCEEDED = 0x9F
        }

        private enum DisconnectReasionCode
        {
            NORMAL_DISCONNECTION = 0,
            DISCONNECT_WITH_WILL_MESSAGE = 4,
            UNSPECIFIED_ERROR = 128,
            MALFORMED_PACKET = 129,
            PROTOCOL_ERROR = 130,
            IMPLEMENTATION_SPECIFIC_ERROR = 131,
            NOT_AUTHORIZED = 135,
            SERVER_BUSY = 137,
            SERVER_SHUTTING_DOWN = 139,
            BAD_AUTHENTICATION_METHOD = 140,
            KEEP_ALIVE_TIMEOUT = 141,
            SESSION_TAKEN_OVER = 142,
            TOPIC_FILTER_INVALID = 143,
            TOPIC_NAME_INVALID = 144,
            RECEIVE_MAXIMUM_EXCEEDED = 147,
            TOPIC_ALIAS_INVALID = 148,
            PACKET_TOO_LARGE = 149,
            MESSAGE_RATE_TOO_HIGH = 150,
            QUOTA_EXCEEDED = 151,
            ADMINISTRATIVE_ACTION = 153,
            PAYLOAD_FORMAT_INVALID = 154,
            RETAIN_NOT_SUPPORTED = 155,
            QoS_NOT_SUPPORTED = 156,
            USE_ANOTHER_SERVER = 157,
            SERVER_MOVED = 158,
            SHARED_SUBSCRIPTIONS_NOT_SUPPORTED = 159,
            CONNECTION_RATE_EXCEEDED = 160,
            MAXIMUM_CONNECT_TIME = 161,
            SUBSCRIPTION_IDENTIFIERS_NOT_SUPPORTED = 162,
            WILDCARD_SUBSCRIPTIONS_NOT_SUPPORTED = 163
        }


        /// <summary>
        /// Here we define the events for the MQTT client
        /// </summary>
        public delegate void ConnectionSuccessDelegate();
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
        /// Key: Message ID
        /// Value: (Topic, Message)
        /// </summary>
        private Dictionary<int, (string, string)> packetMap = [];

        /// <summary>
        /// Variables for the connection state
        /// </summary>
        public Version MQTTVersion { get; set; } = Version.MQTT_3_1_1;
        public bool WillRetain { get; set; } = false;
        public QualityOfService QoS { get; set; } = QualityOfService.EXACTLY_ONCE;
        public bool CleanSession { get; set; } = true;
        public int KeepAlive { get; set; } = 20;
        public int SessionExpiryInterval { get; set; } = 10;

        /// <summary>
        /// TcpClient for the connection and NetworkStream for the data transfer
        /// </summary>
        private TcpClient? client;
        private NetworkStream? stream;

        /// <summary>
        /// Connection state
        /// </summary>
        private bool isConnected = false;

        /// <summary>
        /// Connection failed state (true if the connection failed)
        /// </summary>
        private bool connectionClosed = false;

        /// <summary>
        /// Message Packet ID for the PUBLISH message
        /// </summary>
        private int messagePacketId = 1;

        /// <summary>
        /// Packet ID for the SUBSCRIBE message
        /// </summary>
        private int subscribePacketId = 1;

        /// <summary>
        /// Packet ID for the UNSUBSCRIBE message
        /// </summary>
        private int unsubscribePacketId = 1;

        /// <summary>
        /// Timer for the ping messages
        /// </summary>
        CancellationTokenSource? cts = null;

        private bool willFlag = false;
        private string willTopic = "";
        private string willMessage = "";

        private string brokerAddress = "";
        private int port = 0;
        private string clientID = "";
        private string username = "";
        private string password = "";

        /// <summary>
        /// Set the will message, use SetWill(null, null) to disable the will message
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="message"></param>
        public void SetWill(string topic, string message)
        {
            if (!topic.Equals("") && !message.Equals("")) {
                willTopic = topic;
                willMessage = message;
                willFlag = true;
            }
            else
            {
                willFlag = false;
            }
        }

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
                if (isConnected)
                {
                    return;
                }
                isConnected = false;
                connectionClosed = false;

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
                client = new TcpClient(brokerAddress, port);

                // Get the stream
                stream = client.GetStream();

                cts = new CancellationTokenSource();

                // Starte den Listener für eingehende Pakete
                IncomingPacketListener();

                MqttQueueListener();

                Task.Delay(100).Wait();

                // Erstelle eine MQTT CONNECT Nachricht (simplifizierte Version)
                SendConnect(clientID, username, password);

                // Start the ping timer
                StartPingTimer(KeepAlive);

                // Wait until the connection is established or the connection is closed
                int elapsed = 0;
                int timeout = 3000;
                while (!isConnected && !connectionClosed && elapsed < timeout)
                {
                    elapsed += 50;
                    await Task.Delay(50);
                }
                MonitorConnection();
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
            mqttQueue.Add((messagePacketId++, PacketType.PUBLISH, null, topic, message, "PUBLISH"));
        }

        /// <summary>
        /// Subscribe to a topic
        /// </summary>
        /// <param name="topic"> The topic to subscribe to </param>
        public void Subscribe(string topic)
        {
            mqttQueue.Add((subscribePacketId++, PacketType.SUBSCRIBE, null, topic, null, null));
        }

        /// <summary>
        /// Unsubscribe from a topic
        /// </summary>
        /// <param name="topic"> The topic to unsubscribe from </param>
        public void Unsubscribe(string topic)
        {
            mqttQueue.Add((unsubscribePacketId++, PacketType.UNSUBSCRIBE, null, topic, null, null));
        }

        private void ConnectionLost()
        {
            Disconnect(false);
            OnConnectionLost?.Invoke();
            Reconnect();
        }

        private async void Reconnect()
        {
            while (!isConnected)
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
            if (!isConnected && connectionClosed)
            {
                return;
            }

            connectionClosed = true;
            isConnected = false;

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
            client?.Close();
        }

        // Enum für verschiedene Verbindungsstatus
        private enum ConnectionStatus
        {
            Connected,
            DisconnectedByHost,
            ConnectionError
        }

        private void MonitorConnection()
        {
            CancellationToken token = cts!.Token;
            Task.Run(async () =>
            {
                while (!connectionClosed)
                {
                    ConnectionStatus status = CheckConnectionStatus();
                    
                    switch (status)
                    {
                        case ConnectionStatus.DisconnectedByHost:
                            Console.WriteLine("Die Verbindung wurde vom Host getrennt.");
                            Disconnect(); // Spezifischer Handler für Host-getrennte Verbindungen
                            break;

                        case ConnectionStatus.ConnectionError:
                            Console.WriteLine("Es gab einen Verbindungsfehler.");
                            ConnectionLost(); // Allgemeiner Verbindungsverlust, keine spezifische Host-Trennung
                            break;

                        case ConnectionStatus.Connected:
                            Console.WriteLine("Die Verbindung ist weiterhin aktiv.");
                            break;
                    }
                    await Task.Delay(1000, token);
                }
            }, token);
        }

        private ConnectionStatus CheckConnectionStatus()
        {
            try
            {
                if (client != null && client.Client != null && client.Client.Connected)
                {
                    // Prüfen, ob Daten zur Verfügung stehen oder der Socket geschlossen wurde
                    if (client.Client.Poll(0, SelectMode.SelectRead))
                    {
                        byte[] buff = new byte[1];
                        int receivedBytes = client.Client.Receive(buff, SocketFlags.Peek);

                        if (receivedBytes == 0)
                        {
                            // Host hat die Verbindung getrennt, da keine Daten mehr empfangen werden können
                            return ConnectionStatus.DisconnectedByHost;
                        }
                        else
                        {
                            return ConnectionStatus.Connected;
                        }
                    }
                    return ConnectionStatus.Connected;
                }
                return ConnectionStatus.ConnectionError;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"SocketException aufgetreten: {ex.Message}");
                return ConnectionStatus.ConnectionError; // Netzwerk- oder Socketfehler
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unerwarteter Fehler: {ex.Message}");
                return ConnectionStatus.ConnectionError;
            }
        }

        /// <summary>
        /// Start the ping timer
        /// </summary>
        /// <param name="keepAliveInterval"> The keep alive interval </param>
        private void StartPingTimer(int keepAliveInterval)
        {
            CancellationToken token = cts!.Token;
            Task.Run(async () =>
            {
                SendPingReq();
                await Task.Delay(keepAliveInterval * 1000 / 2);
            }, token);
        }

        private void MqttQueueListener()
        {
            CancellationToken token = cts!.Token;
            Task.Run(async () =>
            {
                while (!connectionClosed)
                {
                    if (isConnected && mqttQueue.Count > 0)
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
                                                SendPubRel((byte)(id >> 8), (byte)(id & 0xFF));
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
                byte[] buffer = new byte[1024];
                int bytesRead;

                try
                {
                    // Read incoming data from the stream and handle the packet accordingly until the connection is closed or an exception occurs
                    do
                    {
                        // Read the incoming data
                        bytesRead = await stream.ReadAsync(buffer);

                        // Handle the incoming packet
                        HandleIncomingPacket(buffer);
                    } while (bytesRead > 0);
                }
                catch { }
            }, token);
        }

        /// <summary>
        /// Handle incoming packet
        /// </summary>
        /// <param name="packet"></param>
        private void HandleIncomingPacket(byte[] packet)
        {
            // 1. Byte: Packet Type
            PacketType packetType = (PacketType)(packet[0] & 0b_1111_0000);

            Console.WriteLine("Incoming Packet: " + packetType);

            // Handle the packet based on the packet type
            switch (packetType)
            {
                case PacketType.CONNACK:
                    HandleConnAck(packet);
                    break;
                case PacketType.PUBLISH:
                    HandlePublish(packet);
                    break;
                case PacketType.PUBACK:
                    HandlePubAck(packet);
                    break;
                case PacketType.PUBREC:
                    HandlePubRec(packet);
                    break;
                case PacketType.PUBREL:
                    HandlePubRel(packet);
                    break;
                case PacketType.PUBCOMP:
                    HandlePubComp(packet);
                    break;
                case PacketType.SUBACK:
                    HandleSubAck(packet);
                    break;
                case PacketType.UNSUBACK:
                    HandleUnsubAck(packet);
                    break;
                case PacketType.PINGREQ:
                    HandlePingReq(packet);
                    break;
                case PacketType.PINGRESP:
                    HandlePingResp(packet);
                    break;
                case PacketType.DISCONNECT:
                    HandleDisconnect(packet);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Handle Publish (PUBLISH) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePublish(byte[] packet)
        {
            // Fixed Header
            int dupFlag = (packet[0] & 0b_0000_1000) >> 3;
            int qoSLevel = (packet[0] & 0b_0000_0110) >> 1;
            int retainFlag = packet[0] & 0b_0000_0001;

            /*Console.WriteLine("Dup Flag: " + dupFlag);
            Console.WriteLine("QoS Level: " + qoSLevel);
            Console.WriteLine("Retain Flag: " + retainFlag);*/

            // Remaining Length
            int remainingLength = packet[1];

            // Index of the next byte
            int index = 2;

            // Topic Length & Topic
            int topicLength = packet[2] << 8 | packet[3];
            string topic = Encoding.UTF8.GetString(packet, 4, topicLength);
            index += 2 + topicLength;

            // Packet ID (If QoS > 0)
            int packetId = 0;
            if (qoSLevel > 0)
            {
                packetId = packet[4 + topicLength] << 8 | packet[5 + topicLength];
                index += 2;
            }

            // Message
            string message = Encoding.UTF8.GetString(packet, index, remainingLength - index + 2);

            /*Console.WriteLine("topic: " + topic);
            Console.WriteLine("message: " + message);*/

            // Verarbeite je nach QoS-Stufe
            switch (qoSLevel)
            {
                case 0: // QoS 0 - "At most once"
                    OnMessageReceived?.Invoke(topic, message);
                    break;
                case 1: // QoS 1 - "At least once"
                    SendPubAck((byte)(packetId >> 8), (byte)(packetId & 0xFF));
                    OnMessageReceived?.Invoke(topic, message);
                    break;
                case 2: // QoS 2 - "Exactly once"
                    packetMap.Add(packetId, (topic, message));
                    SendPubRec((byte)(packetId >> 8), (byte)(packetId & 0xFF));
                    break;
            }
        }

        /// <summary>
        /// Handle Connect Acknowledge (CONNACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleConnAck(byte[] packet)
        {
            OnConnectionSuccess?.Invoke();

            // Remaining Length
            int remainingLength = packet[1];

            // Connect Acknowledge Flags
            byte connectAcknowledgeFlags = packet[2];

            // Return Code
            ConnectReturnCode returnCode = (ConnectReturnCode)packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("Connect Acknowledge Flags: " + Convert.ToString(connectAcknowledgeFlags, 2).PadLeft(8, '0'));
            Console.WriteLine("Return Code: " + returnCode);

            // Set the connection state
            isConnected = true;
        }

        /// <summary>
        /// Handle Publish acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubAck(byte[] packet)
        {
            // Remaining Length
            int remainingLength = packet[1];

            // Message ID
            int packetId = packet[2] << 8 | packet[3];

            if (packetId == mqttQueue[0].Item1)
            {
                mqttQueue.RemoveAt(0);
            }

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBACK: " + packetId);
        }

        /// <summary>
        /// Handle Publish received (PUBREC) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubRec(byte[] packet)
        {
            // Remaining Length
            int remainingLength = packet[1];

            // Message ID
            int packetId = packet[2] << 8 | packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBREC: " + packetId);

            // Send PUBREL
            SendPubRel(packet[2], packet[3]);

            if (packetId == mqttQueue[0].Item1)
            {
                (int id, PacketType packetType, DateTime? timestamp, string topic, string? message, string state) = mqttQueue[0];
                mqttQueue[0] = (id, packetType, timestamp, topic, message, "PUBREC");
            }
        }

        /// <summary>
        /// Handle Publish Release (PUBREL) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubRel(byte[] packet)
        {
            // Remaining Length
            int remainingLength = packet[1];

            // Message ID
            int packetId = packet[2] << 8 | packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBREL: " + packetId);

            // Send PUBCOMP
            SendPubComp(packet[2], packet[3]);

            // Invoke the MessageReceived event
            OnMessageReceived?.Invoke(packetMap[packetId].Item1, packetMap[packetId].Item2);

            // Remove the message from the packetMap
            packetMap.Remove(packetId);
        }

        /// <summary>
        /// Handle Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePubComp(byte[] packet)
        {
            int remainingLength = packet[1];
            int packetId = packet[2] << 8 | packet[3];

            if (packetId == mqttQueue[0].Item1)
            {
                Console.WriteLine("Remove from queue: (publish)" + packetId);
                mqttQueue.RemoveAt(0);
            }

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBCOMP: " + packetId);
        }

        /// <summary>
        /// Handle Subscribe acknowledgement (SUBACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleSubAck(byte[] packet)
        {
            // Remaining Length
            int remainingLength = packet[1];

            // Message ID
            int packetId = packet[2] << 8 | packet[3];

            // Return Codes
            byte[] returnCodes = new byte[remainingLength - 2];
            Array.Copy(packet, 4, returnCodes, 0, returnCodes.Length);

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("SUBACK: " + packetId);

            // Was the subscription successful?
            bool Failure = ((0b_1000_000 & packet[4]) >> 7) == 1;

            // QoS Level
            int QoSLevel = (0b_0000_0011 & packet[4]);

            Console.WriteLine("Failure: " + Failure);
            Console.WriteLine("QoS Level: " + QoSLevel);

            if (packetId == mqttQueue[0].Item1)
            {
                OnSubscribed?.Invoke(mqttQueue[0].Item4);
                Debug.WriteLine("Remove from queue: (subscribe)" + packetId);
                mqttQueue.RemoveAt(0);
            }
        }

        /// <summary>
        /// Handle Unsubscribe acknowledgement (UNSUBACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleUnsubAck(byte[] packet)
        {
            // Remaining Length
            int remainingLength = packet[1];

            // Message ID
            int packetId = packet[2] << 8 | packet[3];

            if (packetId == mqttQueue[0].Item1)
            {
                OnUnsubscribed?.Invoke(mqttQueue[0].Item4);
                Debug.WriteLine("Remove from queue: (unsubscribe)" + packetId);
                mqttQueue.RemoveAt(0);
            }

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("UnsubAck: " + packetId);
        }

        /// <summary>
        /// Handle Ping Request (PINGREQ) message
        /// </summary>
        /// <param name="packet"></param>
        private void HandlePingReq(byte[] packet)
        {
            // ToDo: Implement
            // No Implementation needed [Server specific]
        }

        /// <summary>
        /// Handle Ping Response (PINGRESP) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandlePingResp(byte[] packet)
        {
            Console.WriteLine("Ping Response");
        }

        /// <summary>
        /// Handle Disconnect (DISCONNECT) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleDisconnect(byte[] packet)
        {
            if (MQTTVersion == Version.MQTT_5)
            {
                Console.WriteLine("Disconnect Reason Code: " + (DisconnectReasionCode)packet[2]);
            }
            Disconnect();
        }

        private async void SendConnect(string clientId, string username, string password)
        {
            // Fixed Header: 1 Byte
            byte[] fixedHeader = [
                // Packet Type
                (byte)PacketType.CONNECT,
                // Remaining Length
                10
            ];

            // Connect Flags
            byte connectFlags = 0b_0000_0000;

            Console.WriteLine("username: " + username);
            // User Name Flag (Bit 7)
            // This bit specifies if a user name is present in the payload.
            if (username.Length > 0)
            {
                connectFlags |= 1 << 7;

                // Password Flag (Bit 6)
                // This bit specifies if a password is used.
                if (password.Length > 0)
                {
                    connectFlags |= 1 << 6;
                }
            }

            // Will Flag (Bit 2)
            // This bit specifies if the Will Flag is set.
            if (willFlag)
            {
                // Will Retain Flag (Bit 5) [Only if Will Flag is set]
                // This bit specifies if the Will Message is to be Retained when it is published.
                if (WillRetain)
                {
                    connectFlags |= 1 << 5;
                }

                // Will QoS (Bits 3 and 4) [Only if Will Flag is set]
                connectFlags |= (byte)((int)QoS << 3);

                connectFlags |= 1 << 2;
            }

            // Clean Session Flag (Bit 1)
            //  This bit specifies the handling of the Session state.
            if (CleanSession)
            {
                connectFlags |= 1 << 1;
            }

            // Variable Header
            List<byte> header = [
                // Length MSB (0)
                0x00,
                // Length LSB (4)
                0x04,
                // 'MQTT'
                (byte)'M', (byte)'Q', (byte)'T', (byte)'T',
                // Version
                (byte)MQTTVersion,
                // Connect Flags
                connectFlags,
                // Keep Alive MSB
                (byte)(KeepAlive >> 8),
                // Keep Alive LSB
                (byte)(KeepAlive & 0xFF),
            ];

            if (MQTTVersion == Version.MQTT_5)
            {
                header.AddRange([
                    // Properties Length
                    5,
                    // Property Identifier: Session Expiry Interval
                    0b_0001_0001,
                    // Property Length
                    (byte)(SessionExpiryInterval >> 24),
                    (byte)(SessionExpiryInterval >> 16),
                    (byte)(SessionExpiryInterval >> 8),
                    (byte)(SessionExpiryInterval & 0xFF),
                ]);
            }

            // Payload
            List<byte> payload = [
                (byte)(clientId.Length >> 8),
                (byte)(clientId.Length & 0xFF)
            ];
            //byte[] payload = new byte[2 + clientId.Length];
            payload.AddRange(Encoding.UTF8.GetBytes(clientId));
            //Array.Copy(Encoding.UTF8.GetBytes(clientId), 0, payload, 2, clientId.Length);

            if (willFlag)
            {
                Console.WriteLine("Set Will Flag");
                byte[] topicArray = new byte[2 + willTopic.Length];
                topicArray[0] = (byte)(willTopic.Length >> 8);
                topicArray[1] = (byte)(willTopic.Length & 0xFF);
                Array.Copy(Encoding.UTF8.GetBytes(willTopic), 0, topicArray, 2, willTopic.Length);
                payload.AddRange(topicArray);

                byte[] messageArray = new byte[2 + willMessage.Length];
                messageArray[0] = (byte)(willMessage.Length >> 8);
                messageArray[1] = (byte)(willMessage.Length & 0xFF);
                Array.Copy(Encoding.UTF8.GetBytes(willMessage), 0, messageArray, 2, willMessage.Length);
                payload.AddRange(messageArray);
            }

            if (username.Length > 0)
            {
                Console.WriteLine("Set Username Flag");
                byte[] usernameArray = new byte[2 + username!.Length];
                usernameArray[0] = (byte)(username.Length >> 8);
                usernameArray[1] = (byte)(username.Length & 0xFF);
                Array.Copy(Encoding.UTF8.GetBytes(username), 0, usernameArray, 2, username.Length);
                payload.AddRange(usernameArray);

                if (password.Length > 0)
                {
                    byte[] passwordArray = new byte[2 + password!.Length];
                    passwordArray[0] = (byte)(password.Length >> 8);
                    passwordArray[1] = (byte)(password.Length & 0xFF);
                    Array.Copy(Encoding.UTF8.GetBytes(password), 0, passwordArray, 2, password.Length);
                    payload.AddRange(passwordArray);
                }
            }
            /*for (int i = 0; i < payload.Count; i++)
            {
                Console.WriteLine(Convert.ToString(payload[i], 2).PadLeft(8, '0'));
            }*/

            //Console.WriteLine(Convert.ToString(connectFlags, 2).PadLeft(8, '0'));

            // Set the remaining length and set it in the fixed header
            int remainingLength = header.Count + payload.Count;
            fixedHeader[1] = (byte)remainingLength;

            // Merge all arrays
            byte[] result = new byte[fixedHeader.Length + header.Count + payload.Count];
            Array.Copy(fixedHeader, 0, result, 0, fixedHeader.Length);
            Array.Copy(header.ToArray(), 0, result, fixedHeader.Length, header.Count);
            Array.Copy(payload.ToArray(), 0, result, fixedHeader.Length + header.Count, payload.Count);

            Console.WriteLine("Connect Packet: " + header.Count);
            for (int i = 0; i < header.Count; i++)
            {
                Console.WriteLine(Convert.ToString(header[i], 2).PadLeft(8, '0'));
            }
            Console.WriteLine("=====================================");

            try
            {
                await stream!.WriteAsync(result);
                stream!.Flush();
            }
            catch { }
        }

        private async void SendPublish(int id, string topic, string message, bool isDup = false)
        {
            Console.WriteLine("Send Publish: " + id);
            // Need to implement!
            // It indicates that the message is a duplicate of a previously sent message
            bool dupFlag = false;

            // Fixed Header
            byte[] fixedHeader =
            [
                (byte)((byte)(PacketType.PUBLISH) | (dupFlag ? (1 << 3) : isDup ? 1 : 0) | ((int)QoS << 1) | (WillRetain ? 1 : 0)),
                0
            ];

            // Payload
            byte[] payload = new byte[2 + topic.Length + message.Length + (QoS != QualityOfService.AT_MOST_ONCE ? 2 : 0)];
            payload[0] = (byte)(topic.Length >> 8);
            payload[1] = (byte)(topic.Length & 0xFF);
            Array.Copy(Encoding.UTF8.GetBytes(topic), 0, payload, 2, topic.Length);

            // Set the message ID if QoS > 0
            if (QoS != QualityOfService.AT_MOST_ONCE)
            {
                payload[2 + topic.Length] = (byte)(id >> 8);
                payload[3 + topic.Length] = (byte)(id & 0xFF);
            }

            // Copy the message to the payload
            Array.Copy(Encoding.UTF8.GetBytes(message), 0, payload, 2 + topic.Length + (QoS != QualityOfService.AT_MOST_ONCE ? 2 : 0), message.Length);

            // Set the remaining length
            int remainingLength = payload.Length;
            fixedHeader[1] = (byte)remainingLength;

            // Merge all arrays
            byte[] result = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, result, 0, fixedHeader.Length);
            Array.Copy(payload, 0, result, fixedHeader.Length, payload.Length);

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(result);
                stream.Flush();
            }
            catch { }
        }

        /// <summary>
        /// Send Publish Acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private async void SendPubAck(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            [
                (byte)PacketType.PUBACK | 0b_0000,
                2
            ];

            // Payload
            byte[] payload = [
                MSB,
                LSB
            ];

            // Merge all arrays
            byte[] pubAck = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubAck, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubAck, fixedHeader.Length, payload.Length);

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(pubAck);
                stream?.Flush();
            }
            catch { }
        }

        /// <summary>
        /// Send Publish Received (PUBREC) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private async void SendPubRec(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            [
                (byte)PacketType.PUBREC | 0b_0000,
                2
            ];

            // Payload
            byte[] payload = [
                MSB,
                LSB
            ];

            // Merge all arrays
            byte[] pubRec = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubRec, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubRec, fixedHeader.Length, payload.Length);

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(pubRec);
                stream?.Flush();
            }
            catch { }
        }

        /// <summary>
        /// Send Publish Release (PUBREL) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private async void SendPubRel(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            [
                (byte)PacketType.PUBREL | 0b_0010,
                2
            ];

            // Payload
            byte[] payload = [
                MSB,
                LSB
            ];

            // Merge all arrays
            byte[] pubRel = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubRel, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubRel, fixedHeader.Length, payload.Length);

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(pubRel);
                stream?.Flush();
            }
            catch { }
        }

        /// <summary>
        /// Send Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private async void SendPubComp(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            [
                (byte)PacketType.PUBCOMP | 0b_0000,
                2
            ];

            // Payload
            byte[] payload = [
                MSB,
                LSB
            ];

            // Merge all arrays
            byte[] pubComp = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubComp, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubComp, fixedHeader.Length, payload.Length);

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(pubComp);
                stream?.Flush();
            }
            catch { }
        }

        private async void SendSubscribe(int id, string topic)
        {
            // Fixed Header
            byte[] fixedHeader = [
                (byte)PacketType.SUBSCRIBE | 0b_0010,
                (byte)(2 + 2 + topic.Length + 1),
                (byte)(id >> 8),
                (byte)(id++ & 0b_1111_1111)
            ];

            // Payload
            byte[] payload = new byte[2 + topic.Length + 1];
            payload[0] = (byte)(topic.Length >> 8);
            payload[1] = (byte)(topic.Length & 0xFF);
            Array.Copy(Encoding.UTF8.GetBytes(topic), 0, payload, 2, topic.Length);
            payload[2 + topic.Length] = (byte)(0b_0000_0011 & (int)QoS);

            // Merge all arrays
            byte[] subscribe = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, subscribe, 0, fixedHeader.Length);
            Array.Copy(payload, 0, subscribe, fixedHeader.Length, payload.Length);

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(subscribe);
                stream!.Flush();
            }
            catch { }
        }

        private async void SendUnsubscribe(int id, string topic)
        {
            // Fixed Header
            byte[] bytes = [
                (byte)PacketType.UNSUBSCRIBE | 0b_0010,
                0
            ];

            // Variable Header
            byte[] variableHeader =
            [
                (byte)(id >> 8),
                (byte)(id++ & 0xFF)
            ];

            // Payload
            byte[] payload = new byte[2 + topic.Length];
            payload[0] = (byte)(topic.Length >> 8);
            payload[1] = (byte)(topic.Length & 0xFF);
            Array.Copy(Encoding.UTF8.GetBytes(topic), 0, payload, 2, topic.Length);

            // Set remaining length
            int remainingLength = variableHeader.Length + payload.Length;
            bytes[1] = (byte)remainingLength;

            // Merge all arrays
            byte[] unsubscribe = new byte[bytes.Length + variableHeader.Length + payload.Length];
            Array.Copy(bytes, 0, unsubscribe, 0, bytes.Length);
            Array.Copy(variableHeader, 0, unsubscribe, bytes.Length, variableHeader.Length);
            Array.Copy(payload, 0, unsubscribe, bytes.Length + variableHeader.Length, payload.Length);

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(unsubscribe);
                stream!.Flush();
            }
            catch { }
        }

        /// <summary>
        /// Send Ping Request (PINGREQ) message
        /// </summary>
        private async void SendPingReq()
        {
            Console.WriteLine("Send Ping Request");

            // Fixed Header
            byte[] fixedHeader =
            [
                (byte)PacketType.PINGREQ | 0b_0000,
                0
            ];

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(fixedHeader);
                stream!.Flush();
            }
            catch { }
        }

        /// <summary>
        /// Send Disconnect (DISCONNECT) message
        /// </summary>
        private async void SendDisconnect()
        {
            // Fixed Header
            byte[] fixedHeader =
            [
                (byte)PacketType.DISCONNECT | 0b_0000,
                0
            ];

            try
            {
                // Send the message and flush the stream
                await stream!.WriteAsync(fixedHeader);
                stream!.Flush();
            }
            catch { }
        }
    }
}
