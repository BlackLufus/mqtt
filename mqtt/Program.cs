// See https://aka.ms/new-console-template for more information
using Mqtt.Client;
using Mqtt.Client.ReasonCode;
using System.Diagnostics;
using static System.Net.Mime.MediaTypeNames;

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
    MqttClient mqtt = new(
        //"test.mosquitto.org"
        new MqttOption
        {
            Version = MqttVersion.MQTT_3_1_1,
            WillRetain = false,
            LastWill = new("uutestuu", "Goodbye World"),
            CleanSession = false,
            KeepAlive = 60
        }
    );

    // Register event handlers
    mqtt.OnConnectionEstablished += (bool sessionPresent, ConnectReturnCode returnCode) => Console.WriteLine("Connection established!\n => Session present: " + sessionPresent + "\n => Return Code: " + returnCode);
    mqtt.OnConnectionFailed += (reason) => Console.WriteLine("Connection Failed: " + reason);
    mqtt.OnConnectionLost += () => Console.WriteLine("Connection lost!");
    mqtt.OnMessageReceived += HandleMessage;
    mqtt.OnSubscribed += (topic, qos) => Console.WriteLine("Subscribed to: " + topic);
    mqtt.OnUnsubscribed += (topic) => Console.WriteLine("Unsubscribed from: " + topic);
    mqtt.OnDisconnected += (reason) => Console.WriteLine("Disconnected: " + reason);
    mqtt.OnError += (at, message) => Console.WriteLine("Error at '" + at + "': " + message);

    // Connect to the broker
    await mqtt.Connect("test.mosquitto.org", 1883, "Client_0815");
    //await mqtt.Connect("broker-cn.emqx.io", 1883, "Client_0815");
    //await mqtt.Connect("broker-cn.emqx.io", 1883, "Client_0815");

    Task.Delay(1000).Wait();

    await mqtt.SubscribeAsync([new Topic("uutestuu", QualityOfService.EXACTLY_ONCE)]);

    Task.Delay(1000).Wait();

    mqtt.Publish(new Topic("uutestuu", QualityOfService.EXACTLY_ONCE), "Hello World 1");

    Task.Delay(1000).Wait();

    await mqtt.Disconnect();

    Task.Delay(1000).Wait();

    //await mqtt.Connect("broker-cn.emqx.io", 1883, "Client_0815");
    await mqtt.Connect("test.mosquitto.org", 1883, "Client_0815");

    //mqtt.Subscribe("test/#");
    // Publish a message
    await mqtt.PublishAsync(new Topic("uutestuu"), "Hello World 1");

    Task.Delay(500).Wait();

    // Subscribe to a topic
    await mqtt.SubscribeAsync([new Topic("uutestuu", QualityOfService.EXACTLY_ONCE), new Topic("test", QualityOfService.AT_LEAST_ONCE)]);

    Task.Delay(500).Wait();

    // Publish a message
    await mqtt.PublishAsync("uutestuu", "Hello World 2", QualityOfService.AT_LEAST_ONCE);

    Task.Delay(5000).Wait();

    await mqtt.SubscribeAsync("uutestuu", QualityOfService.EXACTLY_ONCE);

    // Unsubscribe from a topic
    await mqtt.UnsubscribeAsync("uutestuu");

    Task.Delay(5000).Wait();

    // Subscribe to a topic
    mqtt.Subscribe("uutestuu");

    Task.Delay(10000).Wait();

    // Disconnect from the broker
    await mqtt.Disconnect();

    while (true)
    {
        Task.Delay(50).Wait();
    }
}

void HandleMessage(string topic, string message, QualityOfService qos, bool retain)
{
    Console.WriteLine("=====================================");
    Console.WriteLine("Topic: " + topic + " (" + (int)qos + ")" + (retain ? " (Retained)" : ""));
    Console.WriteLine("Message: " + message);
    Console.WriteLine("=====================================");
}