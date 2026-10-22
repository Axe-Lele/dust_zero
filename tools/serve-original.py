"""Serve the preserved web-game copy locally for browser/Unity comparison."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8765)
    args = parser.parse_args()
    data = Path(__file__).resolve().parents[1] / 'GameData'
    if not (data / 'original_index.html').is_file():
        parser.error(f'Original web-game copy missing: {data}')
    handler = partial(SimpleHTTPRequestHandler, directory=str(data))
    with ThreadingHTTPServer(('127.0.0.1', args.port), handler) as server:
        print(f'Original game: http://127.0.0.1:{server.server_port}/original_index.html', flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == '__main__':
    main()
