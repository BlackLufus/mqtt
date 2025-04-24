using Mqtt.Client.Network;
using System.Net;
using System.Text;

namespace Mqtt.Client.Validation
{
    public class MQTTValidator
    {
        public static (string, string)? CheckHost(string host)
        {
            if (string.IsNullOrEmpty(host))
                return ("ArgumentException", "Host cannot be null or empty.");

            try
            {
                Dns.GetHostEntry(host);
                return null;
            }
            catch
            {
                return ("ArgumentException", "Host cannot be resolved via DNS.");
            }
        }

        public static (string, string)? CheckPort(int port)
        {
            if (port < 1 || port > 65535)
                return ("ArgumentOutOfRangeException", "Port must be between 1 and 65535.");

            else return null;
        }

        public static (string, string)? CheckClientID(string clientID)
        {
            if (string.IsNullOrWhiteSpace(clientID))
                return ("ArgumentException", "ClientID cannot be null or empty.");

            else if (clientID.Length > 23)
                return ("ArgumentException", "ClientID must be 23 characters or fewer (MQTT 3.1.1 spec).");

            else if (clientID.Contains("+") || clientID.Contains("#"))
                return ("ArgumentException", "ClientID must not contain MQTT wildcard characters '+' or '#'.");

            else return null;
        }

        public static (string, string)? CheckUsername(string username)
        {
            if (username == null)
                return ("ArgumentException", "Username cannot be null.");

            else return null;
        }

        public static (string, string)? CheckPassword(string password)
        {
            if (password == null)
                return ("ArgumentException", "Password cannot be null.");

            else return null;
        }

        public static (string, string)? CheckTopic(bool isPublish, string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return ("ArgumentException", "Topic cannot be null or empty.");

            if (topic.Contains('\0'))
                return ("ArgumentException", "Topic must not contain null characters.");

            int byteLength = Encoding.UTF8.GetByteCount(topic);
            if (topic.Length > 65535)
                return ("ArgumentException", "Topic exceeds maximum length of 65535 bytes.");

            if (isPublish)
            {
                if (topic.Contains("+") || topic.Contains("#"))
                    return ("ArgumentException", "Publish topics must not contain wildcard characters '+' or '#'.");
            }
            else
            {
                if (topic.Contains("#") && !(topic.EndsWith("/#") || topic == "#"))
                    return ("ArgumentException", "Wildcard '#' is only allowed at the end of a subscribe topic.");

                string[] levels = topic.Split('/');
                foreach (var level in levels)
                {
                    if (level.Contains("#") && level != "#")
                        return ("ArgumentException", "Wildcard '#' must occupy an entire level in the topic.");
                    if (level.Contains("+") && level != "+")
                        return ("ArgumentException", "Wildcard '+' must occupy an entire level in the topic.");
                }
            }
            return null;
        }

        public static (string, string)? CheckMessage(string message)
        {
            if (message == null)
                return ("ArgumentException", "Message cannot be null.");

            int byteLength = Encoding.UTF8.GetByteCount(message);
            if (byteLength > 262144000) // 256 MB limit
                return ("ArgumentException", "Message exceeds maximum MQTT payload size (256 MB).");

            return null;
        }

        public static (string, string)? CheckIsConnected(bool isConnecting, MqttMonitor mqttMonitor)
        {
            if (isConnecting || mqttMonitor.IsClientConnected || mqttMonitor.IsConnectionEstablished)
            {
                return ("InvalidOperationException", "There is already an open connection.");
            }
            return null;
        }

        public static (string, string)? CheckConnectionClosed(MqttMonitor mqttMonitor)
        {
            if (mqttMonitor.IsConnectionClosed)
                return ("InvalidOperationException", "There is no existing connection.");

            return null;
        }

        public static (string, string)? CheckConnectionEstablished(MqttMonitor mqttMonitor)
        {
            if (!mqttMonitor.IsConnectionEstablished)
                return ("InvalidOperationException", "Connection is not established.");

            return null;
        }

        public static (string, string)? CheckClientConnected(MqttMonitor mqttMonitor)
        {
            if (!mqttMonitor.IsClientConnected)
                return ("InvalidOperationException", "Client is not connected.");

            return null;
        }
    }
}
