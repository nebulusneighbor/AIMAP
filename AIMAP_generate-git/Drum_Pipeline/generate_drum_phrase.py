import os
import glob
import torch
import torch.nn.functional as F
from transformers import GPT2LMHeadModel, PreTrainedTokenizerFast
import argparse
import threading
import time
from pythonosc import udp_client, dispatcher, osc_server
from text_to_midi import text_to_midi

# Global client to talk back to Controller
controller_client = udp_client.SimpleUDPClient("127.0.0.1", 11002)
# Global client to talk to Multitrack Generator
multitrack_client = udp_client.SimpleUDPClient("127.0.0.1", 11004)

def get_next_filename(output_dir, genre, prefix="ai_drum_"):
    os.makedirs(output_dir, exist_ok=True)
    pattern = os.path.join(output_dir, f"{prefix}{genre}_*.mid")
    existing_files = glob.glob(pattern)
    max_num = 0
    for f in existing_files:
        try:
            basename = os.path.basename(f)
            parts = basename.replace(prefix, "").replace(".mid", "").split("_")
            if len(parts) >= 2:
                num = int(parts[-1])
                if num > max_num:
                    max_num = num
        except (ValueError, IndexError):
            continue
    return max_num + 1

def generate_drums(model_dir, output_dir, genre, bpm, beat_type, sig, length=150, temperature=1.0, top_k=50, top_p=0.9, auto_fire=True):
    print(f"\n[DRUMS] Generating for Genre: {genre} (Auto-fire: {auto_fire})...")
    
    tokenizer = PreTrainedTokenizerFast(
        tokenizer_file=os.path.join(model_dir, "tokenizer.json"),
        bos_token="<|endoftext|>",
        eos_token="<|endoftext|>",
        unk_token="<|unk|>",
        pad_token="<|pad|>"
    )
    model = GPT2LMHeadModel.from_pretrained(model_dir)

    device = "cuda" if torch.cuda.is_available() else "cpu"
    model.to(device)
    model.eval()

    prompt_tokens = [f"<GENRE_{genre}>", f"<BPM_{bpm}>", f"<TYPE_{beat_type}>", f"<SIG_{sig}>", "<New_Bar>", "<Pos_1>"]
    prompt_text = " ".join(prompt_tokens)

    next_id = get_next_filename(output_dir, genre)
    midi_filename = f"ai_drum_{genre}_{next_id}.mid"
    output_midi_path = os.path.join(output_dir, midi_filename)

    input_ids = tokenizer.encode(prompt_text, return_tensors="pt").to(device)

    with torch.no_grad():
        output = model.generate(
            input_ids,
            max_new_tokens=length,
            temperature=temperature,
            top_k=top_k,
            top_p=top_p,
            pad_token_id=tokenizer.pad_token_id,
            eos_token_id=tokenizer.eos_token_id,
            do_sample=True,
            repetition_penalty=1.0
        )

    generated_text = tokenizer.decode(output[0], skip_special_tokens=True)
    temp_txt_path = os.path.join(output_dir, f"temp_drum_{genre}_{next_id}.txt")
    with open(temp_txt_path, "w", encoding="utf-8") as f:
        f.write(generated_text)

    text_to_midi(temp_txt_path, output_midi_path)
    if os.path.exists(temp_txt_path):
        os.remove(temp_txt_path)

    print(f"[DRUMS] Done! Saved to: {output_midi_path}")
    controller_client.send_message("/web/midi/process_file", [output_midi_path, int(auto_fire)])
    return output_midi_path

def generate_full_ensemble(genre):
    # 1. Generate first drum phrase (auto-fire)
    drum_midi_1 = generate_drums(MODEL_DIR, OUTPUT_DIR, genre, "120", "beat", "4-4", auto_fire=True)
    
    # 2. Trigger multitrack pipeline using this drum midi as seed
    print(f"[OSC] Triggering multitrack pipeline for {genre}...")
    multitrack_client.send_message("/web/multitrack_request", [drum_midi_1, genre])
    
    # 3. Generate second drum phrase (silent)
    time.sleep(1) # Gap to allow multitrack trigger to start
    generate_drums(MODEL_DIR, OUTPUT_DIR, genre, "120", "beat", "4-4", auto_fire=False)

def osc_handler(address, *args):
    genre = str(args[0]).lower() if args else "rock"
    print(f"[OSC] Received trigger for {genre}. Starting ensemble sequence...")
    threading.Thread(target=generate_full_ensemble, args=(genre,)).start()

def main():
    global MODEL_DIR, OUTPUT_DIR
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    MODEL_DIR = os.path.join(curr_dir, "model_output", "final_model")
    OUTPUT_DIR = os.path.join(curr_dir, "midi_generate")

    disp = dispatcher.Dispatcher()
    disp.map("/web/generate_request", osc_handler)

    server = osc_server.ThreadingOSCUDPServer(("127.0.0.1", 11003), disp)
    print("Drum/Master Generator OSC Server started on 11003...")
    server.serve_forever()

if __name__ == "__main__":
    main()

