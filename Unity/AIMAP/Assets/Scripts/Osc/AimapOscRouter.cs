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
        [SerializeField] private DancerSlotController[] dancerSlots;
        [SerializeField] private SkyboxEnvironmentController environmentController;
        [SerializeField] private AimapOscDiagnostics diagnostics;
        [SerializeField] private AimapOscPortDebugPanel debugPanel;
        [Header("Dancer Auto Trigger")]
        [SerializeField] private bool autoTriggerDancersFromMidi = true;
        [SerializeField, Min(0f)] private float dancerAutoStopDelaySeconds = 5f;

        [SerializeField] private readonly Dictionary<string, AimapAvatarMidiHandler> _midiHandlersByRole =
            new Dictionary<string, AimapAvatarMidiHandler>(StringComparer.OrdinalIgnoreCase);
        private float _dancerAutoPlayUntilTime = -1f;
        private bool _dancersAutoPlaying;

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

            if (dancerSlots == null || dancerSlots.Length == 0)
            {
                dancerSlots = GetComponentsInChildren<DancerSlotController>(true);
            }
        }

        private void OnEnable()
        {
            BindAddresses();
        }

        private void OnDisable()
        {
            _dancerAutoPlayUntilTime = -1f;
            _dancersAutoPlaying = false;

            if (oscReceivers == null)
            {
                return;
            }

            for (var i = 0; i < oscReceivers.Length; i++)
            {
                oscReceivers[i]?.ClearBinds();
            }
        }

        private void Update()
        {
            if (!autoTriggerDancersFromMidi)
            {
                return;
            }

            if (!_dancersAutoPlaying || Time.time < _dancerAutoPlayUntilTime)
            {
                return;
            }

            SetAllDancerPerformanceState(false);
            _dancersAutoPlaying = false;
            _dancerAutoPlayUntilTime = -1f;
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

                        var canonicalRoleId = slot.RoleId;
                        if (string.IsNullOrWhiteSpace(canonicalRoleId))
                        {
                            continue;
                        }

                        var midiHandler = FindMidiHandler(slot);
                        if (midiHandler != null)
                        {
                            var midiRoleIds = new List<string>();
                            AddDistinctRoleId(midiRoleIds, canonicalRoleId);
                            foreach (var oscMidiRole in midiHandler.OscRoleIds)
                            {
                                AddDistinctRoleId(midiRoleIds, oscMidiRole);
                            }

                            if (midiRoleIds.Count == 0)
                            {
                                continue;
                            }

                            for (var midiIndex = 0; midiIndex < midiRoleIds.Count; midiIndex++)
                            {
                                var boundRole = midiRoleIds[midiIndex];
                                _midiHandlersByRole[boundRole] = midiHandler;
                                var captureRole = boundRole;
                                receiver.Bind($"/avatar/{captureRole}/midi", message => HandleAvatarMidi(captureRole, listenPort, message));
                            }
                        }
                        else
                        {
                            receiver.Bind($"/avatar/{canonicalRoleId}/state", message => HandleAvatarState(canonicalRoleId, listenPort, message));
                        }

                        receiver.Bind($"/avatar/{canonicalRoleId}/skin", message => HandleAvatarSkin(canonicalRoleId, listenPort, message));
                        receiver.Bind($"/avatar/{canonicalRoleId}/move", message => HandleAvatarMove(canonicalRoleId, listenPort, message));
                    }
                }

                if (dancerSlots != null)
                {
                    for (var index = 0; index < dancerSlots.Length; index++)
                    {
                        var slot = dancerSlots[index];
                        if (slot == null || string.IsNullOrWhiteSpace(slot.RoleId))
                        {
                            continue;
                        }

                        var roleId = slot.RoleId;
                        receiver.Bind($"/avatar/{roleId}/state", message => HandleAvatarState(roleId, listenPort, message));
                        receiver.Bind($"/avatar/{roleId}/skin", message => HandleAvatarSkin(roleId, listenPort, message));
                        receiver.Bind($"/avatar/{roleId}/move", message => HandleAvatarMove(roleId, listenPort, message));
                    }
                }

                receiver.Bind("/environment/skybox", message => HandleEnvironmentSkybox(listenPort, message));
            }
        }

        private void HandleAvatarState(string roleId, int listenPort, OSCMessage message)
        {
            ReportMessage(listenPort, message);
            var slot = FindSlot(roleId);
            if (slot != null)
            {
                slot.SetPerformanceState(ReadBool(message, false));
                return;
            }

            var dancerSlot = FindDancerSlot(roleId);
            if (dancerSlot == null)
            {
                return;
            }

            dancerSlot.SetPerformanceState(ReadBool(message, false));
        }

        private void HandleAvatarSkin(string roleId, int listenPort, OSCMessage message)
        {
            ReportMessage(listenPort, message);
            var slot = FindSlot(roleId);
            if (slot != null)
            {
                slot.SetSkin(ReadInt(message, 0));
                return;
            }

            var dancerSlot = FindDancerSlot(roleId);
            if (dancerSlot == null)
            {
                return;
            }

            dancerSlot.SetSkin(ReadInt(message, 0));
        }

        private void HandleAvatarMove(string roleId, int listenPort, OSCMessage message)
        {
            ReportMessage(listenPort, message);
            var dancerSlot = FindDancerSlot(roleId);
            if (dancerSlot == null)
            {
                return;
            }

            dancerSlot.SetDanceMove(ReadString(message, string.Empty));
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
                TryAutoTriggerDancersFromMidi(midiMessage);
                return;
            }

            Debug.LogWarning(
                $"Failed to decode OSC MIDI message for role '{roleId}' at '{message?.Address}'. " +
                "Expected [noteNumber], [noteNumber, velocity], [channel, noteNumber, velocity], or [noteState, channel, noteNumber, velocity].",
                this);
        }

        private void TryAutoTriggerDancersFromMidi(AimapAvatarMidiMessage midiMessage)
        {
            if (!autoTriggerDancersFromMidi)
            {
                return;
            }

            _dancerAutoPlayUntilTime = Time.time + Mathf.Max(0f, dancerAutoStopDelaySeconds);

            if (!midiMessage.IsNoteOn || midiMessage.Velocity <= 0)
            {
                return;
            }

            SetAllDancerPerformanceState(true);
            _dancersAutoPlaying = true;
        }

        private void SetAllDancerPerformanceState(bool isPlaying)
        {
            if (dancerSlots == null)
            {
                return;
            }

            for (var index = 0; index < dancerSlots.Length; index++)
            {
                var slot = dancerSlots[index];
                if (slot == null)
                {
                    continue;
                }

                slot.SetPerformanceState(isPlaying);
            }
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

        private static string ReadString(OSCMessage message, string fallback)
        {
            if (message == null || message.Values == null || message.Values.Count == 0)
            {
                return fallback;
            }

            var value = message.Values[0];
            return value.Type switch
            {
                OSCValueType.String => value.StringValue,
                OSCValueType.Int => value.IntValue.ToString(),
                OSCValueType.Float => value.FloatValue.ToString(),
                OSCValueType.True => "true",
                OSCValueType.False => "false",
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

        private DancerSlotController FindDancerSlot(string roleId)
        {
            if (dancerSlots == null)
            {
                return null;
            }

            for (var index = 0; index < dancerSlots.Length; index++)
            {
                var slot = dancerSlots[index];
                if (slot == null)
                {
                    continue;
                }

                if (string.Equals(slot.RoleId, roleId, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }
            }

            return null;
        }

        private static void AddDistinctRoleId(List<string> roleIds, string candidate)
        {
            if (roleIds == null || string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var trimmed = candidate.Trim();
            for (var i = 0; i < roleIds.Count; i++)
            {
                if (string.Equals(roleIds[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            roleIds.Add(trimmed);
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
