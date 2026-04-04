from pythonosc import udp_client, dispatcher, osc_server
import threading
import time
import mido
import os
import traceback
import random

# Global client
client_sender = udp_client.SimpleUDPClient("127.0.0.1", 11000)

GENRE_OFFSETS = {
    "drive": 0, "pulse": 1, "groove": 2, "elegant": 3, "elegance": 3,
    "content_drive": 0, "content_pulse": 1, "content_groove": 2, "content_elegance": 3
}
INSTRUMENT_BASES = {
    128: 1,  # Drums (1-4)
    0: 6,    # Piano (6-9)
    24: 11,  # Guitar (11-14)
    33: 16,  # Bass (16-19)
    48: 21,  # Strings (21-24)
    73: 26   # Flute (26-29)
}
# FALLBACK: If no program is found, map channel directly to a likely instrument
CHANNEL_FALLBACK = {9: 128, 0: 0, 1: 24, 2: 33, 3: 48, 4: 73}
ENSEMBLE_PROGRAMS = [128, 0, 24, 33, 48, 73] 

query_event = threading.Event()
query_result = None
last_error = None
processing_lock = threading.Lock()
staged_clips_map = {}
local_slot_cache = {}
session_next_slot = 0
ensemble_count = 0

def ableton_handler(address, *args):
    global query_result, last_error
    if address == "/live/clip_slot/get/has_clip":
        if len(args) >= 3: query_result = args[2]; query_event.set()
    elif address == "/live/error":
        last_error = args[0] if args else "Unknown error"
        if "already has a clip" in str(last_error): query_result = True; query_event.set()

def find_next_empty_slot(track_index, start_slot=0):
    if track_index not in local_slot_cache: local_slot_cache[track_index] = set()
    slot = start_slot
    while slot < 300:
        if slot in local_slot_cache[track_index]: slot += 1; continue
        global query_result, last_error
        query_result = None; last_error = None
        query_event.clear()
        client_sender.send_message("/live/clip_slot/get/has_clip", [track_index, slot])
        if query_event.wait(timeout=0.2):
            if query_result is False:
                local_slot_cache[track_index].add(slot)
                return slot
        else:
            local_slot_cache[track_index].add(slot)
            return slot
        slot += 1
    return slot

