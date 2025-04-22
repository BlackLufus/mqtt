using Mqtt.Client.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Queue
{
    public class PendingPacket
    {
        public ushort Id { get; init; }
        public PacketType PacketType { get; set; }
        public object? Bin { get; init; }
        public bool IsClientMessage { get; init; }

        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; } = null;
        public TimeSpan TimeoutSeconds { get; set; } = TimeSpan.FromSeconds(5);

        public MessageStatus Status { get; set; } = MessageStatus.Queued;

        public PendingPacket(ushort id, PacketType packetType, object? packet, bool isClientMessage = true)
        {
            Id = id;
            PacketType = packetType;
            Bin = packet;
            IsClientMessage = isClientMessage;
        }

        public bool IsTimedOut => SentAt == null || (DateTime.UtcNow - SentAt.Value) > TimeoutSeconds;

        public void Update(PacketType packetType)
        {
            PacketType = packetType;
            SentAt = null;
        }

        public void Dispose()
        {
            if (IsClientMessage)
            {
                PacketIdHandler.FreeId(Id);
            }
        }
    }
}
