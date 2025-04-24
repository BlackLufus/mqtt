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
    public class IncomingHandler(Action<string>? debug) : IDisposable
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
        private void ProcessBuffer(int bytesRead, byte[] buffer)
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
                        HandleSinglePacket(packetType, packet);
                    }
                    catch (Exception ex)
                    {
                        debug?.Invoke($"Invalid packet: {ex.Message}");
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
        public void Start(NetworkStream stream)
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
                            ProcessBuffer(bytesRead, buffer);
                        }
                        catch (ArgumentOutOfRangeException ex)
                        {
                            debug?.Invoke($"AOR in ProcessBuffer: bytesRead={bytesRead}, buffer.Length={buffer.Length}");
                            debug?.Invoke(ex.Message);
                            throw;
                        }
                    }
                } while (bytesRead > 0);
            }, token);
        }

        /// <summary>
        /// Dispatches the decoded packet to the appropriate event handler.
        /// </summary>
        public void HandleSinglePacket(PacketType packetType, object packet)
        {
            try
            {
                switch (packetType)
                {
                    case PacketType.CONNACK:
                        var connAckPacket = (ConnAckPacket)packet;
                        debug?.Invoke(" <- Received CONNACK packet");
                        OnConnAck?.Invoke(connAckPacket);
                        break;
                    case PacketType.PUBLISH:
                        var publishPacket = (PublishPacket)packet;
                        debug?.Invoke(" <- Received PUBLISH packet (id: " + publishPacket.PacketID + ")");
                        OnPublish?.Invoke(publishPacket);
                        break;
                    case PacketType.PUBACK:
                        var pubBackPacket = (PubAckPacket)packet;
                        debug?.Invoke(" <- Received PUBACK packet (id: " + pubBackPacket.PacketID + ")");
                        OnPubAck?.Invoke(pubBackPacket);
                        break;
                    case PacketType.PUBREC:
                        var pubRecPacket = (PubRecPacket)packet;
                        debug?.Invoke(" <- Received PUBREC packet (id: " + pubRecPacket.PacketID + ")");
                        OnPubRec?.Invoke(pubRecPacket);
                        break;
                    case PacketType.PUBREL:
                        var pubRelPacket = (PubRelPacket)packet;
                        debug?.Invoke(" <- Received PUBREL packet (id: " + pubRelPacket.PacketID + ")");
                        OnPubRel?.Invoke(pubRelPacket);
                        break;
                    case PacketType.PUBCOMP:
                        var pubCompPacket = (PubCompPacket)packet;
                        debug?.Invoke(" <- Received PUBCOMP packet (id: " + pubCompPacket.PacketID + ")");
                        OnPubComp?.Invoke(pubCompPacket);
                        break;
                    case PacketType.SUBACK:
                        var subAckPacket = (SubAckPacket)packet;
                        debug?.Invoke(" <- Received SUBACK packet (id: " + subAckPacket.PacketID + ")");
                        OnSubAck?.Invoke(subAckPacket);
                        break;
                    case PacketType.UNSUBACK:
                        var unsubAckPacket = (UnSubAckPacket)packet;
                        debug?.Invoke(" <- Received UNSUBACK packet (id: " + unsubAckPacket.PacketID + ")");
                        OnUnSubAck?.Invoke(unsubAckPacket);
                        break;
                    case PacketType.PINGREQ:
                        var pingReqPacket = (PingReqPacket)packet;
                        debug?.Invoke(" <- Received PINGREQ packet");
                        OnPingReq?.Invoke(pingReqPacket);
                        break;
                    case PacketType.PINGRESP:
                        var pingRespPacket = (PingRespPacket)packet;
                        debug?.Invoke(" <- Received PINGRESP packet");
                        OnPingResp?.Invoke(pingRespPacket);
                        break;
                    case PacketType.DISCONNECT:
                        var disconnectPacket = (DisconnectPacket)packet;
                        debug?.Invoke(" <- Received DISCONNECT packet");
                        OnDisconnect?.Invoke(disconnectPacket);
                        break;
                }
            }
            catch (Exception ex)
            {
                debug?.Invoke($"Error handling packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancels the read loop and releases resources.
        /// </summary>
        public void Dispose()
        {
            debug?.Invoke("Incoming handler terminated!");
            cts?.Cancel();
            cts = null;
            GC.SuppressFinalize(this);
        }
    }
}
