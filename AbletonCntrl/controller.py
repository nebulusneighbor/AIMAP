from pythonosc import udp_client, dispatcher, osc_server
import threading
import time


# Global client to communicate with Ableton
client_sender = udp_client.SimpleUDPClient("127.0.0.1", 11000)

def ableton_handler(address, *args):
    print(f"Received message from Ableton -> Address: {address}, Data: {args}")

def web_handler(address, *args):
    """Fires when a message arrives on the custom script port (11002)"""
    print(f"[CUSTOM] Address: {address} | Data: {args}")

def tempo_handler(address, *args):
    """Handler for /web/tempo messages"""
    if args:
        tempo = args[0]
        print(f"Received tempo update: {tempo}. Sending to Ableton...")
        client_sender.send_message("/live/song/set/tempo", [tempo])

ableton_dispatcher= dispatcher.Dispatcher()
ableton_dispatcher.set_default_handler(ableton_handler)

web_dispatcher=dispatcher.Dispatcher()
web_dispatcher.set_default_handler(web_handler)
web_dispatcher.map("/web/tempo", tempo_handler)

def start_server(ip, port, disp):
    server = osc_server.ThreadingOSCUDPServer((ip, port), disp)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    print(f"Server started on {ip}:{port}")
    return server


def main():
    #read messages from ableton and web
    start_server("127.0.0.1", 11001, ableton_dispatcher) 
    start_server("0.0.0.0", 11002, web_dispatcher)
    

    #sending client to Ableton
    client_sender.send_message("/live/test",[])
    client_sender.send_message("/live/application/get/version",[])

    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nExiting script...")
    

if __name__ == "__main__":
    main()