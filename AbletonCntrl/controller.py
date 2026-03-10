from pythonosc import udp_client, dispatcher, osc_server
import threading
import time
import mido
import os
import traceback

# Global client to communicate with Ableton
client_sender = udp_client.SimpleUDPClient("127.0.0.1", 11000)

MIDI_FOLDER = r"E:\AI Music\AIMAP_generate-git\Drum_Pipeline\midi_generate"

# Genre to Track Offsets (0-4)
GENRE_OFFSETS = {
    "rock": 0,
    "hiphop": 1,
    "jazz": 2,
    "funk": 3,
    "latin": 4
}

# Base Tracks for Instruments
INSTRUMENT_BASES = {
    128: 1, # Drums -> Tracks 1-5
    0: 7,   # Piano -> Tracks 7-11
    24: 13, # Guitar -> Tracks 13-17
    33: 19, # Bass -> Tracks 19-23
    48: 24, # Strings -> Track 24
    73: 25  # Flute -> Track 25
}

# Shared state for sync queries
query_event = threading.Event()
query_result = None
last_error = None

# Threading lock to prevent race conditions during slot detection
processing_lock = threading.Lock()

def ableton_handler(address, *args):
    global query_result, last_error
    if address == "/live/clip_slot/get/has_clip":
        if len(args) >= 3:
            query_result = args[2]
            query_event.set()
    elif address == "/live/error":
        last_error = args[0] if args else "Unknown error"
        if "already has a clip" in str(last_error):
            query_result = True
            query_event.set()

def find_next_empty_slot(track_index, start_slot=0):
    slot = start_slot
    while slot < 100:
        global query_result, last_error
        query_result = None
        last_error = None
        query_event.clear()
        client_sender.send_message("/live/clip_slot/get/has_clip", [track_index, slot])
        if query_event.wait(timeout=0.2):
            if query_result is False: return slot
        else: return slot
        slot += 1
    return slot

def process_file_handler(address, *args):
    """Robust MIDI parsing with threading lock and instrument filtering."""
    with processing_lock:
        file_path = str(args[0])
        auto_fire = bool(args[1]) if len(args) > 1 else True
        filename = os.path.basename(file_path).lower()
        
        print(f"\n[CONTROLLER] Processing: {filename} (Auto-fire: {auto_fire})")

        detected_genre = None
        for genre in GENRE_OFFSETS.keys():
            if f"_{genre}_" in filename:
                detected_genre = genre
                break
        
        target_offset = GENRE_OFFSETS.get(detected_genre, -1)

        try:
            mid = mido.MidiFile(file_path)
            instrument_notes = {}
            
            # 1. Map Channels to Programs
            is_drum_file = "drum" in filename and "interactive" not in filename
            is_multitrack = "interactive" in filename
            channel_to_program = {9: 128} if is_drum_file else {}
            
            for track in mid.tracks:
                current_tick = 0
                active_notes = {}
                for msg in track:
                    current_tick += msg.time
                    
                    if msg.type == "program_change":
                        channel_to_program[msg.channel] = msg.program
                    
                    elif msg.type == "note_on" and msg.velocity > 0:
                        prog = channel_to_program.get(msg.channel, 128 if is_drum_file else 0)
                        
                        # FILTER: If this is a multitrack file, skip program 128 (seed drums)
                        # to prevent overwriting the dedicated drum generation tracks.
                        if is_multitrack and prog == 128:
                            continue
                            
                        active_notes[(msg.channel, msg.note)] = (current_tick, msg.velocity, prog)
                    
                    elif (msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0)):
                        key = (msg.channel, msg.note)
                        if key in active_notes:
                            start_tick, velocity, prog = active_notes.pop(key)
                            start_beats = start_tick / mid.ticks_per_beat
                            duration_beats = (current_tick - start_tick) / mid.ticks_per_beat
                            
                            if prog not in instrument_notes:
                                instrument_notes[prog] = []
                            instrument_notes[prog].extend([msg.note, float(start_beats), float(duration_beats), int(velocity), 0])

            # 2. Stage each instrument
            current_batch = []
            for prog, notes in instrument_notes.items():
                if prog in INSTRUMENT_BASES:
                    base_track = INSTRUMENT_BASES[prog]
                    target_track = base_track if base_track >= 24 else base_track + max(0, target_offset)
                    
                    slot = find_next_empty_slot(target_track)
                    print(f"Assigning {prog} to Track {target_track}, Slot {slot}...")
                    
                    client_sender.send_message("/live/clip_slot/create_clip", [target_track, slot, 8.0])
                    time.sleep(0.1) # Increased delay for slot registration
                    client_sender.send_message("/live/clip/add/notes", [target_track, slot] + notes)
                    
                    if auto_fire:
                        current_batch.append((target_track, slot))

            # 3. Synchronized Drop
            if auto_fire and current_batch:
                if target_offset != -1:
                    tracks_to_stop = []
                    for genre_offset in GENRE_OFFSETS.values():
                        if genre_offset != target_offset:
                            for base in [1, 7, 13, 19]: tracks_to_stop.append(base + genre_offset)
                    
                    # Strings/Flute restart logic
                    tracks_to_stop.extend([24, 25])
                    
                    for t in tracks_to_stop:
                        client_sender.send_message("/live/track/stop_all_clips", [t])
                    time.sleep(0.1)

                print(f"[CONTROLLER] Firing ensemble batch of {len(current_batch)} clips...")
                for t_idx, s_idx in current_batch:
                    client_sender.send_message("/live/clip/fire", [t_idx, s_idx])
                    time.sleep(0.05)
                
                print("[CONTROLLER] All tracks fired.")

        except Exception as e:
            print(f"[CONTROLLER] Error: {e}")
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
    print("Ableton Controller Ready (v5.4 - Lock & Filter).")
    client_sender.send_message("/live/test", [])
    try:
        while True: time.sleep(1)
    except KeyboardInterrupt: print("\nExiting...")

if __name__ == "__main__":
    main()

