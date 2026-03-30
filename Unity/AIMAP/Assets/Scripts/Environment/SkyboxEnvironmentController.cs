using AIMAP.Diagnostics;
using UnityEngine;

namespace AIMAP.Environment
{
    public sealed class SkyboxEnvironmentController : MonoBehaviour
    {
        [SerializeField] private Material[] skyboxMaterials;
        [SerializeField] private int defaultSkyboxIndex;
        [SerializeField] private bool applyOnStart = true;
        [SerializeField] private int trainingStageSkyboxIndex;
        [SerializeField] private GameObject trainingStageGlowObject;
        [SerializeField] private string trainingStageGlowObjectName = "PolygonGrid_Glow";

        private AimapOscDiagnostics _diagnostics;
        private int _currentSkyboxIndex;

        public int CurrentSkyboxIndex => _currentSkyboxIndex;

        public void Configure(Material[] materials, int initialSkyboxIndex)
        {
            skyboxMaterials = materials;
            defaultSkyboxIndex = initialSkyboxIndex;
        }

        private void Start()
        {
            _diagnostics = GetComponent<AimapOscDiagnostics>();
            ResolveTrainingStageGlowObject();

            if (applyOnStart)
            {
                SetSkybox(defaultSkyboxIndex);
            }
        }

        public void SetSkybox(int skyboxIndex)
        {
            if (skyboxMaterials == null || skyboxMaterials.Length == 0)
            {
                return;
            }

            _currentSkyboxIndex = Mathf.Clamp(skyboxIndex, 0, skyboxMaterials.Length - 1);
            var skyboxMaterial = skyboxMaterials[_currentSkyboxIndex];
            if (skyboxMaterial == null)
            {
                return;
            }

            RenderSettings.skybox = skyboxMaterial;
            UpdateTrainingStageGlowState();
            DynamicGI.UpdateEnvironment();
            _diagnostics?.RegisterSkyboxChange(_currentSkyboxIndex);
        }

        private void ResolveTrainingStageGlowObject()
        {
            if (trainingStageGlowObject != null || string.IsNullOrWhiteSpace(trainingStageGlowObjectName))
            {
                return;
            }

            trainingStageGlowObject = GameObject.Find(trainingStageGlowObjectName);
        }

        private void UpdateTrainingStageGlowState()
        {
            ResolveTrainingStageGlowObject();

            if (trainingStageGlowObject == null)
            {
                return;
            }

            trainingStageGlowObject.SetActive(_currentSkyboxIndex == trainingStageSkyboxIndex);
        }
    }
}
