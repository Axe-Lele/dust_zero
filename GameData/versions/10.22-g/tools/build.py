"""엑셀(tables/*.xlsx) -> data/*.json -> data/tables.js

엑셀을 고친 뒤 이것만 실행하면 게임에 반영됩니다.
    python tools/build.py
필요: pip install openpyxl
"""
import json, os, sys
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import xlsx_io, pack_tables

ROOT = os.path.join(here, "..")
TABLES_DIR, DATA_DIR = os.path.join(ROOT, "tables"), os.path.join(ROOT, "data")

def main():
    tables = xlsx_io.read_workbooks(TABLES_DIR)
    os.makedirs(DATA_DIR, exist_ok=True)
    for name, data in tables.items():
        with open(os.path.join(DATA_DIR, f"{name}.json"), "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
    names = pack_tables.pack(DATA_DIR)
    print(f"완료: 테이블 {len(names)}개 -> data/tables.js")
    print("브라우저에서 index.html 을 새로고침하세요.")

if __name__ == "__main__":
    main()
