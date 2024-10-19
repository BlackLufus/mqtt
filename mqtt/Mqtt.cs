using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
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

        /// <summary>
        /// Message received event
        /// </summary>
        /// <param name="topic"> The topic </param>
        /// <param name="message"> The message </param>
        public delegate void MessageReceivedDelegate(string topic, string message);
        public event MessageReceivedDelegate? MessageReceived;

        /// <summary>
        /// Connection lost event
        /// </summary>
        public delegate void ConnectionLostDelegate();
        public event ConnectionLostDelegate? ConnectionLost;

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
        private Timer? pingTimer;

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

            // Starte den Listener für eingehende Pakete
            Task.Run(() => IncomingPacketListener());

            Task.Delay(100).Wait();

            // Erstelle eine MQTT CONNECT Nachricht (simplifizierte Version)
            byte[] connectMessage = CreateConnectMessage(clientID, username, password);
            stream.Write(connectMessage, 0, connectMessage.Length);

            // Start the ping timer
            StartPingTimer(KeepAlive);

            // Wait until the connection is established or the connection is closed
            while (!isConnected && !connectionClosed)
            {
                await Task.Delay(50);
            }
        }

        /// <summary>
        /// Create a CONNECT message for the MQTT protocol
        /// </summary>
        /// <param name="clientId"> The client ID </param>
        /// <returns> The CONNECT message </returns>
        private byte[] CreateConnectMessage(string clientId, string username = "", string password = "")
        {
            byte protocolVersion = 4;

            // Fixed Header: 1 Byte
            byte[] fixedHeader = {
                // Packet Type
                (byte)PacketType.CONNECT,
                // Remaining Length
                10
            };

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
            byte[] header =
            [
                // Length MSB (0)
                0x00,
                // Length LSB (4)
                0x04,
                // 'MQTT'
                (byte)'M', (byte)'Q', (byte)'T', (byte)'T',
                // Version
                protocolVersion,
                // Connect Flags
                connectFlags,
                // Keep Alive MSB
                (byte)(KeepAlive >> 8),
                // Keep Alive LSB
                (byte)(KeepAlive & 0xFF),
            ];

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
            for (int i = 0; i < payload.Count; i++)
            {
                Console.WriteLine(Convert.ToString(payload[i], 2).PadLeft(8, '0'));
            }

            Console.WriteLine(Convert.ToString(connectFlags, 2).PadLeft(8, '0'));

            // Set the remaining length and set it in the fixed header
            int remainingLength = header.Length + payload.Count;
            fixedHeader[1] = (byte)remainingLength;

            // Merge all arrays
            byte[] result = new byte[fixedHeader.Length + header.Length + payload.Count];
            Array.Copy(fixedHeader, 0, result, 0, fixedHeader.Length);
            Array.Copy(header, 0, result, fixedHeader.Length, header.Length);
            Array.Copy(payload.ToArray(), 0, result, fixedHeader.Length + header.Length, payload.Count);

            /*for (int i = 0; i < result.Length; i++)
            {
                Console.WriteLine(Convert.ToString(result[i], 2).PadLeft(8, '0'));
            }*/

            return result;
        }

        /// <summary>
        /// Publish a message to a topic
        /// </summary>
        /// <param name="topic"> The topic to publish to </param>
        /// <param name="message"> The message to publish </param>
        public void Publish(string topic, string message)
        {
            // Need to implement!
            // It indicates that the message is a duplicate of a previously sent message
            bool dupFlag = false;

            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)((byte)(PacketType.PUBLISH) | (dupFlag ? (1 << 3) : 0) | ((int)QoS << 1) | (WillRetain ? 1 : 0)),
                0
            };

            // Payload
            byte[] payload = new byte[2 + topic.Length + message.Length + (QoS != QualityOfService.AT_MOST_ONCE ? 2 : 0)];
            payload[0] = (byte)(topic.Length >> 8);
            payload[1] = (byte)(topic.Length & 0xFF);
            Array.Copy(Encoding.UTF8.GetBytes(topic), 0, payload, 2, topic.Length);

            // Set the message ID if QoS > 0
            if (QoS != QualityOfService.AT_MOST_ONCE)
            {
                payload[2 + topic.Length] = (byte)(messagePacketId >> 8);
                payload[3 + topic.Length] = (byte)(messagePacketId++ & 0xFF);
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

            // Send the message and flush the stream
            stream!.Write(result, 0, result.Length);
        }

        /// <summary>
        /// Subscribe to a topic
        /// </summary>
        /// <param name="topic"> The topic to subscribe to </param>
        public void Subscribe(string topic)
        {
            // Fixed Header
            byte[] fixedHeader = {
                (byte)PacketType.SUBSCRIBE | 0b_0010,
                (byte)(2 + 2 + topic.Length + 1),
                (byte)(subscribePacketId >> 8),
                (byte)(subscribePacketId++ & 0b_1111_1111)
            };

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

            // Send the message and flush the stream
            stream!.Write(subscribe, 0, subscribe.Length);
        }

        /// <summary>
        /// Unsubscribe from a topic
        /// </summary>
        /// <param name="topic"> The topic to unsubscribe from </param>
        public void Unsubscribe(string topic)
        {
            // Fixed Header
            byte[] bytes = {
                (byte)PacketType.UNSUBSCRIBE | 0b_0010,
                0
            };

            // Variable Header
            byte[] variableHeader =
            {
                (byte)(unsubscribePacketId >> 8),
                (byte)(unsubscribePacketId++ & 0xFF)
            };

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

            // Send the message and flush the stream
            stream!.Write(unsubscribe, 0, unsubscribe.Length);
            stream!.Flush();
        }

        /// <summary>
        /// Close the stream and the connection to the broker
        /// </summary>
        public void Disconnect()
        {
            CloseConnection();
        }

        /// <summary>
        /// Close the connection
        /// </summary>
        private void CloseConnection()
        {
            if (connectionClosed)
            {
                return;
            }
            connectionClosed = true;

            pingTimer?.Dispose();

            // Close the stream
            if (stream != null)
            {
                SendDisconnect();
                stream.Close();
            }

            // Disconnect the client
            client?.Close();

            // Invoke the ConnectionLost event
            ConnectionLost?.Invoke();
        }

        /// <summary>
        /// Start the ping timer
        /// </summary>
        /// <param name="keepAliveInterval"> The keep alive interval </param>
        private void StartPingTimer(int keepAliveInterval)
        {
            pingTimer = new Timer((state) =>
            {
                SendPingReq();
            }, null, keepAliveInterval * 1000 / 2, keepAliveInterval * 1000 / 2);
        }

        /// <summary>
        /// Incoming Packet Listener
        /// </summary>
        /// <returns> Task </returns>
        private async Task IncomingPacketListener()
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
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                    // Handle the incoming packet
                    HandleIncomingPacket(buffer);
                } while (bytesRead > 0);
            }
            catch (Exception e)
            {
                if (!connectionClosed)
                {
                    // Handle the exception
                    Console.WriteLine("Exception: " + e.Message);
                }
            }
            finally
            {
                CloseConnection();
            }
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
                    MessageReceived?.Invoke(topic, message);
                    break;
                case 1: // QoS 1 - "At least once"
                    SendPubAck((byte)(packetId >> 8), (byte)(packetId & 0xFF));
                    MessageReceived?.Invoke(topic, message);
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
            MessageReceived?.Invoke(packetMap[packetId].Item1, packetMap[packetId].Item2);

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
            bool Failure = ((0b_1000_000 & packet[4]) >> 7) == 1 ? true : false;

            // QoS Level
            int QoSLevel = (0b_0000_0011 & packet[4]);

            Console.WriteLine("Failure: " + Failure);
            Console.WriteLine("QoS Level: " + QoSLevel);
        }

        /// <summary>
        /// Handle Unsubscribe acknowledgement (UNSUBACK) message
        /// </summary>
        /// <param name="packet"> The received packet </param>
        private void HandleUnsubAck(byte[] packet)
        {
            // ToDo: Implement
            // No Implementation needed [Server specific]
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
            Disconnect();
        }

        /// <summary>
        /// Send Publish Acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubAck(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)PacketType.PUBACK | 0b_0000,
                2
            };

            // Payload
            byte[] payload = {
                MSB,
                LSB
            };

            // Merge all arrays
            byte[] pubAck = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubAck, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubAck, fixedHeader.Length, payload.Length);

            // Send the message and flush the stream
            stream?.Write(pubAck, 0, pubAck.Length);
            stream?.Flush();
        }

        /// <summary>
        /// Send Publish Received (PUBREC) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubRec(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)PacketType.PUBREC | 0b_0000,
                2
            };

            // Payload
            byte[] payload = {
                MSB,
                LSB
            };

            // Merge all arrays
            byte[] pubRec = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubRec, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubRec, fixedHeader.Length, payload.Length);

            // Send the message and flush the stream
            stream?.Write(pubRec, 0, pubRec.Length);
            stream?.Flush();
        }

        /// <summary>
        /// Send Publish Release (PUBREL) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubRel(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)PacketType.PUBREL | 0b_0010,
                2
            };

            // Payload
            byte[] payload = {
                MSB,
                LSB
            };

            // Merge all arrays
            byte[] pubRel = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubRel, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubRel, fixedHeader.Length, payload.Length);

            // Send the message and flush the stream
            stream?.Write(pubRel, 0, pubRel.Length);
            stream?.Flush();
        }

        /// <summary>
        /// Send Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        private void SendPubComp(byte MSB, byte LSB)
        {
            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)PacketType.PUBCOMP | 0b_0000,
                2
            };

            // Payload
            byte[] payload = {
                MSB,
                LSB
            };

            // Merge all arrays
            byte[] pubComp = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, pubComp, 0, fixedHeader.Length);
            Array.Copy(payload, 0, pubComp, fixedHeader.Length, payload.Length);

            // Send the message and flush the stream
            stream?.Write(pubComp, 0, pubComp.Length);
            stream?.Flush();
        }

        /// <summary>
        /// Send Ping Request (PINGREQ) message
        /// </summary>
        private void SendPingReq()
        {
            Console.WriteLine("Send Ping Request");

            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)PacketType.PINGREQ | 0b_0000,
                0
            };

            // Send the message and flush the stream
            stream?.Write(fixedHeader, 0, fixedHeader.Length);
            stream?.Flush();
        }

        /// <summary>
        /// Send Disconnect (DISCONNECT) message
        /// </summary>
        private void SendDisconnect()
        {
            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)PacketType.DISCONNECT | 0b_0000,
                0
            };

            // Send the message and flush the stream
            stream?.Write(fixedHeader, 0, fixedHeader.Length);
            stream?.Flush();
        }
    }
}
