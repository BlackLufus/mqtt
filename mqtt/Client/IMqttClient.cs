using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client
{
    public interface IMqttClient
    {
        /// <summary>
        /// Establishes a connection to the MQTT broker using the specified client ID and optional username and password.
        /// </summary>
        /// <param name="clientID">The unique identifier for the MQTT client.</param>
        /// <param name="username">The optional username for authentication (default is empty).</param>
        /// <param name="password">The optional password for authentication (default is empty).</param>
        /// <returns>A task representing the asynchronous operation of the connection.</returns>
        public Task Connect(string host, int port, string clientID, string username = "", string password = "");

        /// <summary>
        /// Publishes a message to the specified topic with the given Quality of Service (QoS) level.
        /// This method is synchronous and blocks until the message is sent.
        /// </summary>
        /// <param name="topic">The topic to which the message will be published.</param>
        /// <param name="message">The message payload to be sent.</param>
        /// <param name="qualityOfService">The optional QoS level (default is null, which uses the default QoS level).</param>
        public void Publish(string topic, string message, QualityOfService qualityOfService = QualityOfService.AT_MOST_ONCE);

        /// <summary>
        /// Publishes a message to the specified topic asynchronously with the given Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topic">The topic to which the message will be published.</param>
        /// <param name="message">The message payload to be sent.</param>
        /// /// <param name="qualityOfService">The optional QoS level (default is null, which uses the default QoS level).</param>
        /// <returns>A task representing the asynchronous publish operation.</returns>
        public Task PublishAsync(string topic, string message, QualityOfService qualityOfService = QualityOfService.AT_MOST_ONCE);

        /// <summary>
        /// Publishes a message to the specified topic with the given Quality of Service (QoS) level.
        /// This method is synchronous and blocks until the message is sent.
        /// </summary>
        /// <param name="topic">The topic to which the message will be published.</param>
        /// <param name="message">The message payload to be sent.</param>
        /// <param name="qualityOfService">The optional QoS level (default is null, which uses the default QoS level).</param>
        public void Publish(Topic topic, string message);

        /// <summary>
        /// Publishes a message to the specified topic asynchronously with the given Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topic">The topic to which the message will be published.</param>
        /// <param name="message">The message payload to be sent.</param>
        /// <returns>A task representing the asynchronous publish operation.</returns>
        public Task PublishAsync(Topic topic, string message);

        /// <summary>
        /// Subscribes to a single topic with the specified Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topic">The topic to subscribe to.</param>
        /// <param name="qualityOfService">The optional QoS level (default is null, which uses the default QoS level).</param>
        public void Subscribe(string topic, QualityOfService qualityOfService = QualityOfService.AT_MOST_ONCE);

        /// <summary>
        /// Subscribes to a single topic asynchronously with the specified Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topic">The topic to subscribe to.</param>
        /// <param name="qualityOfService">The optional QoS level (default is null, which uses the default QoS level).</param>
        /// <returns>A task representing the asynchronous subscribe operation.</returns>
        public Task SubscribeAsync(string topic, QualityOfService qualityOfService = QualityOfService.AT_MOST_ONCE);

        /// <summary>
        /// Subscribes to a single topic with the specified Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topic">The topic to subscribe to.</param>
        public void Subscribe(Topic topic);

        /// <summary>
        /// Subscribes to a single topic asynchronously with the specified Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topic">The topic to subscribe to.</param>
        /// <returns>A task representing the asynchronous subscribe operation.</returns>
        public Task SubscribeAsync(Topic topic);

        /// <summary>
        /// Subscribes to multiple topics with the specified Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topics">A list of topics to subscribe to.</param>
        public void Subscribe(Topic[] topics);

        /// <summary>
        /// Subscribes to multiple topics asynchronously with the specified Quality of Service (QoS) level.
        /// </summary>
        /// <param name="topics">A list of topics to subscribe to.</param>
        /// <returns>A task representing the asynchronous subscribe operation.</returns>
        public Task SubscribeAsync(Topic[] topics);

        /// <summary>
        /// Unsubscribes from a single topic.
        /// </summary>
        /// <param name="topic">The topic to unsubscribe from.</param>
        public void Unsubscribe(string topic);

        /// <summary>
        /// Unsubscribes from a single topic asynchronously.
        /// </summary>
        /// <param name="topic">The topic to unsubscribe from.</param>
        /// <returns>A task representing the asynchronous unsubscribe operation.</returns>
        public Task UnsubscribeAsync(string topic);

        /// <summary>
        /// Unsubscribes from a single topic.
        /// </summary>
        /// <param name="topic">The topic to unsubscribe from.</param>
        public void Unsubscribe(Topic topic);

        /// <summary>
        /// Unsubscribes from a single topic asynchronously.
        /// </summary>
        /// <param name="topic">The topic to unsubscribe from.</param>
        /// <returns>A task representing the asynchronous unsubscribe operation.</returns>
        public Task UnsubscribeAsync(Topic topic);

        /// <summary>
        /// Unsubscribes from multiple topics.
        /// </summary>
        /// <param name="topics">A list of topics to unsubscribe from.</param>
        public void Unsubscribe(Topic[] topics);

        /// <summary>
        /// Unsubscribes from multiple topics asynchronously.
        /// </summary>
        /// <param name="topics">A list of topics to unsubscribe from.</param>
        /// <returns>A task representing the asynchronous unsubscribe operation.</returns>
        public Task UnsubscribeAsync(Topic[] topics);

        /// <summary>
        /// Disconnects from the MQTT broker.
        /// Optionally triggers an event after disconnecting.
        /// </summary>
        public void Disconnect();
    }
}
