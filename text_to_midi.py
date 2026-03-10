import mido
import os

TICKS_PER_BEAT = 480
# 4 beats * 480 = 1920 ticks per bar.
# 128 positions per bar = 15 ticks per position
TICKS_PER_POS = 15
TICKS_PER_BAR = 1920

def dict_to_midi(token_sequence, output_path):
    """
    Translates a sequence of string tokens ('i-X', 'o-X', 'p-X', 'd-X', 'b-1') 
    back into a properly timed multitrack MIDI file.
    """
    current_bar = 0
    current_inst = 0 # Default to 0
    
    # Store events absolute time explicitly
    # list of dicts: {'time': absolute_ticks, 'type': 'on'/'off', 'pitch': X, 'channel': ch, 'vel': V}
    events = []
    
    # Track allocations (program -> assigned channel)
    ch_allocations = {128: 9} # Drums always on channel 9
    next_ch = 0
    
    # Parse tokens safely
    i = 0
    while i < len(token_sequence):
        tok = token_sequence[i]
        
        if tok == "b-1":
            current_bar += 1
            i += 1
            continue
            
        if tok.startswith("i-"):
            prog = int(tok.split("-")[1])
            current_inst = prog
            
            # Map channel if we haven't seen this program yet
            if current_inst not in ch_allocations:
                if next_ch == 9: # Skip drum channel
                    next_ch += 1
                if next_ch > 15:
                    next_ch = 15 # Max out at 15
                ch_allocations[current_inst] = next_ch
                next_ch += 1
                
            i += 1
            continue
            
        if tok.startswith("o-"):
            onset_pos = int(tok.split("-")[1])
            
            # Lookahead for pitch and duration safely
            pitch = 60
            dur = 1
            
            pos_1 = token_sequence[i+1] if i+1 < len(token_sequence) else ""
            pos_2 = token_sequence[i+2] if i+2 < len(token_sequence) else ""
            
            if pos_1.startswith("p-") and pos_2.startswith("d-"):
                pitch = int(pos_1.split("-")[1])
                dur = int(pos_2.split("-")[1])
                i += 3 # Jump past the note cluster
            elif pos_1.startswith("p-"):
                pitch = int(pos_1.split("-")[1])
                i += 2
            else:
                i += 1
                continue
                
            # Remap drum pitches
            if current_inst == 128:
                pitch -= 128
                
            # Keep bounds
            if pitch < 0 or pitch > 127:
                continue
            if dur <= 0:
                dur = 1
                
            abs_time = (current_bar * TICKS_PER_BAR) + (onset_pos * TICKS_PER_POS)
            duration_ticks = dur * TICKS_PER_POS
            
            channel = ch_allocations.get(current_inst, 0)
            
            events.append({
                'time': abs_time, 
                'type': 'note_on', 
                'pitch': pitch, 
                'channel': channel, 
                'vel': 80
            })
            events.append({
                'time': abs_time + duration_ticks, 
                'type': 'note_off', 
                'pitch': pitch, 
                'channel': channel, 
                'vel': 0
            })
        else:
            # stray p- or d- token
            i += 1
            
    # Rebuild MIDI
    mid: mido.MidiFile = mido.MidiFile(ticks_per_beat=TICKS_PER_BEAT)
    track = mido.MidiTrack()
    mid.tracks.append(track)
    
    # Write default tempo (120 BPM) and Time Sig (4/4) so DAWs parse timing correctly
    track.append(mido.MetaMessage('set_tempo', tempo=500000, time=0))
    track.append(mido.MetaMessage('time_signature', numerator=4, denominator=4, time=0))
    
    # Write program changes at start
    for prog, ch in ch_allocations.items():
        if prog != 128: # Drums don't get program change
            track.append(mido.Message('program_change', program=prog, channel=ch, time=0))
            
    events.sort(key=lambda x: (x['time'], 0 if x['type'] == 'note_off' else 1))
    
    current_ticks = 0
    for ev in events:
        delta = ev['time'] - current_ticks
        if delta < 0:
            delta = 0
            
        track.append(mido.Message(
            ev['type'], 
            channel=ev['channel'], 
            note=ev['pitch'], 
            velocity=ev['vel'], 
            time=delta
        ))
        current_ticks = ev['time']
        
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    mid.save(output_path)
    print(f"MIDI rendered safely to {output_path}")

if __name__ == "__main__":
    # Fake smoke test
    f = ["i-0", "o-0", "p-60", "d-4", "i-128", "o-0", "p-164", "d-2", "b-1"]
    out = os.path.join(os.path.dirname(__file__), "midi_generate", "smoke_test.mid")
    dict_to_midi(f, out)
