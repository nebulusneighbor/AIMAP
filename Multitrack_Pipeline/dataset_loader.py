import torch
import numpy as np
from torch.utils.data import Dataset, DataLoader
import random

# REMI-z Vocabulary Ranges (from data_processor)
I_START = 0
I_END = 128
B_TOKEN = 641

# Custom tokens appended to the end of the vocabulary
PAD_TOKEN = 642
SEP_TOKEN = 643
BOS_TOKEN = 644
EOS_TOKEN = 645
VOCAB_SIZE = 646

def parse_song_to_bars(song_tensor):
    """
    Parses a flat integer sequence of a song into a list of bars.
    Each bar is a dictionary mapping instrument_token (0-128) -> list of note tokens
    """
    bars = []
    current_bar = {}
    current_inst = None
    current_notes = []
    
    for token in song_tensor:
        if token == B_TOKEN:
            if current_inst is not None:
                current_bar[current_inst] = current_notes
            bars.append(current_bar)
            current_bar = {}
            current_inst = None
            current_notes = []
        elif I_START <= token <= I_END:
            if current_inst is not None:
                current_bar[current_inst] = current_notes
            current_inst = token
            current_notes = []
        else:
            current_notes.append(token)
            
    # Add trailing if no b-1
    if current_inst is not None and not (not current_notes and current_inst not in current_bar):
        current_bar[current_inst] = current_notes
        bars.append(current_bar)
        
    return bars

def rebuild_segment(bars_list, track_filter=None):
    """
    Rebuilds a flat token sequence from a list of parsed bars.
    track_filter is an optional set/list of instrument tokens to include.
    """
    seq = []
    for bar in bars_list:
        # Sort tracks by highest average pitch (which was already done, but we use dict keys order which python 3.7+ preserves)
        # To be safe, we can just iterate over the keys as they were inserted.
        for inst, notes in bar.items():
            if track_filter is None or inst in track_filter:
                seq.append(inst)
                seq.extend(notes)
        seq.append(B_TOKEN)
    return seq

class MultitrackDataset(Dataset):
    def __init__(self, data_path, task_type="add", segment_bars=4, max_length=1024):
        """
        task_type:
          - "add": Condition = Pitched, Target = Drums
          - "reduce": Condition = Ensemble, Target = Piano (i-0)
          - "modify": Condition = Random dropout, Target = Complete ensemble
        """
        self.task_type = task_type
        self.segment_bars = segment_bars
        self.max_length = max_length
        self.samples = []
        
        print(f"Loading Dataset from {data_path}...")
        
        if data_path.endswith('.npz'):
            # Load optimized flattened format
            archive = np.load(data_path)
            flat_data = archive['data']
            indices = archive['indices']
            
            # Reconstruct list of sequences
            self.data_raw = []
            for i in range(len(indices)):
                start_idx = indices[i]
                end_idx = indices[i+1] if i + 1 < len(indices) else len(flat_data)
                self.data_raw.append(flat_data[start_idx:end_idx].tolist())
        else:
            # Fallback for older .npy object array
            self.data_raw = np.load(data_path, allow_pickle=True)
        
        print(f"Loading Dataset for task '{task_type}'...")
        self._build_samples()
        print(f"Constructed {len(self.samples)} valid sequence segments.")

    def _build_samples(self):
        for song_tensor in self.data_raw:
            bars = parse_song_to_bars(song_tensor)
            
            # Slide a window of size `segment_bars`
            for i in range(1, len(bars) - self.segment_bars + 1):
                # We need history, so we start at index i > 0
                history_bars = bars[i-1:i] # Use previous 1 bar for history to save context space, or up to segment_bars. Let's use 1 bar.
                current_bars = bars[i:i+self.segment_bars]
                
                # Check what tracks exist in current segment
                existing_tracks = set()
                for b in current_bars:
                    existing_tracks.update(b.keys())
                    
                if not existing_tracks:
                    continue
                    
                target_tracks = set()
                condition_tracks = set()
                
                # Randomly determine task if "all"
                task = self.task_type
                if task == "all":
                    task = random.choice(["add", "reduce", "modify"])
                    
                if task == "add":
                    # Target = Drums (128), Condition = Pitched (0-127)
                    if 128 not in existing_tracks:
                        continue # Skip if no drums to add
                    target_tracks = {128}
                    condition_tracks = {t for t in existing_tracks if t != 128}
                    
                elif task == "reduce":
                    # Target = Piano (0), Condition = Ensemble
                    if 0 not in existing_tracks:
                        continue
                    target_tracks = {0}
                    condition_tracks = existing_tracks # Full ensemble goes to condition
                    
                elif task == "modify":
                    # Target = All, Condition = Subset
                    target_tracks = list(existing_tracks)
                    if len(target_tracks) > 1:
                        # Drop 1 to N-1 tracks
                        drop_count = random.randint(1, len(target_tracks)-1)
                        condition_tracks = set(random.sample(target_tracks, len(target_tracks) - drop_count))
                    else:
                        condition_tracks = existing_tracks
                        
                # 1. Desired Instruments sequence
                desired_insts = list(target_tracks)
                
                # 2. Pure Musical Content (Condition Tracks Sequence)
                condition_seq = rebuild_segment(current_bars, condition_tracks)
                
                # 3. History
                history_seq = rebuild_segment(history_bars, target_tracks if task == "add" else existing_tracks)
                
                # Full Condition = Desired Insts + History + Musical Content
                condition_full = desired_insts + history_seq + condition_seq
                
                # Target
                target_seq = rebuild_segment(current_bars, target_tracks)
                
                # Final Array = BOS + Condition + SEP + Target + EOS
                final_seq = [BOS_TOKEN] + condition_full + [SEP_TOKEN] + target_seq + [EOS_TOKEN]
                
                if len(final_seq) <= self.max_length:
                    # Compute the specific idx where target starts
                    target_start_idx = len(condition_full) + 2 # +2 for BOS and SEP
                    self.samples.append({
                        "input_ids": final_seq,
                        "target_start": target_start_idx
                    })

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        sample = self.samples[idx]
        seq = sample["input_ids"]
        target_start = sample["target_start"]
        
        # Pad sequence
        pad_len = self.max_length - len(seq)
        input_ids = seq + [PAD_TOKEN] * pad_len
        
        # HuggingFace standard ignore index = -100
        labels = [-100] * target_start + seq[target_start:] + [-100] * pad_len
        
        return {
            "input_ids": torch.tensor(input_ids, dtype=torch.long),
            "attention_mask": torch.tensor([1 if t != PAD_TOKEN else 0 for t in input_ids], dtype=torch.long),
            "labels": torch.tensor(labels, dtype=torch.long)
        }

if __name__ == "__main__":
    # Quick Test
    import os
    npy_path = os.path.join(os.path.dirname(__file__), "lamd_dataset.npy")
    if os.path.exists(npy_path):
        ds = MultitrackDataset(npy_path, task_type="add", segment_bars=4, max_length=512)
        print("Sample 0 Input IDs Length:", len(ds[0]["input_ids"]))
        print("Sample 0 Elements masked for loss:", ds[0]["loss_mask"].sum().item())
