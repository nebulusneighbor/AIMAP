using System;
using System.Collections.Generic;
using AIMAP.Diagnostics;
using UnityEngine;

namespace AIMAP.Avatars
{
    [Serializable]
    public sealed class AvatarSkinVariant
    {
        public int familyIndex;
        public string displayName;
        public GameObject prefab;
        public RuntimeAnimatorController controllerOverride;
        public Vector3 localPositionOffset;
        public Vector3 localRotationOffset;
        public Vector3 localScale = Vector3.one;
    }

    [Serializable]
    public sealed class AvatarSkinMaterialSet
    {
        [Tooltip("Family index mapping (0=robots, 1=aliens, 2=monsters).")]
        public int familyIndex;
        [Tooltip("Materials to apply by renderer slot order.")]
        public Material[] materials;
    }

    public sealed class AvatarSlotController : MonoBehaviour
    {
        [SerializeField] private string roleId;
        [SerializeField] private string targetAvatarName;
        [SerializeField] private RuntimeAnimatorController roleController;
        [SerializeField] private string idleStateName = "listen";
        [SerializeField] private string activeStateName = "play";
        [SerializeField] private GameObject fallbackPrefab;
        [SerializeField] private List<AvatarSkinVariant> skinVariants = new List<AvatarSkinVariant>();
        [SerializeField] private bool bindOnStart = true;
        [Header("Skin application")]
        [Tooltip("When enabled, SetSkin updates materials on the current avatar instead of swapping avatar prefabs.")]
        [SerializeField] private bool useMaterialSkinOnly = true;
        [Tooltip("Per-avatar material sets for skin families (0=robots, 1=aliens, 2=monsters).")]
        [SerializeField] private List<AvatarSkinMaterialSet> skinMaterialSets = new List<AvatarSkinMaterialSet>();
        [Tooltip("Renderer name filter for material-only skinning. 'alpha_surface' targets only those meshes; leave empty to affect all renderers.")]
        [SerializeField] private string materialSkinRendererNameFilter = "alpha_surface";
        [Tooltip("Initial skin family index (server mapping: 0=robots, 1=aliens, 2=monsters).")]
        [SerializeField] private int defaultFamilyIndex;

        private GameObject _defaultRoot;
        private GameObject _activeRoot;
        private GameObject _spawnedVariantRoot;
        private Animator _activeAnimator;
        private bool _lastIsPlaying;
        private int _currentSkinIndex;
        private AimapOscDiagnostics _diagnostics;

        public string RoleId => roleId;
        public int CurrentSkinIndex => _currentSkinIndex;

        public void Configure(
            string configuredRoleId,
            string configuredTargetAvatarName,
            RuntimeAnimatorController configuredController,
            string configuredIdleStateName,
            string configuredActiveStateName,
            GameObject configuredFallbackPrefab,
            List<AvatarSkinVariant> configuredSkinVariants)
        {
            roleId = configuredRoleId;
            targetAvatarName = configuredTargetAvatarName;
            roleController = configuredController;
            idleStateName = configuredIdleStateName;
            activeStateName = configuredActiveStateName;
            fallbackPrefab = configuredFallbackPrefab;
            skinVariants = configuredSkinVariants ?? new List<AvatarSkinVariant>();
        }

        private void Start()
        {
            _diagnostics = GetComponentInParent<AimapOscDiagnostics>();

            if (bindOnStart)
            {
                Bind();
                SetSkin(defaultFamilyIndex);
                SetPerformanceState(false);
            }
        }

        public void Bind()
        {
            if ((skinVariants == null || skinVariants.Count == 0) && TryGetComponentInParent(out AvatarVariantLibrary variantLibrary))
            {
                skinVariants = new List<AvatarSkinVariant>(variantLibrary.SkinVariants);
            }

            if (_defaultRoot != null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(targetAvatarName))
            {
                _defaultRoot = GameObject.Find(targetAvatarName);
            }

            if (_defaultRoot == null && fallbackPrefab != null)
            {
                _defaultRoot = Instantiate(fallbackPrefab, transform);
                _defaultRoot.name = $"{roleId}_Default";
                ResetLocalTransform(_defaultRoot.transform);
            }

            UseRoot(_defaultRoot, roleController, keepActiveState: true);
        }

