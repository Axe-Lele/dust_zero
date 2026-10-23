"""data/*.json 을 data/tables.js 하나로 묶는다.

JSON을 직접 고친 뒤에는 이 스크립트만 실행하면 게임에 반영된다.
    python tools/pack_tables.py
(게임은 index.html 을 더블클릭해도 data/tables.js 를 읽어서 돌아간다.)
"""
import json, os, glob

def pack(data_dir):
    tables = {}
    for path in sorted(glob.glob(os.path.join(data_dir, "*.json"))):
        name = os.path.splitext(os.path.basename(path))[0]
        with open(path, encoding="utf-8") as f:
            tables[name] = json.load(f)
    with open(os.path.join(data_dir, "tables.js"), "w", encoding="utf-8") as f:
        f.write("// 자동 생성 파일입니다. 직접 고치지 말고 data/*.json 을 고친 뒤 tools/pack_tables.py 를 실행하세요.\n")
        f.write("window.DUSTZERO_TABLES = ")
        json.dump(tables, f, ensure_ascii=False, separators=(",", ":"))   # 10.18-b 빈칸 없이(용량 약 13% 줄임)
        f.write(";\n")
    return sorted(tables)

if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    names = pack(os.path.join(here, "..", "data"))
    print("packed:", ", ".join(names))
