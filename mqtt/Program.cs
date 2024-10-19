// See https://aka.ms/new-console-template for more information
using mqtt;

Console.WriteLine("MQTT");
Mqtt mqtt = new Mqtt();
//mqtt.Connect("test.mosquitto.org", 1883, "Client_0815");
mqtt.Connect("broker.hivemq.com", 1883, "Client_0815");
mqtt.Publish("uutestuu", "Hello World 3");
mqtt.Publish("uutestuu", "Hello World 2");
