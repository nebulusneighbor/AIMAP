using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace AIMAP.Osc
{
    public sealed class AimapOscServer : MonoBehaviour
    {
        [SerializeField] private int listenPort = 9000;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool logReceivedMessages;

        private readonly ConcurrentQueue<AimapOscMessage> _messageQueue = new ConcurrentQueue<AimapOscMessage>();
        private Thread _receiveThread;
        private UdpClient _udpClient;
        private volatile bool _isRunning;

        public event Action<AimapOscMessage> MessageReceived;

        public int ListenPort => listenPort;
        public bool IsRunning => _isRunning;

        public void Configure(int port, bool startOnAwake, bool shouldLogMessages)
        {
            listenPort = port;
            autoStart = startOnAwake;
            logReceivedMessages = shouldLogMessages;
        }

        private void Start()
        {
            if (autoStart)
            {
                StartListening();
            }
        }

        private void Update()
        {
            while (_messageQueue.TryDequeue(out var message))
            {
                if (logReceivedMessages)
                {
                    Debug.Log($"OSC {message.Address} ({message.ArgumentCount} args)");
                }

                MessageReceived?.Invoke(message);
            }
        }

        public void StartListening()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                _udpClient = new UdpClient(listenPort);
                _udpClient.Client.ReceiveTimeout = 1000;
                _isRunning = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = $"AimapOscServer:{listenPort}"
                };
                _receiveThread.Start();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to start OSC server on port {listenPort}: {exception.Message}");
                StopListening();
            }
        }

        public void StopListening()
        {
            _isRunning = false;

            try
            {
                _udpClient?.Close();
            }
            catch
            {
                // Ignore shutdown exceptions while the receive thread exits.
            }

            _udpClient = null;

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(250);
            }

            _receiveThread = null;
        }

        private void OnDisable()
        {
            StopListening();
        }

        private void OnDestroy()
        {
            StopListening();
        }

        private void ReceiveLoop()
        {
            while (_isRunning && _udpClient != null)
            {
                try
                {
                    var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    var data = _udpClient.Receive(ref remoteEndPoint);
                    if (AimapOscCodec.TryDecode(data, remoteEndPoint, out var message))
                    {
                        _messageQueue.Enqueue(message);
                    }
                }
                catch (SocketException socketException)
                {
                    if (socketException.SocketErrorCode != SocketError.TimedOut && _isRunning)
                    {
                        Debug.LogWarning($"OSC receive error on port {listenPort}: {socketException.Message}");
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (_isRunning)
                    {
                        Debug.LogWarning($"Unexpected OSC receive error on port {listenPort}: {exception.Message}");
                    }
                }
            }
        }
    }
}
