using System;
using System.Collections.Generic;
using AIMAP.Diagnostics;
using UnityEngine;

namespace AIMAP.Avatars
{
    [Serializable]
    public sealed class DanceMoveMapping
    {
        [Tooltip("OSC value for the move, e.g. hiphop, breakdance, latin, drink.")]
        public string oscValue;

        [Tooltip("Animator state name to crossfade into for this move.")]
        public string animatorStateName;
    }

    /// <summary>
    /// Dedicated avatar slot controller for dancers.
    /// Supports prefab skin swapping plus OSC dance move selection.
    /// </summary>
    public sealed class DancerSlotController : MonoBehaviour
    {
        [SerializeField] private string roleId;
        [SerializeField] private string targetAvatarName;
        [SerializeField] private RuntimeAnimatorController roleController;
        [SerializeField] private string idleStateName = "listen2";
        [SerializeField] private string activeStateName = "hippop";
        [SerializeField] private GameObject fallbackPrefab;
        [SerializeField] private List<AvatarSkinVariant> skinVariants = new List<AvatarSkinVariant>();
        [SerializeField] private bool bindOnStart = true;
        [SerializeField] private int defaultFamilyIndex;

        [Header("Dance Move Mapping")]
        [SerializeField] private List<DanceMoveMapping> moveMappings = new List<DanceMoveMapping>();

        private GameObject _defaultRoot;
        private GameObject _activeRoot;
        private GameObject _spawnedVariantRoot;
        private Animator _activeAnimator;
        private bool _lastIsPlaying;
        private string _lastCrossfadedState;
        private int _currentSkinIndex;
        private AimapOscDiagnostics _diagnostics;

        public string RoleId => roleId;
        public int CurrentSkinIndex => _currentSkinIndex;

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
            if (_activeAnimator == null)
            {
                _lastIsPlaying = isPlaying;
                return;
            }

            var targetState = isPlaying ? GetPlayTargetAnimatorState() : idleStateName;
            if (string.IsNullOrWhiteSpace(targetState))
            {
                _lastIsPlaying = isPlaying;
                return;
            }

            if (isPlaying && _lastIsPlaying
                && !string.IsNullOrEmpty(_lastCrossfadedState)
                && string.Equals(targetState, _lastCrossfadedState, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!isPlaying && !_lastIsPlaying
                && !string.IsNullOrEmpty(_lastCrossfadedState)
                && string.Equals(targetState, _lastCrossfadedState, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastIsPlaying = isPlaying;
            _lastCrossfadedState = targetState;
            _activeAnimator.CrossFadeInFixedTime(targetState, 0.15f, 0);
            _diagnostics?.RegisterStateChange(roleId, isPlaying);
        }

        /// <summary>
        /// Animator state used while performing. If active and idle names match (misconfiguration),
        /// uses the first usable <see cref="moveMappings"/> animator state so dance clips can play.
        /// </summary>
        private string GetPlayTargetAnimatorState()
        {
            if (!string.IsNullOrWhiteSpace(activeStateName)
                && !string.Equals(activeStateName.Trim(), idleStateName?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return activeStateName.Trim();
            }

            if (moveMappings != null)
            {
                for (var i = 0; i < moveMappings.Count; i++)
                {
                    var mapping = moveMappings[i];
                    if (mapping == null || string.IsNullOrWhiteSpace(mapping.animatorStateName))
                    {
                        continue;
                    }

                    var dance = mapping.animatorStateName.Trim();
                    if (string.IsNullOrWhiteSpace(idleStateName)
                        || !string.Equals(dance, idleStateName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return dance;
                    }
                }
            }

            return string.IsNullOrWhiteSpace(activeStateName) ? string.Empty : activeStateName.Trim();
        }

        public void SetDanceMove(string oscMoveValue)
        {
            var mappedState = ResolveMoveState(oscMoveValue);
            if (!string.IsNullOrWhiteSpace(mappedState))
            {
                activeStateName = mappedState;
            }

            if (_lastIsPlaying && _activeAnimator != null && !string.IsNullOrWhiteSpace(activeStateName))
            {
                if (string.Equals(activeStateName, _lastCrossfadedState, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _lastCrossfadedState = activeStateName;
                _activeAnimator.CrossFadeInFixedTime(activeStateName, 0.15f, 0);
            }
        }

        public void SetSkin(int familyIndex)
        {
            Bind();

            _currentSkinIndex = Mathf.Max(0, familyIndex);
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

        private string ResolveMoveState(string oscMoveValue)
        {
            if (string.IsNullOrWhiteSpace(oscMoveValue))
            {
                return null;
            }

            var normalized = oscMoveValue.Trim();
            for (var index = 0; index < moveMappings.Count; index++)
            {
                var mapping = moveMappings[index];
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.oscValue))
                {
                    continue;
                }

                if (string.Equals(mapping.oscValue.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.animatorStateName;
                }
            }

            return normalized;
        }

        private AvatarSkinVariant GetVariantForFamily(int requestedFamilyIndex)
        {
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
            var appliedScale = variant.localScale;
            if (Mathf.Approximately(appliedScale.x, 0f)
                && Mathf.Approximately(appliedScale.y, 0f)
                && Mathf.Approximately(appliedScale.z, 0f))
            {
                appliedScale = Vector3.one;
            }

            variantTransform.localScale = Vector3.Scale(variantTransform.localScale, appliedScale);

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
    }
}
