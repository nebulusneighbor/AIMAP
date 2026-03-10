import os
import argparse
import torch
import random
from transformers import GPT2LMHeadModel
from dataset_loader import VOCAB_SIZE, PAD_TOKEN, BOS_TOKEN, EOS_TOKEN, SEP_TOKEN, B_TOKEN, I_START, I_END

# Recreate vocab mapping reverse lookup to translate integers back to string tokens
REV_VOCAB = {}
idx = 0
for i in range(129): REV_VOCAB[idx] = f"i-{i}"; idx += 1
for o in range(128): REV_VOCAB[idx] = f"o-{o}"; idx += 1
for p in range(256): REV_VOCAB[idx] = f"p-{p}"; idx += 1
for d in range(128): REV_VOCAB[idx] = f"d-{d}"; idx += 1
REV_VOCAB[idx] = "b-1"

# Forward dictionary mapping for creating exact prompts manually
FWD_VOCAB = {v: k for k, v in REV_VOCAB.items()}

def to_string_sequence(tensor_ids):
    return [REV_VOCAB.get(t, f"<UNK_{t}>") for t in tensor_ids if t in REV_VOCAB]
    
def dict_to_prompts(track_list):
    """
    track_list: List of integer instrument programs e.g. [0, 128, 33]
    If you want voice control (highest to lowest), you pass the list in that exact order.
    Returns integer ID array for desired instruments.
    """
    return [FWD_VOCAB[f"i-{i}"] for i in track_list]

def inference(model_dir, instruments, history_seq, condition_seq, max_new_tokens=512, temperature=1.0):
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"Loading Model from {model_dir}...")
    
    model = GPT2LMHeadModel.from_pretrained(model_dir)
    model.to(device)
    model.eval()
    
    # 1. Desired Instruments sequence
    inst_prompt = dict_to_prompts(instruments)
    
    # Full Format: BOS + Insts + History + Condition + SEP
    raw_prompt = [BOS_TOKEN] + inst_prompt + history_seq + condition_seq + [SEP_TOKEN]
    
    input_ids = torch.tensor([raw_prompt], dtype=torch.long).to(device)
    
    print(f"\nCondition Prompt Length: {len(raw_prompt)} tokens.")
    
    # NEW: Prevent out of bounds error by ensuring prompt + generation does not exceed model capacity
    max_positions = model.config.n_positions
    prompt_len = input_ids.shape[1]
    
    if prompt_len + max_new_tokens > max_positions:
        adjusted_max = max_positions - prompt_len
        print(f"Warning: Prompt length ({prompt_len}) + requested new tokens ({max_new_tokens}) exceeds model max ({max_positions}).")
        print(f"Throttling max_new_tokens to {adjusted_max}.")
        max_new_tokens = adjusted_max
        if max_new_tokens <= 0:
            print("Prompt is already at or exceeds model maximum capacity. Cannot generate.")
            return []
            
    print("Autoregressively generating target sequence...")
    
    with torch.no_grad():
        output = model.generate(
            input_ids,
            max_new_tokens=max_new_tokens,
            temperature=temperature,
            top_k=50,
            top_p=0.9,
            pad_token_id=PAD_TOKEN,
            eos_token_id=EOS_TOKEN,
            do_sample=True,
            repetition_penalty=1.0 # REMI doesn't like repetition penalties
        )
        
    # The output contains the prompt + generated tokens. Isolate the generation.
    generated_ids = output[0][len(raw_prompt):].cpu().tolist()
    
    # Clip at EOS if model emits it gracefully
    if EOS_TOKEN in generated_ids:
        generated_ids = generated_ids[:generated_ids.index(EOS_TOKEN)]
        
    # Convert back to human-readable strings
    str_sequence = to_string_sequence(generated_ids)
    
    return str_sequence

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    parser.add_argument("--model_dir", default=os.path.join(curr_dir, "model_output", "final_model"))
    parser.add_argument("--temp", type=float, default=1.0)
    
    # Example task
    parser.add_argument("--task", default="add", choices=["add", "reduce", "modify"], help="Demonstration task")
    args = parser.parse_args()
    
    # Synthesize a fake context to demonstrate inference format
    print(f"--- REMI-z Inference Demo: {args.task.upper()} ---")
    
    if args.task == "add":
        # Simulate feeding Piano (0) and Bass (33) to get Drums (128)
        instruments = [128] # Target is drums
        history = [FWD_VOCAB['i-128'], FWD_VOCAB['o-0'], FWD_VOCAB['p-164'], FWD_VOCAB['d-4'], FWD_VOCAB['b-1']]
        condition = [FWD_VOCAB['i-0'], FWD_VOCAB['o-0'], FWD_VOCAB['p-60'], FWD_VOCAB['d-4'], FWD_VOCAB['b-1']]
        
    elif args.task == "reduce":
        # Simulate feeding Ensemble to get Piano (0)
        instruments = [0]
        history = []
        condition = [
            FWD_VOCAB['i-0'], FWD_VOCAB['o-0'], FWD_VOCAB['p-60'], FWD_VOCAB['d-4'], 
            FWD_VOCAB['i-33'], FWD_VOCAB['o-0'], FWD_VOCAB['p-36'], FWD_VOCAB['d-4'], FWD_VOCAB['b-1']
        ]
        
    elif args.task == "modify":
        # Force model to generate Piano (0) and Bass (33) based on a context that has Bass deleted
        instruments = [0, 33] # Order matters! Voice Control: Piano melody, Bass harmony.
        history = []
        condition = [FWD_VOCAB['i-0'], FWD_VOCAB['o-0'], FWD_VOCAB['p-60'], FWD_VOCAB['d-4'], FWD_VOCAB['b-1']]
        
    try:
        if os.path.exists(args.model_dir):
            output_tags = inference(args.model_dir, instruments, history, condition, temperature=args.temp)
            print("\nGenerated Target Sequence:")
            print(" ".join(output_tags))
        else:
            print(f"\n[!] Model directory {args.model_dir} not found. Please run train.py first to generate the weights.")
            print("\nInference input format tested successfully.")
    except Exception as e:
        print(f"Inference error: {e}")
