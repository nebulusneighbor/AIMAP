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
    
    // Keyboard visual gameobject
    [Tooltip("Keyboard visual gameobject")]
    public GameObject Keyboard;

    [Header("Drum")]
    // Drum Rig
    [Tooltip("Drum Rig")]
    public Rig DrumRig;

    // Drum Rig Handler
    [Tooltip("Drum Rig Handler")]
    public DrumRigHandler DrumRigHandler;
    
    // Drum Object
    [Tooltip("Drum Object")]
    public GameObject Drum;

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
    

    void Start()
    {
        TryAssignMidiSources();

        // Initialize nreal hand rig to be active, all others inactive.
        // Deactivate the instrument objects

        HandsRig.weight = 1;
        KeyboardRig.weight = 0;
        Keyboard.SetActive(false);

        DrumRig.weight = 0;
        Drum.SetActive(false);

        GuitarRig.weight = 0;
        Guitar.SetActive(false);

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
                Keyboard.SetActive(false);
                Drum.SetActive(false);
                Guitar.SetActive(false);
                return;
            case 1:
                KeyboardRig.weight = 1;
                DrumRig.weight = 0;
                GuitarRig.weight = 0;
                Keyboard.SetActive(true);
                Drum.SetActive(false);
                Guitar.SetActive(false);
                return;
            case 2:
                KeyboardRig.weight = 0;
                DrumRig.weight = 1;
                GuitarRig.weight = 0;
                Keyboard.SetActive(false);
                Drum.SetActive(true);
                Guitar.SetActive(false);
                return;
            case 3:
                KeyboardRig.weight = 0;
                DrumRig.weight = 0;
                GuitarRig.weight = 1;
                Keyboard.SetActive(false);
                Drum.SetActive(false);
                Guitar.SetActive(true);
                return;
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
            }
        }
        else
        {
            HandsRig.weight = 0;
            KeyboardRig.gameObject.SetActive(true);
            DrumRig.gameObject.SetActive(true);
            GuitarRig.gameObject.SetActive(true);
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
            }
        }
    }
}
