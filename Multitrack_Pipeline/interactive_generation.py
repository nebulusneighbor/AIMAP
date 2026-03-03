import os
import argparse
import torch
import mido

from data_processor import parse_and_normalize_midi, zig_zag_sort
from inference import dict_to_prompts, inference, REV_VOCAB, FWD_VOCAB
from text_to_midi import dict_to_midi

DEFAULT_INSTRUMENTS = {
    0: "Piano",
    24: "Guitar",
    33: "Bass",
    48: "Strings",
    73: "Flute",
    128: "Drums"
}

def extract_condition_from_midi(midi_path, drop_instruments=[]):
    """
    Parses a MIDI file using the REMI-z pipeline, and drops the specified instrument programs
    to create a conditioning context to force the model to regenerate them.
    """
    if not os.path.exists(midi_path):
        print(f"Error: {midi_path} not found.")
        return []
        
    print(f"Parsing seed MIDI: {midi_path}...")
    notes = parse_and_normalize_midi(midi_path)
    if not notes:
        print("Failed to parse MIDI (might not be 4/4 or has no pitch content).")
        return []
        
    # Remove dropped instruments from the parsed notes BEFORE zig_zag sort
    filtered_notes = [n for n in notes if n['program'] not in drop_instruments]
    
    # Generate the zig-zag tokens for the remaining ensemble
    condition_tokens = zig_zag_sort(filtered_notes)
    
    # Convert string tokens to Integer IDs based on our Vocab
    condition_ids = [FWD_VOCAB[t] for t in condition_tokens if t in FWD_VOCAB]
    
    # We will use the last N tokens of the context to limit prompt size (e.g. max 512 context)
    max_context = 512
    if len(condition_ids) > max_context:
        condition_ids = condition_ids[-max_context:]
        
    return condition_ids

def main(args):
    print("=== Multitrack REMI-z Interactive Generation ===")
    
    # 1. Determine Target Instruments to Generate
    target_progs = []
    if args.add:
        target_progs.extend(args.add)
        
    if not target_progs:
        # Default fallback target if user provides nothing
        target_progs = [0, 128] # Piano + Drums
        print("No target instruments specified. Defaulting to Piano (0) + Drums (128).")
        
    print(f"Targeting generation for Instrument Programs: {target_progs}")
    
    # 2. Extract Condition from Seed MIDI (if provided)
    condition_ids = []
    if args.seed_midi:
        print(f"Using Seed MIDI for context. Dropping target instruments from context (if they exist) so model can reimagine them.")
        condition_ids = extract_condition_from_midi(args.seed_midi, drop_instruments=target_progs)
    else:
        print("No Seed MIDI provided. Generating from a zero-shot empty conditional context (4 bars).")
        # Zero-shot default: Emulate 4 empty bars to align with training dimensionality
        condition_ids = [FWD_VOCAB["b-1"]] * 4
        
    # 3. Generate Sequence
    # History is empty for singular inference prompt unless we want to chain
    history_ids = [] 
    
    print("\nStarting Autoregressive Inference...")
    generated_string_tokens = inference(
        model_dir=args.model_dir, 
        instruments=target_progs, 
        history_seq=history_ids, 
        condition_seq=condition_ids,
        max_new_tokens=args.length,
        temperature=args.temp
    )
    
    print(f"\Generated {len(generated_string_tokens)} tokens.")
    
    # 4. Convert Strings to MIDI
    output_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "midi_generate")
    os.makedirs(output_dir, exist_ok=True)
    
    # increment filename
    count = 1
    while os.path.exists(os.path.join(output_dir, f"interactive_gen_{count}.mid")):
        count += 1
        
    output_file = os.path.join(output_dir, f"interactive_gen_{count}.mid")
    
    dict_to_midi(generated_string_tokens, output_file)
    print(f"\n✅ All Done! Generated track saved to: {output_file}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Interactively Generate/Modify Multitrack MIDI.")
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    
    parser.add_argument("--model_dir", default=os.path.join(curr_dir, "model_output", "final_model"), help="Path to trained model")
    parser.add_argument("--seed_midi", type=str, default=None, help="Optional: Path to an existing MIDI file to use as the base condition.")
    
    parser.add_argument("--add", nargs="+", type=int, help="List of MIDI Program IDs to generate/add (e.g. --add 0 128 33 for Piano, Drums, Bass)")
    parser.add_argument("--length", type=int, default=1024, help="Max tokens to generate.")
    parser.add_argument("--temp", type=float, default=1.0, help="Softmax temperature.")
    
    args = parser.parse_args()
    
    print("\nStandard MIDI Programs Reference:")
    print("0: Piano, 24: Acoustic Guitar, 33: Fingered Bass, 48: String Ensemble, 73: Flute, 128: Drum Kit")
    
    main(args)
