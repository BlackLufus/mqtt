using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt
{
    public interface IMqtt
    {
        public void SetWill(string topic, string message);
        public Task Connect(string brokerAddress, int port, string clientID, string username = "", string password = "");
        public Task Publish(string topic, string message);
        public Task Subscribe(string topic);
        public Task Unsubscribe(string topic);
        public void Disconnect();
    }
}
