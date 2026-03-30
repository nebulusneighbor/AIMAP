using System.Text;
using UnityEditor;
using UnityEngine;

namespace AIMAP.Editor
{
    public static class AvatarMaterialAudit
    {
        private static readonly string[] ModelPaths =
        {
            "Assets/charactor/Model/xybot/ybotT-Pose.fbx",
            "Assets/charactor/Model/xybot/xbotT-Pose.fbx",
            "Assets/charactor/Model/alien/AlienT-Pose.fbx",
            "Assets/charactor/Model/alien/MaynardT-Pose.fbx",
            "Assets/charactor/Model/alien/jonesT-Pose.fbx",
            "Assets/charactor/Model/alien/zlorpT-Pose.fbx",
            "Assets/charactor/Model/Goblins/GoblinT-Pose.fbx",
            "Assets/charactor/Model/Goblins/MawT-Pose.fbx",
            "Assets/charactor/Model/Goblins/VampireT-Pose.fbx",
            "Assets/charactor/Model/Goblins/WarrockT-Pose.fbx"
        };

        [MenuItem("AIMAP/Diagnostics/Audit Avatar Materials")]
        public static void Audit()
        {
            var builder = new StringBuilder();

            foreach (var path in ModelPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                builder.AppendLine($"MODEL: {path}");

                if (prefab == null)
                {
                    builder.AppendLine("  Missing prefab asset");
                    continue;
                }

                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                builder.AppendLine($"  Renderers: {renderers.Length}");

                foreach (var renderer in renderers)
                {
                    builder.AppendLine($"  Renderer: {renderer.name} ({renderer.GetType().Name})");
                    var materials = renderer.sharedMaterials;

                    if (materials == null || materials.Length == 0)
                    {
                        builder.AppendLine("    No materials");
                        continue;
                    }

                    for (var index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        if (material == null)
                        {
                            builder.AppendLine($"    [{index}] null");
                            continue;
                        }

                        var shaderName = material.shader != null ? material.shader.name : "null";
                        var mainTexture = material.mainTexture != null ? material.mainTexture.name : "null";
                        builder.AppendLine($"    [{index}] {material.name} | Shader={shaderName} | MainTex={mainTexture} | Color={material.color}");
                    }
                }
            }

            Debug.Log(builder.ToString());
        }
    }
}
