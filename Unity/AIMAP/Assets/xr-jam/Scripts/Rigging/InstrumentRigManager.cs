using System.Collections;
using System.Collections.Generic;
using AIMAP.Avatars;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Script that handles the active instrument rig and applies processed local MIDI state to the rig handlers.
/// </summary>
public class InstrumentRigManager : MonoBehaviour
{
    [Header("State handlers")]
    // Local AIMAP MIDI source for OSC-only scenes
    [Tooltip("Local AIMAP MIDI source for OSC-only scenes")]
    public AimapAvatarMidiHandler _aimapMidiHandler = null;

    // Currently Active Rig Index
    [Tooltip("Currently Active Rig Index")]
    public int CurrentRig;
 
    [Header("Hands")]
    // Nreal hand-tracking animation rig
    [Tooltip("Nreal hand-tracking animation rig")]
    public Rig HandsRig;

    [Header("Keyboard")]
    // Keyboard animation rig
    [Tooltip("Keyboard animation rig")]
    public Rig KeyboardRig;

    // Keyboard animation rig handler
    [Tooltip("Keyboard animation rig handler")]
    public KeyboardRigHandler KeyboardRigHandler;
    
    [Tooltip("Keyboard visuals (e.g. multiple props); enabled together in keyboard mode")]
    public GameObject[] Keyboard;

    [Header("Drum")]
    // Drum Rig
    [Tooltip("Drum Rig")]
    public Rig DrumRig;

    // Drum Rig Handler
    [Tooltip("Drum Rig Handler")]
    public DrumRigHandler DrumRigHandler;
    
    [Tooltip("Drum visuals (e.g. kit pieces); enabled together in drum mode")]
    public GameObject[] Drum;

    [Header("Guitar")]
    // Guitar Rig
    [Tooltip("Guitar Rig")]
    public Rig GuitarRig;
    
    // Guitar Rig Handler
    [Tooltip("Guitar Rig Handler")]
    public GuitarRigHandler GuitarRigHandler;
    
    // Guitar Object
    [Tooltip("Guitar Object")]
    public GameObject Guitar;

    [Header("Bass")]
    [Tooltip("Bass animation rig")]
    public Rig BassRig;

    [Tooltip("Bass rig handler (same drive pattern as guitar)")]
    public GuitarRigHandler BassRigHandler;

    [Tooltip("Bass visual")]
    public GameObject Bass;

    [Header("Strings")]
    [Tooltip("Strings (bowed) animation rig")]
    public Rig StringsRig;

    [Tooltip("Strings rig handler (left hand + bow IK)")]
    public StringsRigHandler StringsRigHandler;

    [Tooltip("Strings visuals (e.g. bow and violin); enabled together in strings mode")]
    public GameObject[] Strings;

    [Header("Winds")]
    [Tooltip("Winds animation rig")]
    public Rig WindsRig;

    [Tooltip("Winds rig handler (head motion from MIDI blends)")]
    public WindsRigHandler WindsRigHandler;

    [Tooltip("Winds visual")]
    public GameObject Winds;

    void Start()
    {
        TryAssignMidiSources();

        // Initialize nreal hand rig to be active, all others inactive.
        // Deactivate the instrument objects

        HandsRig.weight = 1;
        KeyboardRig.weight = 0;
        SetKeyboardVisualsActive(false);

        DrumRig.weight = 0;
        SetDrumVisualsActive(false);

        GuitarRig.weight = 0;
        Guitar.SetActive(false);

        BassRig.weight = 0;
        Bass.SetActive(false);

        StringsRig.weight = 0;
        SetStringsVisualsActive(false);

        WindsRig.weight = 0;
        Winds.SetActive(false);

        CurrentRig = 0;
    }

    void Update()
    {
        if (_aimapMidiHandler == null)
        {
            TryAssignMidiSources();
        }

        if (_aimapMidiHandler == null)
        {
            return;
        }

        UpdateFromAimapMidiHandler();
    }

    public void BindMidiHandler(AimapAvatarMidiHandler midiHandler)
    {
        if (midiHandler != null)
        {
            _aimapMidiHandler = midiHandler;
        }
    }

    private void TryAssignMidiSources()
    {
        if (_aimapMidiHandler == null)
        {
            _aimapMidiHandler = GetComponent<AimapAvatarMidiHandler>();
            if (_aimapMidiHandler == null)
            {
                _aimapMidiHandler = GetComponentInParent<AimapAvatarMidiHandler>();
            }

            if (_aimapMidiHandler == null)
            {
                _aimapMidiHandler = GetComponentInChildren<AimapAvatarMidiHandler>(true);
            }
        }
    }

