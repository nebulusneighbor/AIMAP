# AIMAP Unity — agent memory (implementation snapshot)

**Purpose:** High-level map of the current OSC → avatar MIDI → animation rig pipeline so follow-up work can start from this doc instead of re-discovering the whole tree.  
**Last reviewed:** 2026-04-12 (matches repo state at time of writing).  
**Caveat:** Treat this as a **snapshot**. Before precise edits, diff or open the cited files; line numbers and small details can drift.

---

## Repository layout (relevant parts)

| Area | Path | Role |
|------|------|------|
| Core OSC / avatars | `Unity/AIMAP/Assets/Scripts/Osc/`, `.../Avatars/`, `.../Diagnostics/` | Production AIMAP routing, MIDI decode, avatar slots, optional plane debug UI |
| XR jam pack | `Unity/AIMAP/Assets/xr-jam/` | Scenes, prefabs, materials/models, rigging scripts (`Scripts/Rigging/`, `Scripts/OSC/MIDIProcessor.cs`) |

---

## Data flow (end-to-end)

```
extOSC OSCReceiver(s) on listen port(s)
    → AimapOscRouter binds per-role addresses
    → (midi) AimapAvatarMidiMessageDecoder → AimapAvatarMidiHandler.ApplyMidiMessage
    → (optional) AvatarSlotController.SetPerformanceState from active notes
    → InstrumentRigManager reads handler each frame → KeyboardRigHandler / DrumRigHandler / GuitarRigHandler
```

- **Without** `AimapAvatarMidiHandler` on a slot: router binds `/avatar/{roleId}/state` (not `/midi`) for that slot; skin and skybox still bind.
- **Important scene wiring rule:** `AimapOscRouter.FindMidiHandler` only searches the slot object itself plus its children and parents. In `AIMAPVR`, slot objects such as `guitar_Slot` use `targetAvatarName` and are not automatically parented to the named avatar object, so putting `AimapAvatarMidiHandler` only on the standalone avatar prefab is not enough unless that avatar is also in the slot hierarchy.

---

## OSC addresses (AimapOscRouter)

Bound **per** `OSCReceiver` instance, for each `AvatarSlotController` with a non-empty role (role from `AimapAvatarMidiHandler.RoleId` if present, else `AvatarSlotController.RoleId`):

| Address pattern | When | Handler behavior |
|-----------------|------|------------------|
| `/avatar/{roleId}/midi` | Slot has `AimapAvatarMidiHandler` | Decode OSC args → `ApplyMidiMessage` |
| `/avatar/{roleId}/state` | No MIDI handler on slot | `AvatarSlotController.SetPerformanceState(bool)` |
| `/avatar/{roleId}/skin` | Always (per slot) | `SetSkin(int)` |
| `/environment/skybox` | Once per receiver | `SkyboxEnvironmentController.SetSkybox(int)` |

**Role resolution:** `FindSlot` / handler lookup match `AimapAvatarMidiHandler.MatchesRole` first, then `slot.RoleId` (case-insensitive).

**Diagnostics:** Every handled message can report to `AimapOscDiagnostics` and `AimapOscPortDebugPanel` via `ReportMessage`.

**Key scripts:** `Assets/Scripts/Osc/AimapOscRouter.cs`, `Assets/Scripts/Avatars/AvatarSlotController.cs`.

---

## MIDI message encoding (AimapAvatarMidiMessage)

**Types:** `AIMAP.Osc.AimapAvatarMidiMessage` (struct) + `AimapAvatarMidiMessageDecoder.TryDecode(extOSC.OSCMessage, out ...)`.

**Supported OSC argument layouts:**

1. **1 value:** `[noteNumber]` — defaults to channel `1`, velocity `127`, treated as a transient note trigger.
2. **2 values:** `[noteNumber, velocity]` — defaults to channel `1`; treated as a transient trigger layout.
3. **3 values:** `[channel, noteNumber, velocity]` — note-on inferred as `velocity > 0`.
4. **4+ values:** `[noteOnState, channel, noteNumber, velocity]` — first arg is bool-like (true/false, int, float, or strings like `noteon` / `off` / `on` / etc.).

**Sanitization after decode:** channel ∈ [1, 16], note ∈ [0, 127], velocity ∈ [0, 127].

**Transient behavior:** One-value and two-value layouts set `ShouldSustain = false`, so `AimapAvatarMidiHandler` processes them as trigger-style events and clears active notes afterward instead of requiring a matching note-off.

**File:** `Assets/Scripts/Osc/AimapAvatarMidiMessage.cs`.

---

## AimapAvatarMidiHandler (bridge to rigs)

**Namespace:** `AIMAP.Avatars`. **File:** `Assets/Scripts/Avatars/AimapAvatarMidiHandler.cs`.

**Serialized:** `roleId` (optional; falls back to linked `AvatarSlotController.RoleId`), `InstrumentRigMode`, `avatarSlot`, `instrumentRigManager`, `driveAvatarPerformanceState`.

**Instrument modes (int `InstrumentMode` mirrors enum order):**

| Mode | Value | Behavior |
|------|-------|----------|
| Hands | 0 | Clears rig-driven state; no keyboard/drum/guitar processing |
| Keyboard | 1 | Middle note **44**; channel **1** only for hand averages and key hits. Left: note &lt; 44, right: ≥ 44. `KeyLeftHand` / `KeyRightHand` from averages of active notes; `LeftKeyHit` / `RightKeyHit` on note-on for last message on ch 1 |
| Drums | 2 | Channel **1** only. Even `noteNumber` → left hit, odd → right hit (boolean for current message note-on with velocity &gt; 0) |
| Guitar | 3 | Fretting: any active note on channel **7 or 1** sets `GuitarLeftHand = clamp((leftNote - 40) / 10, 0, 1)`. `ShouldStrum` = note-on with velocity &gt; 0 on **every** message (not channel-filtered in code) |

