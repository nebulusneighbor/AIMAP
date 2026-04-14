using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Bowed strings rig: left hand slides on the neck (same idea as <see cref="GuitarRigHandler"/>),
/// right-hand IK drives bow motion while MIDI indicates playing (sustained notes).
/// </summary>
public class StringsRigHandler : MonoBehaviour
{
    [Header("Strings Rig")]
    [Tooltip("Animation rig for bowed strings")]
    public Rig StringsRig;

    [SerializeField]
    [Tooltip("Left hand IK target (fingering)")]
    private GameObject _leftHandIK;

    [SerializeField]
    [Tooltip("Left hand end of travel along the neck")]
    private GameObject _leftHandEnd;

    [SerializeField]
    [Tooltip("Right hand IK target (bow)")]
    private GameObject _rightHandIK;

    [Header("Hand positions")]
    [Tooltip("Position of left hand along the neck")]
    [Range(0f, 1f)]
    public float LeftHandPosition;

    [Header("Movement")]
    [SerializeField]
    [Tooltip("Smooth time for left hand along the neck")]
    private float _leftHandSmoothTime = 0.08f;

    [Header("Bow")]
    [SerializeField]
    [Tooltip("Bow plane rotation (degrees) applied at full stroke")]
    private float _bowPlaneAngle = 18f;

    [SerializeField]
    [Tooltip("Bow stroke oscillation speed while playing")]
    private float _bowStrokeSpeed = 10f;

    private Vector3 _leftHandStartPos;
    private Vector3 _leftDisplacement;
    private Vector3 _leftVelocity;
    private Vector3 _leftTarget;

    private Quaternion _bowNeutralLocal;
    private Quaternion _bowStrokeLocal;
    private bool _bowPlaying;

    private void Start()
    {
        _leftHandStartPos = _leftHandIK.transform.localPosition;
        _leftDisplacement = _leftHandEnd.transform.localPosition - _leftHandStartPos;

        _bowNeutralLocal = _rightHandIK.transform.localRotation;
        _bowStrokeLocal = _bowNeutralLocal * Quaternion.Euler(0f, _bowPlaneAngle, 0f);
    }

    private void Update()
    {
        if (StringsRig == null || StringsRig.weight < 1f)
        {
            return;
        }

        _leftTarget = _leftHandStartPos + _leftDisplacement * LeftHandPosition;
        _leftHandIK.transform.localPosition = Vector3.SmoothDamp(
            _leftHandIK.transform.localPosition,
            _leftTarget,
            ref _leftVelocity,
            _leftHandSmoothTime);

        if (_bowPlaying)
        {
            var stroke = (Mathf.Sin(Time.time * _bowStrokeSpeed) + 1f) * 0.5f;
            _rightHandIK.transform.localRotation = Quaternion.Slerp(_bowNeutralLocal, _bowStrokeLocal, stroke);
        }
        else
        {
            _rightHandIK.transform.localRotation = _bowNeutralLocal;
        }
    }

    /// <summary>
    /// Drive bow animation from MIDI: true while notes are active (playing).
    /// </summary>
    public void SetBowPlaying(bool playing)
    {
        _bowPlaying = playing;
    }
}
