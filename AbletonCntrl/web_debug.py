from pythonosc import udp_client
import time
import random
import sys
import os

def main():
    # Ports
    master_port = 11003
    hybrid_port = 11005
    zeroshot_port = 11006
    z_groove_port = 11007
    controller_port = 11002 # Main Controller for Tonality/Tempo
    
    mode = sys.argv[1] if len(sys.argv) > 1 else "2"
    
    if mode == "4" or mode == "5" or mode == "6":
        genres = ["Content_Groove", "Content_Drive", "Content_Pulse", "Content_Elegance"]
    else:
        genres = ["rock", "hiphop", "jazz", "funk", "latin"]
        
    PROG_NAMES = {128: "Drums", 0: "Piano", 24: "Guitar", 33: "Bass", 48: "Strings", 73: "Flute"}
    selected_genre = random.choice(genres)
    selected_tempo = random.randint(90, 140)
    target_tonality = random.choice(["major", "minor"])
    selected_guitar_fx = random.choice(["1", "2", "3"])

    other_insts = [0, 24, 33, 48, 73] 
    count = random.randint(2, len(other_insts))
    selected_instruments = [128] + random.sample(other_insts, count)
    inst_names = [PROG_NAMES.get(i, str(i)) for i in selected_instruments]

    print(f"--- WEB DEBUG ENSEMBLE TRIGGER ---")
    print(f"Genre: {selected_genre} | Mode: {mode} | Tempo: {selected_tempo} BPM | Tonality: {target_tonality.upper()} | Guitar FX: {selected_guitar_fx}")
    print(f"Target Instrumentation: {', '.join(inst_names)}")

    # 1. Send Setup to Controller (Handles Tempo and MIDI Tonality Toggles)
    controller_client = udp_client.SimpleUDPClient("127.0.0.1", controller_port)
    controller_client.send_message("/performance/setup", [selected_genre, float(selected_tempo), target_tonality, 0, selected_guitar_fx])

    # 2. Send Generation Request
    if mode == "1":
        client = udp_client.SimpleUDPClient("127.0.0.1", master_port)
        client.send_message("/web/generate_request", [selected_genre])
    elif mode == "2":
        client = udp_client.SimpleUDPClient("127.0.0.1", hybrid_port)
        client.send_message("/web/full_ensemble_request", [selected_genre] + selected_instruments)
    elif mode == "3":
        client = udp_client.SimpleUDPClient("127.0.0.1", zeroshot_port)
        client.send_message("/web/full_ensemble_request", [selected_genre] + selected_instruments)
    elif mode == "4":
        client = udp_client.SimpleUDPClient("127.0.0.1", z_groove_port)
        client.send_message("/web/full_ensemble_request", [selected_genre] + selected_instruments)
    elif mode == "5":
        client = udp_client.SimpleUDPClient("127.0.0.1", z_groove_port)
        client.send_message("/web/zero_shot_request", [selected_genre] + selected_instruments)
    elif mode == "6":
        client = udp_client.SimpleUDPClient("127.0.0.1", z_groove_port)
        client.send_message("/web/drum_seeded_request", [selected_genre] + selected_instruments)

    print("\nGeneration request and Tonality setup dispatched to Controller.")
    print("Test cycle complete.")

if __name__ == "__main__":
    main()
