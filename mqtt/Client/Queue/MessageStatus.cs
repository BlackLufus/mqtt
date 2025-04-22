using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Queue
{    public enum MessageStatus
    {
        Queued,
        Sent,
        Acked
    }
}
