using mqtt.Network;
using mqtt.Packets;
using mqtt.ReasonCode;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Network
{
    public class IncomingHandler : IDisposable
    {
        public event Action<ConnAckPacket>? OnConnAck;
        public event Action<PublishPacket>? OnPublish;
        public event Action<PubAckPacket>? OnPubAck;
        public event Action<PubRecPacket>? OnPubRec;
        public event Action<PubRelPacket>? OnPubRel;
        public event Action<PubCompPacket>? OnPubComp;
        public event Action<SubAckPacket>? OnSubAck;
        public event Action<UnsubscribePacket>? OnUnsubAck;
        public event Action<PingReqPacket>? OnPingReq;
        public event Action<PingRespPacket>? OnPingResp;
        public event Action<DisconnectPacket>? OnDisconnect;

        public void IncomingPacketListener(CancellationTokenSource cts, NetworkStream stream, MqttMonitor mqttMonitor)
        {
            Debug.WriteLine("Incoming Packet Listener started");

            CancellationToken token = cts.Token;
            Task.Run(async () =>
            {
                // Check if the stream is null
                if (stream == null)
                {
                    return;
                }

                // Buffer for incoming data (256 MB) and number of bytes read
                byte[] buffer = new byte[1024 * 1024 * 256];
                int bytesRead;

                // Read incoming data from the stream and handle the packet accordingly until the connection is closed or an exception occurs
                do
                {
                    // Read the incoming data
                    bytesRead = await stream.ReadAsync(buffer);

                    // Handle the incoming packet
                    Debug.WriteLine("Bytes read: " + bytesRead);

                    token.ThrowIfCancellationRequested();

                    HandleIncomingPacket(bytesRead, buffer);
                } while (bytesRead > 0);

                Debug.WriteLine("Incoming Packet Listener stopped");
            }, token);
        }

        /// <summary>
        /// Handle incoming packet
        /// </summary>
        /// <param name="packet"></param>
        public void HandleIncomingPacket(int bytesRead, byte[] packet)
        {
            try
            {
                // 1. Byte: Packet Type
                PacketType packetType = (PacketType)(packet[0] & 0b_1111_0000);

                Console.WriteLine("Incoming Packet: " + packetType);

                // Handle the packet based on the packet type
                switch (packetType)
                {
                    case PacketType.CONNACK:
                        OnConnAck?.Invoke(ConnAckPacket.Decode(packet));
                        break;
                    case PacketType.PUBLISH:
                        OnPublish?.Invoke(PublishPacket.Decode(packet));
                        break;
                    case PacketType.PUBACK:
                        OnPubAck?.Invoke(PubAckPacket.Decode(packet));
                        break;
                    case PacketType.PUBREC:
                        OnPubRec?.Invoke(PubRecPacket.Decode(packet));
                        break;
                    case PacketType.PUBREL:
                        OnPubRel?.Invoke(PubRelPacket.Decode(packet));
                        break;
                    case PacketType.PUBCOMP:
                        OnPubComp?.Invoke(PubCompPacket.Decode(packet));
                        break;
                    case PacketType.SUBACK:
                        OnSubAck?.Invoke(SubAckPacket.Decode(packet));
                        break;
                    case PacketType.UNSUBACK:
                        OnUnsubAck?.Invoke(UnsubscribePacket.Decode(packet));
                        break;
                    case PacketType.PINGREQ:
                        OnPingReq?.Invoke(PingReqPacket.Decode(packet));
                        break;
                    case PacketType.PINGRESP:
                        OnPingResp?.Invoke(PingRespPacket.Decode(packet));
                        break;
                    case PacketType.DISCONNECT:
                        OnDisconnect?.Invoke(DisconnectPacket.Decode(packet));
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while handling incoming packet: " + ex.Message);
                Console.WriteLine("Stack Trace: " + ex.StackTrace);
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
