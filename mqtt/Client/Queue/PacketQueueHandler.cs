using Mqtt.Client.Network;
using Mqtt.Client.Packets;
using Mqtt.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Queue
{
    public class PacketQueueHandler(string id)
    {
        public delegate void PacketPorcessedDelegate(int id);
        public event PacketPorcessedDelegate? OnPacketProcessed;

        private CancellationTokenSource? cts;

        private readonly Queue<PendingPacket> pendingQueue = [];
        private readonly Dictionary<ushort, PendingPacket> pendingDict = [];
        private readonly List<ushort> pendingIds = [];

        public void Enqueue(PendingPacket pendingPacket)
        {
            pendingQueue.Enqueue(pendingPacket);
            if (pendingQueue.Count > 0 && pendingIds.Count <= 10)
            {
                Dequeue();
            }
        }

        private void Dequeue()
        {
            PendingPacket pendingPacket = pendingQueue.Dequeue();
            Console.WriteLine(pendingPacket.PacketType);
            pendingDict.Add(pendingPacket.Id, pendingPacket);
            pendingIds.Add(pendingPacket.Id);
        }

        public object? Update(ushort id, PacketType newType)
        {
            Debug.WriteLine(id + " - " +  newType);
            PendingPacket packet = pendingDict[id];
            packet.Update(newType);
            return packet.Bin;
        }

        public void Delete(ushort id)
        {
            pendingDict.Remove(id);
            pendingIds.Remove(id);
            if (pendingQueue.Count > 0)
            {
                Dequeue();
            }
        }


        public void Start(MqttMonitor mqttMonitor, OutgoingHandler outgoingHandler)
        {
            if (cts != null)
            {
                Debug.WriteLine("Packet queue " + id + " task is already running!");
                return;
            }
            cts = new CancellationTokenSource();
            Debug.WriteLine("Packet queue " + id + " task is running now!");

            CancellationToken token = cts.Token;

            // Start the Task to listen to the packet queue and send the packets to the server
            Task.Run(async () =>
            {
                while (!mqttMonitor.IsConnectionClosed)
                {

                    if (mqttMonitor.IsConnected)
                    {
                        List<ushort> idsToCheck = pendingIds.Take(10).ToList();

                        foreach (ushort id in idsToCheck)
                        {
                            if (!pendingDict.TryGetValue(id, out var packet)) continue;

                            if (!packet.IsTimedOut) continue;

                            packet.SentAt = DateTime.Now;

                            switch (packet.PacketType)
                            {
                                case PacketType.CONNECT:
                                    packet.Status = MessageStatus.Sent;
                                    break;
                                case PacketType.CONNACK:
                                    packet.Status = MessageStatus.Acked;
                                    OnPacketProcessed?.Invoke(id);
                                    Delete(id);
                                    break;
                                case PacketType.PUBLISH:
                                    {
                                        PublishPacket publishPacket = (PublishPacket)packet.Bin!;
                                        Debug.WriteLine(publishPacket.QoS);
                                        if (publishPacket.QoS == QualityOfService.AT_MOST_ONCE)
                                        {
                                            outgoingHandler!.SendPublish(id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS);
                                            packet.Status = MessageStatus.Sent;
                                            OnPacketProcessed?.Invoke(id);
                                            Delete(id);
                                        }
                                        else if (publishPacket.QoS == QualityOfService.AT_LEAST_ONCE)
                                        {
                                            outgoingHandler!.SendPublish(id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS, packet.Status == MessageStatus.Sent);
                                            packet.Status = MessageStatus.Sent;
                                        }
                                        else
                                        {
                                            outgoingHandler!.SendPublish(id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS, packet.Status == MessageStatus.Sent);
                                            packet.Status = MessageStatus.Sent;
                                        }
                                        break;
                                    }
                                case PacketType.PUBACK:
                                    packet.Status = MessageStatus.Acked;
                                    OnPacketProcessed?.Invoke(id);
                                    Delete(id);
                                    break;
                                case PacketType.PUBREC:
                                    packet.Status = MessageStatus.Sent;
                                    outgoingHandler!.SendPubRec(id);
                                    break;
                                case PacketType.PUBREL:
                                    packet.Status = MessageStatus.Acked;
                                    outgoingHandler.SendPubRel(id);
                                    break;
                                case PacketType.PUBCOMP:
                                    if (packet.Status != MessageStatus.Acked)
                                    {
                                        outgoingHandler.SendPubComp(id);
                                    }
                                    packet.Status = MessageStatus.Acked;
                                    OnPacketProcessed?.Invoke(id);
                                    Delete(id);
                                    break;
                                case PacketType.SUBSCRIBE:
                                    SubscribePacket subscribePacket = (SubscribePacket)packet.Bin!;
                                    outgoingHandler!.SendSubscribe(id, subscribePacket.Topics);
                                    packet.Status = MessageStatus.Sent;
                                    break;
                                case PacketType.SUBACK:
                                    packet.Status = MessageStatus.Acked;
                                    OnPacketProcessed?.Invoke(id);
                                    Delete(id);
                                    break;
                                case PacketType.UNSUBSCRIBE:
                                    UnsubscribePacket unsubscribePacket = (UnsubscribePacket)packet.Bin!;
                                    outgoingHandler!.SendUnsubscribe(id, unsubscribePacket.Topics);
                                    packet.Status = MessageStatus.Sent;
                                    break;
                                case PacketType.UNSUBACK:
                                    packet.Status = MessageStatus.Acked;
                                    OnPacketProcessed?.Invoke(id);
                                    Delete(id);
                                    break;
                                case PacketType.PINGREQ:
                                    outgoingHandler!.SendPingReq();
                                    packet.Status = MessageStatus.Sent;
                                    break;
                                case PacketType.PINGRESP:
                                    packet.Status = MessageStatus.Acked;
                                    Delete(id);
                                    break;
                                default: break;
                            }
                        }
                    }
                    await Task.Delay(10);
                }
                Debug.WriteLine("Packet queue " + id + " task terminated!");
            }, token);
        }

        public void Dispose(bool reset)
        {
            if (reset)
            {
                pendingQueue.Clear();
                pendingDict.Clear();
                pendingIds.Clear();
            }
            if (cts != null)
            {
                cts.Cancel();
                cts = null;
            }
        }
    }
}
