using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace mqtt.Connection
{
    public class MqttMonitor
    {
        public bool IsConnectionClosed = false;
        public bool IsConnected = false;

        // Enum für verschiedene Verbindungsstatus
        private enum ConnectionStatus
        {
            Connected,
            DisconnectedByHost,
            ConnectionError
        }

        public delegate void ConnectionLostHandler();
        public event ConnectionLostHandler? OnConnectionLost;

        public delegate void DisconnectHandler();
        public event DisconnectHandler? OnDisconnect;

        public void Start(TcpClient tcpClient, CancellationTokenSource cts, int keepAlive, Action SendPingReq)
        {
            CancellationToken token = cts!.Token;
            Task.Run(async () =>
            {
                while (!IsConnectionClosed)
                {
                    ConnectionStatus status = CheckConnectionStatus(tcpClient);

                    switch (status)
                    {
                        case ConnectionStatus.DisconnectedByHost:
                            Console.WriteLine("Die Verbindung wurde vom Host getrennt.");
                            OnDisconnect?.Invoke(); // Spezifischer Handler für Host-getrennte Verbindungen
                            break;

                        case ConnectionStatus.ConnectionError:
                            Console.WriteLine("Es gab einen Verbindungsfehler.");
                            OnConnectionLost?.Invoke(); // Allgemeiner Verbindungsverlust, keine spezifische Host-Trennung
                            break;

                        case ConnectionStatus.Connected:
                            Console.WriteLine("Die Verbindung ist weiterhin aktiv.");
                            break;
                    }
                    await Task.Delay(1000, token);
                }
            }, token);

            Task.Run(async () =>
            {
                while (!IsConnectionClosed)
                {
                    Debug.WriteLine("Send Ping Request");
                    SendPingReq();
                    await Task.Delay(keepAlive * 1000 / 2);
                }
            }, token);
        }

        private ConnectionStatus CheckConnectionStatus(TcpClient tcpClient)
        {
            try
            {
                // Check if the client is connected
                if (tcpClient != null && tcpClient.Client != null && tcpClient.Client.Connected)
                {
                    // Check if data is available or the socket has been closed
                    if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
                    {
                        byte[] buff = new byte[1];
                        int receivedBytes = tcpClient.Client.Receive(buff, SocketFlags.Peek);

                        // If no bytes are received, the connection has been closed
                        if (receivedBytes == 0)
                        {
                            // Host has disconnected the connection, because no more data can be received
                            return ConnectionStatus.DisconnectedByHost;
                        }
                        else
                        {
                            // Data is available, so the connection is still active
                            return ConnectionStatus.Connected;
                        }
                    }
                    // No data available, but the connection is still active
                    return ConnectionStatus.Connected;
                }
                // The client is not connected
                return ConnectionStatus.ConnectionError;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"SocketException occurred: {ex.Message}");
                // The connection has been closed due a socket exception
                return ConnectionStatus.ConnectionError;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error occurred: {ex.Message}");
                // The connection has been closed due to an unexpected error
                return ConnectionStatus.ConnectionError;
            }
        }

        public void ResetVariables()
        {
            IsConnectionClosed = false;
            IsConnected = false;
        }
    }
}
