using System;
using extOSC;
using UnityEngine;

namespace AIMAP.Osc
{
    public struct AimapAvatarMidiMessage
    {
        public AimapAvatarMidiMessage(bool isNoteOn, int channel, int noteNumber, int velocity, bool shouldSustain = true)
        {
            IsNoteOn = isNoteOn;
            Channel = channel;
            NoteNumber = noteNumber;
            Velocity = velocity;
            ShouldSustain = shouldSustain;
        }

        public bool IsNoteOn { get; }
        public int Channel { get; }
        public int NoteNumber { get; }
        public int Velocity { get; }
        public bool ShouldSustain { get; }
    }

    public static class AimapAvatarMidiMessageDecoder
    {
        public static bool TryDecode(OSCMessage message, out AimapAvatarMidiMessage midiMessage)
        {
            midiMessage = default(AimapAvatarMidiMessage);

            if (message == null || message.Values == null)
            {
                return false;
            }

            if (message.Values.Count >= 4
                && TryReadNoteState(message.Values[0], out var isNoteOnFromState))
            {
                midiMessage = new AimapAvatarMidiMessage(
                    isNoteOnFromState,
                    ReadInt(message.Values[1], 1),
                    ReadInt(message.Values[2], 0),
                    ReadInt(message.Values[3], isNoteOnFromState ? 127 : 0));
                midiMessage = Sanitize(midiMessage);
                return true;
            }

            if (message.Values.Count >= 3)
            {
                var channel = ReadInt(message.Values[0], 1);
                var noteNumber = ReadInt(message.Values[1], 0);
                var velocity = ReadInt(message.Values[2], 0);
                midiMessage = Sanitize(new AimapAvatarMidiMessage(velocity > 0, channel, noteNumber, velocity));
                return true;
            }

            if (message.Values.Count >= 2)
            {
                var noteNumber = ReadInt(message.Values[0], 0);
                var velocity = ReadInt(message.Values[1], 127);
                midiMessage = Sanitize(new AimapAvatarMidiMessage(velocity > 0, 1, noteNumber, velocity, false));
                return true;
            }

            if (message.Values.Count >= 1)
            {
                var noteNumber = ReadInt(message.Values[0], 0);
                midiMessage = Sanitize(new AimapAvatarMidiMessage(true, 1, noteNumber, 127, false));
                return true;
            }

            return false;
        }

        private static AimapAvatarMidiMessage Sanitize(AimapAvatarMidiMessage midiMessage)
        {
            return new AimapAvatarMidiMessage(
                midiMessage.IsNoteOn,
                Mathf.Clamp(midiMessage.Channel, 1, 16),
                Mathf.Clamp(midiMessage.NoteNumber, 0, 127),
                Mathf.Clamp(midiMessage.Velocity, 0, 127),
                midiMessage.ShouldSustain);
        }

        private static bool TryReadNoteState(OSCValue value, out bool isNoteOn)
        {
            isNoteOn = false;
            if (value == null)
            {
                return false;
            }

            switch (value.Type)
            {
                case OSCValueType.True:
                    isNoteOn = true;
                    return true;
                case OSCValueType.False:
                    isNoteOn = false;
                    return true;
                case OSCValueType.Int:
                    isNoteOn = value.IntValue != 0;
                    return true;
                case OSCValueType.Float:
                    isNoteOn = !Mathf.Approximately(value.FloatValue, 0f);
                    return true;
                case OSCValueType.String:
                    return TryReadNoteState(value.StringValue, out isNoteOn);
                default:
                    return false;
            }
        }

        private static bool TryReadNoteState(string rawValue, out bool isNoteOn)
        {
            isNoteOn = false;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            var normalized = rawValue.Trim();
            if (string.Equals(normalized, "noteon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "play", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase))
            {
                isNoteOn = true;
                return true;
            }

            if (string.Equals(normalized, "noteoff", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "idle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase))
            {
                isNoteOn = false;
                return true;
            }

            if (int.TryParse(normalized, out var parsedInt))
            {
                isNoteOn = parsedInt != 0;
                return true;
            }

            if (float.TryParse(normalized, out var parsedFloat))
            {
                isNoteOn = !Mathf.Approximately(parsedFloat, 0f);
                return true;
            }

            return false;
        }

        private static int ReadInt(OSCValue value, int fallback)
        {
            if (value == null)
            {
                return fallback;
            }

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
    }
}
