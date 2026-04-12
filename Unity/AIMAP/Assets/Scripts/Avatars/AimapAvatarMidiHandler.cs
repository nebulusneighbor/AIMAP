using System;
using System.Collections.Generic;
using AIMAP.Osc;
using UnityEngine;

namespace AIMAP.Avatars
{
    [DisallowMultipleComponent]
    public sealed class AimapAvatarMidiHandler : MonoBehaviour
    {
        public enum InstrumentRigMode
        {
            Hands = 0,
            Keyboard = 1,
            Drums = 2,
            Guitar = 3,
        }

        [SerializeField] private string roleId;
        [SerializeField] private InstrumentRigMode instrumentMode = InstrumentRigMode.Keyboard;
        [SerializeField] private AvatarSlotController avatarSlot;
        [SerializeField] private InstrumentRigManager instrumentRigManager;
        [SerializeField] private bool driveAvatarPerformanceState = true;

        private const int MiddleNote = 44;
        private readonly List<Note> _notes = new List<Note>();

        public string RoleId => string.IsNullOrWhiteSpace(roleId) ? avatarSlot != null ? avatarSlot.RoleId : string.Empty : roleId;
        public int InstrumentMode => (int)instrumentMode;

        public float KeyRightHand { get; private set; }
        public float KeyLeftHand { get; private set; }
        public bool RightKeyHit { get; private set; }
        public bool LeftKeyHit { get; private set; }

        public float GuitarLeftHand { get; private set; }
        public bool ShouldStrum { get; private set; }

        public bool RightDrumHit { get; private set; }
        public bool LeftDrumHit { get; private set; }

        public bool HasActiveNotes => _notes.Count > 0;

        private void Reset()
        {
            CacheReferences();
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            instrumentRigManager?.BindMidiHandler(this);
        }

        private void OnValidate()
        {
            CacheReferences();
        }

        public bool MatchesRole(string incomingRoleId)
        {
            return !string.IsNullOrWhiteSpace(incomingRoleId)
                && string.Equals(RoleId, incomingRoleId, StringComparison.OrdinalIgnoreCase);
        }

        public void ApplyMidiMessage(AimapAvatarMidiMessage midiMessage)
        {
            var shouldClearAfterProcessing = !midiMessage.ShouldSustain;
            UpdateActiveNotes(midiMessage);
            ProcessInstrumentState(midiMessage);

            if (driveAvatarPerformanceState)
            {
                avatarSlot?.SetPerformanceState(shouldClearAfterProcessing ? midiMessage.IsNoteOn : HasActiveNotes);
            }

            if (shouldClearAfterProcessing)
            {
                _notes.Clear();
            }
        }

        private void CacheReferences()
        {
            if (avatarSlot == null)
            {
                avatarSlot = GetComponent<AvatarSlotController>();
                if (avatarSlot == null)
                {
                    avatarSlot = GetComponentInParent<AvatarSlotController>();
                }

                if (avatarSlot == null)
                {
                    avatarSlot = GetComponentInChildren<AvatarSlotController>(true);
                }
            }

            if (instrumentRigManager == null)
            {
                instrumentRigManager = GetComponent<InstrumentRigManager>();
                if (instrumentRigManager == null)
                {
                    instrumentRigManager = GetComponentInParent<InstrumentRigManager>();
                }

                if (instrumentRigManager == null)
                {
                    instrumentRigManager = GetComponentInChildren<InstrumentRigManager>(true);
                }
            }
        }

        private void UpdateActiveNotes(AimapAvatarMidiMessage midiMessage)
        {
            if (!midiMessage.ShouldSustain)
            {
                _notes.Clear();
                if (midiMessage.IsNoteOn && midiMessage.Velocity > 0)
                {
                    _notes.Add(new Note(midiMessage.NoteNumber, midiMessage.Channel));
                }

                return;
            }

            var note = new Note(midiMessage.NoteNumber, midiMessage.Channel);
            if (midiMessage.IsNoteOn && midiMessage.Velocity > 0)
            {
                if (!_notes.Contains(note))
                {
                    _notes.Add(note);
                }
            }
            else
            {
                _notes.Remove(note);
            }
        }

