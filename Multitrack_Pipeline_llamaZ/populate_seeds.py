import os
import shutil
import glob
import pickle

LAMD_PATH = r"E:\AI Music\Database\Los-Angeles-MIDI-Dataset-Ver-4-0-CC-BY-NC-SA"
SEEDS_DIR = r"E:\AI Music\AIMAP_generate-git\Seeds"
GENRE_MAP_PATH = r"E:\AI Music\Multitrack_Pipeline_Z_Groove\metadata\genre_map_contentsplit.pickle"

# Mapping from Z-Groove categories to folder names
mapping = {
    'Content_Groove': 'groove',
    'Content_Drive': 'drive',
    'Content_Pulse': 'pulse',
    'Content_Elegance': 'elegance'
}

def main():
    if not os.path.exists(GENRE_MAP_PATH):
        print(f"Error: {GENRE_MAP_PATH} not found.")
        return

    print("Loading Genre Map...")
    with open(GENRE_MAP_PATH, 'rb') as f:
        data = pickle.load(f)

    # Group IDs by genre
    genres = {}
    for mid_id, genre in data.items():
        genres.setdefault(genre, []).append(mid_id)

    for g_type, folder in mapping.items():
        target_dir = os.path.join(SEEDS_DIR, folder)
        os.makedirs(target_dir, exist_ok=True)
        
        ids = genres.get(g_type, [])
        random_sample = ids[:100] # Increase sample to ensure we find 25
        
        count = 0
        target_count = 25 # We want 25 good seeds per folder
        
        print(f"Populating {folder}...")
        for mid_id in random_sample:
            if count >= target_count:
                break
                
            # Search for file in LAMD
            pattern = os.path.join(LAMD_PATH, "**", f"{mid_id}.mid")
            matches = glob.glob(pattern, recursive=True)
            
            if matches:
                src = matches[0]
                dest = os.path.join(target_dir, f"{mid_id}.mid")
                try:
                    shutil.copy2(src, dest)
                    count += 1
                except Exception as e:
                    print(f"Error copying {mid_id}: {e}")
            
        print(f"Finished {folder}: {count} files copied.")

if __name__ == "__main__":
    main()