    private void UpdateFromAimapMidiHandler()
    {
        // Hand mode
        if (_aimapMidiHandler.InstrumentMode == 0)
        {
            // Switch Rigs
            if (CurrentRig != 0)
            {
                SetInstrumentRigs(0);
            }
        }
        else
        // Keyboard mode
        if (_aimapMidiHandler.InstrumentMode == 1)
        {
            // Switch Rigs
            if (CurrentRig != 1)
            {
                SetInstrumentRigs(1);
            }

            // Apply the keyboard position and hit variables
            KeyboardRigHandler.LeftHandPosition = _aimapMidiHandler.KeyLeftHand;
            KeyboardRigHandler.RightHandPosition = _aimapMidiHandler.KeyRightHand;

            KeyboardRigHandler.LeftHit(_aimapMidiHandler.LeftKeyHit);
            KeyboardRigHandler.RightHit(_aimapMidiHandler.RightKeyHit);
        }
        else
        // Drum Mode
        if (_aimapMidiHandler.InstrumentMode == 2)
        {
            // Switch Rigs
            if (CurrentRig != 2)
            {
                SetInstrumentRigs(2);
            }

            // Apply drum hit values
            DrumRigHandler.LeftHit(_aimapMidiHandler.LeftDrumHit);
            DrumRigHandler.RightHit(_aimapMidiHandler.RightDrumHit);
        }
        else
        // Guitar Mode
        if (_aimapMidiHandler.InstrumentMode == 3)
        {
            // Switch Rigs
            if (CurrentRig != 3)
            {
                SetInstrumentRigs(3);
            }

            // Apply guitar position and strum values
            GuitarRigHandler.LeftHandPosition = _aimapMidiHandler.GuitarLeftHand;
            GuitarRigHandler.Strum(_aimapMidiHandler.ShouldStrum);
        }
        else
        // Bass mode
        if (_aimapMidiHandler.InstrumentMode == 4)
        {
            if (CurrentRig != 4)
            {
                SetInstrumentRigs(4);
            }

            BassRigHandler.LeftHandPosition = _aimapMidiHandler.GuitarLeftHand;
            BassRigHandler.Strum(_aimapMidiHandler.ShouldStrum);
        }
        else
        if (_aimapMidiHandler.InstrumentMode == 5)
        {
            if (CurrentRig != 5)
            {
                SetInstrumentRigs(5);
            }

            StringsRigHandler.LeftHandPosition = _aimapMidiHandler.GuitarLeftHand;
            StringsRigHandler.SetBowPlaying(_aimapMidiHandler.StringsBowPlaying);
        }
        else
        if (_aimapMidiHandler.InstrumentMode == 6)
        {
            if (CurrentRig != 6)
            {
                SetInstrumentRigs(6);
            }

            WindsRigHandler.SetMidiHeadDrive(_aimapMidiHandler.KeyLeftHand, _aimapMidiHandler.KeyRightHand);
        }
    }

