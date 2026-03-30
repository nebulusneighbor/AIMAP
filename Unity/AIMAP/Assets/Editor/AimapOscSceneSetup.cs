using System.Collections.Generic;
using AIMAP.Avatars;
using AIMAP.Diagnostics;
using AIMAP.Environment;
using AIMAP.Osc;
using extOSC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIMAP.Editor
{
    public static class AimapOscSceneSetup
    {
        private const string MainScenePath = "Assets/Scenes/AIMAPVR.unity";
        private const string TestScenePath = "Assets/Scenes/AIMAPVR_OscTest.unity";
        private const int OscListenPort = 9000;

        private static readonly RoleConfig[] RoleConfigs =
        {
            new RoleConfig("dancer1", "ybotDancer1", "Assets/charactor/DanceController 1.controller", "listen2", "hippop", "Assets/charactor/Model/xybot/ybotT-Pose.fbx", new Vector3(-6f, 0f, 3f)),
            new RoleConfig("dancer2", "ybotDancer2", "Assets/charactor/DanceController 2.controller", "listen2", "salsa", "Assets/charactor/Model/xybot/ybotT-Pose.fbx", new Vector3(6f, 0f, 3f)),
            new RoleConfig("drum1", "ybotDrum1", "Assets/charactor/DrumCrushPlayingController.controller", "sit1", "drum1", "Assets/charactor/Model/xybot/ybotT-Pose.fbx", new Vector3(-4f, 0f, -1f)),
            new RoleConfig("drum2", "ybotDrum2", "Assets/charactor/DrumtomPlayingController.controller", "sit2", "drum2", "Assets/charactor/Model/xybot/ybotT-Pose.fbx", new Vector3(4f, 0f, -1f)),
            new RoleConfig("bass", "ybotBass", "Assets/charactor/BassPlayingController.controller", "listen2", "guitarPlaying2", "Assets/charactor/Model/xybot/ybotT-Pose.fbx", new Vector3(-2f, 0f, 1f)),
            new RoleConfig("guitar", "xbotGuitar", "Assets/charactor/GuitarPlayingController.controller", "listen", "Guitarplay1", "Assets/charactor/Model/xybot/xbotT-Pose.fbx", new Vector3(2f, 0f, 1f)),
            new RoleConfig("violin", "xbotViolin", "Assets/charactor/ViolinPlayingController.controller", "listen", "violin", "Assets/charactor/Model/xybot/xbotT-Pose.fbx", new Vector3(0f, 0f, 4f)),
        };

        private static readonly VariantConfig[] VariantConfigs =
        {
            new VariantConfig(0, "Assets/charactor/Model/xybot/ybotT-Pose.fbx"),
            new VariantConfig(0, "Assets/charactor/Model/xybot/xbotT-Pose.fbx"),
            new VariantConfig(1, "Assets/charactor/Model/alien/AlienT-Pose.fbx"),
            new VariantConfig(1, "Assets/charactor/Model/alien/MaynardT-Pose.fbx"),
            new VariantConfig(1, "Assets/charactor/Model/alien/jonesT-Pose.fbx"),
            new VariantConfig(1, "Assets/charactor/Model/alien/zlorpT-Pose.fbx"),
            new VariantConfig(2, "Assets/charactor/Model/Goblins/GoblinT-Pose.fbx"),
            new VariantConfig(2, "Assets/charactor/Model/Goblins/MawT-Pose.fbx"),
            new VariantConfig(2, "Assets/charactor/Model/Goblins/VampireT-Pose.fbx"),
            new VariantConfig(2, "Assets/charactor/Model/Goblins/WarrockT-Pose.fbx"),
        };

        [MenuItem("AIMAP/OSC/Setup Scenes")]
        public static void RunAll()
        {
            SetupMainScene();
            SetupOscTestScene();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
        }

        public static void SetupMainScene()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var manager = CreateOrReplaceManager(scene, includeTestConsole: false);

            foreach (var roleConfig in RoleConfigs)
            {
                CreateSlot(manager.transform, roleConfig, useSceneAvatarBinding: true);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void SetupOscTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "AIMAPVR_OscTest";

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 6f, -12f);
            cameraObject.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;

            var manager = CreateOrReplaceManager(scene, includeTestConsole: true);

            foreach (var roleConfig in RoleConfigs)
            {
                CreateSlot(manager.transform, roleConfig, useSceneAvatarBinding: false);
            }

            EditorSceneManager.SaveScene(scene, TestScenePath);
        }

        private static GameObject CreateOrReplaceManager(Scene scene, bool includeTestConsole)
        {
            var existingManager = GameObject.Find("AIMAP OSC Manager");
            if (existingManager != null)
            {
                Object.DestroyImmediate(existingManager);
            }

            var manager = new GameObject("AIMAP OSC Manager");
            SceneManager.MoveGameObjectToScene(manager, scene);

            var receiver = manager.AddComponent<OSCReceiver>();
            receiver.LocalPort = OscListenPort;

            var diagnostics = manager.AddComponent<AimapOscDiagnostics>();
            diagnostics.Configure(true);

            var environmentController = manager.AddComponent<SkyboxEnvironmentController>();
            environmentController.Configure(LoadSkyboxMaterials(), 0);

            manager.AddComponent<AimapOscRouter>();

            if (includeTestConsole)
            {
                var transmitter = manager.AddComponent<OSCTransmitter>();
                transmitter.RemoteHost = "127.0.0.1";
                transmitter.RemotePort = OscListenPort;

                var testConsole = manager.AddComponent<AimapOscTestConsole>();
                testConsole.Configure(transmitter);
            }

            return manager;
        }

        private static void CreateSlot(Transform parent, RoleConfig roleConfig, bool useSceneAvatarBinding)
        {
            var slotObject = new GameObject($"{roleConfig.RoleId}_Slot");
            slotObject.transform.SetParent(parent);
            slotObject.transform.localPosition = roleConfig.TestScenePosition;
            slotObject.transform.localRotation = Quaternion.identity;
            slotObject.transform.localScale = Vector3.one;

            var slotController = slotObject.AddComponent<AvatarSlotController>();
            var fallbackPrefab = useSceneAvatarBinding ? null : LoadPrefab(roleConfig.FallbackPrefabPath);
            slotController.Configure(
                roleConfig.RoleId,
                useSceneAvatarBinding ? roleConfig.TargetAvatarName : string.Empty,
                LoadController(roleConfig.ControllerPath),
                roleConfig.IdleStateName,
                roleConfig.ActiveStateName,
                fallbackPrefab,
                BuildSkinVariants(roleConfig.ControllerPath));
        }

        private static List<AvatarSkinVariant> BuildSkinVariants(string controllerPath)
        {
            var controller = LoadController(controllerPath);
            var variants = new List<AvatarSkinVariant>();

            for (var index = 0; index < VariantConfigs.Length; index++)
            {
                var prefab = LoadPrefab(VariantConfigs[index].PrefabPath);
                if (prefab == null)
                {
                    continue;
                }

                variants.Add(new AvatarSkinVariant
                {
                    familyIndex = VariantConfigs[index].FamilyIndex,
                    displayName = prefab.name,
                    prefab = prefab,
                    controllerOverride = controller,
                    localScale = Vector3.one
                });
            }

            return variants;
        }

        private static RuntimeAnimatorController LoadController(string path)
        {
            return AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
        }

        private static GameObject LoadPrefab(string path)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static Material[] LoadSkyboxMaterials()
        {
            return new[]
            {
                AssetDatabase.LoadAssetAtPath<Material>("Assets/hdri/HDRI_Training_Stage_MAT.mat"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/hdri/AlienSceneSkybox.mat"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/hdri/GoblinsScene.mat")
            };
        }

        private static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MainScenePath, true),
                new EditorBuildSettingsScene(TestScenePath, true)
            };

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private readonly struct RoleConfig
        {
            public RoleConfig(
                string roleId,
                string targetAvatarName,
                string controllerPath,
                string idleStateName,
                string activeStateName,
                string fallbackPrefabPath,
                Vector3 testScenePosition)
            {
                RoleId = roleId;
                TargetAvatarName = targetAvatarName;
                ControllerPath = controllerPath;
                IdleStateName = idleStateName;
                ActiveStateName = activeStateName;
                FallbackPrefabPath = fallbackPrefabPath;
                TestScenePosition = testScenePosition;
            }

            public string RoleId { get; }
            public string TargetAvatarName { get; }
            public string ControllerPath { get; }
            public string IdleStateName { get; }
            public string ActiveStateName { get; }
            public string FallbackPrefabPath { get; }
            public Vector3 TestScenePosition { get; }
        }

        private readonly struct VariantConfig
        {
            public VariantConfig(int familyIndex, string prefabPath)
            {
                FamilyIndex = familyIndex;
                PrefabPath = prefabPath;
            }

            public int FamilyIndex { get; }
            public string PrefabPath { get; }
        }
    }
}
