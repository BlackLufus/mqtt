using Mqtt.Core.ReasonCode;

namespace Mqtt.Core.Packets
{
    public class DisconnectPacket(DisconnectReasionCode? reasonCode = null)
    {
        public DisconnectReasionCode? ReasonCode { get; } = reasonCode;

        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.DISCONNECT;

            // Remaining Length
            int remainingLength = 0;

            // Encode
            byte[] data = new byte[] { fixedHeader, (byte)remainingLength };

            return data;
        }

        public static DisconnectPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            return new DisconnectPacket();
        }
    }
}
