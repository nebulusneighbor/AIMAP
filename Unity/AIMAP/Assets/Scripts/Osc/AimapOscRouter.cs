using System;
using AIMAP.Avatars;
using AIMAP.Diagnostics;
using AIMAP.Environment;
using extOSC;
using UnityEngine;

namespace AIMAP.Osc
{
    public sealed class AimapOscRouter : MonoBehaviour
    {
        [SerializeField] private OSCReceiver[] oscReceivers;
        [SerializeField] private AvatarSlotController[] avatarSlots;
        [SerializeField] private SkyboxEnvironmentController environmentController;
        [SerializeField] private AimapOscDiagnostics diagnostics;

        private void Awake()
        {
            if (oscReceivers == null || oscReceivers.Length == 0)
            {
                oscReceivers = GetComponents<OSCReceiver>();
            }

            if (environmentController == null)
            {
                environmentController = GetComponent<SkyboxEnvironmentController>();
            }

            if (diagnostics == null)
            {
                diagnostics = GetComponent<AimapOscDiagnostics>();
            }

            if (avatarSlots == null || avatarSlots.Length == 0)
            {
                avatarSlots = GetComponentsInChildren<AvatarSlotController>(true);
            }
        }

        private void OnEnable()
        {
            BindAddresses();
        }

        private void OnDisable()
        {
            if (oscReceivers == null)
            {
                return;
            }

            for (var i = 0; i < oscReceivers.Length; i++)
            {
                oscReceivers[i]?.ClearBinds();
            }
        }

        private void BindAddresses()
        {
            if (oscReceivers == null || oscReceivers.Length == 0)
            {
                return;
            }

            for (var r = 0; r < oscReceivers.Length; r++)
            {
                var receiver = oscReceivers[r];
                if (receiver == null)
                {
                    continue;
                }

                receiver.ClearBinds();

                if (avatarSlots != null)
                {
                    for (var index = 0; index < avatarSlots.Length; index++)
                    {
                        var slot = avatarSlots[index];
                        if (slot == null || string.IsNullOrWhiteSpace(slot.RoleId))
                        {
                            continue;
                        }

                        var roleId = slot.RoleId;
                        receiver.Bind($"/avatar/{roleId}/state", message => HandleAvatarState(roleId, message));
                        receiver.Bind($"/avatar/{roleId}/skin", message => HandleAvatarSkin(roleId, message));
                    }
                }

                receiver.Bind("/environment/skybox", HandleEnvironmentSkybox);
            }
        }

        private void HandleAvatarState(string roleId, OSCMessage message)
        {
            diagnostics?.RegisterMessage(message?.Address);
            var slot = FindSlot(roleId);
            if (slot == null)
            {
                return;
            }

            slot.SetPerformanceState(ReadBool(message, false));
        }

        private void HandleAvatarSkin(string roleId, OSCMessage message)
        {
            diagnostics?.RegisterMessage(message?.Address);
            var slot = FindSlot(roleId);
            if (slot == null)
            {
                return;
            }

            slot.SetSkin(ReadInt(message, 0));
        }

        private void HandleEnvironmentSkybox(OSCMessage message)
        {
            diagnostics?.RegisterMessage(message?.Address);
            environmentController?.SetSkybox(ReadInt(message, 0));
        }

        private static int ReadInt(OSCMessage message, int fallback)
        {
            if (message == null || message.Values == null || message.Values.Count == 0)
            {
                return fallback;
            }

            var value = message.Values[0];
            return value.Type switch
            {
                OSCValueType.Int => value.IntValue,
                OSCValueType.Float => Mathf.RoundToInt(value.FloatValue),
                OSCValueType.True => 1,
                OSCValueType.False => 0,
                OSCValueType.String when int.TryParse(value.StringValue, out var parsedInt) => parsedInt,
                OSCValueType.String when float.TryParse(value.StringValue, out var parsedFloat) => Mathf.RoundToInt(parsedFloat),
                _ => fallback,
            };
        }

        private static bool ReadBool(OSCMessage message, bool fallback)
        {
            if (message == null || message.Values == null || message.Values.Count == 0)
            {
                return fallback;
            }

            var value = message.Values[0];
            return value.Type switch
            {
                OSCValueType.True => true,
                OSCValueType.False => false,
                OSCValueType.Int => value.IntValue != 0,
                OSCValueType.Float => !Mathf.Approximately(value.FloatValue, 0f),
                OSCValueType.String when bool.TryParse(value.StringValue, out var parsedBool) => parsedBool,
                OSCValueType.String when int.TryParse(value.StringValue, out var parsedInt) => parsedInt != 0,
                OSCValueType.String => string.Equals(value.StringValue, "play", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value.StringValue, "playing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value.StringValue, "dance", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value.StringValue, "dancing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value.StringValue, "on", StringComparison.OrdinalIgnoreCase),
                _ => fallback,
            };
        }

        private AvatarSlotController FindSlot(string roleId)
        {
            if (avatarSlots == null)
            {
                return null;
            }

            for (var index = 0; index < avatarSlots.Length; index++)
            {
                var slot = avatarSlots[index];
                if (slot != null && string.Equals(slot.RoleId, roleId, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }

            return null;
        }
    }
}
