from pythonosc import udp_client
import time

def main():
    # IP and port for controller.py's web server
    ip = "127.0.0.1"
    port = 11002
    
    client = udp_client.SimpleUDPClient(ip, port)
    
    print(f"Sending OSC messages to {ip}:{port}...")

    # Send tempo messages
    for i in range(5):
        tempo = 120.0 + i
        print(f"Sending /web/tempo: {tempo}")
        client.send_message("/web/tempo", [tempo])
        time.sleep(1)
    
    # Send ASCII messages
    for msg in ["1", "2", "3"]:
        print(f"Sending /web/msg: {msg}")
        client.send_message("/web/msg", [msg])
        time.sleep(1)

if __name__ == "__main__":
    main()

