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
        // Events raised when specific MQTT packets are received
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

        // Accumulates incoming bytes until a full MQTT packet can be parsed
        private byte[] _accumulatedBuffer = Array.Empty<byte>();
        private readonly object _bufferLock = new object();

        /// <summary>
        /// Calculates the full packet length using the MQTT Remaining Length encoding.
        /// Returns -1 if the header or length field is incomplete.
        /// </summary>
        private int CalculatePacketLength(byte[] buffer, out PacketType packetType)
        {
            packetType = (PacketType)(buffer[0] & 0b_1111_0000);
            if (buffer.Length < 2) return -1;

            int offset = 1;
            int multiplier = 1;
            int remainingLength = 0;

            // Decode variable-length Remaining Length field
            byte currentByte;

            do
            {
                if (offset >= buffer.Length) return -1;
                currentByte = buffer[offset];
                remainingLength += (currentByte & 0x7F) * multiplier;
                multiplier *= 128;
                offset++;
            } while ((currentByte & 0x80) != 0);

            // Total size = header bytes + payload bytes
            return offset + remainingLength;
        }

        /// <summary>
        /// Appends new bytes and processes as many complete MQTT packets as possible.
        /// </summary>
        private void ProcessBuffer(int bytesRead, byte[] buffer, bool debug)
        {
            // Ensure that only one thread at a time modifies the accumulated buffer
            lock (_bufferLock)
            {
                // Append new data to the accumulated buffer
                // Effizienteres Hinzufügen mit Array.Resize
                int originalLength = _accumulatedBuffer.Length;
                Array.Resize(ref _accumulatedBuffer, originalLength + bytesRead);
                Buffer.BlockCopy(buffer, 0, _accumulatedBuffer, originalLength, bytesRead);

                // Extract and handle all full packets
                while (true)
                {
                    if (_accumulatedBuffer.Length < 2) break;

                    // Determine the expected length of the next packet
                    int packetLength = CalculatePacketLength(_accumulatedBuffer, out PacketType packetType);
                    if (packetLength == -1 || _accumulatedBuffer.Length < packetLength) break;

                    // Isolate the full packet bytes
                    byte[] packetBytes = new byte[packetLength];
                    Buffer.BlockCopy(_accumulatedBuffer, 0, packetBytes, 0, packetLength);

                    // Decode and dispatch the packet
                    try
                    {
                        object packet = DecodeFullPacket(packetBytes, packetType);
                        HandleSinglePacket(packetType, packet, debug);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Invalid packet: {ex.Message}");
                        packetLength = _accumulatedBuffer.Length;
                    }

                    // Remove the processed packet bytes from the buffer
                    _accumulatedBuffer = _accumulatedBuffer.Skip(packetLength).ToArray();
                }
            }
        }

        /// <summary>
        /// Decodes a complete packet byte array into the appropriate MQTT packet object.
        /// </summary>
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

        /// <summary>
        /// Starts an asynchronous loop reading from the network stream.
        /// Incoming data is buffered and processed.
        /// </summary>
        public void Start(NetworkStream stream, MqttMonitor mqttMonitor, MqttOption mqttOption)
        {
            if (cts != null)
            {
                return;
            }
            cts = new CancellationTokenSource();

            CancellationToken token = cts.Token;
            Task.Run(async () =>
            {
                byte[] buffer = new byte[4096];
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
        /// Dispatches the decoded packet to the appropriate event handler.
        /// </summary>
        public void HandleSinglePacket(PacketType packetType, object packet, bool debug)
        {
            try
            {
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
                if (debug)
                    Debug.WriteLine($"Error handling packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancels the read loop and releases resources.
        /// </summary>
        public void Dispose()
        {
            cts?.Cancel();
            cts = null;
            GC.SuppressFinalize(this);
        }
    }
}