        public void SetPerformanceState(bool isPlaying)
        {
            _lastIsPlaying = isPlaying;

            if (_activeAnimator == null)
            {
                return;
            }

            var targetState = isPlaying ? activeStateName : idleStateName;
            if (string.IsNullOrWhiteSpace(targetState))
            {
                return;
            }

            _activeAnimator.CrossFadeInFixedTime(targetState, 0.15f);
            _diagnostics?.RegisterStateChange(roleId, isPlaying);
        }

        /// <summary>
        /// Switches the visible avatar prefab by family index (server mapping: 0=robots, 1=aliens, 2=monsters).
        /// If there is no variant for the requested family, falls back to the default root.
        /// </summary>
        public void SetSkin(int familyIndex)
        {
            Bind();

            _currentSkinIndex = Mathf.Max(0, familyIndex);

            if (useMaterialSkinOnly)
            {
                ApplyMaterialSkin(_activeRoot != null ? _activeRoot : _defaultRoot, _currentSkinIndex);
                _diagnostics?.RegisterSkinChange(roleId, _currentSkinIndex);
                return;
            }

            var variant = GetVariantForFamily(_currentSkinIndex);

            if (variant == null)
            {
                DestroySpawnedVariant();
                if (_defaultRoot != null)
                {
                    UseRoot(_defaultRoot, roleController, keepActiveState: false);
                }
            }
            else
            {
                ActivateVariant(variant);
            }

            _diagnostics?.RegisterSkinChange(roleId, _currentSkinIndex);
        }

        private void ApplyMaterialSkin(GameObject root, int familyIndex)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            var palette = GetFamilyPalette(familyIndex);
            var hasPalette = palette != null && palette.Length > 0;
            Material[] materialsFromLibrary = null;
            var hasLibraryMaterials = TryGetMaterialSet(familyIndex, out materialsFromLibrary);

            if (!hasPalette && !hasLibraryMaterials)
            {
                return;
            }

            var paletteOffset = hasPalette ? GetStableVariantIndex(familyIndex + 100, palette.Length) : 0;
            var hasFilter = !string.IsNullOrWhiteSpace(materialSkinRendererNameFilter);

            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                if (hasFilter
                    && renderer.name.IndexOf(materialSkinRendererNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Intentionally uses material instances so each avatar role can keep independent colors.
                var materials = renderer.materials;
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    if (hasLibraryMaterials)
                    {
                        var mappedMaterial = materialsFromLibrary[Mathf.Min(materialIndex, materialsFromLibrary.Length - 1)];
                        if (mappedMaterial != null)
                        {
                            materials[materialIndex] = mappedMaterial;
                            continue;
                        }
                    }

                    if (hasPalette)
                    {
                        var color = palette[(paletteOffset + materialIndex) % palette.Length];
                        SetMaterialColor(material, color);
                    }
                }

                if (hasLibraryMaterials)
                {
                    renderer.materials = materials;
                }
            }
        }

        private bool TryGetMaterialSet(int familyIndex, out Material[] materials)
        {
            if (skinMaterialSets != null)
            {
                for (var index = 0; index < skinMaterialSets.Count; index++)
                {
                    var set = skinMaterialSets[index];
                    if (set != null && set.familyIndex == familyIndex && set.materials != null && set.materials.Length > 0)
                    {
                        materials = set.materials;
                        return true;
                    }
                }
            }

            materials = null;
            return false;
        }

        private AvatarSkinVariant GetVariantForFamily(int requestedFamilyIndex)
        {
            // Accept family index 0 as valid so it matches server mapping (0=robots).
            if (requestedFamilyIndex < 0)
            {
                return null;
            }

            var matchingVariants = new List<AvatarSkinVariant>();
            for (var index = 0; index < skinVariants.Count; index++)
            {
                var variant = skinVariants[index];
                if (variant != null && variant.familyIndex == requestedFamilyIndex)
                {
                    matchingVariants.Add(variant);
                }
            }

            if (matchingVariants.Count == 0)
            {
                return null;
            }

            var chosenIndex = GetStableVariantIndex(requestedFamilyIndex, matchingVariants.Count);
            return matchingVariants[chosenIndex];
        }

