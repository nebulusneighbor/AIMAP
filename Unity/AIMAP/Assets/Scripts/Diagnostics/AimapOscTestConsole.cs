using extOSC;
using UnityEngine;

namespace AIMAP.Diagnostics
{
    public sealed class AimapOscTestConsole : MonoBehaviour
    {
        [SerializeField] private OSCTransmitter oscTransmitter;
        [SerializeField] private string targetHost = "127.0.0.1";
        [SerializeField] private int targetPort = 11003;
        [SerializeField] private string[] roleIds =
        {
            "dancer1",
            "dancer2",
            "drum1",
            "drum2",
            "bass",
            "guitar",
            "violin"
        };

        private int _nextSkinIndex;
        private int _skyboxIndex;
        private string _lastSentAddress = "None";
        private string _lastTarget = "127.0.0.1:11003";

        public void Configure(OSCTransmitter transmitter)
        {
            oscTransmitter = transmitter;
        }

        private void Awake()
        {
            if (oscTransmitter == null)
            {
                oscTransmitter = GetComponent<OSCTransmitter>();
            }

            if (oscTransmitter != null)
            {
                targetHost = string.IsNullOrWhiteSpace(oscTransmitter.RemoteHost) ? targetHost : oscTransmitter.RemoteHost;
                targetPort = oscTransmitter.RemotePort > 0 ? oscTransmitter.RemotePort : targetPort;
                ApplyTargetSettings();
            }
        }

        private void OnGUI()
        {
            if (oscTransmitter == null)
            {
                return;
            }

            GUI.BeginGroup(new Rect(12f, 244f, 520f, 500f), GUI.skin.box);
            GUILayout.Label("OSC Test Console");
            GUILayout.Label("Desktop sender -> headset receiver");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target IP", GUILayout.Width(80f));
            targetHost = GUILayout.TextField(targetHost, GUILayout.Width(180f));
            GUILayout.Label("Port", GUILayout.Width(40f));
            var portText = GUILayout.TextField(targetPort.ToString(), GUILayout.Width(70f));
            if (int.TryParse(portText, out var parsedPort))
            {
                targetPort = Mathf.Clamp(parsedPort, 1, 65535);
            }

            if (GUILayout.Button("Apply Target", GUILayout.Width(110f)))
            {
                ApplyTargetSettings();
            }

            GUILayout.EndHorizontal();
            GUILayout.Label($"Sending To: {_lastTarget}");
            GUILayout.Label($"Last Sent: {_lastSentAddress}");

            foreach (var roleId in roleIds)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(roleId, GUILayout.Width(80f));

                if (GUILayout.Button("Idle", GUILayout.Width(64f)))
                {
                    SendInt($"/avatar/{roleId}/state", 0);
                }

                if (GUILayout.Button("Play", GUILayout.Width(64f)))
                {
                    SendInt($"/avatar/{roleId}/state", 1);
                }

                if (GUILayout.Button("Skin", GUILayout.Width(64f)))
                {
                    SendInt($"/avatar/{roleId}/skin", _nextSkinIndex);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Skin Set: {_nextSkinIndex}", GUILayout.Width(120f));
            _nextSkinIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(_nextSkinIndex, 0f, 2f, GUILayout.Width(180f)));
            GUILayout.Label("0=xybot 1=alien 2=goblins", GUILayout.Width(170f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Skybox: {_skyboxIndex}", GUILayout.Width(120f));
            _skyboxIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(_skyboxIndex, 0f, 2f, GUILayout.Width(180f)));
            if (GUILayout.Button("Send Skybox", GUILayout.Width(120f)))
            {
                SendInt("/environment/skybox", _skyboxIndex);
            }

            GUILayout.EndHorizontal();
            GUI.EndGroup();
        }

        private void SendInt(string address, int value)
        {
            if (oscTransmitter == null)
            {
                return;
            }

            var message = new OSCMessage(address);
            message.AddValue(OSCValue.Int(value));
            oscTransmitter.Send(message);
            _lastSentAddress = $"{address} [{value}]";
            _lastTarget = $"{targetHost}:{targetPort}";
        }

        private void ApplyTargetSettings()
        {
            if (oscTransmitter == null)
            {
                return;
            }

            oscTransmitter.RemoteHost = string.IsNullOrWhiteSpace(targetHost) ? "127.0.0.1" : targetHost.Trim();
            oscTransmitter.RemotePort = Mathf.Clamp(targetPort, 1, 65535);
            _lastTarget = $"{oscTransmitter.RemoteHost}:{oscTransmitter.RemotePort}";
        }
    }
}
