using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static mqtt.Mqtt;

namespace mqtt
{
    public class MqttPacketHandler
    {
        private NetworkStream stream;

        public MqttPacketHandler(NetworkStream stream)
        {
            this.stream = stream;
        }

        public (string, string) HandlePublish(byte[] packet, int packetLength)
        {
            // ToDo: Implement
            return ("", "");
        }

        public void HandleConnAck(byte[] packet, int packetLength)
        {
            int remainingLength = packet[1];
            byte connectAcknowledgeFlags = packet[2];
            ConnectReturnCode returnCode = (ConnectReturnCode)packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("Connect Acknowledge Flags: " + Convert.ToString(connectAcknowledgeFlags, 2).PadLeft(8, '0'));
            Console.WriteLine("Return Code: " + returnCode);
        }

        public void HandlePubAck(byte[] packet, int packetLength)
        {
            int remainingLength = packet[1];
            int packetId = packet[2] << 8 | packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBACK: " + packetId);
        }

        public void HandlePubRec(byte[] packet, int packetLength)
        {
            int remainingLength = packet[1];
            int packetId = packet[2] << 8 | packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBREC: " + packetId);
        }

        public void HandlePubRel(byte[] packet, int packetLength)
        {
            int remainingLength = packet[1];
            int packetId = packet[2] << 8 | packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBREC: " + packetId);
        }

        public void HandlePubComp(byte[] packet, int packetLength)
        {
            int remainingLength = packet[1];
            int packetId = packet[2] << 8 | packet[3];

            Console.WriteLine("Remaining Length: " + remainingLength);
            Console.WriteLine("PUBCOMP: " + packetId);
        }

        public void HandleSubAck(byte[] packet, int packetLength)
        {
            // ToDo: Implement
        }

        public void HandleUnsubAck(byte[] packet, int packetLength)
        {
            // ToDo: Implement
        }

        public void HandlePingReq(byte[] packet, int packetLength)
        {
            // ToDo: Implement
        }

        public void HandlePingResp(byte[] packet, int packetLength)
        {
            // ToDo: Implement
        }

        public void HandleDisconnect(byte[] packet, int packetLength)
        {
            // ToDo: Implement
        }

        bool dupFlag = false;
        QoS WillQoS = QoS.AT_MOST_ONCE;
        bool WillRetain = false;
        int messageId = 1;
        private void Publish(string topic, string message)
        {
            Console.WriteLine("");
            Console.WriteLine("Publishing message to topic: " + topic);
            Console.WriteLine("Message: " + message);
            // Fixed Header
            byte[] fixedHeader =
            {
                (byte)((byte)(PacketType.PUBLISH) | (dupFlag ? (1 << 3) : 0) | ((int)WillQoS << 1) | (WillRetain ? 1 : 0)),
                0
            };

            Console.WriteLine(2);
            Console.WriteLine(topic.Length);
            Console.WriteLine(message.Length);
            Console.WriteLine(WillQoS > (int)QoS.AT_MOST_ONCE);
            Console.WriteLine((QoS)WillQoS);
            byte[] payload = new byte[2 + topic.Length + message.Length + (WillQoS != QoS.AT_MOST_ONCE ? 2 : 0)];
            payload[0] = (byte)(topic.Length >> 8);
            payload[1] = (byte)(topic.Length & 0xFF);
            Array.Copy(Encoding.UTF8.GetBytes(topic), 0, payload, 2, topic.Length);
            if (WillQoS != QoS.AT_MOST_ONCE)
            {
                Console.WriteLine("Message ID: " + messageId);
                payload[2 + topic.Length] = (byte)(messageId >> 8);
                payload[3 + topic.Length] = (byte)(messageId++ & 0xFF);
            }
            Array.Copy(Encoding.UTF8.GetBytes(message), 0, payload, 2 + topic.Length + (WillQoS != QoS.AT_MOST_ONCE ? 2 : 0), message.Length);

            int remainingLength = payload.Length;
            fixedHeader[1] = (byte)remainingLength;

            byte[] result = new byte[fixedHeader.Length + payload.Length];
            Array.Copy(fixedHeader, 0, result, 0, fixedHeader.Length);
            Array.Copy(payload, 0, result, fixedHeader.Length, payload.Length);

            for (int i = 0; i < result.Length; i++)
            {
                Console.WriteLine(Convert.ToString(result[i], 2).PadLeft(8, '0'));
            }

            stream.Write(result, 0, result.Length);
        }
    }
}
