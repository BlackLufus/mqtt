using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Packets
{
    public class UnsubscribePacket(int packetID, string[] topics)
    {
        private static int packetID = 0;
        public static int NextPacketID
        {
            get
            {
                packetID++;
                return packetID;
            }
        }

        public int PacketID { get; } = packetID;
        public string[] Topics { get; } = topics;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.UNSUBSCRIBE | 0b_0010;

            // Variable Header
            byte[] packetIDBytes = new byte[] { (byte)(PacketID >> 8), (byte)(PacketID & 0xFF) };

            // Payload
            List<byte> payload = new List<byte>();
            foreach (string topic in Topics)
            {
                byte[] topicBytes = Encoding.UTF8.GetBytes(topic);
                byte[] topicLengthBytes = new byte[] { (byte)(topicBytes.Length >> 8), (byte)(topicBytes.Length & 0xFF) };
                payload.AddRange(topicLengthBytes);
                payload.AddRange(topicBytes);
            }

            // Remaining Length
            int remainingLength = packetIDBytes.Length + payload.Count;

            // Encode
            byte[] data = new byte[2 + remainingLength];
            data[0] = fixedHeader;
            data[1] = (byte)remainingLength;
            Array.Copy(packetIDBytes, 0, data, 2, packetIDBytes.Length);
            Array.Copy(payload.ToArray(), 0, data, 4, payload.Count);

            return data;
        }

        public static UnsubscribePacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            // Variable Header
            int packetID = data[2] << 8 | data[3];

            // Payload
            List<string> topics = new List<string>();
            int index = 4;
            while (index < remainingLength + 4)
            {
                int topicLength = data[index] << 8 | data[index + 1];
                string topic = Encoding.UTF8.GetString(data, index + 2, topicLength);
                topics.Add(topic);
                index += 2 + topicLength;
            }

            return new UnsubscribePacket(packetID, topics.ToArray());
        }
    }
}
