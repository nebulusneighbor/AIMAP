import os
import glob
import random
import numpy as np
import mido
from collections import defaultdict
from concurrent.futures import ProcessPoolExecutor# --- Phase 1: Constants & Configurations ---
LAMD_PATH = r"E:\AI Music\Database\Los-Angeles-MIDI-Dataset-Ver-4-0-CC-BY-NC-SA"
TARGET_DATASET_SIZE = 25000

# 4/4 Time Signature means 4 Quarter notes per bar.
# 48th-note quantization means dividing a quarter note into 12 parts.
# 4 quarter notes * 12 parts = 48 parts per whole note = 128 positions per 4/4 bar? 
# Wait, 48th note grid = 1/48 of a whole note.
# A 4/4 bar has 4 quarter notes (which is 1 whole note). So 48 positions per bar! 
# Let's clarify the prompt: "48th-note grid. This divides a single 4/4 bar into 128 possible discrete temporal positions."
# Wait, standard MIDI uses 32nd notes or 96th notes.
# If a 4/4 bar is divided into 128 positions, that is effectively a 128th-note grid.
# The user might have meant 128 ticks per beat, or 128 positions per bar. 
# We will use 128 positions per bar as explicitly stated: "divides a single 4/4 bar into 128 possible discrete temporal positions."
POSITIONS_PER_BAR = 128

# Krumhansl-Schmuckler key profiles
# 12-dimensional profiles for Major and Minor keys
MAJOR_PROFILE = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MINOR_PROFILE = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])

def calculate_key(pitch_histogram):
    """
    Estimates the key of a song using the Krumhansl-Schmuckler algorithm.
    Returns (key_index, is_major) where key_index is 0-11 (C, C#, D, ...).
    """
    max_corr = -1.0
    best_key = 0
    is_major = True
    
    for i in range(12):
        # Shift profile to match current key hypothesis
        shifted_major = np.roll(MAJOR_PROFILE, i)
        shifted_minor = np.roll(MINOR_PROFILE, i)
        
        # Calculate correlation natively
        corr_major = np.corrcoef(pitch_histogram, shifted_major)[0, 1]
        corr_minor = np.corrcoef(pitch_histogram, shifted_minor)[0, 1]
        
        if corr_major > max_corr:
            max_corr = corr_major
            best_key = i
            is_major = True
            
        if corr_minor > max_corr:
            max_corr = corr_minor
            best_key = i
            is_major = False
            
    return best_key, is_major

def get_transpose_shift(key_index, is_major):
    """
    Returns the integer offset to transpose the song to C Major or A Minor.
    """
    if is_major:
        # Transpose to C (index 0)
        shift = 0 - key_index
    else:
        # Transpose to A (index 9)
        shift = 9 - key_index
        
    # Minimize the shift distance (e.g., instead of +11, do -1)
    if shift > 6:
        shift -= 12
    elif shift < -6:
        shift += 12
        
    return shift

