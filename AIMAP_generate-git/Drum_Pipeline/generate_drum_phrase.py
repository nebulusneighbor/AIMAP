import os
import glob
import torch
import torch.nn.functional as F
from transformers import GPT2LMHeadModel, PreTrainedTokenizerFast
import argparse
from text_to_midi import text_to_midi

def get_next_filename(output_dir, prefix="ai_drum_"):
    """Finds the next available filename in the directory."""
    os.makedirs(output_dir, exist_ok=True)
    existing_files = glob.glob(os.path.join(output_dir, f"{prefix}*.mid"))
    
    max_num = 0
    for f in existing_files:
        try:
            basename = os.path.basename(f)
            num_str = basename.replace(prefix, "").replace(".mid", "")
            num = int(num_str)
            if num > max_num:
                max_num = num
        except ValueError:
            continue
            
    return max_num + 1

def generate_drums(model_dir, output_dir, genre, bpm, beat_type, sig, length=150, temperature=1.0, top_k=50, top_p=0.9):
    print(f"Loading Drum Model from {model_dir}...")
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

    # Build the Metadata Prompt precisely matching our tokenization scheme
    prompt_tokens = [f"<GENRE_{genre}>", f"<BPM_{bpm}>", f"<TYPE_{beat_type}>", f"<SIG_{sig}>", "<New_Bar>", "<Pos_1>"]
    prompt_text = " ".join(prompt_tokens)
    
    # Determine the next file number
    next_id = get_next_filename(output_dir)
    midi_filename = f"ai_drum_{next_id}.mid"
    output_midi_path = os.path.join(output_dir, midi_filename)
    
    print(f"Generating drums based on prompt: {prompt_text}")
    print(f"Targeting: {midi_filename}")

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
    
    temp_txt_path = os.path.join(output_dir, f"temp_drum_{next_id}.txt")
    with open(temp_txt_path, "w", encoding="utf-8") as f:
        f.write(generated_text)
        
    print(f"Generation successful. Reconstructing MIDI...")
    text_to_midi(temp_txt_path, output_midi_path)
    
    if os.path.exists(temp_txt_path):
        os.remove(temp_txt_path)
        
    print(f"Done! New MIDI file: {output_midi_path}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate Drum MIDI using Metadata Tags.")
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    
    parser.add_argument("--model_dir", default=os.path.join(curr_dir, "model_output", "final_model"), help="Path to the saved model directory")
    parser.add_argument("--output_dir", default=os.path.join(curr_dir, "midi_generate"), help="Directory to save the generated files")
    parser.add_argument("--genre", default="rock", help="Genre tag (e.g., rock, funk, jazz)")
    parser.add_argument("--bpm", default="120", help="BPM tag (e.g., 88, 120)")
    parser.add_argument("--type", default="beat", help="Beat type (e.g., beat, fill)")
    parser.add_argument("--sig", default="4-4", help="Time signature (e.g., 4-4, 6-8)")
    parser.add_argument("--length", type=int, default=150, help="Number of drum events")
    
    args = parser.parse_args()
    
    generate_drums(args.model_dir, args.output_dir, args.genre, args.bpm, args.type, args.sig, args.length)
