using Mqtt.Client;
using Mqtt.Client.Packets;
using Mqtt.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Network
{
    public class OutgoingHandler(MqttOption option, NetworkStream stream) : IDisposable
    {
        // ToDo: Implement to Connect Packet in a separate class (ConnectPacket.cs)
        public void SendConnect(string clientId, string username, string password)
        {
            // Connect Packet
            ConnectPacket connectPacket = new ConnectPacket(clientId, username, password, option.LastWill, option.WillRetain, option.QoS, option.CleanSession, option.Version, (ushort)option.KeepAlive, (uint)option.SessionExpiryInterval);

            // Send the message and flush the stream
            Send(connectPacket.Encode());
        }

        public void SendPublish(int id, string topic, string message, QualityOfService qos, bool isDup = false)
        {
            // Publish Packet
            PublishPacket publishPacket = new PublishPacket(id, topic, message, qos, option.WillRetain, isDup);

            // Send the message and flush the stream
            Send(publishPacket.Encode());
        }

        /// <summary>
        /// Send Publish Acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public void SendPubAck(int packetId)
        {
            // PubAck Packet
            PubAckPacket pubAckPacket = new PubAckPacket(packetId);

            // Send the message and flush the stream
            Send(pubAckPacket.Encode());
        }

        /// <summary>
        /// Send Publish Received (PUBREC) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public void SendPubRec(int packetId)
        {
            // PubRec Packet
            PubRecPacket pubRecPacket = new PubRecPacket(packetId);

            // Send the message and flush the stream
            Send(pubRecPacket.Encode());
        }

        /// <summary>
        /// Send Publish Release (PUBREL) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public void SendPubRel(int packetId)
        {
            // PubRel Packet
            PubRelPacket pubRelPacket = new PubRelPacket(packetId);

            // Send the message and flush the stream
            Send(pubRelPacket.Encode());
        }

        /// <summary>
        /// Send Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public void SendPubComp(int packetId)
        {
            // PubComp Packet
            PubCompPacket pubCompPacket = new PubCompPacket(packetId);

            // Send the message and flush the stream
            Send(pubCompPacket.Encode());
        }

        public void SendSubscribe(int id, Topic[] topics)
        {
            // Subscribe Packet
            SubscribePacket subscribe = new SubscribePacket(id, topics);

            // Send the message and flush the stream
            Send(subscribe.Encode());
        }

        public void SendUnsubscribe(int id, Topic[] topics)
        {
            // Unsubscribe Packet
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(id, topics);

            // Send the message and flush the stream
            Send(unsubscribePacket.Encode());
        }

        /// <summary>
        /// Send Ping Request (PINGREQ) message
        /// </summary>
        public void SendPingReq()
        {
            // Ping Request Packet
            PingReqPacket pingReqPacket = new PingReqPacket();

            // Send the message and flush the stream
            Send(pingReqPacket.Encode());
        }

        /// <summary>
        /// Send Disconnect (DISCONNECT) message
        /// </summary>
        public void SendDisconnect()
        {
            // Disconnect Packet
            DisconnectPacket disconnectPacket = new DisconnectPacket();

            // Send the message and flush the stream
            Send(disconnectPacket.Encode());
        }

        private async void Send(byte[] data)
        {
            try
            {
                if (option.Debug)
                {
                    Console.WriteLine("Send: " + (PacketType)(data[0] & 0b_1111_0000));
                }
                await stream!.WriteAsync(data);
                stream!.Flush();
            }
            catch { }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