def parse_and_normalize_midi(file_path):
    """
    Phase 1:
    - Filters for 4/4
    - Normalizes Key to C Major / A Minor
    - Quantizes to 128 positions per bar
    """
    try:
        mid = mido.MidiFile(file_path)
    except Exception as e:
        return None
        
    # 1. Filter by Time Signature (Must be 4/4)
    # Default is 4/4 if not specified
    time_sig_found = False
    is_4_4 = True
    
    for track in mid.tracks:
        for msg in track:
            if msg.type == 'time_signature':
                time_sig_found = True
                if msg.numerator != 4 or msg.denominator != 4:
                    is_4_4 = False
                    break
        if not is_4_4:
            break
            
    if time_sig_found and not is_4_4:
        return None # Skip this file
        
    # 2. Key Normalization
    # Collect all pitched notes to build histogram
    pitch_histogram = np.zeros(12)
    has_notes = False
    
    for track in mid.tracks:
        for msg in track:
            if msg.type == 'note_on' and msg.velocity > 0:
                # Ignore channel 9 (drums) for pitch calculation!
                if msg.channel != 9:
                    pitch_histogram[msg.note % 12] += 1
                    has_notes = True
                    
    if not has_notes:
        best_key = 0
        is_major = True
        transpose_shift = 0
    else:
        best_key, is_major = calculate_key(pitch_histogram)
        transpose_shift = get_transpose_shift(best_key, is_major)
    
    # 3. Quantization and Extraction
    ticks_per_quarter = mid.ticks_per_beat
    ticks_per_bar = ticks_per_quarter * 4
    ticks_per_pos = ticks_per_bar / POSITIONS_PER_BAR
    
    parsed_events = [] # Elements: (abs_pos, ev_type, channel, val1)
                       # ev_type: 0 = note_off, 1 = note_on, 2 = program_change
    
    # Iterate through tracks individually to avoid mido.merge_tracks overhead
    for track in mid.tracks:
        current_ticks = 0
        for msg in track:
            current_ticks += msg.time
            abs_pos = int(round(current_ticks / ticks_per_pos))
            
            if msg.type == 'program_change':
                parsed_events.append((abs_pos, 2, msg.channel, msg.program))
                
            elif msg.type == 'note_on' and msg.velocity > 0:
                pitch = msg.note
                if msg.channel != 9: # Transpose pitched instruments
                    pitch = pitch + transpose_shift
                    if pitch < 0 or pitch > 127: # Out of bounds after transpose
                        continue
                parsed_events.append((abs_pos, 1, msg.channel, pitch))
                
            elif msg.type == 'note_off' or (msg.type == 'note_on' and msg.velocity == 0):
                pitch = msg.note
                if msg.channel != 9:
                    pitch = pitch + transpose_shift
                parsed_events.append((abs_pos, 0, msg.channel, pitch))
                
    # Fast sort by absolute position
    parsed_events.sort(key=lambda x: x[0])
            
    # Track instrument programs. Channel -> Program. Defaults to 0 (Piano)
    channel_programs = {i: 0 for i in range(16)}
    channel_programs[9] = 128 # Force drums to custom ID 128
    
    # Match ON and OFF to calculate durations
    open_notes = {} # (channel, note) -> start_pos
    closed_notes = [] # dict of verified notes
    
    for ev in parsed_events:
        abs_pos, ev_type, channel, val1 = ev
        
        if ev_type == 2:
            if channel != 9:
                channel_programs[channel] = val1
        elif ev_type == 1:
            pitch = val1
            key = (channel, pitch)
            if key not in open_notes:
                open_notes[key] = abs_pos
        elif ev_type == 0:
            pitch = val1
            key = (channel, pitch)
            if key in open_notes:
                start_pos = open_notes[key]
                duration = abs_pos - start_pos
                if duration <= 0:
                    duration = 1 # Minimum duration
                
                closed_notes.append({
                    'channel': channel,
                    'program': channel_programs[channel],
                    'pitch': pitch,
                    'abs_onset': start_pos,
                    'duration': duration
                })
                del open_notes[key]
                
    return closed_notes

