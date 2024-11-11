using mqtt.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Packets
{
    public class ConnectPacket(string clientID, string username = "", string password = "", LastWill? lastWill = null, bool willRetain = false, QualityOfService qos = QualityOfService.AT_MOST_ONCE, bool cleanSession = true, MqttVersion version = MqttVersion.MQTT_3_1_1, ushort keepAlive = 60, uint sessionExpiryInterval = 0)
    {
        public string ClientID { get; } = clientID;
        public string Username { get; } = username;
        public string Password { get; } = password;

        public LastWill LastWill { get; } = lastWill;
        public bool WillRetain { get; } = willRetain;
        public QualityOfService QoS { get; } = qos;

        public bool CleanSession { get; } = cleanSession;

        public MqttVersion Version { get; } = version;
        public ushort KeepAlive { get; } = keepAlive;

        public uint SessionExpiryInterval { get; } = sessionExpiryInterval;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.CONNECT;

            // Connect Flags
            byte connectFlags = 0b_0000_0000;

            // User Name Flag (Bit 7)
            // This bit specifies if a user name is present in the payload.
            if (Username.Length > 0)
            {
                connectFlags |= 1 << 7;

                // Password Flag (Bit 6)
                // This bit specifies if a password is used.
                if (Password.Length > 0)
                {
                    connectFlags |= 1 << 6;
                }
            }

            // Will Flag (Bit 2)
            // This bit specifies if the Will Flag is set.
            if (LastWill != null)
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
                (byte)Version,
                // Connect Flags
                connectFlags,
                // Keep Alive MSB
                (byte)(KeepAlive >> 8),
                // Keep Alive LSB
                (byte)(KeepAlive & 0xFF),
            ];

            if (Version == MqttVersion.MQTT_5)
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
                (byte)(ClientID.Length >> 8),
                (byte)(ClientID.Length & 0xFF)
            ];
            //byte[] payload = new byte[2 + clientId.Length];
            payload.AddRange(Encoding.UTF8.GetBytes(ClientID));
            //Array.Copy(Encoding.UTF8.GetBytes(clientId), 0, payload, 2, clientId.Length);

            if (LastWill != null)
            {
                Console.WriteLine("Set Will Flag");
                byte[] topicArray = new byte[2 + LastWill.Topic.Length];
                topicArray[0] = (byte)(LastWill.Topic.Length >> 8);
                topicArray[1] = (byte)(LastWill.Topic.Length & 0xFF);
                Array.Copy(Encoding.UTF8.GetBytes(LastWill.Topic), 0, topicArray, 2, LastWill.Topic.Length);
                payload.AddRange(topicArray);

                byte[] messageArray = new byte[2 + LastWill.Message.Length];
                messageArray[0] = (byte)(LastWill.Message.Length >> 8);
                messageArray[1] = (byte)(LastWill.Message.Length & 0xFF);
                Array.Copy(Encoding.UTF8.GetBytes(LastWill.Message), 0, messageArray, 2, LastWill.Message.Length);
                payload.AddRange(messageArray);
            }

            if (Username.Length > 0)
            {
                Console.WriteLine("Set Username Flag");
                byte[] usernameArray = new byte[2 + Username!.Length];
                usernameArray[0] = (byte)(Username.Length >> 8);
                usernameArray[1] = (byte)(Username.Length & 0xFF);
                Array.Copy(Encoding.UTF8.GetBytes(Username), 0, usernameArray, 2, Username.Length);
                payload.AddRange(usernameArray);

                if (Password.Length > 0)
                {
                    byte[] passwordArray = new byte[2 + Password!.Length];
                    passwordArray[0] = (byte)(Password.Length >> 8);
                    passwordArray[1] = (byte)(Password.Length & 0xFF);
                    Array.Copy(Encoding.UTF8.GetBytes(Password), 0, passwordArray, 2, Password.Length);
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

            // Merge all arrays
            byte[] data = new byte[2 + header.Count + payload.Count];
            data[0] = fixedHeader;
            data[1] = (byte)remainingLength;
            Array.Copy(header.ToArray(), 0, data, 2, header.Count);
            Array.Copy(payload.ToArray(), 0, data, 2 + header.Count, payload.Count);

            Console.WriteLine("Connect Packet: " + header.Count);
            for (int i = 0; i < header.Count; i++)
            {
                Console.WriteLine(Convert.ToString(header[i], 2).PadLeft(8, '0'));
            }
            Console.WriteLine("=====================================");

            return data;
        }
    }
}
