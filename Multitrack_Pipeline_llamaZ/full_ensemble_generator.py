import os
import glob
import random
import torch
import torch.nn.functional as F
from transformers import GPT2LMHeadModel
import threading
import time
import sys
import pickle
import re
from pythonosc import udp_client, dispatcher, osc_server

# Relative imports from the current folder
curr_dir = os.path.dirname(os.path.abspath(__file__))
from data_processor_contentsplit import parse_and_normalize_midi, zig_zag_sort
from text_to_midi import dict_to_midi

# --- Configuration ---
# Point to your model weights
MODEL_DIR = r"E:\AI Music\Multitrack_Pipeline_Z_Groove\model_output_contentsplit\epoch-5"
INFO_PATH = r"E:\AI Music\Multitrack_Pipeline_Z_Groove\data\content_split_dataset_info.pickle"
SEEDS_DIR = os.path.join(curr_dir, "..", "Seeds") 
OUTPUT_DIR = os.path.join(curr_dir, "midi_generate")
CONTROLLER_PORT = 11002
GENERATOR_PORT = 11007
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"

# Load vocab/genre info from info pickle
if os.path.exists(INFO_PATH):
    with open(INFO_PATH, 'rb') as f:
        info = pickle.load(f)
    GENRES = info['genres']
    VOCAB_SIZE = info['vocab_size']
else:
    GENRES = ['Content_Groove', 'Content_Drive', 'Content_Pulse', 'Content_Elegance']
    VOCAB_SIZE = 646 

# Recreate Vocab
REV_VOCAB = {}
idx = 0
for i in range(129): REV_VOCAB[idx] = f"i-{i}"; idx += 1
for o in range(128): REV_VOCAB[idx] = f"o-{o}"; idx += 1
for p in range(256): REV_VOCAB[idx] = f"p-{p}"; idx += 1
for d in range(128): REV_VOCAB[idx] = f"d-{d}"; idx += 1
REV_VOCAB[idx] = "b-1"; idx += 1
for g in GENRES: REV_VOCAB[idx] = f"g-{g}"; idx += 1

FWD_VOCAB = {v: k for k, v in REV_VOCAB.items()}
PAD_TOKEN = VOCAB_SIZE - 4
SEP_TOKEN = VOCAB_SIZE - 3
BOS_TOKEN = VOCAB_SIZE - 2
EOS_TOKEN = VOCAB_SIZE - 1

MODEL = None
controller_client = udp_client.SimpleUDPClient("127.0.0.1", CONTROLLER_PORT)

def load_model():
    global MODEL
    print(f"\n[Z-GROOVE] Loading Model from {MODEL_DIR}...")
    if not os.path.exists(MODEL_DIR):
        print(f"Error: Model directory {MODEL_DIR} not found!")
        return
    MODEL = GPT2LMHeadModel.from_pretrained(MODEL_DIR).to(DEVICE)
    MODEL.eval()
    print("[Z-GROOVE] Model Ready.\n")

def get_random_seed(genre):
    # Normalize genre name for folder searching
    folder_name = genre.lower().replace("content_", "")
    genre_path = os.path.join(SEEDS_DIR, folder_name)
    
    if not os.path.exists(genre_path):
        genre_path = os.path.join(SEEDS_DIR, genre.lower())
        if not os.path.exists(genre_path):
            return None
        
    seed_files = glob.glob(os.path.join(genre_path, "*.mid")) + glob.glob(os.path.join(genre_path, "*.midi"))
    if not seed_files: return None
    return random.choice(seed_files)

def get_random_drum_seed(genre):
    # ONLY search in <genre>_drums folder
    folder_name = genre.lower().replace("content_", "") + "_drums"
    genre_path = os.path.join(SEEDS_DIR, folder_name)
    
    if not os.path.exists(genre_path):
        return None
        
    seed_files = glob.glob(os.path.join(genre_path, "*.mid")) + glob.glob(os.path.join(genre_path, "*.midi"))
    if not seed_files: return None
    return random.choice(seed_files)

