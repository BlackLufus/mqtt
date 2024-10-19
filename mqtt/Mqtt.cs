using System;
using System.Collections.Generic;
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
        public enum PacketType
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
            DISCONNECT = 0xE0,
            Reserved = 0xF0
        }

        public enum QoS
        {
            AT_MOST_ONCE = 0,
            AT_LEAST_ONCE = 1,
            EXACTLY_ONCE = 2
        }

        public enum ConnectReturnCode
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

        private int messageId = 1;

        public delegate void MessageReceivedDelegate(string topic, string message);
        public event MessageReceivedDelegate MessageReceived;

        public delegate void ConnectionLostDelegate();
        public event ConnectionLostDelegate ConnectionLost;

        public bool UserNameFlag { get; set; } = false;
        public bool PasswordFlag { get; set; } = false;
        public bool WillRetain { get; set; } = false;
        public QoS WillQoS { get; set; } = QoS.EXACTLY_ONCE;
        public bool WillFlag { get; set; } = false;
        public bool CleanSession { get; set; } = false;
        public int KeepAlive { get; set; } = 10;

        private bool dupFlag = false;

        private TcpClient client;
        private NetworkStream stream;

        private void Response(byte[] response)
        {
            Console.WriteLine("Response: " + "(" + response.Length + ")");
            for (int i = 0; i < response.Length; i++)
            {
                if (i == 0)
                {
                    Console.WriteLine((PacketType)response[i]);
                }
                else if (i == 1)
                {
                    Console.WriteLine("Remaining Length: " + (int)response[i]);
                }
                else if (i == 2)
                {
                    Console.WriteLine("Connect Acknowledge Flags ");
                }
                else
                {
                    Console.WriteLine("Return Code: " + (ConnectReturnCode)response[i]);
                }
                Console.WriteLine(Convert.ToString(response[i], 2).PadLeft(8, '0'));
            }
        }
        public void Connect(string brokerAddress, int port, string clientID)
        {
            client = new TcpClient(brokerAddress, port);
            stream = client.GetStream();

            // Erstelle eine MQTT CONNECT Nachricht (simplifizierte Version)
            byte[] connectMessage = CreateConnectMessage(clientID);
            stream.Write(connectMessage, 0, connectMessage.Length);

            // Lies die Antwort des Brokers (CONNACK)
            byte[] response = new byte[4];
            stream.Read(response, 0, response.Length);
            Response(response);

            stream.Flush();
        }

        private byte[] CreateConnectMessage(string clientID)
        {
            byte[] protocolName = Encoding.UTF8.GetBytes("MQTT");
            byte protocolVersion = 4;
            byte[] clientIdBytes = Encoding.UTF8.GetBytes(clientID);

            // Fixed Header: 1 Byte
            byte[] fixedHeader = {
                // Packet Type
                (byte)PacketType.CONNECT,
                // Remaining Length
                10
            };
            Console.WriteLine("Fixed Header: " + "(" + fixedHeader.Length + ")");
            for (int i = 0; i < fixedHeader.Length; i++)
            {
                Console.WriteLine(Convert.ToString(fixedHeader[i], 2).PadLeft(8, '0'));
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
                0b_0000_0010,//GetConnectFlags(),
                // Keep Alive MSB
                (byte)(KeepAlive >> 8),
                // Keep Alive LSB
                (byte)(KeepAlive & 0xFF),
            ];
            Console.WriteLine("Header: " + "(" + header.Length + ")");
            for (int i = 0; i < header.Length; i++)
            {
                Console.WriteLine(Convert.ToString(header[i], 2).PadLeft(8, '0'));
            }

            byte[] payload = new byte[2 + clientIdBytes.Length];
            payload[0] = (byte)(clientIdBytes.Length >> 8);
            payload[1] = (byte)(clientIdBytes.Length & 0xFF);
            Array.Copy(clientIdBytes, 0, payload, 2, clientIdBytes.Length);

            Console.WriteLine("Payload: " + "(" + payload.Length + ")");
            for (int i = 0; i < payload.Length; i++)
            {
                Console.WriteLine(Convert.ToString(payload[i], 2).PadLeft(8, '0'));
            }

            int remainingLength = header.Length + payload.Length;
            fixedHeader[1] = (byte)remainingLength;

            byte[] result = new byte[fixedHeader.Length + header.Length + payload.Length];
            Array.Copy(fixedHeader, 0, result, 0, fixedHeader.Length);
            Array.Copy(header, 0, result, fixedHeader.Length, header.Length);
            Array.Copy(payload, 0, result, fixedHeader.Length + header.Length, payload.Length);
            Console.WriteLine("Result: " + "(" + result.Length + ")");

            return result;
        }

        private byte GetConnectFlags()
        {
            byte connectFlags = 0b_0000_0000;

            // Setze das User Name Flag (Bit 7)
            if (UserNameFlag)
            {
                connectFlags |= (1 << 7);
            }

            // Setze das Password Flag (Bit 6)
            if (PasswordFlag)
            {
                connectFlags |= (1 << 6);
            }

            // Setze das Will Retain Flag (Bit 5)
            if (WillRetain)
            {
                connectFlags |= (1 << 5);
            }

            // Setze das Will QoS (Bits 4 und 3)
            if (WillFlag)
            {
                // WillQoS muss in den Bits 3 und 4 gesetzt werden
                // Hier erfolgt das Setzen basierend auf dem Wert von WillQoS (0, 1 oder 2)
                connectFlags |= (byte)((int)WillQoS << 3); // Maske mit 0x03 stellt sicher, dass nur die unteren 2 Bits genutzt werden

                // Setze das Will Flag (Bit 2)
                connectFlags |= (1 << 2);
            }

            // Setze das Clean Session Flag (Bit 1)
            if (CleanSession)
            {
                connectFlags |= (1 << 1);
            }

            // Das LSB (Bit 0) ist immer 0 und muss nicht gesetzt werden.
            return connectFlags;
        }

        public void Publish(string topic, string message)
        {
            Console.WriteLine("");
            Console.WriteLine("Publishing message to topic: " + topic);
            Console.WriteLine("Message: " + message);
            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)((byte)(PacketType.PUBLISH) | (dupFlag ? (1 << 3) : 0) | ((int)WillQoS << 1) | (WillRetain ? 1 : 0)),
                0
            };

            Console.WriteLine(2);
            Console.WriteLine(topic.Length);
            Console.WriteLine(message.Length);
            Console.WriteLine(WillQoS > (int)QoS.AT_MOST_ONCE);
            Console.WriteLine((QoS)WillQoS);
            byte[] payload = new byte[2 + topic.Length + message.Length + (WillQoS != QoS.AT_MOST_ONCE ? 2 : 0)];
            payload[0] = (byte)(topic.Length >> 8);
            payload[1] = (byte)(topic.Length & 0xFF);
            Array.Copy(Encoding.UTF8.GetBytes(topic), 0, payload, 2, topic.Length);
            if (WillQoS != QoS.AT_MOST_ONCE)
            {
                Console.WriteLine("Message ID: " + messageId);
                payload[2 + topic.Length] = (byte)(messageId >> 8);
                payload[3 + topic.Length] = (byte)(messageId++ & 0xFF);
            }
            Array.Copy(Encoding.UTF8.GetBytes(message), 0, payload, 2 + topic.Length + (WillQoS != QoS.AT_MOST_ONCE ? 2 : 0), message.Length);

            int remainingLength = payload.Length;
            fixedHeader[1] = (byte)remainingLength;

            byte[] result = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, result, 0, fixedHeader.Length);
            Array.Copy(payload, 0, result, fixedHeader.Length, payload.Length);

            for (int i = 0; i < result.Length; i++)
            {
                Console.WriteLine(Convert.ToString(result[i], 2).PadLeft(8, '0'));
            }

            stream.Write(result, 0, result.Length);

            stream.Flush();

            if (WillQoS != QoS.AT_MOST_ONCE)
            {
                byte[] response = new byte[4];
                stream.Read(response, 0, response.Length);
                Response(response);

                if (WillQoS == QoS.EXACTLY_ONCE)
                {
                    Console.WriteLine("WillQoS == QoS.EXACTLY_ONCE");
                    byte[] fixedHeaderPubRec =
                    {
                        (byte)PacketType.PUBREC,
                        2
                    };

                    byte[] payloadPubRec = {
                        response[1],
                        response[2],
                    };
                    byte[] pubRecMessage = new byte[fixedHeaderPubRec.Length + payloadPubRec.Length];

                    Array.Copy(fixedHeaderPubRec, 0, pubRecMessage, 0, fixedHeaderPubRec.Length);
                    Array.Copy(payloadPubRec, 0, pubRecMessage, fixedHeaderPubRec.Length, payloadPubRec.Length);

                    stream.Write(pubRecMessage, 0, pubRecMessage.Length);

                    Console.WriteLine("PUBREC");

                    byte[] responsePubComp = new byte[4];
                    stream.Read(responsePubComp, 0, responsePubComp.Length);
                    Response(responsePubComp);

                    stream.Flush();
                }
            }

        }

        byte[] EncodeRemainingLength(int length)
        {
            List<byte> encodedBytes = new List<byte>();
            do
            {
                byte digit = (byte)(length % 128);
                length /= 128;
                // if there are more digits to encode, set the top bit of this digit
                if (length > 0)
                {
                    digit |= 0x80;
                }
                encodedBytes.Add(digit);
            } while (length > 0);

            return encodedBytes.ToArray();
        }


        public void Subscribe(string topic)
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe(string topic)
        {
            throw new NotImplementedException();
        }

        public void Disconnect()
        {
            if (client != null)
            {
                stream.Close();
                client.Close();
            }
        }
    }
}
