using System;
using AIMAP.Avatars;
using AIMAP.Diagnostics;
using AIMAP.Environment;
using extOSC;
using UnityEngine;
using System.Collections.Generic;

namespace AIMAP.Osc
{
    public sealed class AimapOscRouter : MonoBehaviour
    {
        [SerializeField] private OSCReceiver[] oscReceivers;
        [SerializeField] private AvatarSlotController[] avatarSlots;
        [SerializeField] private SkyboxEnvironmentController environmentController;
        [SerializeField] private AimapOscDiagnostics diagnostics;
        [SerializeField] private AimapOscPortDebugPanel debugPanel;

        [SerializeField] private readonly Dictionary<string, AimapAvatarMidiHandler> _midiHandlersByRole =
            new Dictionary<string, AimapAvatarMidiHandler>(StringComparer.OrdinalIgnoreCase);

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

            if (debugPanel == null)
            {
                debugPanel = GetComponent<AimapOscPortDebugPanel>();
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

            _midiHandlersByRole.Clear();

            for (var r = 0; r < oscReceivers.Length; r++)
            {
                var receiver = oscReceivers[r];
                if (receiver == null)
                {
                    continue;
                }

                receiver.ClearBinds();
                var listenPort = receiver.LocalPort;

                if (avatarSlots != null)
                {
                    for (var index = 0; index < avatarSlots.Length; index++)
                    {
                        var slot = avatarSlots[index];
                        if (slot == null)
                        {
                            continue;
                        }

                        var midiHandler = FindMidiHandler(slot);
                        var roleId = midiHandler != null && !string.IsNullOrWhiteSpace(midiHandler.RoleId)
                            ? midiHandler.RoleId
                            : slot.RoleId;
                        if (string.IsNullOrWhiteSpace(roleId))
                        {
                            continue;
                        }

                        if (midiHandler != null)
                        {
                            _midiHandlersByRole[roleId] = midiHandler;
                            receiver.Bind($"/avatar/{roleId}/midi", message => HandleAvatarMidi(roleId, listenPort, message));
                        }
                        else
                        {
                            receiver.Bind($"/avatar/{roleId}/state", message => HandleAvatarState(roleId, listenPort, message));
                        }

                        receiver.Bind($"/avatar/{roleId}/skin", message => HandleAvatarSkin(roleId, listenPort, message));
                    }
                }

                receiver.Bind("/environment/skybox", message => HandleEnvironmentSkybox(listenPort, message));
            }
        }

        private void HandleAvatarState(string roleId, int listenPort, OSCMessage message)
        {
            ReportMessage(listenPort, message);
            var slot = FindSlot(roleId);
            if (slot == null)
            {
                return;
            }

            slot.SetPerformanceState(ReadBool(message, false));
        }

        private void HandleAvatarSkin(string roleId, int listenPort, OSCMessage message)
        {
            ReportMessage(listenPort, message);
            var slot = FindSlot(roleId);
            if (slot == null)
            {
                return;
            }

            slot.SetSkin(ReadInt(message, 0));
        }

        private void HandleAvatarMidi(string roleId, int listenPort, OSCMessage message)
        {
            ReportMessage(listenPort, message);
            if (!_midiHandlersByRole.TryGetValue(roleId, out var midiHandler) || midiHandler == null)
            {
                var slot = FindSlot(roleId);
                midiHandler = FindMidiHandler(slot);
                if (midiHandler == null)
                {
                    return;
                }

                _midiHandlersByRole[roleId] = midiHandler;
            }

            if (AimapAvatarMidiMessageDecoder.TryDecode(message, out var midiMessage))
            {
                midiHandler.ApplyMidiMessage(midiMessage);
                return;
            }

            Debug.LogWarning(
                $"Failed to decode OSC MIDI message for role '{roleId}' at '{message?.Address}'. " +
                "Expected [noteNumber], [noteNumber, velocity], [channel, noteNumber, velocity], or [noteState, channel, noteNumber, velocity].",
                this);
        }

        private void HandleEnvironmentSkybox(int listenPort, OSCMessage message)
        {
            ReportMessage(listenPort, message);
            environmentController?.SetSkybox(ReadInt(message, 0));
        }

        private void ReportMessage(int listenPort, OSCMessage message)
        {
            diagnostics?.RegisterMessage(message?.Address);
            debugPanel?.RegisterMessage(listenPort, message?.Address, message);
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
                if (slot == null)
                {
                    continue;
                }

                var midiHandler = FindMidiHandler(slot);
                if (midiHandler != null && midiHandler.MatchesRole(roleId))
                {
                    return slot;
                }

                if (string.Equals(slot.RoleId, roleId, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }

            return null;
        }

        private static AimapAvatarMidiHandler FindMidiHandler(AvatarSlotController slot)
        {
            if (slot == null)
            {
                return null;
            }

            var onSlot = slot.GetComponent<AimapAvatarMidiHandler>();
            if (onSlot != null)
            {
                return onSlot;
            }

            var children = slot.GetComponentsInChildren<AimapAvatarMidiHandler>(true);
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                if (child != null)
                {
                    return child;
                }
            }

            var parents = slot.GetComponentsInParent<AimapAvatarMidiHandler>(true);
            for (var index = 0; index < parents.Length; index++)
            {
                var parent = parents[index];
                if (parent != null)
                {
                    return parent;
                }
            }

            return null;
        }
    }
}
