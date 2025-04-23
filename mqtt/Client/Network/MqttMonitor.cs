using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Network
{
    public enum ConnectionStatus
    {
        Connected,
        DisconnectedByHost,
        ConnectionError
    }

    public class MqttMonitor : IDisposable
    {
        public delegate void ConnectionLostHandler();
        public event ConnectionLostHandler? OnConnectionLost;

        public delegate void DisconnectHandler();
        public event DisconnectHandler? OnDisconnect;

        private CancellationTokenSource? cts;
        private bool isConnectionEstablished = false;
        private bool isConnectionClosed = true;

        public bool IsConnectionEstablished => isConnectionEstablished;
        public bool IsConnectionClosed => isConnectionClosed;
        public bool IsClientConnected { get; set; } = false;

        public void Start(TcpClient tcpClient, int keepAlive, Action SendPingReq)
        {
            if (!IsConnectionClosed)
            {
                Debug.WriteLine("Mqtt monitor is already runnning!");
                return;
            }

            isConnectionClosed = false;
            isConnectionEstablished = true;
            Debug.WriteLine("Mqtt monitor is runnning now!");

            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            Task.Run(async () =>
            {
                while (!IsConnectionClosed)
                {
                    ConnectionStatus status = CheckConnectionStatus(tcpClient);

                    switch (status)
                    {
                        case ConnectionStatus.DisconnectedByHost:
                            isConnectionEstablished = false;
                            OnConnectionLost?.Invoke();
                            return;

                        case ConnectionStatus.ConnectionError:
                            isConnectionEstablished = false;
                            OnConnectionLost?.Invoke();
                            return;

                        case ConnectionStatus.Connected:
                            break;
                    }
                    await Task.Delay(10);
                }

                Debug.WriteLine("MqttMonitor stopped");
            }, token);

            Task.Run(async () =>
            {
                while (!IsConnectionClosed)
                {
                    await Task.Delay(keepAlive * 1000 / 2);
                    Debug.WriteLine("Send Ping Request");
                    SendPingReq();
                }
            }, token);
        }

        private ConnectionStatus CheckConnectionStatus(TcpClient tcpClient)
        {
            try
            {
                if (tcpClient != null && tcpClient.Client != null && tcpClient.Client.Connected)
                {
                    if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
                    {
                        byte[] buff = new byte[1];
                        int receivedBytes = tcpClient.Client.Receive(buff, SocketFlags.Peek);

                        if (receivedBytes == 0)
                        {
                            return ConnectionStatus.DisconnectedByHost;
                        }
                        else
                        {
                            return ConnectionStatus.Connected;
                        }
                    }
                    return ConnectionStatus.Connected;
                }
                return ConnectionStatus.ConnectionError;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"SocketException occurred: {ex.Message}");
                return ConnectionStatus.ConnectionError;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error occurred: {ex.Message}");
                return ConnectionStatus.ConnectionError;
            }
        }

        public void Dispose()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts = null;
            }
            
            isConnectionClosed = true;
            isConnectionEstablished = false;
            IsClientConnected = false;

            OnConnectionLost = null;
            OnDisconnect = null;

            GC.SuppressFinalize(this);
        }
    }
}
