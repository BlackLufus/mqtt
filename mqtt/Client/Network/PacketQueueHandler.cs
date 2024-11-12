using Mqtt.Client;
using Mqtt.Client.Packets;
using Mqtt.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Network
{
    public class PacketQueueHandler
    {
        public delegate void PacketPorcessedDelegate(int id, PacketType packetType);
        public event PacketPorcessedDelegate? OnPacketProcessed;

        private static int staticID = 0;
        private int id = staticID++;

        private readonly Queue<QueuePacket> packetQueue = [];
        public void Add(QueuePacket queuePacket)
        {
            packetQueue.Enqueue(queuePacket);
        }

        public QueuePacket Get => packetQueue.Peek();

        public void PacketReceived(int id, PacketType receivedPacketType, PacketType newPacketType)
        {
            if (packetQueue.Count == 0)
            {
                return;
            }

            QueuePacket queuePacket = packetQueue.Peek();
            if (queuePacket.Id == id && queuePacket.PacketType == receivedPacketType)
            {
                queuePacket.Timestamp = null;
                queuePacket.PacketType = newPacketType;
            }
        }

        public void Clear() {
            packetQueue.Clear();
        }

        public void Start(CancellationTokenSource cts, MqttMonitor mqttMonitor, OutgoingHandler outgoingHandler)
        {
            Debug.WriteLine("Mqtt Queue Listener started");

            CancellationToken token = cts.Token;

            // Start the Task to listen to the packet queue and send the packets to the server
            Task.Run(async () =>
            {
                while (!mqttMonitor.IsConnectionClosed)
                {
                    token.ThrowIfCancellationRequested();

                    if (mqttMonitor.IsConnected && packetQueue.Count > 0)
                    {

                        QueuePacket queuePacket = packetQueue.Peek();
                       
                        if (queuePacket.Timeout == 0)
                        {
                            packetQueue.Dequeue();
                            OnPacketProcessed?.Invoke(queuePacket.Id, queuePacket.PacketType);
                        }
                        else if (queuePacket.Timestamp == null || DateTime.Now - queuePacket.Timestamp > TimeSpan.FromSeconds(5))
                        {
                            queuePacket.Timeout -= queuePacket.Timestamp == null ? 0 : 1;
                            switch (queuePacket.PacketType)
                            {
                                case PacketType.PUBLISH:
                                    {
                                        PublishPacket publishPacket = (PublishPacket)queuePacket.Packet!;
                                        if (publishPacket.QoS == QualityOfService.AT_MOST_ONCE)
                                        {
                                            outgoingHandler!.SendPublish(queuePacket.Id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS, queuePacket.Timestamp != null);
                                            packetQueue.Dequeue();
                                            OnPacketProcessed?.Invoke(queuePacket.Id, PacketType.PUBLISH);
                                        }
                                        else if (publishPacket.QoS == QualityOfService.AT_LEAST_ONCE)
                                        {
                                            outgoingHandler!.SendPublish(queuePacket.Id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS, queuePacket.Timestamp != null);
                                        }
                                        else
                                        {
                                            outgoingHandler!.SendPublish(queuePacket.Id, publishPacket.Topic, publishPacket.Message, publishPacket.QoS, queuePacket.Timestamp != null);
                                        }
                                    }
                                    break;
                                case PacketType.PUBACK:
                                    packetQueue.Dequeue();
                                    OnPacketProcessed?.Invoke(queuePacket.Id, PacketType.PUBLISH);
                                    break;
                                case PacketType.PUBREC:
                                    outgoingHandler!.SendPubRec(queuePacket.Id);
                                    break;
                                case PacketType.PUBREL:
                                    outgoingHandler.SendPubRel(queuePacket.Id);
                                    queuePacket.IsAcknowledged = true;
                                    break;
                                case PacketType.PUBCOMP:
                                    if (!queuePacket.IsAcknowledged)
                                    {
                                        outgoingHandler.SendPubComp(queuePacket.Id);
                                    }
                                    packetQueue.Dequeue();
                                    OnPacketProcessed?.Invoke(queuePacket.Id, PacketType.PUBLISH);
                                    break;
                                case PacketType.SUBSCRIBE:
                                    SubscribePacket subscribePacket = (SubscribePacket)queuePacket.Packet!;
                                    outgoingHandler!.SendSubscribe(queuePacket.Id, subscribePacket.Topics);
                                    break;
                                case PacketType.SUBACK:
                                    packetQueue.Dequeue();
                                    OnPacketProcessed?.Invoke(queuePacket.Id, PacketType.SUBSCRIBE);
                                    break;
                                case PacketType.UNSUBSCRIBE:
                                    UnsubscribePacket unsubscribePacket = (UnsubscribePacket)queuePacket.Packet!;
                                    outgoingHandler!.SendUnsubscribe(queuePacket.Id, unsubscribePacket.Topics);
                                    break;
                                case PacketType.UNSUBACK:
                                    packetQueue.Dequeue();
                                    OnPacketProcessed?.Invoke(queuePacket.Id, PacketType.UNSUBSCRIBE);
                                    break;
                                case PacketType.PINGREQ:
                                    outgoingHandler!.SendPingReq();
                                    break;
                                case PacketType.PINGRESP:
                                    packetQueue.Dequeue();
                                    break;
                            }
                            queuePacket.Timestamp = DateTime.Now;
                        }
                    }
                    await Task.Delay(10);
                }
                Debug.WriteLine("Mqtt Queue Listener stopped");
            }, token);
        }
    }
}