        private void ProcessInstrumentState(AimapAvatarMidiMessage midiMessage)
        {
            switch (instrumentMode)
            {
                case InstrumentRigMode.Hands:
                    ClearRigState();
                    return;
                case InstrumentRigMode.Keyboard:
                    ProcessKeyboard(midiMessage);
                    return;
                case InstrumentRigMode.Drums:
                    ProcessDrums(midiMessage);
                    return;
                case InstrumentRigMode.Guitar:
                    ProcessGuitar(midiMessage);
                    return;
                default:
                    ClearRigState();
                    return;
            }
        }

        private void ProcessKeyboard(AimapAvatarMidiMessage midiMessage)
        {
            ClearDrumState();
            ClearGuitarState();

            var leftNotes = 0;
            var rightNotes = 0;
            var leftNoteSum = 0;
            var rightNoteSum = 0;

            for (var index = 0; index < _notes.Count; index++)
            {
                var note = _notes[index];
                if (note.NoteChannel != 1)
                {
                    continue;
                }

                if (note.NoteNumber < MiddleNote)
                {
                    leftNotes++;
                    leftNoteSum += note.NoteNumber;
                }
                else
                {
                    rightNotes++;
                    rightNoteSum += note.NoteNumber;
                }
            }

            if (rightNotes > 0)
            {
                var rightNoteAverage = (float)rightNoteSum / rightNotes;
                KeyRightHand = (rightNoteAverage - MiddleNote) / MiddleNote;
            }

            if (leftNotes > 0)
            {
                var leftNoteAverage = (float)leftNoteSum / leftNotes;
                KeyLeftHand = 1f - (leftNoteAverage / MiddleNote);
            }

            if (midiMessage.Channel != 1)
            {
                return;
            }

            if (midiMessage.NoteNumber < MiddleNote)
            {
                LeftKeyHit = midiMessage.IsNoteOn && midiMessage.Velocity > 0;
            }
            else
            {
                RightKeyHit = midiMessage.IsNoteOn && midiMessage.Velocity > 0;
            }
        }

        private void ProcessDrums(AimapAvatarMidiMessage midiMessage)
        {
            ClearKeyboardState();
            ClearGuitarState();

            if (midiMessage.Channel != 1)
            {
                return;
            }

            var isHitActive = midiMessage.IsNoteOn && midiMessage.Velocity > 0;
            if (midiMessage.NoteNumber % 2 == 0)
            {
                LeftDrumHit = isHitActive;
            }
            else
            {
                RightDrumHit = isHitActive;
            }
        }

        private void ProcessGuitar(AimapAvatarMidiMessage midiMessage)
        {
            ClearKeyboardState();
            ClearDrumState();

            var isPressed = false;
            var leftNote = 40;
            for (var index = 0; index < _notes.Count; index++)
            {
                var note = _notes[index];
                if (note.NoteChannel == 7 || note.NoteChannel == 1)
                {
                    isPressed = true;
                    leftNote = note.NoteNumber;
                }
            }

            if (isPressed)
            {
                GuitarLeftHand = Mathf.Clamp((leftNote - 40f) / 10f, 0f, 1f);
            }

            ShouldStrum = midiMessage.IsNoteOn && midiMessage.Velocity > 0;
        }

        private void ClearRigState()
        {
            ClearKeyboardState();
            ClearDrumState();
            ClearGuitarState();
        }

        private void ClearKeyboardState()
        {
            LeftKeyHit = false;
            RightKeyHit = false;
        }

        private void ClearDrumState()
        {
            LeftDrumHit = false;
            RightDrumHit = false;
        }

        private void ClearGuitarState()
        {
            ShouldStrum = false;
        }

        private struct Note : IEquatable<Note>
        {
            public Note(int noteNumber, int noteChannel)
            {
                NoteNumber = noteNumber;
                NoteChannel = noteChannel;
            }

            public int NoteNumber { get; }
            public int NoteChannel { get; }

            public bool Equals(Note other)
            {
                return NoteNumber == other.NoteNumber && NoteChannel == other.NoteChannel;
            }
        }
    }
}
