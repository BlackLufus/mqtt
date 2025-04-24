using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mqtt.Client;

namespace Mqtt.Client.Packets
{
    public class PublishPacket(ushort? id, string topic, string message, QualityOfService qos = 0, bool retain = false, bool dup = false)
    {
        // Fixed Header (DUP, QoS, Retain)
        public bool DUP { get; set; } = dup;
        public QualityOfService QoS { get; set; } = qos;
        public bool Retain { get; set; } = retain;

        // Variable Header (Topic, Packet ID)
        public string Topic { get; set; } = topic;
        public ushort PacketID { get; set; } = (ushort)(id.HasValue ? id : PacketIdHandler.GetFreeId());

        // Payload (Message)
        public string Message { get; set; } = message;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)((byte)PacketType.PUBLISH | (DUP ? 1 : 0) << 3 | (int)QoS << 1 | (Retain ? 1 : 0));

            Console.WriteLine(fixedHeader.ToString());

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
            int offset = 0;

            // Fixed Header
            byte fixedHeader = data[0];
            bool dup = (fixedHeader & 0x08) >> 3 == 1;
            QualityOfService qos = (QualityOfService)((fixedHeader & 0x06) >> 1);
            bool retain = (fixedHeader & 0x01) == 1;
            offset += 1;


            // Remaining Length
            int multiplier = 1;
            int remainingLength = 0;
            byte currentByte;
            do
            {
                currentByte = data[offset];
                remainingLength += (currentByte & 127) * multiplier;
                multiplier *= 128;
                if (multiplier > 128 * 128 * 128)
                    throw new Exception("Malformed Remining Length");
                offset += 1;
            } while ((currentByte & 0x80) != 0);

            int startOffset = offset;


            // Variable Header (Topic Length, Topic, Packet ID)
            int topicLength = (data[offset] << 8) | data[offset + 1];
            offset += 2;

            // Topic
            string topic = Encoding.UTF8.GetString(data, offset, topicLength);
            offset += topicLength;


            // Packet id
            ushort packetID = 0;
            if (qos > 0)
            {
                packetID = (ushort)((data[offset] << 8) | data[offset + 1]);
                offset += 2;
            }


            // Length for header
            int headerLength = offset - startOffset;

            // Length for Payload (Message)
            int payloadLength = value - headerLength;

            //Payload (Message)
            string message = Encoding.UTF8.GetString(data, offset, payloadLength);
            offset += payloadLength;

            return new PublishPacket(packetID, topic, message, qos, retain, dup);
        }
    }
}
