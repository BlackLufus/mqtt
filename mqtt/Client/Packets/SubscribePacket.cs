using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mqtt.Client;

namespace Mqtt.Client.Packets
{
    public class SubscribePacket(int packetId, Topic[] topics)
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
            byte fixedHeader = (byte)PacketType.SUBSCRIBE | 0b_0010;

            // Variable Header
            byte[] variableHeader = new byte[] { (byte)(PacketId >> 8), (byte)(PacketId & 0xFF) };

            // Payload
            List<byte> payload = new List<byte>();
            foreach (Topic topic in Topics)
            {
                byte[] topicBytes = Encoding.UTF8.GetBytes(topic.Name);
                byte[] topicLengthBytes = new byte[] { (byte)(topicBytes.Length >> 8), (byte)(topicBytes.Length & 0xFF) };
                // Topic Length
                payload.AddRange(topicLengthBytes);
                // Topic
                payload.AddRange(topicBytes);
                // QoS
                payload.AddRange(new byte[] { (byte)topic.QoS });
            }

            // QoS
            //byte[] qosArray = new byte[] { (byte)Qos };

            // Remaining Length
            int remainingLength = variableHeader.Length + payload.Count;// + qosArray.Length;

            // Encode
            byte[] data = new byte[2 + remainingLength];
            data[0] = fixedHeader;
            data[1] = (byte)remainingLength;
            Array.Copy(variableHeader, 0, data, 2, variableHeader.Length);
            Array.Copy(payload.ToArray(), 0, data, 4, payload.Count);
            //Array.Copy(qosArray, 0, data, 4 + payload.Count, qosArray.Length);

            return data;
        }

        public static SubscribePacket Decode(byte[] data)
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
            while (index < data.Length)
            {
                int topicLength = data[index] << 8 | data[index + 1];
                index += 2;
                string topicName = Encoding.UTF8.GetString(data, index, topicLength);
                index += topicLength;
                QualityOfService qos =  (QualityOfService)(data[index] & 0b_0000_0011);
                topics.Add(new Topic(topicName, qos));
            }

            // QoS
            //QualityOfService qos = (QualityOfService)data[data.Length - 1];

            return new SubscribePacket(packetID, topics.ToArray());
        }
    }
}
