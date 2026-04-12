using System.Text;
using extOSC;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AIMAP.Diagnostics
{
    public sealed class AimapOscPlaneCanvasBootstrap : MonoBehaviour
    {
        private const string TargetSceneName = "animationtest";
        private const string TargetPlaneName = "Plane";
        private const string CanvasObjectName = "OSC Receive Canvas";
        private const string TextObjectName = "OSC Receive Text";
        private const int ListenPort = 11003;
        private static readonly string[] RoleIds = { "dancer1", "dancer2", "drum1", "drum2", "bass", "guitar", "violin" };
        private static readonly Vector3 CanvasLocalPosition = new Vector3(0f, 0.05f, 0f);
        private static readonly Quaternion CanvasLocalRotation = Quaternion.Euler(90f, 0f, 0f);
        private static readonly Vector3 CanvasLocalScale = Vector3.one * 0.005f;
        private static readonly Vector2 CanvasSize = new Vector2(1000f, 1000f);

        private static AimapOscPlaneCanvasBootstrap _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var host = new GameObject(nameof(AimapOscPlaneCanvasBootstrap));
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<AimapOscPlaneCanvasBootstrap>();
        }

        private OSCReceiver _oscReceiver;
        private Text _messageText;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySetup(SceneManager.GetActiveScene());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeFromServer();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            TrySetup(scene);
        }

        private void TrySetup(Scene scene)
        {
            if (!string.Equals(scene.name, TargetSceneName, System.StringComparison.Ordinal))
            {
                UnsubscribeFromServer();
                _oscReceiver = null;
                _messageText = null;
                return;
            }

            var plane = GameObject.Find(TargetPlaneName);
            if (plane == null)
            {
                return;
            }

            _messageText = EnsureCanvasText(plane.transform);
            _oscReceiver = EnsureOscReceiver(plane);
            BindExtOscAddresses(_oscReceiver);
            _messageText.text = $"Waiting for OSC message on port {ListenPort}...";
        }

        private void HandleMessageReceived(string address, OSCMessage message)
        {
            if (_messageText == null || message == null)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("OSC Last Received");
            builder.AppendLine(address);
            builder.Append("Args: ");

            if (message.Values == null || message.Values.Count <= 0)
            {
                builder.Append("none");
            }
            else
            {
                for (var index = 0; index < message.Values.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(message.Values[index]);
                }
            }

            _messageText.text = builder.ToString();
        }

        private static OSCReceiver EnsureOscReceiver(GameObject plane)
        {
            var receiver = plane.GetComponent<OSCReceiver>();
            if (receiver == null)
            {
                receiver = plane.AddComponent<OSCReceiver>();
            }

            receiver.LocalPort = ListenPort;
            return receiver;
        }

        private void BindExtOscAddresses(OSCReceiver receiver)
        {
            if (receiver == null)
            {
                return;
            }

            receiver.ClearBinds();
            for (var index = 0; index < RoleIds.Length; index++)
            {
                var role = RoleIds[index];
                var stateAddress = $"/avatar/{role}/state";
                var skinAddress = $"/avatar/{role}/skin";
                var midiAddress = $"/avatar/{role}/midi";
                receiver.Bind(stateAddress, message => HandleMessageReceived(stateAddress, message));
                receiver.Bind(skinAddress, message => HandleMessageReceived(skinAddress, message));
                receiver.Bind(midiAddress, message => HandleMessageReceived(midiAddress, message));
            }

            const string skyboxAddress = "/environment/skybox";
            receiver.Bind(skyboxAddress, message => HandleMessageReceived(skyboxAddress, message));
        }

        private static Text EnsureCanvasText(Transform planeTransform)
        {
            var canvasTransform = planeTransform.Find(CanvasObjectName);
            Canvas canvas;
            if (canvasTransform == null)
            {
                var canvasObject = new GameObject(CanvasObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasTransform = canvasObject.transform;
                canvasTransform.SetParent(planeTransform, false);

                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = null;
                canvas.planeDistance = 1f;

                var rect = (RectTransform)canvasTransform;
                rect.sizeDelta = CanvasSize;

                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.dynamicPixelsPerUnit = 10f;
            }
            else
            {
                canvas = canvasTransform.GetComponent<Canvas>();
            }

            // Keep alignment deterministic even for previously created canvases.
            canvasTransform.localPosition = CanvasLocalPosition;
            canvasTransform.localRotation = CanvasLocalRotation;
            canvasTransform.localScale = CanvasLocalScale;
            ((RectTransform)canvasTransform).sizeDelta = CanvasSize;

            var textTransform = canvasTransform.Find(TextObjectName);
            Text text;
            if (textTransform == null)
            {
                var textObject = new GameObject(TextObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                textTransform = textObject.transform;
                textTransform.SetParent(canvasTransform, false);

                var textRect = (RectTransform)textTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(20f, 20f);
                textRect.offsetMax = new Vector2(-20f, -20f);

                text = textObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 64;
                text.alignment = TextAnchor.UpperLeft;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.color = Color.black;
                text.text = $"Waiting for OSC message on port {ListenPort}...";
            }
            else
            {
                text = textTransform.GetComponent<Text>();
            }

            return text;
        }

        private void UnsubscribeFromServer()
        {
            if (_oscReceiver != null)
            {
                _oscReceiver.ClearBinds();
            }
        }
    }
}
