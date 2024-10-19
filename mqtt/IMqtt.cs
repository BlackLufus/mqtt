using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt
{
    public interface IMqtt
    {
        public void Connect(string brokerAddress, int port, string clientID);
        public void Publish(string topic, string message);
        public void Subscribe(string topic);
        public void Unsubscribe(string topic);
        public void Disconnect();
    }
}
