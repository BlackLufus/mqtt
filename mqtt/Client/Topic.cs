using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client
{
    public class Topic(string name, QualityOfService qos = QualityOfService.AT_MOST_ONCE)
    {
        public string Name { get; set; } = name;
        public QualityOfService QoS { get; set; } = qos;
    }
}