**Active note list:** `(noteNumber, channel)` pairs; note-on with velocity &gt; 0 adds, else removes. For transient note-only / note+velocity payloads, the handler clears the note list after processing.

**Hookup:** `OnEnable` → `instrumentRigManager?.BindMidiHandler(this)`.

**Placement guidance:** For the current `AIMAPVR` scene structure, the safest place for `AimapAvatarMidiHandler` is on the `*_Slot` object itself, or anywhere in that slot's parent/child hierarchy. A handler placed only on a separately named avatar object referenced by `targetAvatarName` will not be discovered by the router.

---

## InstrumentRigManager (xr-jam rig driver)

**File:** `Assets/xr-jam/Scripts/Rigging/InstrumentRigManager.cs` (global namespace).

**Role:** Single `_aimapMidiHandler` reference; `Update` calls `UpdateFromAimapMidiHandler()` when assigned. `BindMidiHandler` / `TryAssignMidiSources` resolve handler on same object, parent, or children.

**Rig indices (`CurrentRig`):** 0 = hands only (weights + deactivate instrument GOs); 1 = keyboard; 2 = drums; 3 = guitar. `SetInstrumentRigs` sets Animation Rigging `Rig.weight` and `SetActive` on instrument GameObjects.

**Per-frame mapping:**

- Mode 1 → `KeyboardRigHandler` positions + `LeftHit`/`RightHit`
- Mode 2 → `DrumRigHandler` `LeftHit`/`RightHit`
- Mode 3 → `GuitarRigHandler` `LeftHandPosition` + `Strum`

**Also exposes:** `RealHands(bool)` for toggling real hand tracking vs instrument rig objects.

---

## Rig handlers (xr-jam)

| Class | File | Effect |
|-------|------|--------|
| `KeyboardRigHandler` | `.../Rigging/KeyboardRigHandler.cs` | SmoothDamp IK targets along spread axis from `LeftHandPosition`/`RightHandPosition`; key hit rotates IK targets |
| `DrumRigHandler` | `.../Rigging/DrumRigHandler.cs` | Hit offsets IK local Y by `HitDepth` |
| `GuitarRigHandler` | `.../Rigging/GuitarRigHandler.cs` | SmoothDamp left IK along neck vector; `Strum` rotates right IK |

All use `UnityEngine.Animations.Rigging.Rig`.

---

## Diagnostics: AimapOscPlaneCanvasBootstrap

**File:** `Assets/Scripts/Diagnostics/AimapOscPlaneCanvasBootstrap.cs`.

- `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`: creates `DontDestroyOnLoad` host.
- Only activates when **active scene name** is exactly `"animationtest"`.
- Finds **`Plane`**, adds/finds `OSCReceiver` on it with **`LocalPort = 11003`**, binds the same `/avatar/{role}/state|skin|midi` roles plus `/environment/skybox` (roles: `dancer1`, `dancer2`, `drum1`, `drum2`, `bass`, `guitar`, `violin`).
- Renders last message on a world-space UI `Text` under the plane — **display only**; does not drive avatars.

---

## Legacy / empty

- **`Assets/xr-jam/Scripts/OSC/MIDIProcessor.cs`:** Entire previous MIDI/OSC logic is **commented out**; class is an empty `MonoBehaviour`. The live MIDI path is **OSC → AimapOscRouter → AimapAvatarMidiHandler → InstrumentRigManager**.

---

## Scenes / assets (high level)

- Main VR / app scenes under `Assets/Scenes/` (e.g. `AIMAPVR.unity` — subject to project changes).
- `Assets/Scenes/AIMAPVR.unity` currently uses scene slot objects with `targetAvatarName` references; this matters for MIDI handler discovery because the router does not search arbitrary named objects outside the slot hierarchy.
- `Assets/Scenes/AIMAPVR_OscTest.unity` includes slot-local `AimapAvatarMidiHandler` components for all test roles and an `AimapOscTestConsole` configured to send note-only MIDI payloads to the Quest IP `10.0.0.124`.
- XR jam test scenes under `Assets/xr-jam/_Scenes/` including `networked-room-xrjam.unity` and `Tests/*`.
- Prefabs under `Assets/xr-jam/Resources/` (`AvatarAnimV1`, `BasicAvatarSitting`, `BasicMIDISender`, `BasicPlayer`, etc.).

---

## Quick file index

| Topic | Path |
|-------|------|
| OSC routing | `Unity/AIMAP/Assets/Scripts/Osc/AimapOscRouter.cs` |
| MIDI decode | `Unity/AIMAP/Assets/Scripts/Osc/AimapAvatarMidiMessage.cs` |
| MIDI → slot + rigs | `Unity/AIMAP/Assets/Scripts/Avatars/AimapAvatarMidiHandler.cs` |
| Avatar visuals / skin / play state | `Unity/AIMAP/Assets/Scripts/Avatars/AvatarSlotController.cs` |
| Rig orchestration | `Unity/AIMAP/Assets/xr-jam/Scripts/Rigging/InstrumentRigManager.cs` |
| Plane OSC debug | `Unity/AIMAP/Assets/Scripts/Diagnostics/AimapOscPlaneCanvasBootstrap.cs` |

---

## Suggested follow-up for Cursor

Add a **short** rule under `.cursor/rules/` that says: *For Unity OSC/MIDI/rigging work, read `/memory.md` first; open C# files only when changing behavior or verifying details.*

(Only add that if you want the editor to enforce it — the file above is sufficient on its own.)
