using Mqtt.Client.ReasonCode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Packets
{
    public class UnSubAckPacket(ushort packetID)
    {
        public ushort PacketID { get; } = packetID;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.UNSUBACK;

            // Remaining Length
            int remainingLength = 2;

            // Variable Header (Packet ID)
            byte[] packetIDBytes = new byte[] { (byte)(PacketID >> 8), (byte)(PacketID & 0xFF) };

            // Encode (Fixed Header + Remaining Length + Variable Header + Payload)
            byte[] data = new byte[remainingLength];
            data[0] = fixedHeader;
            data[1] = (byte)remainingLength;
            data[2] = packetIDBytes[0];
            data[3] = packetIDBytes[1];

            return data;
        }

        public static UnSubAckPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            // Variable Header (Packet ID)
            ushort packetID = (ushort)(data[2] << 8 | data[3]);

            return new UnSubAckPacket(packetID);
        }
    }
}
