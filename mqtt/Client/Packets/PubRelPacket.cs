using Mqtt.Client.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Packets
{
    public class PubRelPacket(ushort packetID)
    {
        public ushort PacketID { get; } = packetID;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.PUBREL | 0b_0010;

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

        public static PubRelPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            // Variable Header
            ushort packetID = (ushort)(data[2] << 8 | data[3]);

            return new PubRelPacket(packetID);
        }
    }
}
