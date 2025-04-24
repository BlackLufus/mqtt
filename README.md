# MQTT Client for C# (.NET) – Based on MQTT 3.1.1 Specification

A fully asynchronous and event-driven MQTT client for C#, built to comply with the [MQTT 3.1.1 protocol specification](http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html). This client is designed to be lightweight, easy to integrate, and suitable for real-time communication.

## 🔧 Features

- Supports all MQTT 3.1.1 control packets
- Asynchronous API for seamless integration
- Event-based architecture for message handling and status updates
- Built-in support for **Last Will** messages and **QoS 0–2**
- Dynamic **debug mode** for detailed runtime diagnostics
- Buffering logic to handle fragmented network packets

## 🚀 Quick Start

```csharp
mqtt = new(
    new MqttOption
    {
        Version = MqttVersion.MQTT_3_1_1,
        WillRetain = false,
        LastWill = new("uutestuu", "Goodbye World"),
        CleanSession = true,
        KeepAlive = 60
    },
    debug: Debug // Optional
);

void Debug(string log)
{
    Console.WriteLine("Debug: " + log);
};
mqtt.OnConnectionEstablished += (sessionPresent, returnCode) =>
{
    Console.WriteLine("Connected: " + (sessionPresent ? "(session resumed)" : "new session"));
};

mqtt.OnMessageReceived += (topic, message, qos, retain) =>
{
    Console.WriteLine($"Message on {topic}: {message} (QoS {qos}, Retain={retain})");
};

await mqtt.Connect("broker.hivemq.com", 1883, "MyClientId");
mqtt.Subscribe("test/topic", QualityOfService.AtLeastOnce);
mqtt.Publish("test/topic", "Hello MQTT!", QualityOfService.AtLeastOnce);
```

## 📦 Packet Types Supported

`CONNECT` / `CONNACK`

`PUBLISH` / `PUBACK` / `PUBREC` / `PUBREL` / `PUBCOMP`

`SUBSCRIBE` / `SUBACK`

`UNSUBSCRIBE` / `UNSUBACK`

`PINGREQ` / `PINGRESP`

`DISCONNECT`

## 🐞 Debugging
You can enable debug logging by setting `Debug = Action<string>` in the `MqttClient` constructor. Debug output includes:

Packet parsing information

Fragmented packet handling

Error messages and stack traces

Connection lifecycle events

All debug logs are sent to `Debug` method.

## ✅ Events
The client provides fine-grained event hooks:

`OnConnectionEstablished`

`OnConnectionLost`

`OnConnectionFailed`

`OnDisconnected`

`OnMessageReceived`

`OnSubscribed` / `OnUnsubscribed`

`OnError`

## 🧪 Example Use Case
Use this client in any .`C#` application where `MQTT` communication is needed.

## 📄 License
This project is licensed under the `MIT License`. Feel free to use and contribute.