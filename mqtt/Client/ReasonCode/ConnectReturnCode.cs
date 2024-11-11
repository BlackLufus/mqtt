using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.ReasonCode
{
    /// <summary>
    /// Enum for the Connect Return Code
    /// </summary>
    public enum ConnectReturnCode
    {
        SUCCESS = 0x00,

        UNSPECIFIED_ERROR = 0x80,
        MALFORMED_PACKET = 0x81,
        PROTOCOL_ERROR = 0x82,
        IMPLEMENTATION_SPECIFIC_ERROR = 0x83,
        UNSUPPORTED_PROTOCOL_VERSION = 0x84,
        CLIENT_IDENTIFIER_NOT_VALID = 0x85,
        BAD_USER_NAME_OR_PASSWORD = 0x86,
        NOT_AUTHORIZED = 0x87,
        SERVER_UNAVAILABLE = 0x88,
        SERVER_BUSY = 0x89,
        BANNED = 0x8A,
        BAD_AUTHENTICATION_METHOD = 0x8C,
        TOPIC_NAME_INVALID = 0x90,
        PACKET_TOO_LARGE = 0x95,
        QUOTA_EXCEEDED = 0x97,
        PAYLOAD_FORMAT_INVALID = 0x99,
        RETAIN_NOT_SUPPORTED = 0x9A,
        QoS_NOT_SUPPORTED = 0x9B,
        USE_ANOTHER_SERVER = 0x9C,
        SERVER_MOVED = 0x9D,
        CONNECTION_RATE_EXCEEDED = 0x9F
    }
}
