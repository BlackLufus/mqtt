using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using mqtt.Client;

namespace mqtt.Packets
{
    public class PublishPacket(string topic, string message, QualityOfService qos = 0, bool retain = false, bool dup = false, int packetID = -1)
    {
        private static Dictionary<int, PublishPacket> pendingPackets = [];
        public static Dictionary<int, PublishPacket> PendingPackets => pendingPackets;

        private static int packetID = 0;
        public static int NextPacketID
        {
            get
            {
                packetID++;
                return packetID;
            }
        }

        // Fixed Header (DUP, QoS, Retain)
        public bool DUP { get; set; } = dup;
        public QualityOfService QoS { get; set; } = qos;
        public bool Retain { get; set; } = retain;

        // Variable Header (Topic, Packet ID)
        public string Topic { get; set; } = topic;
        public int PacketID { get; set; } = packetID;

        // Payload (Message)
        public string Message { get; set; } = message;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)((byte)(PacketType.PUBLISH) | ((DUP ? 1 : 0) << 3) | ((int)QoS << 1) | (Retain ? 1 : 0));

            // Variable Header
            byte[] topicBytes = Encoding.UTF8.GetBytes(Topic);
            byte[] topicLengthBytes = new byte[] { (byte)(topicBytes.Length >> 8), (byte)(topicBytes.Length & 0xFF) };
            byte[] packetIDBytes = new byte[] { (byte)(PacketID >> 8), (byte)(PacketID & 0xFF) };

            // Payload
            byte[] messageBytes = Encoding.UTF8.GetBytes(Message);

            // Remaining Length
            int remainingLength = topicBytes.Length + topicLengthBytes.Length + messageBytes.Length;
            if (QoS > 0)
            {
                remainingLength += 2;
            }

            // Get the remaining length value as a list of bytes
            List<byte> remainingLengthBytes = new List<byte>();
            int x = remainingLength;
            do
            {
                byte encodedByte = (byte)(x % 128);
                x /= 128;
                if (x > 0)
                {
                    encodedByte |= 0x80;
                }
                remainingLengthBytes.Add(encodedByte);
            } while (x > 0);


            // Encode
            byte[] data = new byte[1 + remainingLengthBytes.Count + remainingLength];
            data[0] = fixedHeader;
            Array.Copy(remainingLengthBytes.ToArray(), 0, data, 1, remainingLengthBytes.Count);
            int offset = 1 + remainingLengthBytes.Count;
            Array.Copy(topicLengthBytes, 0, data, offset, topicLengthBytes.Length);
            offset += topicLengthBytes.Length;
            Array.Copy(topicBytes, 0, data, offset, topicBytes.Length);
            offset += topicBytes.Length;
            if (QoS > 0)
            {
                Array.Copy(packetIDBytes, 0, data, offset, packetIDBytes.Length);
                offset += packetIDBytes.Length;
            }
            Array.Copy(messageBytes, 0, data, offset, messageBytes.Length);
            offset += messageBytes.Length;
                
            return data;

        }

        public static PublishPacket Decode(byte[] data)
        {
            Console.WriteLine(data[0] & 0x01);
            // Fixed Header
            byte fixedHeader = data[0];
            bool dup = (fixedHeader & 0x08) >> 3 == 1;
            QualityOfService qos = (QualityOfService)((fixedHeader & 0x06) >> 1);
            bool retain = (fixedHeader & 0x01) == 1;

            // Remaining Length
            int multiplier = 1;
            int remainingLength = 0;
            int offset = 1;
            byte currentByte;
            do
            {
                currentByte = data[offset];
                remainingLength += (currentByte & 0x7F) * multiplier;
                multiplier *= 128;
                offset++;
            } while ((currentByte & 0x80) != 0);

            // Variable Header (Topic Length, Topic, Packet ID)
            int topicLength = (data[offset] << 8) + data[offset + 1];
            string topic = Encoding.UTF8.GetString(data, offset + 2, topicLength);
            offset += 2 + topicLength;

            int packetID = -1;
            if (qos > 0)
            {
                packetID = (data[offset] << 8) + data[offset + 1];
                offset += 2;
            }

            // Berechne die Länge der Payload
            int payloadLength = remainingLength - (offset - 2);

            Debug.WriteLine("Remaining Length: " + remainingLength);
            Debug.WriteLine("Left: " + (data.Length - offset));
            Debug.WriteLine("Payload Length: " + payloadLength);

            // Payload (Message)
            string message = Encoding.UTF8.GetString(data, offset, payloadLength);

            return new PublishPacket(topic, message, qos, retain, dup, packetID);
        }
    }
}