def generate_z_ensemble(genre, target_progs, auto_fire=True, session_seed_mid=None, is_drum_seed=False, drum_offset_beats=0.0):
    print(f"\n[Z-GROOVE] Generating Ensemble for {genre} (Auto-fire: {auto_fire})...")
    
    if MODEL is None:
        print("[Z-GROOVE] Error: Model not loaded!")
        return

    # Safety: ensure target_progs is a list
    if target_progs is None: target_progs = [128, 0]
    if not isinstance(target_progs, (list, tuple)): target_progs = [target_progs]
    
    # 1. Conditioning
    condition_ids = []
    if session_seed_mid:
        print(f"[Z-GROOVE] Conditioning on seed: {os.path.basename(session_seed_mid)} (Offset: {drum_offset_beats})")
        try:
            notes = parse_and_normalize_midi(session_seed_mid)
            if notes is not None:
                if is_drum_seed:
                    # Looping logic for generator conditioning
                    all_pos = [n['start_pos'] for n in notes]
                    max_pos = max(all_pos) if all_pos else 0
                    # Assume loop is multiple of 1 bar (128 pos)
                    total_len_pos = ((max_pos // 128) + 1) * 128
                    if total_len_pos < 128: total_len_pos = 512 # Fallback
                    
                    pos_offset = int(drum_offset_beats * 32)
                    
                    filtered_notes = []
                    for n in notes:
                        if n['program'] == 128:
                            # Relative position with modulo wrapping
                            rel_pos = (n['start_pos'] - pos_offset) % total_len_pos
                            # We provide exactly 4 bars (512 positions) of context
                            if rel_pos < 512:
                                n_shifted = n.copy()
                                n_shifted['start_pos'] = rel_pos
                                filtered_notes.append(n_shifted)
                    
                    # Also remove drums from target_progs so they aren't generated
                    if 128 in target_progs:
                        target_progs = [p for p in target_progs if p != 128]
                else:
                    filtered_notes = [n for n in notes if n['program'] not in target_progs]
                
                condition_tokens = zig_zag_sort(filtered_notes)
                condition_ids = [FWD_VOCAB[t] for t in condition_tokens if t in FWD_VOCAB]
            else:
                print("[Z-GROOVE] Warning: parse_and_normalize_midi returned None. Seed might be invalid.")
        except Exception as e:
            print(f"[Z-GROOVE] Error parsing seed MIDI: {e}")
    
    if not condition_ids:
        condition_ids = [FWD_VOCAB["b-1"]] * 4
        
    if len(condition_ids) > 512: condition_ids = condition_ids[-512:] 
    
    genre_tid = FWD_VOCAB.get(f"g-{genre}", FWD_VOCAB.get(f"g-{GENRES[0]}", 0))
    inst_ids = [FWD_VOCAB.get(f"i-{p}", 0) for p in target_progs]
    
    raw_prompt = [BOS_TOKEN, genre_tid] + inst_ids + condition_ids + [SEP_TOKEN]
    
    # 2. Sequential Generation
    final_gen_ids = []
    remaining_targets = list(target_progs)
    
    for step in range(8):
        current_input = torch.tensor([raw_prompt + final_gen_ids], dtype=torch.long).to(DEVICE)
        max_new = 1024 - len(raw_prompt) - len(final_gen_ids)
        if max_new <= 0: break
        
        with torch.no_grad():
            output = MODEL.generate(
                current_input, 
                max_new_tokens=max_new, 
                temperature=1.1, 
                top_p=0.9, 
                do_sample=True, 
                pad_token_id=PAD_TOKEN, 
                eos_token_id=EOS_TOKEN
            )
        
        new_ids = output[0][len(raw_prompt) + len(final_gen_ids):].cpu().tolist()
        if EOS_TOKEN in new_ids:
            new_ids = new_ids[:new_ids.index(EOS_TOKEN)]
        
        final_gen_ids.extend(new_ids)
        
        found_progs = set()
        for tid in final_gen_ids:
            tok = REV_VOCAB.get(tid, "")
            if tok.startswith("i-"):
                try: found_progs.add(int(tok.split("-")[1]))
                except: pass
        
        missing = [p for p in remaining_targets if p not in found_progs]
        if not missing:
            break
        
        # Inject the first missing instrument
        final_gen_ids.extend([FWD_VOCAB.get(f"i-{missing[0]}", 0), FWD_VOCAB.get("o-0", 0)])
        if len(raw_prompt + final_gen_ids) >= 1000: break

    # 3. Organic Silence Trim
    # Prepend headers from prompt so dict_to_midi knows the configuration
    header_tokens = [REV_VOCAB.get(genre_tid, "g-unknown")] + [REV_VOCAB.get(tid, "i-0") for tid in inst_ids]
    body_tokens = [REV_VOCAB.get(t, "<UNK>") for t in final_gen_ids]
    
    actual_drum_offset = drum_offset_beats
    
    # Strictly trim in 4-bar (16 beat) blocks as long as the first 4 bars are empty
    while True:
        first_note_bar = 999
        temp_bar = 0
        found_note = False
        
        for tok in body_tokens:
            if tok.startswith("o-"):
                first_note_bar = temp_bar
                found_note = True
                break
            if tok == "b-1":
                temp_bar += 1
        
        # If the first 4 bars are empty, trim them
        if found_note and first_note_bar >= 4:
            print(f"[Z-GROOVE] Organic Trim: 4 empty bars detected. Removing and shifting seed.")
            
            # Remove tokens until the 4th 'b-1'
            bs_seen = 0
            split_idx = 0
            while split_idx < len(body_tokens) and bs_seen < 4:
                if body_tokens[split_idx] == "b-1":
                    bs_seen += 1
                split_idx += 1
            
            body_tokens = body_tokens[split_idx:]
            actual_drum_offset += 16.0 # 4 bars = 16 beats
        else:
            # Music starts within the first 4 bars, or no notes found
            break

    # Final combined sequence
    generated_tokens = header_tokens + body_tokens

    # 4. Save and Send
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    timestamp = str(int(time.time() * 100))[-6:]
    filename = f"full_ensemble_z_groove_{genre}_{timestamp}.mid"
    output_file = os.path.join(OUTPUT_DIR, filename)
    
    dict_to_midi(generated_tokens, output_file)
    
    time.sleep(0.5)
    print(f"[Z-GROOVE] Sending to Controller: {filename}")
    
    # Staging drum seed with Effective Offset
    if is_drum_seed and session_seed_mid:
        print(f"[Z-GROOVE] Staging drum seed: {os.path.basename(session_seed_mid)} (Sync Offset: {actual_drum_offset})")
        # Added genre as 6th argument
        controller_client.send_message("/web/midi/process_file", [session_seed_mid, 0, "", 1, actual_drum_offset, genre]) 
        time.sleep(0.3) # Increased sleep for stability

    # Added genre as 6th argument
    controller_client.send_message("/web/midi/process_file", [output_file, int(auto_fire), session_seed_mid if is_drum_seed else "", 0, 0.0, genre])

def osc_handler(address, *args):
    if not args: return
    genre = str(args[0])
    target_progs = []
    for val in args[1:]:
        if isinstance(val, (list, tuple)):
            for v in val:
                try: target_progs.append(int(v))
                except: pass
        else:
            try: target_progs.append(int(val))
            except: pass
    
    if not target_progs: target_progs = [128, 0, 24, 33, 48, 73]
    
    if genre not in GENRES:
        found = False
        for g in GENRES:
            if genre.lower() in g.lower():
                genre = g; found = True; break
        if not found: genre = GENRES[0]

    session_seed = get_random_seed(genre)
    
    def run_batch():
        for i in range(4):
            try:
                generate_z_ensemble(genre, target_progs, auto_fire=(i==0), session_seed_mid=session_seed)
            except Exception as e:
                print(f"[Z-GROOVE] Error: {e}")
            time.sleep(2) # Increased sleep for stability
            
    threading.Thread(target=run_batch).start()

def find_first_musical_bar(midi_path):
    """Returns the beat offset of the first 4-bar boundary where drums are found."""
    try:
        notes = parse_and_normalize_midi(midi_path)
        if not notes: return 0.0
        
        earliest_pos = 999999
        for n in notes:
            if n['program'] == 128:
                if n['start_pos'] < earliest_pos:
                    earliest_pos = n['start_pos']
        
        if earliest_pos == 999999: return 0.0
        
        # Snap to the 4-bar boundary (512 pos = 16 beats)
        block = earliest_pos // 512
        offset_beats = float(block * 16)
        print(f"[Z-GROOVE] Seed content found. Starting at beat {offset_beats}")
        return offset_beats
    except:
        return 0.0

def drum_seeded_handler(address, *args):
    if not args: return
    genre = str(args[0])
    target_progs = []
    for val in args[1:]:
        if isinstance(val, (list, tuple)):
            for v in val:
                try: target_progs.append(int(v))
                except: pass
        else:
            try: target_progs.append(int(val))
            except: pass
            
    if not target_progs: target_progs = [128, 0, 24, 33, 48, 73]
    
    if genre not in GENRES:
        found = False
        for g in GENRES:
            if genre.lower() in g.lower():
                genre = g; found = True; break
        if not found: genre = GENRES[0]

    session_seed = get_random_drum_seed(genre)
    
    if session_seed:
        # PURE DRUM SEED CASE: Use the file, don't generate drums
        print(f"[Z-GROOVE] Using drum seed for {genre}: {os.path.basename(session_seed)}")
        
        # Ensure we start at a 4-bar boundary that actually has music
        base_offset = find_first_musical_bar(session_seed)
        
        def run_batch():
            current_seed = session_seed
            for i in range(4):
                # SPECIAL RULE: For pulse, pick a new seed for the 2nd pair (scenes 3 & 4)
                if i == 2 and genre.lower().replace("content_", "") == "pulse":
                    new_seed = get_random_drum_seed(genre)
                    if new_seed:
                        current_seed = new_seed
                        nonlocal base_offset
                        base_offset = find_first_musical_bar(current_seed)
                        print(f"[Z-GROOVE] Pulse Swap: New drum seed for scenes 3-4: {os.path.basename(current_seed)}")

                try:
                    if i >= 2 and current_seed != session_seed:
                        offset = base_offset + float((i - 2) * 16)
                    else:
                        offset = base_offset + float(i * 16)
                        
                    generate_z_ensemble(genre, target_progs, auto_fire=(i==0), session_seed_mid=current_seed, is_drum_seed=True, drum_offset_beats=offset)
                except Exception as e:
                    print(f"[Z-GROOVE] Error: {e}")
                time.sleep(2) # Increased sleep for stability
        threading.Thread(target=run_batch).start()
    else:
        # FALLBACK / ELEGANCE CASE: Pull from main seed folder but GENERATE drums
        print(f"[Z-GROOVE] No specific drum seed for {genre}. Using general seed and generating drums.")
        main_seed = get_random_seed(genre)
        def run_batch():
            for i in range(4):
                try:
                    # is_drum_seed=False means we condition on the seed BUT still generate program 128
                    generate_z_ensemble(genre, target_progs, auto_fire=(i==0), session_seed_mid=main_seed, is_drum_seed=False)
                except Exception as e:
                    print(f"[Z-GROOVE] Error: {e}")
                time.sleep(1)
        threading.Thread(target=run_batch).start()


def zero_shot_handler(address, *args):
    if not args: return
    genre = str(args[0])
    target_progs = []
    for val in args[1:]:
        if isinstance(val, (list, tuple)):
            for v in val:
                try: target_progs.append(int(v))
                except: pass
        else:
            try: target_progs.append(int(val))
            except: pass
    if not target_progs: target_progs = [128, 0, 24, 33, 48, 73]
    
    def run_batch():
        for i in range(4):
            try:
                generate_z_ensemble(genre, target_progs, auto_fire=(i==0), session_seed_mid=None)
            except Exception as e:
                print(f"[Z-GROOVE] Error: {e}")
            time.sleep(1)
            
    threading.Thread(target=run_batch).start()

def main():
    load_model()
    disp = dispatcher.Dispatcher()
    disp.map("/web/full_ensemble_request", osc_handler)
    disp.map("/web/zero_shot_request", zero_shot_handler)
    disp.map("/web/drum_seeded_request", drum_seeded_handler)
    server = osc_server.ThreadingOSCUDPServer(("0.0.0.0", GENERATOR_PORT), disp)
    print(f"Z-Groove Ensemble Generator started on 0.0.0.0:{GENERATOR_PORT}...")
    server.serve_forever()

if __name__ == "__main__":
    main()
