# -*- coding: utf-8 -*-
"""enemies.html 이 만든 적 스프라이트를 Unity 아트 폴더에 쓴다.

Playwright 가 넘겨준 data:URL 묶음을 받아 PNG 로 풀고, **잘림이 하나라도 있으면
아무것도 쓰지 않는다**. 디스크의 기존 19장이 전부 캔버스 가득 찬 잘린 그림이었던
사고가 조용히 지나간 이유가 바로 이 검사가 없었기 때문이다.
"""
import base64, io, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEST = os.path.join(ROOT, "DustZeroUnity", "Assets", "DustZero", "Art", "Sprites")


def main(payload_path):
    payload = json.load(io.open(payload_path, encoding="utf-8"))
    report = {r["id"]: r for r in payload["report"]}
    clipped = [i for i, r in report.items() if r["clipped"]]
    if clipped:
        raise SystemExit("잘린 스프라이트가 있어 중단합니다: %s" % ", ".join(clipped))

    written = []
    for name, url in payload["sprites"].items():
        raw = base64.b64decode(url.split(",", 1)[1])
        path = os.path.join(DEST, name + ".png")
        io.open(path, "wb").write(raw)
        written.append(name)
    print("%d개 기록, span=%s" % (len(written), payload["span"]))
    for n in sorted(written):
        print("  ", n)


if __name__ == "__main__":
    main(sys.argv[1])
