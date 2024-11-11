using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Client
{
    public interface IMqttClient
    {
        public Task Connect(string clientID, string username = "", string password = "");
        public void Publish(string topic, string message, bool isDup = false);
        public void Subscribe(string topic);
        public void Unsubscribe(string topic);

        public void Disconnect(bool triggerEvent = true);
    }
}
