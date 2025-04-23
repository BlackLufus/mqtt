using Mqtt.Client.Packets;
using Mqtt.Packets;
using System.Diagnostics;
using System.Net.Sockets;

namespace Mqtt.Client.Network
{
    public class OutgoingHandler(MqttOption option, NetworkStream stream) : IDisposable
    {
        // ToDo: Implement to Connect Packet in a separate class (ConnectPacket.cs)
        public async Task SendConnect(string clientId, string username, string password)
        {
            // Connect Packet
            ConnectPacket connectPacket = new ConnectPacket(clientId, username, password, option.LastWill, option.WillRetain, option.QoS, option.CleanSession, option.Version, (ushort)option.KeepAlive, (uint)option.SessionExpiryInterval);

            // Send the message and flush the stream
            await Send(connectPacket.Encode());
        }

        public async Task SendPublish(ushort id, string topic, string message, QualityOfService qos, bool isDup = false)
        {
            // Publish Packet
            PublishPacket publishPacket = new PublishPacket(id, topic, message, qos, option.WillRetain, isDup);

            // Send the message and flush the stream
            await Send(publishPacket.Encode());
        }

        /// <summary>
        /// Send Publish Acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubAck(ushort packetId)
        {
            // PubAck Packet
            PubAckPacket pubAckPacket = new PubAckPacket(packetId);

            // Send the message and flush the stream
            await Send(pubAckPacket.Encode());
        }

        /// <summary>
        /// Send Publish Received (PUBREC) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubRec(ushort packetId)
        {
            // PubRec Packet
            PubRecPacket pubRecPacket = new PubRecPacket(packetId);

            // Send the message and flush the stream
            await Send(pubRecPacket.Encode());
        }

        /// <summary>
        /// Send Publish Release (PUBREL) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubRel(ushort packetId)
        {
            // PubRel Packet
            PubRelPacket pubRelPacket = new PubRelPacket(packetId);

            // Send the message and flush the stream
            await Send(pubRelPacket.Encode());
        }

        /// <summary>
        /// Send Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubComp(ushort packetId)
        {
            // PubComp Packet
            PubCompPacket pubCompPacket = new PubCompPacket(packetId);

            // Send the message and flush the stream
            await Send(pubCompPacket.Encode());
        }

        public async Task SendSubscribe(ushort id, Topic[] topics)
        {
            // Subscribe Packet
            SubscribePacket subscribe = new SubscribePacket(id, topics);

            // Send the message and flush the stream
            await Send(subscribe.Encode());
        }

        public async Task SendUnsubscribe(ushort id, Topic[] topics)
        {
            // Unsubscribe Packet
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(id, topics);

            // Send the message and flush the stream
            await Send(unsubscribePacket.Encode());
        }

        /// <summary>
        /// Send Ping Request (PINGREQ) message
        /// </summary>
        public async Task SendPingReq()
        {
            // Ping Request Packet
            PingReqPacket pingReqPacket = new PingReqPacket();

            // Send the message and flush the stream
            await Send(pingReqPacket.Encode());
        }

        /// <summary>
        /// Send Disconnect (DISCONNECT) message
        /// </summary>
        public async Task SendDisconnect()
        {
            // Disconnect Packet
            DisconnectPacket disconnectPacket = new DisconnectPacket();

            // Send the message and flush the stream
            await Send(disconnectPacket.Encode());
        }

        private async Task Send(byte[] data)
        {
            try
            {
                if (option.Debug)
                {
                    Debug.WriteLine("Send: " + (PacketType)(data[0] & 0b_1111_0000));
                }
                await stream!.WriteAsync(data);
            }
            catch { }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
