import mido
import argparse
import re

def text_to_midi(input_txt, output_midi):
    """Converts a sequence of REMI text tokens back into a MIDI file for drums."""
    with open(input_txt, 'r', encoding='utf-8') as f:
        content = f.read().strip()
        
    # Find everything that looks like < ... > and remove the spaces inside
    # This repairs tokens that the GPT2 decoder fractured (e.g., "< ON_60 >" -> "<ON_60>")
    cleaned_content = re.sub(r'<\s*([^>]+?)\s*>', lambda m: f"<{m.group(1).replace(' ', '')}>", content)
    
    # Extract all valid looking tokens
    tokens = re.findall(r'<[^>]+>', cleaned_content)
    
    mid = mido.MidiFile()
    track = mido.MidiTrack()
    mid.tracks.append(track)
    
    ticks_per_quarter = 480
    mid.ticks_per_beat = ticks_per_quarter
    ticks_per_pos = ticks_per_quarter / 4.0
    
    # Defaults
    tempo = 500000 # 120 BPM
    positions_per_bar = 16
    
    # Parse metadata tokens first to set tempo and time signature
    for token in tokens[:50]: # Check beginning of sequence
        if token.startswith("<BPM_"):
            try:
                bpm_str = token.replace("<BPM_", "").replace(">", "")
                # Some filenames have bpm like "88" others might be "88.5" or "unknown"
                if bpm_str != "unknown":
                    bpm = float(bpm_str)
                    tempo = mido.bpm2tempo(bpm)
            except ValueError:
                pass
        elif token.startswith("<SIG_"):
            try:
                sig_str = token.replace("<SIG_", "").replace(">", "")
                if sig_str != "unknown":
                    num, den = sig_str.split("-")
                    numerator = int(num)
                    denominator = int(den)
                    positions_per_bar = int(numerator * (4 / denominator) * 4)
                    track.append(mido.MetaMessage('time_signature', numerator=numerator, denominator=denominator, clocks_per_click=24, notated_32nd_notes_per_beat=8, time=0))
            except ValueError:
                pass

    track.append(mido.MetaMessage('set_tempo', tempo=int(tempo), time=0))
    
    current_bar = 0
    current_pos = 1
    current_velocity = 64
    
    # We will collect events with their absolute tick time
    # (absolute_tick, type, note, velocity)
    events = []
    
    for token in tokens:
        try:
            if token == "<New_Bar>":
                current_bar += 1
                current_pos = 1 # optionally reset pos, though Pos_1 will likely follow
            elif token.startswith("<Pos_"):
                current_pos = int(token.replace("<Pos_", "").replace(">", ""))
            elif token.startswith("<VEL_"):
                vel_bin = int(token.replace("<VEL_", "").replace(">", ""))
                current_velocity = int((vel_bin / 8.0) * 127)
            elif token.startswith("<ON_"):
                note = int(token.replace("<ON_", "").replace(">", ""))
                abs_tick = int(((current_bar - 1) * positions_per_bar + (current_pos - 1)) * ticks_per_pos)
                events.append((abs_tick, 'note_on', note, current_velocity))
                # Sythetic Note Off to satisfy drum samplers (e.g., 60 ticks later)
                events.append((abs_tick + 60, 'note_off', note, 0))
        except ValueError:
            print(f"Skipping malformed token: {token}")
            continue
            
    # Sort events strictly by absolute tick
    events.sort(key=lambda x: x[0])
    
    last_tick = 0
    for ev in events:
        abs_tick, ev_type, note, vel = ev
        delta_tick = abs_tick - last_tick
        
        # If the generated sequence goes backwards, cap it at 0 to maintain forward time
        if delta_tick < 0:
            delta_tick = 0
        else:
            last_tick = abs_tick
            
        # Write to Channel 9 for Drums
        track.append(mido.Message(ev_type, note=note, velocity=vel, time=delta_tick, channel=9))
        
    mid.save(output_midi)
    print(f"Successfully reconstructed REMI Drum MIDI and saved to {output_midi}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Convert REMI generated text sequences back to MIDI files.")
    parser.add_argument("input_text", help="Path to input text file (containing tokens)")
    parser.add_argument("output_midi", help="Path to save the reconstructed MIDI file")
    
    args = parser.parse_args()
    text_to_midi(args.input_text, args.output_midi)
