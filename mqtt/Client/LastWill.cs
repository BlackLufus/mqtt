﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client
{
    public class LastWill(string topic, string message)
    {
        public string Topic { get; } = topic;
        public string Message { get; } = message;
    }
}
