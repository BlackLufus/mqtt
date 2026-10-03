using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Mqtt.Client;
using Mqtt.Core;

namespace Mqtt.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MqttClient mqtt;

        public MainWindow()
        {
            InitializeComponent();

            mqtt = new(
                new MqttOption
                {
                    Version = MqttVersion.MQTT_3_1_1,
                    WillRetain = false,
                    LastWill = new("uutestuu", "Goodbye World"),
                    CleanSession = true,
                    KeepAlive = 60,
                },
                debug: Debug
            );

            void Debug(string log)
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage("Debug: " + log);
                });
            }
            ;
            mqtt.OnConnectionEstablished += (sessionPresent, returnCode) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage(
                        "Connected successfully" + (sessionPresent ? " (sessionPresent)" : "")
                    );
                });
            };
            mqtt.OnError += (at, reason) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage(at + ": " + reason);
                });
            };
            mqtt.OnConnectionFailed += (reason) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage("Connection failed: " + reason);
                });
            };
            mqtt.OnConnectionLost += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage("Connection lost");
                });
            };
            mqtt.OnDisconnected += (reason) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage("Disconnected");
                });
            };
            mqtt.OnMessageReceived += (topic, message, qos, retain) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage(
                        $"Received message on topic {topic}{(retain ? " (Retained)" : "")}: ({(int)qos}) {message}"
                    );
                });
            };
            mqtt.OnSubscribed += (topic, qos) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage($"Subscribed to topic '{topic}' ({(int)qos})");
                });
            };
            mqtt.OnUnsubscribed += (topic) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AddMessage($"Unsubscribed from topic '{topic}'");
                });
            };
        }

        private void AddMessage(string message)
        {
            Debug.WriteLine(message);
            Label label = new();
            TextBlock textBlock = new() { Text = message, TextWrapping = TextWrapping.Wrap };
            label.Content = textBlock;
            Messages.Children.Add(label);
            if (MessageContainer.VerticalOffset == MessageContainer.ScrollableHeight)
            {
                MessageContainer.ScrollToEnd();
            }
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            AddMessage("Connecting to " + IpAdress.Text + "...");
            //await mqtt.Connect("test.mosquitto.org", 1883, "Test");
            //await mqtt.Connect("broker-cn.emqx.io", 1883, "Client_0815");

            await mqtt.Connect(
                //"test.mosquitto.org",
                //"broker-cn.emqx.io",
                IpAdress.Text,
                1883,
                "Client_0815"
            );
        }

        private void Publish_Click(object sender, RoutedEventArgs e)
        {
            // Hol das Template der TextBox
            var template = TopicTemplate.Template;

            // Suche das 'topic' TextBox-Element im Template
            TextBox? topicTextBox = template.FindName("Topic", TopicTemplate) as TextBox;

            if (topicTextBox == null || topicTextBox.Text == "" || Message.Text == "")
                return;

            mqtt.Publish(
                topicTextBox.Text,
                Message.Text,
                (QualityOfService)MessageQoS.SelectedIndex
            );

            Message.Text = "";
            Message.Clear();
        }

        private void Subscribe_Click(object sender, RoutedEventArgs e)
        {
            if (SubScribeTopic.Text == "")
                return;
            mqtt.Subscribe(SubScribeTopic.Text, (QualityOfService)QoS.SelectedIndex);
        }

        private void Unsubscribe_Click(object sender, RoutedEventArgs e)
        {
            if (UnSubScribeTopic.Text == "")
                return;
            mqtt.Unsubscribe(UnSubScribeTopic.Text);
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            mqtt.Disconnect();
        }
    }
}
