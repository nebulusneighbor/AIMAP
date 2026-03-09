from pythonosc import udp_client, dispatcher, osc_server
import threading
import time

global_dispatcher = dispatcher.Dispatcher()

def default_handler(address, *args):
    print(f"Received message from Ableton -> Address: {address}, Data: {args}")

global_dispatcher.set_default_handler(default_handler)

def start_server(ip="127.0.0.1", port=11001, disp=None):
    if disp is None:
        disp = global_dispatcher
    server = osc_server.ThreadingOSCUDPServer((ip, port), disp)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    print(f"Server started on {ip}:{port}")
    return server

def main():
    #read messages from ableton
    start_server()

    #sending client to Ableton
    ip="127.0.0.1"
    port=11000 
    client_sender = udp_client.SimpleUDPClient(ip, port)
    client_sender.send_message("/live/test",[])
    client_sender.send_message("/live/application/get/version",[])

    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nExiting script...")
    

if __name__ == "__main__":
    main()