using mqtt.ReasonCode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Packets
{
    public class ConnAckPacket(bool sessionPresent, ConnectReturnCode returnCode)
    {
        // Variable Header (Session Present, Return Code)
        public bool SessionPresent { get; } = sessionPresent;
        public ConnectReturnCode ReturnCode { get; } = returnCode;

        public byte[] Encode()
        {
            byte[] data = [
                (byte)PacketType.CONNACK,
                2,
                (byte)(SessionPresent ? 1 : 0),
                (byte)ReturnCode
            ];

            return data;
        }

        public static ConnAckPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            // Variable Header
            bool sessionPresent = (data[2] & 0x01) == 1;

            ConnectReturnCode returnCode = (ConnectReturnCode)data[3];

            return new ConnAckPacket(sessionPresent, returnCode);
        }
    }
}
