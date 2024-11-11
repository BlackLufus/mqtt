using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Packets
{
    public class PubAckPacket(int packetID)
    {
        public int PacketID { get; } = packetID;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.PUBACK;

            // Remaining Length
            int remainingLength = 2;

            // Variable Header
            byte[] packetIDBytes = new byte[] { (byte)(PacketID >> 8), (byte)(PacketID & 0xFF) };

            // Encode
            byte[] data = [
                fixedHeader,
                (byte)remainingLength,
                packetIDBytes[0],
                packetIDBytes[1]
            ];

            return data;
        }

        public static PubAckPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            // Variable Header
            int packetID = data[2] << 8 | data[3];

            return new PubAckPacket(packetID);
        }
    }
}
