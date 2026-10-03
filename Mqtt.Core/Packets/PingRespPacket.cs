namespace Mqtt.Core.Packets
{
    public class PingRespPacket
    {
        public byte[] Encode()
        {
            // Fixed Header
            byte fixedHeader = (byte)PacketType.PINGRESP;

            // Remaining Length
            int remainingLength = 0;

            // Encode
            byte[] data = new byte[] { fixedHeader, (byte)remainingLength };

            return data;
        }

        public static PingRespPacket Decode(byte[] data)
        {
            // Fixed Header
            byte fixedHeader = data[0];

            // Remaining Length
            int remainingLength = data[1];

            return new PingRespPacket();
        }
    }
}
