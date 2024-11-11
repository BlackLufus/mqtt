using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.ReasonCode
{
    public enum SubAckReasonCode
    {
        SuccessMaximumQoS0 = 0x00,
        SuccessMaximumQoS1 = 0x01,
        SuccessMaximumQoS2 = 0x02,
        Failure = 0x80
    }
}
