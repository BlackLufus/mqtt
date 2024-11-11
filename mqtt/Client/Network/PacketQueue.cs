using Mqtt.Client.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Network
{
    public class PacketQueue(int id, PacketType packetType, DateTime? timestamp, string topic, string? message, string? state)
    {
        public int Id { get; set; } = id;
        public PacketType PacketType { get; set; } = packetType;
        public DateTime? Timestamp { get; set; } = timestamp;
        public string Topic { get; set; } = topic;
        public string? Message { get; set; } = message;
        public string? State { get; set; } = state;
    }
}
