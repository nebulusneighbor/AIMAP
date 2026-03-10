from pythonosc import udp_client
import time
import random

def main():
    # Master Generator (Drums + Multitrack Trigger)
    generator_ip = "127.0.0.1"
    generator_port = 11003
    
    gen_client = udp_client.SimpleUDPClient(generator_ip, generator_port)

    # Randomly select a genre
    genres = ["rock", "hiphop", "jazz", "funk", "latin"]
    selected_genre = random.choice(genres)

    print(f"--- WEB DEBUG ENSEMBLE TRIGGER ---")
    print(f"Selected Genre: {selected_genre}")
    
    # Trigger full ensemble generation
    print(f"Sending /web/generate_request to Master Generator on {generator_ip}:{generator_port}...")
    gen_client.send_message("/web/generate_request", [selected_genre])
    
    print("\n[INFO] Master Generator will now:")
    print(f"1. Generate 2 Drum phrases for {selected_genre}.")
    print(f"2. Trigger Multitrack pipeline (Piano, Guitar, Bass, Strings, Flute).")
    print(f"3. Generate 2 Multitrack phrases for {selected_genre}.")
    print(f"4. All tracks will be fired synchronously in Ableton (Tracks 1-5, 7-11, 13-17, 19-23, 24, 25).")
    
    print("\nGeneration in progress. Check the terminal outputs for Controller and Generators.")
    
    # Large delay for multitrack generation to complete
    time.sleep(60) 
    print("Test cycle complete.")

if __name__ == "__main__":
    main()