        private void ActivateVariant(AvatarSkinVariant variant)
        {
            if (variant.prefab == null)
            {
                return;
            }

            DestroySpawnedVariant();

            Transform parentTransform;
            Vector3 worldPosition;
            Quaternion worldRotation;

            if (_defaultRoot != null)
            {
                parentTransform = _defaultRoot.transform.parent;
                worldPosition = _defaultRoot.transform.position;
                worldRotation = _defaultRoot.transform.rotation;
                _defaultRoot.SetActive(false);
            }
            else
            {
                parentTransform = transform;
                worldPosition = transform.position;
                worldRotation = transform.rotation;
            }

            _spawnedVariantRoot = Instantiate(variant.prefab, worldPosition, worldRotation, parentTransform);
            _spawnedVariantRoot.name = $"{roleId}_{variant.displayName}";

            var variantTransform = _spawnedVariantRoot.transform;
            if (parentTransform == transform)
            {
                ResetLocalTransform(variantTransform);
            }

            variantTransform.localPosition += variant.localPositionOffset;
            variantTransform.localRotation *= Quaternion.Euler(variant.localRotationOffset);
            variantTransform.localScale = Vector3.Scale(variantTransform.localScale, variant.localScale);
            ApplyFallbackMaterialPalette(_spawnedVariantRoot, variant);

            UseRoot(_spawnedVariantRoot, variant.controllerOverride != null ? variant.controllerOverride : roleController, keepActiveState: false);
        }

        private void UseRoot(GameObject root, RuntimeAnimatorController controller, bool keepActiveState)
        {
            _activeRoot = root;
            if (_activeRoot == null)
            {
                _activeAnimator = null;
                return;
            }

            _activeRoot.SetActive(true);
            _activeAnimator = _activeRoot.GetComponentInChildren<Animator>(true);

            if (_activeAnimator != null && controller != null)
            {
                _activeAnimator.runtimeAnimatorController = controller;
            }

            if (!keepActiveState)
            {
                SetPerformanceState(_lastIsPlaying);
            }
        }

        private void DestroySpawnedVariant()
        {
            if (_spawnedVariantRoot != null)
            {
                Destroy(_spawnedVariantRoot);
                _spawnedVariantRoot = null;
            }
        }

        private static void ResetLocalTransform(Transform target)
        {
            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        private bool TryGetComponentInParent<T>(out T component) where T : Component
        {
            component = GetComponentInParent<T>();
            return component != null;
        }

        private int GetStableVariantIndex(int familyIndex, int variantCount)
        {
            unchecked
            {
                var hash = 17;
                var source = $"{roleId}:{familyIndex}";
                for (var index = 0; index < source.Length; index++)
                {
                    hash = hash * 31 + source[index];
                }

                hash = Mathf.Abs(hash);
                return variantCount == 0 ? 0 : hash % variantCount;
            }
        }

        private void ApplyFallbackMaterialPalette(GameObject root, AvatarSkinVariant variant)
        {
            if (root == null || variant == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            var palette = GetFamilyPalette(variant.familyIndex);
            if (palette == null || palette.Length == 0)
            {
                return;
            }

            var paletteOffset = GetStableVariantIndex(variant.familyIndex + 100, palette.Length);

            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var materials = renderer.materials;

                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null || material.mainTexture != null || !IsWhiteMaterial(material))
                    {
                        continue;
                    }

                    var color = palette[(paletteOffset + materialIndex) % palette.Length];
                    SetMaterialColor(material, color);
                }
            }
        }

        private static bool IsWhiteMaterial(Material material)
        {
            var color = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.color;

            return color.r > 0.95f && color.g > 0.95f && color.b > 0.95f;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static Color[] GetFamilyPalette(int familyIndex)
        {
            return familyIndex switch
            {
                0 => new[]
                {
                    new Color(0.76f, 0.79f, 0.86f),
                    new Color(0.45f, 0.49f, 0.56f),
                    new Color(0.58f, 0.62f, 0.70f),
                    new Color(0.31f, 0.35f, 0.43f),
                },
                1 => new[]
                {
                    new Color(0.36f, 0.78f, 0.52f),
                    new Color(0.54f, 0.85f, 0.74f),
                    new Color(0.62f, 0.46f, 0.89f),
                    new Color(0.26f, 0.55f, 0.76f),
                },
                2 => new[]
                {
                    new Color(0.42f, 0.67f, 0.24f),
                    new Color(0.58f, 0.39f, 0.22f),
                    new Color(0.31f, 0.44f, 0.16f),
                    new Color(0.67f, 0.55f, 0.29f),
                },
                _ => Array.Empty<Color>(),
            };
        }
    }
}
