using Mqtt.Client.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Network
{
    public class QueuePacket(int id, PacketType packetType, object? packet)
    {
        public int Id { get; set; } = id;
        public PacketType PacketType { get; set; } = packetType;
        public DateTime? Timestamp { get; set; } = null;
        public object? Packet { get; set; } = packet;
        public int Timeout { get; set; } = 5;
        public bool IsAcknowledged { get; set; } = false;
    }
}
