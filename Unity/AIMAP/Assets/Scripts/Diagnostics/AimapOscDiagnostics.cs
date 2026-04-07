using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AIMAP.Diagnostics
{
    public sealed class AimapOscDiagnostics : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private string lastAddress = "None";
        [SerializeField] private string lastRole = "None";
        [SerializeField] private int currentSkyboxIndex;

        private readonly Dictionary<string, int> _skinByRole = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _stateByRole = new Dictionary<string, string>();

        public void Configure(bool overlayVisible)
        {
            showOverlay = overlayVisible;
        }

        public void RegisterMessage(string address)
        {
            lastAddress = string.IsNullOrWhiteSpace(address) ? "None" : address;
        }

        public void RegisterStateChange(string roleId, bool isPlaying)
        {
            lastRole = roleId;
            _stateByRole[roleId] = isPlaying ? "playing" : "idle";
        }

        public void RegisterSkinChange(string roleId, int skinIndex)
        {
            lastRole = roleId;
            _skinByRole[roleId] = skinIndex;
        }

        public void RegisterSkyboxChange(int skyboxIndex)
        {
            currentSkyboxIndex = skyboxIndex;
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("OSC Diagnostics");
            builder.AppendLine($"Last Address: {lastAddress}");
            builder.AppendLine($"Last Avatar: {lastRole}");
            builder.AppendLine($"Current Skybox: {currentSkyboxIndex}");

            foreach (var stateEntry in _stateByRole)
            {
                var skinIndex = _skinByRole.TryGetValue(stateEntry.Key, out var storedSkin) ? storedSkin : 0;
                builder.AppendLine($"{stateEntry.Key}: {stateEntry.Value}, skin {skinIndex}");
            }

            GUI.Box(new Rect(12f, 12f, 340f, 220f), builder.ToString());
        }
    }
}
