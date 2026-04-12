using System;
using System.Text;
using TMPro;
using extOSC;
using UnityEngine;

namespace AIMAP.Diagnostics
{
    [DisallowMultipleComponent]
    public sealed class AimapOscPortDebugPanel : MonoBehaviour
    {
        private const string TextObjectPrefix = "OSCmessage";

        [Serializable]
        private struct PortTextBinding
        {
            public int port;
            public TMP_Text text;
        }

        [SerializeField] private PortTextBinding[] portOutputs =
        {
            new PortTextBinding { port = 11003 },
            new PortTextBinding { port = 2348 }
        };

        [SerializeField] private string waitingFormat = "Waiting for OSC on port {0}...";

        private void Awake()
        {
            AutoAssignTexts();
            ResetDisplay();
        }

        private void OnEnable()
        {
            AutoAssignTexts();
            ResetDisplay();
        }

        private void OnValidate()
        {
            AutoAssignTexts();
        }

        public void RegisterMessage(int port, string address, OSCMessage message)
        {
            var output = FindOutput(port);
            if (output == null)
            {
                return;
            }

            output.text = BuildMessage(port, address, message);
        }

        private void ResetDisplay()
        {
            if (portOutputs == null)
            {
                return;
            }

            for (var index = 0; index < portOutputs.Length; index++)
            {
                if (portOutputs[index].text == null)
                {
                    continue;
                }

                portOutputs[index].text.text = string.Format(waitingFormat, portOutputs[index].port);
            }
        }

        private void AutoAssignTexts()
        {
            if (portOutputs == null)
            {
                return;
            }

            for (var index = 0; index < portOutputs.Length; index++)
            {
                if (portOutputs[index].text != null)
                {
                    continue;
                }

                var target = GameObject.Find($"{TextObjectPrefix}{portOutputs[index].port}");
                if (target == null || !target.TryGetComponent<TMP_Text>(out var tmpText))
                {
                    continue;
                }

                portOutputs[index].text = tmpText;
            }
        }

        private TMP_Text FindOutput(int port)
        {
            if (portOutputs == null)
            {
                return null;
            }

            for (var index = 0; index < portOutputs.Length; index++)
            {
                if (portOutputs[index].port != port)
                {
                    continue;
                }

                if (portOutputs[index].text == null)
                {
                    AutoAssignTexts();
                }

                return portOutputs[index].text;
            }

            return null;
        }

        private static string BuildMessage(int port, string address, OSCMessage message)
        {
            var builder = new StringBuilder();
            builder.Append("Port ");
            builder.AppendLine(port.ToString());
            builder.Append("Address: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(address) ? "None" : address);
            builder.Append("Args: ");

            if (message?.Values == null || message.Values.Count == 0)
            {
                builder.Append("none");
                return builder.ToString();
            }

            for (var index = 0; index < message.Values.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(message.Values[index]);
            }

            return builder.ToString();
        }
    }
}
