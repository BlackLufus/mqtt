using Mqtt.Client.ReasonCode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Packets
{
    public class SubAckPacket(int packetID, SubAckReasonCode[] returnCodes)
    {
        public int PacketID { get; } = packetID;

        public SubAckReasonCode[] ReturnCodes { get; } = returnCodes;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.SUBACK;

            // Remaining Length
            int remainingLength = 2 + 2 + ReturnCodes.Length;

            // Variable Header (Packet ID)
            byte[] packetIDBytes = new byte[] { (byte)(PacketID >> 8), (byte)(PacketID & 0xFF) };

            // Encode (Fixed Header + Remaining Length + Variable Header + Payload)
            byte[] data = new byte[remainingLength];
            data[0] = fixedHeader;
            data[1] = (byte)remainingLength;
            data[2] = packetIDBytes[0];
            data[3] = packetIDBytes[1];
            Array.Copy(ReturnCodes, 0, data, 4, ReturnCodes.Length);

            return data;
        }

        public static SubAckPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            // Variable Header (Packet ID)
            int packetID = data[2] << 8 | data[3];

            // Payload
            SubAckReasonCode[] returnCodes = new SubAckReasonCode[remainingLength - 2];
            Array.Copy(data, 4, returnCodes, 0, returnCodes.Length);

            return new SubAckPacket(packetID, returnCodes);
        }
    }
}