    /// <summary>
    /// Switch rigs and instruments based on a given index
    /// </summary>
    /// <param name="index"></param>
    public void SetInstrumentRigs(int index)
    {
        CurrentRig = index;
        switch(index)
        {
            case 0:
                HandsRig.weight = 1;
                KeyboardRig.weight = 0;
                DrumRig.weight = 0;
                GuitarRig.weight = 0;
                BassRig.weight = 0;
                StringsRig.weight = 0;
                WindsRig.weight = 0;
                SetKeyboardVisualsActive(false);
                SetDrumVisualsActive(false);
                Guitar.SetActive(false);
                Bass.SetActive(false);
                SetStringsVisualsActive(false);
                Winds.SetActive(false);
                return;
            case 1:
                KeyboardRig.weight = 1;
                DrumRig.weight = 0;
                GuitarRig.weight = 0;
                BassRig.weight = 0;
                StringsRig.weight = 0;
                WindsRig.weight = 0;
                SetKeyboardVisualsActive(true);
                SetDrumVisualsActive(false);
                Guitar.SetActive(false);
                Bass.SetActive(false);
                SetStringsVisualsActive(false);
                Winds.SetActive(false);
                return;
            case 2:
                KeyboardRig.weight = 0;
                DrumRig.weight = 1;
                GuitarRig.weight = 0;
                BassRig.weight = 0;
                StringsRig.weight = 0;
                WindsRig.weight = 0;
                SetKeyboardVisualsActive(false);
                SetDrumVisualsActive(true);
                Guitar.SetActive(false);
                Bass.SetActive(false);
                SetStringsVisualsActive(false);
                Winds.SetActive(false);
                return;
            case 3:
                KeyboardRig.weight = 0;
                DrumRig.weight = 0;
                GuitarRig.weight = 1;
                BassRig.weight = 0;
                StringsRig.weight = 0;
                WindsRig.weight = 0;
                SetKeyboardVisualsActive(false);
                SetDrumVisualsActive(false);
                Guitar.SetActive(true);
                Bass.SetActive(false);
                SetStringsVisualsActive(false);
                Winds.SetActive(false);
                return;
            case 4:
                KeyboardRig.weight = 0;
                DrumRig.weight = 0;
                GuitarRig.weight = 0;
                BassRig.weight = 1;
                StringsRig.weight = 0;
                WindsRig.weight = 0;
                SetKeyboardVisualsActive(false);
                SetDrumVisualsActive(false);
                Guitar.SetActive(false);
                Bass.SetActive(true);
                SetStringsVisualsActive(false);
                Winds.SetActive(false);
                return;
            case 5:
                KeyboardRig.weight = 0;
                DrumRig.weight = 0;
                GuitarRig.weight = 0;
                BassRig.weight = 0;
                StringsRig.weight = 1;
                WindsRig.weight = 0;
                SetKeyboardVisualsActive(false);
                SetDrumVisualsActive(false);
                Guitar.SetActive(false);
                Bass.SetActive(false);
                SetStringsVisualsActive(true);
                Winds.SetActive(false);
                return;
            case 6:
                KeyboardRig.weight = 0;
                DrumRig.weight = 0;
                GuitarRig.weight = 0;
                BassRig.weight = 0;
                StringsRig.weight = 0;
                WindsRig.weight = 1;
                SetKeyboardVisualsActive(false);
                SetDrumVisualsActive(false);
                Guitar.SetActive(false);
                Bass.SetActive(false);
                SetStringsVisualsActive(false);
                Winds.SetActive(true);
                return;
        }
    }

    private void SetKeyboardVisualsActive(bool active)
    {
        SetVisualsArrayActive(Keyboard, active);
    }

    private void SetDrumVisualsActive(bool active)
    {
        SetVisualsArrayActive(Drum, active);
    }

    private void SetStringsVisualsActive(bool active)
    {
        SetVisualsArrayActive(Strings, active);
    }

    private static void SetVisualsArrayActive(GameObject[] visuals, bool active)
    {
        if (visuals == null)
        {
            return;
        }

        for (var i = 0; i < visuals.Length; i++)
        {
            if (visuals[i] != null)
            {
                visuals[i].SetActive(active);
            }
        }
    }

    /// <summary>
    /// Set real handtracking according to a given state
    /// </summary>
    /// <param name="state"></param>
    public void RealHands(bool state)
    {
        if(state)
        {
            HandsRig.weight = 1;
            KeyboardRig.gameObject.SetActive(false);
            DrumRig.gameObject.SetActive(false);
            GuitarRig.gameObject.SetActive(false);
            BassRig.gameObject.SetActive(false);
            StringsRig.gameObject.SetActive(false);
            WindsRig.gameObject.SetActive(false);

            switch (CurrentRig)
            {
                case 1:
                    KeyboardRig.weight = 0;
                    return;
                case 2:
                    DrumRig.weight = 0;
                    return;
                case 3:
                    GuitarRig.weight = 0;
                    return;
                case 4:
                    BassRig.weight = 0;
                    return;
                case 5:
                    StringsRig.weight = 0;
                    return;
                case 6:
                    WindsRig.weight = 0;
                    return;
            }
        }
        else
        {
            HandsRig.weight = 0;
            KeyboardRig.gameObject.SetActive(true);
            DrumRig.gameObject.SetActive(true);
            GuitarRig.gameObject.SetActive(true);
            BassRig.gameObject.SetActive(true);
            StringsRig.gameObject.SetActive(true);
            WindsRig.gameObject.SetActive(true);
            switch (CurrentRig)
            {
                case 1:
                    KeyboardRig.weight = 1;
                    return;
                case 2:
                    DrumRig.weight = 1;
                    return;
                case 3:
                    GuitarRig.weight = 1;
                    return;
                case 4:
                    BassRig.weight = 1;
                    return;
                case 5:
                    StringsRig.weight = 1;
                    return;
                case 6:
                    WindsRig.weight = 1;
                    return;
            }
        }
    }
}
