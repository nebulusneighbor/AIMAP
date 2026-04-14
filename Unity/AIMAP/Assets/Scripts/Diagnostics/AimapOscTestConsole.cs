using extOSC;
using UnityEngine;
using System.Collections;

namespace AIMAP.Diagnostics
{
    public sealed class AimapOscTestConsole : MonoBehaviour
    {
        private const int WebOscPort = 11003;
        private const int AbletonOscPort = 2348;

        [SerializeField] private OSCTransmitter oscTransmitter;
        [SerializeField] private string targetHost = "127.0.0.1";
        [SerializeField] private int targetPort = WebOscPort;
        [SerializeField] private string[] roleIds =
        {
            "dancer1",
            "dancer2",
            "drum1",
            "drum2",
            "bass",
            "guitar",
            "strings",
            "winds"
        };
        [SerializeField] private int midiNote = 60;
        [SerializeField] private int randomMidiCount = 8;
        [SerializeField] private Vector2Int randomMidiNoteRange = new Vector2Int(48, 72);
        [SerializeField] private float randomMidiStepSeconds = 0.2f;

        private int _nextSkinIndex;
        private int _skyboxIndex;
        private string _lastSentAddress = "None";
        private string _lastTarget = $"127.0.0.1:{WebOscPort}";
        private Coroutine _randomMidiRoutine;

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

            GUI.BeginGroup(new Rect(12f, 244f, 700f, 620f), GUI.skin.box);
            GUILayout.Label("OSC Test Console");
            GUILayout.Label("Desktop sender -> headset receiver for state, skin, and note-trigger MIDI tests");

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
            GUILayout.BeginHorizontal();
            GUILayout.Label("Quick Ports", GUILayout.Width(80f));
            if (GUILayout.Button(WebOscPort.ToString(), GUILayout.Width(80f)))
            {
                targetPort = WebOscPort;
                ApplyTargetSettings();
            }

            if (GUILayout.Button(AbletonOscPort.ToString(), GUILayout.Width(80f)))
            {
                targetPort = AbletonOscPort;
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

                if (GUILayout.Button("Send Note", GUILayout.Width(90f)))
                {
                    SendMidiNote($"/avatar/{roleId}/midi", midiNote);
                }

                if (GUILayout.Button("Random MIDI", GUILayout.Width(100f)))
                {
                    StartRandomMidiSeries($"/avatar/{roleId}/midi");
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

            if (GUILayout.Button("Reset All", GUILayout.Width(120f)))
            {
                SendInt("/system/resetall", 1);
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.Label("MIDI Test Payload");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Note", GUILayout.Width(35f));
            midiNote = Mathf.RoundToInt(GUILayout.HorizontalSlider(midiNote, 0f, 127f, GUILayout.Width(140f)));
            GUILayout.Label(midiNote.ToString(), GUILayout.Width(30f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Count", GUILayout.Width(40f));
            var countText = GUILayout.TextField(randomMidiCount.ToString(), GUILayout.Width(40f));
            if (int.TryParse(countText, out var parsedCount))
            {
                randomMidiCount = Mathf.Clamp(parsedCount, 1, 32);
            }

            GUILayout.Label("Min", GUILayout.Width(28f));
            var minText = GUILayout.TextField(randomMidiNoteRange.x.ToString(), GUILayout.Width(40f));
            GUILayout.Label("Max", GUILayout.Width(30f));
            var maxText = GUILayout.TextField(randomMidiNoteRange.y.ToString(), GUILayout.Width(40f));
            GUILayout.Label("Step", GUILayout.Width(32f));
            var stepText = GUILayout.TextField(randomMidiStepSeconds.ToString("0.00"), GUILayout.Width(55f));

            var minNote = randomMidiNoteRange.x;
            var maxNote = randomMidiNoteRange.y;
            if (int.TryParse(minText, out var parsedMin))
            {
                minNote = Mathf.Clamp(parsedMin, 0, 127);
            }

            if (int.TryParse(maxText, out var parsedMax))
            {
                maxNote = Mathf.Clamp(parsedMax, 0, 127);
            }

            if (maxNote < minNote)
            {
                maxNote = minNote;
            }

            randomMidiNoteRange = new Vector2Int(minNote, maxNote);

            if (float.TryParse(stepText, out var parsedStep))
            {
                randomMidiStepSeconds = Mathf.Clamp(parsedStep, 0.05f, 2f);
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

        private void SendMidiNote(string address, int note)
        {
            if (oscTransmitter == null)
            {
                return;
            }

            var message = new OSCMessage(address);
            message.AddValue(OSCValue.Int(Mathf.Clamp(note, 0, 127)));
            oscTransmitter.Send(message);
            _lastSentAddress = $"{address} [note:{note}]";
            _lastTarget = $"{targetHost}:{targetPort}";
        }

        private void StartRandomMidiSeries(string address)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_randomMidiRoutine != null)
            {
                StopCoroutine(_randomMidiRoutine);
            }

            _randomMidiRoutine = StartCoroutine(SendRandomMidiSeries(address));
        }

        private IEnumerator SendRandomMidiSeries(string address)
        {
            var stepDelay = new WaitForSeconds(randomMidiStepSeconds);
            var noteCount = Mathf.Clamp(randomMidiCount, 1, 32);
            var minNote = Mathf.Clamp(randomMidiNoteRange.x, 0, 127);
            var maxNote = Mathf.Clamp(randomMidiNoteRange.y, minNote, 127);

            for (var index = 0; index < noteCount; index++)
            {
                var note = Random.Range(minNote, maxNote + 1);
                SendMidiNote(address, note);
                yield return stepDelay;
            }

            _randomMidiRoutine = null;
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
