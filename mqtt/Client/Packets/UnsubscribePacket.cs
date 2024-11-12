using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Packets
{
    public class UnsubscribePacket(int packetId, Topic[] topics)
    {
        private static int packetId = 0;
        public static int NextPacketId
        {
            get
            {
                packetId++;
                return packetId;
            }
        }

        public int PacketId { get; } = packetId;
        public Topic[] Topics { get; } = topics;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.UNSUBSCRIBE | 0b_0010;

            // Variable Header
            byte[] packetIDBytes = new byte[] { (byte)(PacketId >> 8), (byte)(PacketId & 0xFF) };

            // Payload
            List<byte> payload = new List<byte>();
            foreach (Topic topic in Topics)
            {
                byte[] topicNameBytes = Encoding.UTF8.GetBytes(topic.Name);
                byte[] topicLengthBytes = new byte[] { (byte)(topicNameBytes.Length >> 8), (byte)(topicNameBytes.Length & 0xFF) };
                payload.AddRange(topicLengthBytes);
                payload.AddRange(topicNameBytes);
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
            List<Topic> topics = new List<Topic>();
            int index = 4;
            while (index < remainingLength + 4)
            {
                int topicLength = data[index] << 8 | data[index + 1];
                index += 2;
                string topicName = Encoding.UTF8.GetString(data, index + 2, topicLength);
                index += 2 + topicLength;
                topics.Add(new Topic(topicName));
            }

            return new UnsubscribePacket(packetID, topics.ToArray());
        }
    }
}
