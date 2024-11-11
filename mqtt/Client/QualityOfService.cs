using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Client
{
    /// <summary>
    /// Enum for the Quality of Service (QoS)
    /// </summary>
    public enum QualityOfService
    {
        AT_MOST_ONCE = 0,
        AT_LEAST_ONCE = 1,
        EXACTLY_ONCE = 2
    }
}
