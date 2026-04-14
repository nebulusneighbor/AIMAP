using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Winds rig: maps MIDI-derived blends (from <see cref="AIMAP.Avatars.AimapAvatarMidiHandler"/> keyboard-style processing)
/// to head rotation instead of hand IK.
/// </summary>
public class WindsRigHandler : MonoBehaviour
{
    [Header("Winds Rig")]
    [Tooltip("Animation rig for winds")]
    public Rig WindsRig;

    [SerializeField]
    [Tooltip("Head bone or IK target to rotate (local space relative to this transform's parent chain)")]
    private Transform _headTarget;

    [Header("Head mapping")]
    [SerializeField]
    [Tooltip("Max yaw (left/right turn) in degrees when right-hand blend is 0 vs 1")]
    private float _maxYawDegrees = 28f;

    [SerializeField]
    [Tooltip("Max pitch (nod) in degrees when left-hand blend is 0 vs 1")]
    private float _maxPitchDegrees = 22f;

    [SerializeField]
    [Tooltip("Smooth time for head angles")]
    private float _angleSmoothTime = 0.12f;

    private Quaternion _headNeutralLocal;
    private float _midiLeft01;
    private float _midiRight01;
    private float _smoothedPitch;
    private float _smoothedYaw;
    private float _pitchVel;
    private float _yawVel;

    private void Start()
    {
        if (_headTarget != null)
        {
            _headNeutralLocal = _headTarget.localRotation;
        }
    }

    private void LateUpdate()
    {
        if (_headTarget == null || WindsRig == null || WindsRig.weight < 1f)
        {
            return;
        }

        var left = Mathf.Clamp01(_midiLeft01);
        var right = Mathf.Clamp01(_midiRight01);

        var targetPitch = (left - 0.5f) * 2f * _maxPitchDegrees;
        var targetYaw = (right - 0.5f) * 2f * _maxYawDegrees;

        _smoothedPitch = Mathf.SmoothDampAngle(_smoothedPitch, targetPitch, ref _pitchVel, _angleSmoothTime);
        _smoothedYaw = Mathf.SmoothDampAngle(_smoothedYaw, targetYaw, ref _yawVel, _angleSmoothTime);

        _headTarget.localRotation = _headNeutralLocal * Quaternion.Euler(-_smoothedPitch, _smoothedYaw, 0f);
    }

    /// <summary>
    /// Call each frame from the rig driver. Values are typically <see cref="AIMAP.Avatars.AimapAvatarMidiHandler.KeyLeftHand"/> / KeyRightHand (0–1 style blends from note layout).
    /// </summary>
    public void SetMidiHeadDrive(float keyLeftBlend01, float keyRightBlend01)
    {
        _midiLeft01 = keyLeftBlend01;
        _midiRight01 = keyRightBlend01;
    }
}
