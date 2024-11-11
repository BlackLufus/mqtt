using Mqtt.Client;
using Mqtt.Client.Packets;
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
        private readonly List<PacketQueue> packetQueueList = [];
        public void Add(PacketQueue packetQueue)
        {
            packetQueueList.Add(packetQueue);
        }

        public PacketQueue Get => packetQueueList[0];

        public void Remove()
        {
            if (packetQueueList.Count > 0)
            {
                packetQueueList.RemoveAt(0);
            }
        }

        public void Clear() {
            packetQueueList.Clear();
        }

        public void Start(CancellationTokenSource cts, MqttMonitor mqttMonitor, MqttOption mqttOption, OutgoingHandler outgoingHandler)
        {
            Debug.WriteLine("Mqtt Queue Listener started");

            CancellationToken token = cts.Token;
            Task.Run(async () =>
            {
                while (!mqttMonitor.IsConnectionClosed)
                {
                    token.ThrowIfCancellationRequested();

                    if (mqttMonitor.IsConnected && packetQueueList.Count > 0)
                    {
                        PacketQueue packetQueue = Get;

                        if (packetQueue.Timestamp == null || DateTime.Now - packetQueue.Timestamp > TimeSpan.FromSeconds(1))
                        {
                            packetQueue.Timestamp = DateTime.Now;

                            switch (packetQueue.PacketType)
                            {
                                case PacketType.PUBLISH:
                                    if (mqttOption.QoS == QualityOfService.AT_MOST_ONCE)
                                    {
                                        outgoingHandler!.SendPublish(packetQueue.Id, packetQueue.Topic, packetQueue.Message!);
                                        Remove();
                                    }
                                    else if (mqttOption.QoS == QualityOfService.AT_LEAST_ONCE)
                                    {
                                        outgoingHandler!.SendPublish(packetQueue.Id, packetQueue.Topic, packetQueue.Message!);
                                    }
                                    else if (mqttOption.QoS == QualityOfService.EXACTLY_ONCE)
                                    {
                                        switch (packetQueue.State)
                                        {
                                            case "PUBLISH":
                                                outgoingHandler!.SendPublish(packetQueue.Id, packetQueue.Topic, packetQueue.Message!);
                                                break;
                                            case "PUBREC":
                                                outgoingHandler!.SendPubRel(packetQueue.Id);
                                                break;
                                        }
                                    }
                                    break;
                                case PacketType.SUBSCRIBE:
                                    outgoingHandler!.SendSubscribe(packetQueue.Id, packetQueue.Topic);
                                    break;
                                case PacketType.UNSUBSCRIBE:
                                    outgoingHandler!.SendUnsubscribe(packetQueue.Id, packetQueue.Topic);
                                    break;
                            }
                        }
                    }
                    await Task.Delay(10);
                }
                Debug.WriteLine("Mqtt Queue Listener stopped");
            }, token);
        }
    }
}
