using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Packets
{
    public class PacketIdHandler
    {
        private static readonly Queue<ushort> freeIds = new(Enumerable.Range(1, ushort.MaxValue).Select(i => (ushort)i));

        public static void FreeId(ushort id)
        {
            freeIds.Enqueue(id);
        }

        public static ushort GetFreeId()
        {
            return freeIds.Dequeue();
        }


    }
}
