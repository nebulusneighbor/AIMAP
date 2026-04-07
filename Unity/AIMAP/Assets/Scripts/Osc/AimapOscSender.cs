using System;
using System.Net.Sockets;
using UnityEngine;

namespace AIMAP.Osc
{
    public sealed class AimapOscSender : MonoBehaviour
    {
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 9000;

        public string Host
        {
            get => host;
            set => host = value;
        }

        public int Port
        {
            get => port;
            set => port = value;
        }

        public void Configure(string remoteHost, int remotePort)
        {
            host = remoteHost;
            port = remotePort;
        }

        public void Send(string address, params object[] arguments)
        {
            try
            {
                using var client = new UdpClient();
                var payload = AimapOscCodec.Encode(address, arguments);
                client.Send(payload, payload.Length, host, port);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to send OSC message {address}: {exception.Message}");
            }
        }
    }
}
