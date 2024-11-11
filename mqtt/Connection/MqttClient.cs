using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Connection
{
    public class MqttClient
    {
        private TcpClient client;
        private NetworkStream stream;

        public MqttClient(string host, int port)
        {
            client = new TcpClient(host, port);
            stream = client.GetStream();
        }
    }
}
