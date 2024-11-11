using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Client
{
    public interface IMqtt
    {
        public Task Connect(string brokerAddress, int port, string clientID, string username = "", string password = "");
        public void Publish(string topic, string message);
        public void Subscribe(string topic);
        public void Unsubscribe(string topic);
        public void Disconnect(bool triggerEvent = true);
    }
}
