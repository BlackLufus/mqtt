using Mqtt.Client.Packets;
using Mqtt.Packets;
using System.Diagnostics;
using System.Net.Sockets;

namespace Mqtt.Client.Network
{
    public class OutgoingHandler(MqttOption option, NetworkStream stream, Action<string>? debug) : IDisposable
    {
        // ToDo: Implement to Connect Packet in a separate class (ConnectPacket.cs)
        public async Task SendConnect(string clientId, string username, string password)
        {
            // Connect Packet
            ConnectPacket connectPacket = new ConnectPacket(clientId, username, password, option.LastWill, option.WillRetain, option.QoS, option.CleanSession, option.Version, (ushort)option.KeepAlive, (uint)option.SessionExpiryInterval);

            debug?.Invoke(" -> Send CONNECT packet (client id: " + clientId + ")");

            // Send the message and flush the stream
            await Send(connectPacket.Encode());
        }

        public async Task SendPublish(ushort id, string topic, string message, QualityOfService qos, bool isDup = false)
        {
            // Publish Packet
            PublishPacket publishPacket = new PublishPacket(id, topic, message, qos, option.WillRetain, isDup);

            debug?.Invoke(" -> Send PUBLISH packet (id: " + id + ")");

            // Send the message and flush the stream
            await Send(publishPacket.Encode());
        }

        /// <summary>
        /// Send Publish Acknowledgement (PUBACK) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubAck(ushort id)
        {
            // PubAck Packet
            PubAckPacket pubAckPacket = new PubAckPacket(id);

            debug?.Invoke(" -> Send PUBACK packet (id: " + id + ")");

            // Send the message and flush the stream
            await Send(pubAckPacket.Encode());
        }

        /// <summary>
        /// Send Publish Received (PUBREC) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubRec(ushort id)
        {
            // PubRec Packet
            PubRecPacket pubRecPacket = new PubRecPacket(id);

            debug?.Invoke(" -> Send PUBREC packet (id: " + id + ")");

            // Send the message and flush the stream
            await Send(pubRecPacket.Encode());
        }

        /// <summary>
        /// Send Publish Release (PUBREL) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubRel(ushort id)
        {
            // PubRel Packet
            PubRelPacket pubRelPacket = new PubRelPacket(id);

            debug?.Invoke(" -> Send PUBREL packet (id: " + id + ")");

            // Send the message and flush the stream
            await Send(pubRelPacket.Encode());
        }

        /// <summary>
        /// Send Publish complete (PUBCOMP) message
        /// </summary>
        /// <param name="MSB"> Message ID MSB </param>
        /// <param name="LSB"> Message ID LSB </param>
        public async Task SendPubComp(ushort id)
        {
            // PubComp Packet
            PubCompPacket pubCompPacket = new PubCompPacket(id);

            debug?.Invoke(" -> Send PUBCOMP packet (id: " + id + ")");

            // Send the message and flush the stream
            await Send(pubCompPacket.Encode());
        }

        public async Task SendSubscribe(ushort id, Topic[] topics)
        {
            // Subscribe Packet
            SubscribePacket subscribe = new SubscribePacket(id, topics);

            debug?.Invoke(" -> Send SUBSCRIBE packet (id: " + id + ")");

            // Send the message and flush the stream
            await Send(subscribe.Encode());
        }

        public async Task SendUnsubscribe(ushort id, Topic[] topics)
        {
            // Unsubscribe Packet
            UnsubscribePacket unsubscribePacket = new UnsubscribePacket(id, topics);

            debug?.Invoke(" -> Send UNSUBSCRIBE packet (id: " + id + ")");

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

            debug?.Invoke(" -> Send PINGREQ packet");

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

            debug?.Invoke(" -> Send DISCONNECT packet");

            // Send the message and flush the stream
            await Send(disconnectPacket.Encode());
        }

        private async Task Send(byte[] data)
        {
            try
            {
                await stream!.WriteAsync(data);
            }
            catch { }
        }

        public void Dispose()
        {
            debug?.Invoke("Outgoing handler terminated!");
            GC.SuppressFinalize(this);
        }
    }
}
