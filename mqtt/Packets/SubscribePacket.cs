using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using mqtt.Options;

namespace mqtt.Packets
{
    public class SubscribePacket (int packetID, List<string> topics, QualityOfService qos)
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

        public int PacketId { get; } = packetID;

        public List<string> Topics { get; } = topics;

        public QualityOfService Qos { get; } = qos;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.SUBSCRIBE | 0b_0010;

            // Variable Header
            byte[] variableHeader = new byte[] { (byte)(PacketId >> 8), (byte)(PacketId & 0xFF) };

            // Payload
            List<byte> payload = new List<byte>();
            foreach (string topic in Topics)
            {
                byte[] topicBytes = Encoding.UTF8.GetBytes(topic);
                byte[] topicLengthBytes = new byte[] { (byte)(topicBytes.Length >> 8), (byte)(topicBytes.Length & 0xFF) };
                payload.AddRange(topicLengthBytes);
                payload.AddRange(topicBytes);
            }

            // QoS
            byte[] qosArray = new byte[] { (byte)Qos };

            // Remaining Length
            int remainingLength = variableHeader.Length + payload.Count + qosArray.Length;

            // Encode
            byte[] data = new byte[2 + remainingLength];
            data[0] = fixedHeader;
            data[1] = (byte)remainingLength;
            Array.Copy(variableHeader, 0, data, 2, variableHeader.Length);
            Array.Copy(payload.ToArray(), 0, data, 4, payload.Count);
            Array.Copy(qosArray, 0, data, 4 + payload.Count, qosArray.Length);

            return data;
        }

        public static SubscribePacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            // Variable Header
            int packetID = (data[2] << 8) | data[3];

            // Payload
            List<string> topics = new List<string>();
            int index = 4;
            while (index < data.Length)
            {
                int topicLength = (data[index] << 8) | data[index + 1];
                string topic = Encoding.UTF8.GetString(data, index + 2, topicLength);
                topics.Add(topic);
                index += 2 + topicLength;
            }

            // QoS
            QualityOfService qos = (QualityOfService)data[data.Length - 1];

            return new SubscribePacket(packetID, topics, qos);
        }
    }
}
