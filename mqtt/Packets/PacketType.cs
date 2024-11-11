using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Packets
{
    /// <summary>
    /// Enum for the Packet Type
    /// </summary>
    public enum PacketType
    {
        CONNECT = 0x10,
        CONNACK = 0x20,
        PUBLISH = 0x30,
        PUBACK = 0x40,
        PUBREC = 0x50,
        PUBREL = 0x60,
        PUBCOMP = 0x70,
        SUBSCRIBE = 0x80,
        SUBACK = 0x90,
        UNSUBSCRIBE = 0xA0,
        UNSUBACK = 0xB0,
        PINGREQ = 0xC0,
        PINGRESP = 0xD0,
        DISCONNECT = 0xE0
    }
}
