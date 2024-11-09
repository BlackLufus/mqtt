// See https://aka.ms/new-console-template for more information
using mqtt;

await Start();
try
{
    //await Start();
}
catch (Exception ex)
{
    Console.WriteLine("Exception: " + ex.Message);
}

async Task Start()
{
    Console.WriteLine("MQTT");
    // Create a new MQTT client instance
    Mqtt mqtt = new()
    {
        MQTTVersion = Mqtt.Version.MQTT_3_1_1,
        WillRetain = false,
        QoS = Mqtt.QualityOfService.EXACTLY_ONCE,
        CleanSession = true,
        KeepAlive = 60,
    };

    // Register event handlers
    mqtt.OnMessageReceived += HandleMessage;
    mqtt.OnConnectionFailed += () => Console.WriteLine("Connection Failed");
    mqtt.OnConnectionLost += () => Console.WriteLine("Connection lost!");

    // Set the last will and testament
    mqtt.SetWill("uutestuu", "Goodbye World");

    // Connect to the broker
    await mqtt.Connect("test.mosquitto.org", 1883, "Client_0815");
    //await mqtt.Connect("broker-cn.emqx.io", 1883, "Client_0815");

    // Publish a message
    mqtt.Publish("uutestuu", "Hello World 1");

    Task.Delay(500).Wait();

    // Subscribe to a topic
    mqtt.Subscribe("uutestuu");

    Task.Delay(500).Wait();

    // Publish a message
    mqtt.Publish("uutestuu", "Hello World 2");

    Task.Delay(5000).Wait();

    // Unsubscribe from a topic
    mqtt.Unsubscribe("uutestuu");

    Task.Delay(5000).Wait();

    // Subscribe to a topic
    mqtt.Subscribe("uutestuu");

    Task.Delay(2000).Wait();

    // Disconnect from the broker
    //mqtt.Disconnect();

    while (true)
    {
        Task.Delay(50).Wait();
    }
}

void HandleMessage(string topic, string message)
{
    Console.WriteLine("=====================================");
    Console.WriteLine("Topic: " + topic);
    Console.WriteLine("Message: " + message);
    Console.WriteLine("=====================================");
}