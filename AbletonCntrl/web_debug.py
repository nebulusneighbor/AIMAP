from pythonosc import udp_client
import time
import random
import sys
import os
import keyboard

# State file for Tonality memory
STATE_FILE = os.path.join(os.path.dirname(__file__), "mode_state.txt")

def activate_ableton():
    try:
        import win32gui, win32con
    except ImportError:
        print("[WEB DEBUG] Error: 'pywin32' library not found. Run 'pip install pywin32'.")
        return False

    def window_enum_handler(hwnd, resultList):
        title = win32gui.GetWindowText(hwnd)
        if win32gui.IsWindowVisible(hwnd) and title.strip():
            t_lower = title.lower()
            # Match by project name or Ableton keywords
            if "aimap" in t_lower or "live" in t_lower or "ableton" in t_lower:
                resultList.append((hwnd, title))
    
    handles = []
    win32gui.EnumWindows(window_enum_handler, handles)
    
    if not handles:
        print("[WEB DEBUG] Ableton NOT FOUND. Ensure Ableton is open and visible.")
        return False

    # Pick the longest title (usually the main project window)
    handles.sort(key=lambda x: len(x[1]), reverse=True)
    hwnd, title = handles[0]
    
    print(f"[WEB DEBUG] Targeting Window: '{title}'")
    try:
        # Force activation
        keyboard.press('alt')
        win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        win32gui.SetForegroundWindow(hwnd)
        keyboard.release('alt')
        time.sleep(0.5) 
        return True
    except Exception as e:
        print(f"[WEB DEBUG] Focus Error: {e}")
        return False

def apply_tonality(target_mode):
    last_mode = None
    if os.path.exists(STATE_FILE):
        with open(STATE_FILE, "r") as f:
            last_mode = f.read().strip().lower()
    
    print(f"[WEB DEBUG] Tonality Logic: Last={last_mode} | Target={target_mode}")
    
    if last_mode == target_mode:
        print(f"[WEB DEBUG] Already in {target_mode} mode. No keys sent.")
        return

    # Attempt to activate Ableton
    if not activate_ableton():
        print("[WEB DEBUG] Skipping keys: Could not focus Ableton window.")
        return

    # Toggle Logic: 1=Major, 2=Minor
    if last_mode == "major":
        print("[WEB DEBUG] Sending '1' to turn OFF Major...")
        keyboard.press_and_release('1')
        time.sleep(0.3)
    elif last_mode == "minor":
        print("[WEB DEBUG] Sending '2' to turn OFF Minor...")
        keyboard.press_and_release('2')
        time.sleep(0.3)
        
    if target_mode == "major":
        print("[WEB DEBUG] Sending '1' to turn ON Major...")
        keyboard.press_and_release('1')
    else:
        print("[WEB DEBUG] Sending '2' to turn ON Minor...")
        keyboard.press_and_release('2')
        
    with open(STATE_FILE, "w") as f:
        f.write(target_mode)

def main():
    # Ports
    master_port = 11003
    hybrid_port = 11005
    zeroshot_port = 11006
    z_groove_port = 11007
    
    mode = sys.argv[1] if len(sys.argv) > 1 else "2"
    
    if mode == "4" or mode == "5" or mode == "6":
        genres = ["Content_Groove", "Content_Drive", "Content_Pulse", "Content_Elegance"]
    else:
        genres = ["rock", "hiphop", "jazz", "funk", "latin"]
        
    PROG_NAMES = {128: "Drums", 0: "Piano", 24: "Guitar", 33: "Bass", 48: "Strings", 73: "Flute"}
    selected_genre = random.choice(genres)
    selected_tempo = random.randint(90, 140)
    target_tonality = random.choice(["major", "minor"])

    other_insts = [0, 24, 33, 48, 73] 
    count = random.randint(2, len(other_insts))
    selected_instruments = [128] + random.sample(other_insts, count)
    inst_names = [PROG_NAMES.get(i, str(i)) for i in selected_instruments]

    print(f"--- WEB DEBUG ENSEMBLE TRIGGER ---")
    print(f"Genre: {selected_genre} | Mode: {mode} | Tempo: {selected_tempo} BPM | Tonality: {target_tonality.upper()}")
    print(f"Target Instrumentation: {', '.join(inst_names)}")

    # 1. Send Tempo
    tempo_client = udp_client.SimpleUDPClient("127.0.0.1", 11000)
    tempo_client.send_message("/live/song/set/tempo", [float(selected_tempo)])

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

    # 3. Settle delay
    print("\n[WEB DEBUG] Generation sent. Waiting 3s for system to settle before tonality switch...")
    time.sleep(3.0)

    # 4. Apply Tonality
    apply_tonality(target_tonality)
    
    print("\nTest cycle complete. Check terminal outputs.")

if __name__ == "__main__":
    main()
