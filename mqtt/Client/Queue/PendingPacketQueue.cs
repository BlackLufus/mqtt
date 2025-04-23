using Mqtt.Client.Network;
using Mqtt.Client.Packets;
using System.Diagnostics;

namespace Mqtt.Client.Queue2
{
    public class PendingPacketQueue
    {
        private CancellationTokenSource? cts;

        private readonly Queue<PendingPacket> pendingPacketQueue = [];
        private readonly Dictionary<(PendingPacketType, ushort), PendingPacket> pendingPacketDict = [];
        private readonly List<(PendingPacketType, ushort)> nextPendingPacket = [];

        public void Enqueue(PendingPacket pendingPacket)
        {
            pendingPacketQueue.Enqueue(pendingPacket);
            if (nextPendingPacket.Count <= 10)
            {
                var pp = pendingPacketQueue.Dequeue();
                (PendingPacketType, ushort) pp_tuple = (pp.Type, pp.ID);
                pendingPacketDict.Add(pp_tuple, pp);
                nextPendingPacket.Add(pp_tuple);
            }
        }

        public object? Dequeue(PendingPacketType type, ushort id)
        {
            (PendingPacketType, ushort) pp_tuple = (type, id);
            var pp = pendingPacketDict[pp_tuple];
            nextPendingPacket.Remove(pp_tuple);
            return pp.Bin;
        }

        public object? UpdatePacketTypeStatus(PendingPacketType type, ushort id, PacketType packetType)
        {
            (PendingPacketType, ushort) pp_tuple = (type, id);
            var pp = pendingPacketDict[pp_tuple];
            pp.PacketType = packetType;
            pp.SentAt = null;
            return pp.Bin;
        }

        public void Start(MqttMonitor mqttMonitor, OutgoingHandler outgoingHandler)
        {
            if (cts != null)
            {
                Debug.WriteLine("Packet queue task is already running!");
                return;
            }

            Debug.WriteLine("Packet queue task is running now!");

            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            // Start the Task to listen to the packet queue and send the packets to the server
            Task.Run(async () =>
            {
                while (!mqttMonitor.IsConnectionClosed)
                {

                    if (mqttMonitor.IsConnectionEstablished && mqttMonitor.IsClientConnected)
                    {
                        var tuple_list = nextPendingPacket.Take(10).ToList();

                        foreach (var tuple in tuple_list)
                        {
                            var type = tuple.Item1;
                            var id = tuple.Item2;
                            PendingPacket packet = pendingPacketDict[tuple];

                            if (packet.SentAt == null || (DateTime.UtcNow - packet.SentAt) > packet.TimeoutSeconds)
                            {
                                packet.SentAt = DateTime.UtcNow;

                                switch (packet.PacketType)
                                {
                                    case PacketType.PUBLISH:
                                        {
                                            PublishPacket publishPacket = (PublishPacket)packet.Bin!;
                                            Debug.WriteLine(" -> Send PUBLISH! - " + packet.ID);
                                            if (publishPacket.QoS == QualityOfService.AT_MOST_ONCE)
                                            {
                                                await outgoingHandler!.SendPublish(id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS);
                                                Dequeue(type, id);
                                            }
                                            else if (publishPacket.QoS == QualityOfService.AT_LEAST_ONCE)
                                            {
                                                await outgoingHandler!.SendPublish(id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS, packet.Attempt > 0);
                                                packet.Attempt++;
                                            }
                                            else
                                            {
                                                await outgoingHandler!.SendPublish(id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS, packet.Attempt > 0);
                                                packet.Attempt++;
                                            }
                                            break;
                                        }
                                    case PacketType.PUBREC:
                                        Debug.WriteLine(" -> Send PUBREC! - " + packet.ID);
                                        await outgoingHandler!.SendPubRec(id);
                                        break;
                                    case PacketType.PUBREL:
                                        Debug.WriteLine(" -> Send PUBREL! - " + packet.ID);
                                        await outgoingHandler!.SendPubRel(id);
                                        break;
                                    case PacketType.SUBSCRIBE:
                                        SubscribePacket subscribePacket = (SubscribePacket)packet.Bin!;
                                        Debug.WriteLine(" -> Send SUBSCRIBE! - " + packet.ID);
                                        await outgoingHandler!.SendSubscribe(id, subscribePacket.Topics);
                                        break;
                                    case PacketType.UNSUBSCRIBE:
                                        Debug.WriteLine(" -> Send UNSUBSCRIBE! - " + packet.ID);
                                        UnsubscribePacket unsubscribePacket = (UnsubscribePacket)packet.Bin!;
                                        await outgoingHandler!.SendUnsubscribe(id, unsubscribePacket.Topics);
                                        break;
                                }
                            }
                        }
                    }
                    await Task.Delay(10);
                }
                Debug.WriteLine("Packet queue task terminated!");
            }, token);
        }

        public void Dispose(bool reset)
        {
            if (reset)
            {
                pendingPacketQueue.Clear();
                pendingPacketDict.Clear();
                nextPendingPacket.Clear();
            }
            if (cts != null)
            {
                cts.Cancel();
                cts = null;
            }
        }
    }
}
