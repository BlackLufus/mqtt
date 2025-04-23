using Mqtt.Client.Packets;

namespace Mqtt.Client.Queue
{
    public class PendingPacket(ushort id, PacketType type, object? bin, PendingPacketType isSend)
    {
        public ushort ID { get; init; } = id;
        public PacketType PacketType { get; set; } = type;
        public object? Bin { get; set; } = bin;
        public PendingPacketType Type { get; init; } = isSend;

        public int Attempt { get; set; } = 0;

        public DateTime? SentAt { get; set; } = null;
        public TimeSpan TimeoutSeconds { get; set; } = TimeSpan.FromSeconds(10);
    }
}