def zig_zag_sort(closed_notes):
    """
    Phase 2: The "Zig-Zag" Sorting Algorithm 
    - Chunks notes by Bar
    - Groups by Instrument Program
    - Sorts Track by Highest Avg Pitch
    - Sorts Notes by Onset -> Descending Pitch
    """
    if not closed_notes:
        return []
        
    # 1. Chunk into Bars
    # Find the max bar
    max_onset = max(n['abs_onset'] for n in closed_notes)
    total_bars = (max_onset // POSITIONS_PER_BAR) + 1
    
    # Structure: bars[bar_idx][program] = list of notes
    bars = [defaultdict(list) for _ in range(total_bars)]
    
    for note in closed_notes:
        bar_idx = note['abs_onset'] // POSITIONS_PER_BAR
        prog = note['program']
        # Localize onset to the relative position within that bar (0-127)
        local_onset = note['abs_onset'] % POSITIONS_PER_BAR
        
        # We don't want notes crossing bars for this setup, so cap duration
        dur = note['duration']
        max_dur = POSITIONS_PER_BAR - local_onset
        if dur > max_dur:
            dur = max_dur
            
        bars[bar_idx][prog].append({
            'local_onset': local_onset,
            'pitch': note['pitch'],
            'duration': dur
        })
        
    # 2. Sort Tracks and Notes
    sorted_sequence = []
    
    for bar_idx, bar_groups in enumerate(bars):
        if not bar_groups:
            # Empty bar, just emit bar boundary if needed or skip
            continue
            
        # Voice-Level Sorting (Track Ordering)
        track_orders = []
        for prog, notes in bar_groups.items():
            if prog == 128:
                # Drums bypass pitch sorting, default to lowest priority (-1)
                avg_pitch = -1
            else:
                avg_pitch = sum(n['pitch'] for n in notes) / len(notes)
            track_orders.append((avg_pitch, prog, notes))
            
        # Sort tracks: Highest Avg Pitch -> Lowest
        track_orders.sort(key=lambda x: x[0], reverse=True)
        
        bar_sequence = []
        for avg_pitch, prog, notes in track_orders:
            # Emit Instrument token
            bar_sequence.append(f"i-{prog}")
            
            # Note-Level Sorting: Onset (Ascending) -> Pitch (Descending)
            notes.sort(key=lambda x: (x['local_onset'], -x['pitch']))
            
            for n in notes:
                pitch_val = n['pitch']
                if prog == 128:
                    pitch_val += 128 # Shift drum pitches to 128-255
                    
                # Ensure values stay in 0-127 bounds for positions/durations
                o_val = min(127, max(0, n['local_onset']))
                d_val = min(127, max(0, n['duration']))
                
                bar_sequence.append(f"o-{o_val}")
                bar_sequence.append(f"p-{pitch_val}")
                bar_sequence.append(f"d-{d_val}")
                
        # Emit Bar Boundary
        bar_sequence.append("b-1")
        sorted_sequence.extend(bar_sequence)
        
    return sorted_sequence

# --- Phase 4: Token to Tensor Mapping Definitions ---
# We build a static dictionary mapping strings to integers
VOCAB_SIZE = 129 + 128 + 256 + 128 + 1 # i, o, p, d, b
vocab_map = {}
idx = 0

# i-X (0 to 128)
for i in range(129):
    vocab_map[f"i-{i}"] = idx
    idx += 1
# o-X (0 to 127)
for o in range(128):
    vocab_map[f"o-{o}"] = idx
    idx += 1
# p-X (0 to 255)
for p in range(256):
    vocab_map[f"p-{p}"] = idx
    idx += 1
# d-X (0 to 127)
for d in range(128):
    vocab_map[f"d-{d}"] = idx
    idx += 1
# b-1
vocab_map["b-1"] = idx

def tokens_to_tensor_ids(token_sequence):
    """Converts a list of string tokens into a list of integer IDs based on the static vocab."""
    return [vocab_map[tok] for tok in token_sequence if tok in vocab_map]

def process_single_file(fpath):
    """Encapsulate the logic for one file to be run on one core."""
    notes = parse_and_normalize_midi(fpath)
    if notes:
        seq_tokens = zig_zag_sort(notes)
        return tokens_to_tensor_ids(seq_tokens)
    return None

def main():
    print(f"Gathering {TARGET_DATASET_SIZE} valid files from {LAMD_PATH}...")
    
    all_midi_files = glob.glob(os.path.join(LAMD_PATH, "**", "*.mid"), recursive=True) + \
                     glob.glob(os.path.join(LAMD_PATH, "**", "*.midi"), recursive=True)
                     
    if not all_midi_files:
        print("No MIDI files found! Check dataset path.")
        return
        
    random.seed(42) # Reproducibility
    random.shuffle(all_midi_files)
    
    print(f"Total files available: {len(all_midi_files)}. Beginning Multitrack Pipeline Processing...")
    
    valid_songs_tokens = []
    
    import multiprocessing
    cores = multiprocessing.cpu_count()
    print(f"Using {cores} CPU cores for multiprocessing.")
    
    with ProcessPoolExecutor(max_workers=cores) as executor:
        chunk_size = 5000
        for i in range(0, len(all_midi_files), chunk_size):
            chunk = all_midi_files[i:i + chunk_size]
            results = executor.map(process_single_file, chunk, chunksize=50)
            
            for tensor_ids in results:
                if tensor_ids:
                    valid_songs_tokens.append(tensor_ids)
                    if len(valid_songs_tokens) % 100 == 0:
                        print(f"Collected {len(valid_songs_tokens)}/{TARGET_DATASET_SIZE} valid sequences...")
                        
                    if len(valid_songs_tokens) >= TARGET_DATASET_SIZE:
                        break
                        
            if len(valid_songs_tokens) >= TARGET_DATASET_SIZE:
                break

    print(f"\nPipeline Complete! Extracted {len(valid_songs_tokens)} pure, Zig-Zag sorted, multi-track sequences.")
    
    # Efficient Data Saving
    flat_data = []
    song_indices = []
    current_idx = 0
    
    for seq in valid_songs_tokens:
        song_indices.append(current_idx)
        flat_data.extend(seq)
        current_idx += len(seq)
        
    flat_array = np.array(flat_data, dtype=np.uint16)
    indices_array = np.array(song_indices, dtype=np.uint64)
    
    output_path = os.path.join(os.path.dirname(__file__), "lamd_dataset.npz")
    np.savez_compressed(output_path, data=flat_array, indices=indices_array)
    print(f"Saved optimized NumPy archive to: {output_path}")

if __name__ == "__main__":
    main()
