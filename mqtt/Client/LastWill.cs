namespace Mqtt.Client
{
    public class LastWill(string topic, string message)
    {
        public string Topic { get; } = topic;
        public string Message { get; } = message;
    }
}
