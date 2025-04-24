using System.Diagnostics;
using System.Net.Sockets;

namespace Mqtt.Client.Network
{
    public enum ConnectionStatus
    {
        Connected,
        DisconnectedByHost,
        ConnectionError
    }

    public class MqttMonitor
    {
        public delegate Task ConnectionLostHandler();
        public event ConnectionLostHandler? OnConnectionLost;

        public delegate Task DisconnectHandler();
        public event DisconnectHandler? OnDisconnect;

        private CancellationTokenSource? cts;
        private bool isConnectionEstablished = false;
        private bool isConnectionClosed = true;

        public bool IsConnectionEstablished => isConnectionEstablished;
        public bool IsConnectionClosed => isConnectionClosed;
        public bool IsClientConnected { get; set; } = false;

        public void Start(TcpClient tcpClient, int keepAlive, OutgoingHandler outgoingHandler)
        {
            if (cts != null)
            {
                Debug.WriteLine("Mqtt monitor is already runnning!");
                return;
            }

            cts = new CancellationTokenSource();
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
            }, token);

            Task.Run(async () =>
            {
                while (!IsConnectionClosed)
                {
                    await Task.Delay(keepAlive * 1000 / 2, token);
                    Debug.WriteLine("Send Ping Request");
                    await outgoingHandler.SendPingReq();
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

        public void Dispose(bool closeConnection)
        {
            if (cts != null)
            {
                Debug.WriteLine("stopping mqtt monitor!");
                cts.Cancel();
                cts = null;
            }

            if (closeConnection)
            {
                isConnectionClosed = true;
            }
            isConnectionEstablished = false;
            IsClientConnected = false;

            OnConnectionLost = null;
            OnDisconnect = null;
        }
    }
}
