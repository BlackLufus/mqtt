using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Packets
{
    public class PingReqPacket
    {
        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.PINGREQ;

            // Remaining Length
            int remainingLength = 0;

            // Encode
            byte[] data = new byte[] { fixedHeader, (byte)remainingLength };

            return data;
        }

        public static PingReqPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            return new PingReqPacket();
        }
    }
}
