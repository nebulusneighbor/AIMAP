import os
import argparse
import torch
import mido
import threading
import time
from pythonosc import udp_client, dispatcher, osc_server

from data_processor import parse_and_normalize_midi, zig_zag_sort
from inference import dict_to_prompts, inference, REV_VOCAB, FWD_VOCAB
from text_to_midi import dict_to_midi

# Global client to talk back to Controller
controller_client = udp_client.SimpleUDPClient("127.0.0.1", 11002)

def extract_condition_from_midi(midi_path, drop_instruments=[]):
    if not os.path.exists(midi_path):
        return []
    notes = parse_and_normalize_midi(midi_path)
    if not notes:
        return []
    filtered_notes = [n for n in notes if n["program"] not in drop_instruments]
    condition_tokens = zig_zag_sort(filtered_notes)
    condition_ids = [FWD_VOCAB[t] for t in condition_tokens if t in FWD_VOCAB]
    max_context = 512
    if len(condition_ids) > max_context:
        condition_ids = condition_ids[-max_context:]
    return condition_ids

def generate_multitrack(model_dir, seed_midi, genre, auto_fire=True):
    # Target all instruments: Piano, Guitar, Bass, Strings, Flute
    # (Drums are already in the seed, so we "re-imagine" them or add them too)
    target_progs = [0, 24, 33, 48, 73] 
    
    print(f"\n[MULTITRACK] Generating for Genre: {genre} (Auto-fire: {auto_fire})...")
    
    condition_ids = []
    if seed_midi and os.path.exists(seed_midi):
        condition_ids = extract_condition_from_midi(seed_midi, drop_instruments=target_progs)
    else:
        condition_ids = [FWD_VOCAB["b-1"]] * 4

    history_ids = []
    generated_string_tokens = inference(
        model_dir=model_dir,
        instruments=target_progs,
        history_seq=history_ids,
        condition_seq=condition_ids,
        max_new_tokens=1024,
        temperature=1.0
    )

    output_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "midi_generate")
    os.makedirs(output_dir, exist_ok=True)
    
    # Filename format: interactive_{genre}_{next_id}.mid
    count = 1
    while os.path.exists(os.path.join(output_dir, f"interactive_{genre}_{count}.mid")):
        count += 1
    output_file = os.path.join(output_dir, f"interactive_{genre}_{count}.mid")
    
    dict_to_midi(generated_string_tokens, output_file)
    print(f"[MULTITRACK] Done! Saved to: {output_file}")
    
    # Notify Controller (using track_id -1 to indicate multitrack routing needed)
    controller_client.send_message("/web/midi/process_file", [output_file, int(auto_fire)])

def osc_handler(address, *args):
    """/web/multitrack_request [seed_midi_path, genre]"""
    seed_midi = str(args[0])
    genre = str(args[1]).lower()
    
    print(f"[OSC] Multitrack request for {genre} using seed {os.path.basename(seed_midi)}")
    
    def run_generation():
        # Generate 2 versions
        generate_multitrack(MODEL_DIR, seed_midi, genre, auto_fire=True)
        time.sleep(1) # Gap between generations
        generate_multitrack(MODEL_DIR, seed_midi, genre, auto_fire=False)
        
    threading.Thread(target=run_generation).start()

def main():
    global MODEL_DIR
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    MODEL_DIR = os.path.join(curr_dir, "model_output", "final_model")
    
    disp = dispatcher.Dispatcher()
    disp.map("/web/multitrack_request", osc_handler)
    
    server = osc_server.ThreadingOSCUDPServer(("127.0.0.1", 11004), disp)
    print("Multitrack Generator OSC Server started on 11004...")
    server.serve_forever()

if __name__ == "__main__":
    main()

