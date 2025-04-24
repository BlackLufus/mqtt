using Mqtt.Client.Packets;
using Mqtt.Client.ReasonCode;
using Mqtt.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace Mqtt.Client.Network
{
    public class IncomingHandler : IDisposable
    {
        public event Action<ConnAckPacket>? OnConnAck;
        public event Func<PublishPacket, Task>? OnPublish;
        public event Action<PubAckPacket>? OnPubAck;
        public event Action<PubRecPacket>? OnPubRec;
        public event Action<PubRelPacket>? OnPubRel;
        public event Action<PubCompPacket>? OnPubComp;
        public event Action<SubAckPacket>? OnSubAck;
        public event Action<UnSubAckPacket>? OnUnSubAck;
        public event Action<PingReqPacket>? OnPingReq;
        public event Action<PingRespPacket>? OnPingResp;
        public event Action<DisconnectPacket>? OnDisconnect;

        private CancellationTokenSource? cts;

        private byte[] _accumulatedBuffer = Array.Empty<byte>();
        private readonly object _bufferLock = new object();

        private int CalculatePacketLength(byte[] buffer, out PacketType packetType)
        {
            packetType = (PacketType)(buffer[0] & 0b_1111_0000);
            if (buffer.Length < 2) return -1;

            int offset = 1;
            int multiplier = 1;
            int remainingLength = 0;
            byte currentByte;

            do
            {
                if (offset >= buffer.Length) return -1;
                currentByte = buffer[offset];
                remainingLength += (currentByte & 0x7F) * multiplier;
                multiplier *= 128;
                offset++;
            } while ((currentByte & 0x80) != 0);

            return offset + remainingLength; // Gesamtlänge
        }

        private void ProcessBuffer(int bytesRead, byte[] buffer, bool debug)
        {
            lock (_bufferLock)
            {
                // Effizienteres Hinzufügen mit Array.Resize
                int originalLength = _accumulatedBuffer.Length;
                Array.Resize(ref _accumulatedBuffer, originalLength + bytesRead);
                Buffer.BlockCopy(buffer, 0, _accumulatedBuffer, originalLength, bytesRead);

                while (true)
                {
                    if (_accumulatedBuffer.Length < 2) break;

                    // 1. NUR Länge berechnen
                    int packetLength = CalculatePacketLength(_accumulatedBuffer, out PacketType packetType);

                    // Unvollständiges Paket?
                    if (packetLength == -1 || _accumulatedBuffer.Length < packetLength) break;

                    // 2. Paket isolieren
                    byte[] packetBytes = new byte[packetLength];
                    Buffer.BlockCopy(_accumulatedBuffer, 0, packetBytes, 0, packetLength);

                    // 3. Jetzt erst dekodieren
                    try
                    {
                        object packet = DecodeFullPacket(packetBytes, packetType);
                        HandleSinglePacket(packetType, packet, debug);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Invalid packet: {ex.Message}");
                        // ENTFERNE das gesamte fehlerhafte Paket
                        packetLength = _accumulatedBuffer.Length; // Worst Case: Alles verwerfen
                    }

                    // 4. Buffer kürzen
                    _accumulatedBuffer = _accumulatedBuffer.Skip(packetLength).ToArray();
                }
            }
        }

        private object DecodeFullPacket(byte[] packetBytes, PacketType packetType)
        {
            return packetType switch
            {
                PacketType.CONNACK => ConnAckPacket.Decode(packetBytes),
                PacketType.PUBLISH => PublishPacket.Decode(packetBytes),
                PacketType.PUBACK => PubAckPacket.Decode(packetBytes),
                PacketType.PUBREC => PubRecPacket.Decode(packetBytes),
                PacketType.PUBREL => PubRelPacket.Decode(packetBytes),
                PacketType.PUBCOMP => PubCompPacket.Decode(packetBytes),
                PacketType.SUBACK => SubAckPacket.Decode(packetBytes),
                PacketType.UNSUBACK => UnSubAckPacket.Decode(packetBytes),
                PacketType.PINGREQ => PingReqPacket.Decode(packetBytes),
                PacketType.PINGRESP => PingRespPacket.Decode(packetBytes),
                PacketType.DISCONNECT => DisconnectPacket.Decode(packetBytes),
                _ => throw new NotSupportedException($"Unknown packet type: {packetType}")
            };
        }

        public void Start(NetworkStream stream, MqttMonitor mqttMonitor, MqttOption mqttOption)
        {
            if (cts != null)
            {
                Debug.WriteLine("Incoming Packet Listener is already running!");
                return;
            }
            cts = new CancellationTokenSource();
            Debug.WriteLine("Incoming Packet Listener started");

            CancellationToken token = cts.Token;
            Task.Run(async () =>
            {
                byte[] buffer = new byte[4096]; // Sinnvolle Puffergröße (z. B. 4 KB)
                int bytesRead;

                do
                {
                    bytesRead = await stream.ReadAsync(buffer);
                    if (bytesRead > 0)
                    {
                        try
                        {
                            ProcessBuffer(bytesRead, buffer, mqttOption.Debug);
                        }
                        catch (ArgumentOutOfRangeException ex)
                        {
                            Debug.WriteLine($"AOR in ProcessBuffer: bytesRead={bytesRead}, buffer.Length={buffer.Length}");
                            Debug.WriteLine(ex);
                            throw;
                        }
                    }
                } while (bytesRead > 0);
            }, token);
        }

        /// <summary>
        /// Handle incoming packet
        /// </summary>
        /// <param name="packet"></param>
        public void HandleSinglePacket(PacketType packetType, object packet, bool debug)
        {
            try
            {

                // Handle the packet based on the packet type
                switch (packetType)
                {
                    case PacketType.CONNACK:
                        OnConnAck?.Invoke((ConnAckPacket)packet);
                        break;
                    case PacketType.PUBLISH:
                        OnPublish?.Invoke((PublishPacket)packet);
                        break;
                    case PacketType.PUBACK:
                        OnPubAck?.Invoke((PubAckPacket)packet);
                        break;
                    case PacketType.PUBREC:
                        OnPubRec?.Invoke((PubRecPacket)packet);
                        break;
                    case PacketType.PUBREL:
                        OnPubRel?.Invoke((PubRelPacket)packet);
                        break;
                    case PacketType.PUBCOMP:
                        OnPubComp?.Invoke((PubCompPacket)packet);
                        break;
                    case PacketType.SUBACK:
                        OnSubAck?.Invoke((SubAckPacket)packet);
                        break;
                    case PacketType.UNSUBACK:
                        OnUnSubAck?.Invoke((UnSubAckPacket)packet);
                        break;
                    case PacketType.PINGREQ:
                        OnPingReq?.Invoke((PingReqPacket)packet);
                        break;
                    case PacketType.PINGRESP:
                        OnPingResp?.Invoke((PingRespPacket)packet);
                        break;
                    case PacketType.DISCONNECT:
                        OnDisconnect?.Invoke((DisconnectPacket)packet);
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
            if (cts != null)
            {
                cts.Cancel();
                cts = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
