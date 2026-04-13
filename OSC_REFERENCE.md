# AIMAP OSC Reference

This document describes the OSC addresses currently supported by the AIMAP OSC pipeline.

## Ports

- `11003`: primary OSC input port.
- `2348`: secondary OSC input port.

Both ports are bound by `AimapOscRouter`, and either port can receive the same supported addresses.

## Role IDs

The current osc test scene `Unity/AIMAP/Assets/Scenes/AIMAPVR_OscTest.unity` binds these role IDs:

- `dancer1`
- `dancer2`
- `drum1`
- `drum2`
- `bass`
- `guitar`
- `violin`

## Router Rules

`AimapOscRouter` binds addresses per slot using these rules:

- If a slot has an `AimapAvatarMidiHandler`, the router binds `/avatar/{roleId}/midi`.
- If a slot does not have an `AimapAvatarMidiHandler`, the router binds `/avatar/{roleId}/state`.
- The router always binds `/avatar/{roleId}/skin`.
- The router always binds `/environment/skybox`.

Because of that, MIDI reception depends on the scene having an `AimapAvatarMidiHandler` for the target role.

Important discovery detail:

- The router finds handlers only on the slot object itself, its children, or its parents.
- In `AIMAPVR`, slot objects can point at avatars by `targetAvatarName` without those avatars being in the slot hierarchy.
- Because of that, putting `AimapAvatarMidiHandler` only on a standalone avatar prefab is not enough unless that avatar is also under the slot hierarchy.
- For the current main scene structure, the safest placement is on the `*_Slot` object itself or in that slot's parent/child chain.

## Supported Addresses

### Avatar State

Address pattern:

```text
/avatar/{roleId}/state
```

Used by:

- `AimapOscRouter.HandleAvatarState()`
- `AvatarSlotController.SetPerformanceState()`

Accepted first value forms:

- `int`: `0` or `1`
- `float`: `0.0` or non-zero
- `bool`
- `string`: `play`, `playing`, `dance`, `dancing`, `on`, `true`, `false`

Examples:

```text
/avatar/dancer1/state [1]
/avatar/dancer2/state ["play"]
/avatar/drum1/state [true]
```

### Avatar Skin

Address pattern:

```text
/avatar/{roleId}/skin
```

Used by:

- `AimapOscRouter.HandleAvatarSkin()`
- `AvatarSlotController.SetSkin()`

Accepted first value forms:

- `int`
- `float` rounded to int
- numeric string

Examples:

```text
/avatar/guitar/skin [0]
/avatar/bass/skin [1]
/avatar/violin/skin [2]
```

### Avatar MIDI

Address pattern:

```text
/avatar/{roleId}/midi
```

##Need to update
now each instrument has 2 midi value
/avatar/{roleid}1/midi,41,48 etc
one is note, one is vality
animation can fit volicty
instrument:
Drums,piano,Gutairs,Bass,Strings,winds
dacemove:
Hippop,brakedance,latin,1lessdrink in address
/avatar/dancer1/move -> just for dancing pattern
Once any osc message is sending to the headset, start dancing and stop after 5 sceonds
Standing will be better

Used by:

- `AimapOscRouter.HandleAvatarMidi()`
- `AimapAvatarMidiMessageDecoder.TryDecode()`
- `AimapAvatarMidiHandler.ApplyMidiMessage()`

Supported payload formats:

1. One-value format:

```text
[noteNumber]
```

2. Two-value format:

```text
[noteNumber, velocity]
```

3. Three-value format:

```text
[channel, noteNumber, velocity]
```

4. Four-value format:

```text
[noteState, channel, noteNumber, velocity]
```

Where:

- `channel` is clamped to `1..16`
- `noteNumber` is clamped to `0..127`
- `velocity` is clamped to `0..127`

Accepted `noteState` forms:

- `bool`
- `int` or `float`
- `string`: `noteon`, `noteoff`, `on`, `off`, `play`, `idle`, `true`, `false`

Decode behavior:

- In one-value format, the decoder treats the message as note-on on channel `1` with velocity `127`.
- In one-value and two-value formats, the message is treated as a transient trigger rather than a sustained held note.
- In two-value format, `velocity > 0` means note on.
- In two-value format, `velocity == 0` means note off.
- In three-value format, `velocity > 0` means note on.
- In three-value format, `velocity == 0` means note off.
- In four-value format, `noteState` explicitly determines note on or note off.

Examples:

```text
/avatar/violin/midi [60]
/avatar/violin/midi [60, 96]
/avatar/violin/midi [1, 60, 96]
/avatar/violin/midi [1, 60, 0]
/avatar/drum1/midi ["noteon", 1, 38, 127]
/avatar/drum1/midi ["noteoff", 1, 38, 0]
```

## Instrument Handling

`AimapAvatarMidiHandler` supports these instrument modes:

- `0`: Hands
- `1`: Keyboard
- `2`: Drums
- `3`: Guitar

Behavior notes:

- Note-only MIDI messages are treated as transient trigger events for easier OSC integration.
- Hands mode only updates active-note state and avatar performance state.
- Keyboard mode only applies hit logic for channel `1`.
- Drum mode only applies hit logic for channel `1`.
- Guitar mode treats active notes on channel `1` or `7` as fretting input.

## Test Scene Notes

For the built-in osc test scene:

- The test console sends MIDI to `/avatar/{roleId}/midi`.
- The test console now sends the one-value MIDI payload format: `[noteNumber]`.
- The saved test scene now has `AimapAvatarMidiHandler` components on all test roles, so the original router will bind MIDI addresses there.
- The saved test scene transmitter target host is set to the Quest device IP `10.0.0.124`.
- The main `AIMAPVR` scene may need handlers on the slot hierarchy rather than only on the named avatar object if you want the router to bind MIDI there.

## Common Debug Checks

If MIDI still does not react:

1. Confirm you are sending to the same port the receiver is listening on: `11003` or `2348`.
2. Confirm the OSC address exactly matches `/avatar/{roleId}/midi`.
3. Confirm the role ID exactly matches the scene binding, for example `violin` not `Violin`.
4. Confirm the payload is one of `[noteNumber]`, `[noteNumber, velocity]`, `[channel, noteNumber, velocity]`, or `[noteState, channel, noteNumber, velocity]`.
5. Check the Unity Console for the router warning about failed MIDI decode.
6. Check that the scene object for that role actually has an `AimapAvatarMidiHandler`.
7. In `AIMAPVR`, check that the handler is on the slot object or within the slot hierarchy, not only on a separate avatar object found by name.