def process_file_handler(address, *args):
    """Ensemble-aware MIDI processing with Channel-wise Fallback."""
    global session_next_slot
    with processing_lock:
        # Normalize paths for reliable lookup
        file_path = os.path.normpath(str(args[0]))
        auto_fire = bool(args[1]) if len(args) > 1 else True
        seed_path = os.path.normpath(str(args[2])) if (len(args) > 2 and args[2] and str(args[2]).strip() != "") else None
        is_drum_seed_flag = bool(args[3]) if len(args) > 3 else False
        beat_offset = float(args[4]) if len(args) > 4 else 0.0
        genre_arg = str(args[5]).lower() if len(args) > 5 else None
        
        filename = os.path.basename(file_path).lower()
        full_path_lower = file_path.lower()
        print(f"\n[CONTROLLER] Processing: {filename}")
        if seed_path: print(f"[CONTROLLER] Seed path: {os.path.basename(seed_path)}")
        if beat_offset > 0: print(f"[CONTROLLER] Applying beat offset: {beat_offset}")

        # Explicit Genre vs Detection
        detected_genre = "unknown"
        if genre_arg:
            detected_genre = genre_arg.replace("content_", "")
        else:
            # Fallback to detection if not provided
            for g in ["drive", "pulse", "groove", "elegance", "elegant"]:
                if f"content_{g}" in filename:
                    detected_genre = g
                    break
            if detected_genre == "unknown":
                temp_name = filename.replace("z_groove", "")
                for g in ["drive", "pulse", "groove", "elegance", "elegant"]:
                    if g in temp_name:
                        detected_genre = g
                        break
        
        # Normalize: elegance -> elegant
        if detected_genre == "elegance": detected_genre = "elegant"
        
        target_offset = GENRE_OFFSETS.get(detected_genre, -1)
        # Default to 0 if still unknown
        if target_offset == -1: target_offset = 0
        
        print(f"[CONTROLLER] Category: {detected_genre.upper()} | Offset: {target_offset}")
        
        is_multitrack = any(word in filename for word in ["interactive", "hybrid", "full_ensemble", "z_groove"])
        is_drum_file = (("drum" in filename or "drum" in full_path_lower) or is_drum_seed_flag) and not is_multitrack

        try:
            mid = mido.MidiFile(file_path)
            
            # Calculate total length in beats for looping
            max_tick = 0
            for track in mid.tracks:
                t = 0
                for msg in track:
                    t += msg.time
                if t > max_tick: max_tick = t
            file_len_beats = max_tick / mid.ticks_per_beat
            if file_len_beats < 1.0: file_len_beats = 4.0 # Sane minimum (1 bar)
            print(f"[CONTROLLER] Seed file length: {file_len_beats} beats")

            instrument_notes = {}
            # track_channel -> program_id
            channel_to_program = {} 
            max_beat_found = 0
            total_notes_parsed = 0
            
            for track in mid.tracks:
                current_tick = 0
                active_notes = {}
                for msg in track:
                    current_tick += msg.time
                    
                    if msg.type == "program_change":
                        channel_to_program[msg.channel] = msg.program
                    
                    elif msg.type == "note_on" and msg.velocity > 0:
                        if is_drum_seed_flag:
                            prog = 128
                        else:
                            prog = channel_to_program.get(msg.channel, CHANNEL_FALLBACK.get(msg.channel, 0))
                        
                        if is_multitrack and seed_path and prog == 128: continue 
                            
                        active_notes[(msg.channel, msg.note)] = (current_tick, msg.velocity, prog)
                    
                    elif (msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0)):
                        key = (msg.channel, msg.note)
                        if key in active_notes:
                            start_tick, velocity, prog = active_notes.pop(key)
                            
                            orig_start_beats = start_tick / mid.ticks_per_beat
                            duration_beats = (current_tick - start_tick) / mid.ticks_per_beat
                            
                            if is_drum_seed_flag:
                                # Looping/Wrapping logic
                                first_start = (orig_start_beats - beat_offset) % file_len_beats
                                
                                t = first_start
                                while t < 64.0:
                                    if prog not in instrument_notes: instrument_notes[prog] = []
                                    # EXPLICIT RULE: Original velocity for non-elegant drums
                                    if detected_genre == "elegant":
                                        out_vel = random.randint(15, 95)
                                    else:
                                        out_vel = int(velocity) # EXACT from file
                                    
                                    instrument_notes[prog].extend([msg.note, float(t), float(duration_beats), out_vel, 0])
                                    max_beat_found = max(max_beat_found, t + duration_beats)
                                    t += file_len_beats
                                    if file_len_beats <= 0: break # Safety
                                total_notes_parsed += 1
                            else:
                                start_beats = orig_start_beats - beat_offset
                                if start_beats < 0: continue
                                
                                max_beat_found = max(max_beat_found, start_beats + duration_beats)
                                if prog not in instrument_notes: instrument_notes[prog] = []
                                
                                # EXPLICIT RULE: Original for non-elegant drums, random for everything else
                                if prog == 128 and detected_genre != "elegant":
                                    out_vel = int(velocity) 
                                else:
                                    out_vel = random.randint(15, 95)
                                    
                                instrument_notes[prog].extend([msg.note, float(start_beats), float(duration_beats), out_vel, 0])
                                total_notes_parsed += 1

            print(f"[CONTROLLER] Parsed {total_notes_parsed} notes. Multitrack: {is_multitrack}, DrumFile: {is_drum_file}")

            # 1. Musically Conscious Silence Removal (PER INSTRUMENT)
            # We trim each track individually in 4-bar (16 beat) increments
            for prog in instrument_notes:
                notes = instrument_notes[prog]
                if not notes: continue
                
                while True:
                    earliest_this_prog = 999.0
                    for i in range(1, len(notes), 5):
                        if notes[i] < earliest_this_prog:
                            earliest_this_prog = notes[i]
                    
                    if earliest_this_prog != 999.0 and earliest_this_prog >= 16.0:
                        # Trim exactly 4 bars (16 beats)
                        print(f"[CONTROLLER] Instrument {prog}: 4 empty bars detected. Trimming for grid alignment.")
                        for i in range(1, len(notes), 5):
                            notes[i] -= 16.0
                    else:
                        break

            # 2. Slot Alignment
            if is_multitrack and seed_path and seed_path in staged_clips_map:
                target_track_idx, target_slot, _ = staged_clips_map[seed_path]
                print(f"[CONTROLLER] Reusing slot {target_slot} from seed: {os.path.basename(seed_path)}")
            else:
                primary_base = 1 if is_drum_file or (is_multitrack and not seed_path) else 7
                primary_track = primary_base + max(0, target_offset)
                target_slot = find_next_empty_slot(primary_track, start_slot=session_next_slot)
                
                # DJ WORKFLOW: 4 in a row, then 1 space gap
                global ensemble_count
                ensemble_count += 1
                if ensemble_count % 4 == 0:
                    session_next_slot = target_slot + 2
                    print(f"[CONTROLLER] Batch complete. Adding gap. Next batch starts at {session_next_slot}")
                else:
                    session_next_slot = target_slot + 1
                
                # Mark this slot as "staged" for the current file
                staged_clips_map[file_path] = (primary_track, target_slot, 0.0)
                
                scene_name = f"GEN-{target_slot + 1} ({detected_genre.upper()})"
                client_sender.send_message("/live/scene/set/name", [target_slot, scene_name])
                print(f"[CONTROLLER] Assigned NEW slot {target_slot}. ensemble_count: {ensemble_count}")

            # 3. Clip Duration
            clip_duration = 16.0
            if max_beat_found > 0:
                bars = int((max_beat_found + 3.99) // 4)
                clip_duration = float(min(16, max(4, bars * 4)))

            # 3. Process Ensemble
            progs_to_process = ENSEMBLE_PROGRAMS if is_multitrack else instrument_notes.keys()
            
            for prog in progs_to_process:
                if prog in INSTRUMENT_BASES:
                    base_track = INSTRUMENT_BASES[prog]
                    target_track = base_track + max(0, target_offset)
                    
                    if target_track not in local_slot_cache: local_slot_cache[target_track] = set()
                    local_slot_cache[target_track].add(target_slot)
                    
                    client_sender.send_message("/live/clip_slot/create_clip", [target_track, target_slot, clip_duration])
                    time.sleep(0.1) # Increased for stability
                    
                    notes = instrument_notes.get(prog, [])
                    if notes:
                        client_sender.send_message("/live/clip/add/notes", [target_track, target_slot] + notes)
                    
                    label = "[MIDI]" if notes else "[EMPTY]"
                    print(f"Row {target_slot} -> Track {target_track} ({prog}) {label}")


            # 4. Clean Slate (Stop Previous)
            # Find all tracks that might be playing and stop them
            tracks_to_stop = []
            for genre_offset in GENRE_OFFSETS.values():
                for base in INSTRUMENT_BASES.values():
                    tracks_to_stop.append(base + genre_offset)
            
            for t in tracks_to_stop: client_sender.send_message("/live/track/stop_all_clips", [t])
            print(f"[CONTROLLER] Clean slate: stopped all managed tracks.")

        except Exception as e:
            print("[CONTROLLER] Error: " + str(e))
            traceback.print_exc()

def main():
    ableton_dispatcher = dispatcher.Dispatcher()
    ableton_dispatcher.set_default_handler(ableton_handler)
    ableton_dispatcher.map("/live/clip_slot/get/has_clip", ableton_handler)
    ableton_dispatcher.map("/live/error", ableton_handler)
    web_dispatcher = dispatcher.Dispatcher()
    web_dispatcher.map("/web/midi/process_file", process_file_handler)
    start_server = lambda ip, port, disp: threading.Thread(
        target=osc_server.ThreadingOSCUDPServer((ip, port), disp).serve_forever, daemon=True
    ).start()
    start_server("127.0.0.1", 11001, ableton_dispatcher)
    start_server("0.0.0.0", 11002, web_dispatcher)
    print("Ableton Controller Ready (v5.15 - Channel Fallback).")
    client_sender.send_message("/live/test", [])
    try:
        while True: time.sleep(1)
    except KeyboardInterrupt: print("\nExiting...")

if __name__ == "__main__":
    main()

