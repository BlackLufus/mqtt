namespace Mqtt.Core.Packets
{
    public class PacketIdHandler
    {
        private static readonly Queue<ushort> freeIds = new(
            Enumerable.Range(1, ushort.MaxValue).Select(i => (ushort)i)
        );

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
