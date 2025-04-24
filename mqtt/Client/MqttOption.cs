namespace Mqtt.Client
{
    public class MqttOption
    {
        /// <summary>
        /// Variables for the connection state
        /// </summary>
        public MqttVersion Version { get; set; } = MqttVersion.MQTT_3_1_1;
        public bool WillRetain { get; set; } = false;
        public LastWill? LastWill { get; set; }
        public QualityOfService QoS { get; set; } = QualityOfService.AT_LEAST_ONCE;
        public bool CleanSession { get; set; } = true;
        public int KeepAlive { get; set; } = 20;
        public int SessionExpiryInterval { get; set; } = 10;
    }
}
