"""엑셀 -> JSON -> Unity StreamingAssets 동기화.

밸런스를 고친 뒤 이것 하나만 실행하면 게임 데이터가 갱신됩니다.
    python tools/sync_to_unity.py

흐름
    GameData/tables/*.xlsx          <- 편집 원본 (ACTIVE-VERSION.txt 의 버전)
      -> tools/build.py             <- 변환기 (원본 제공분, 수정하지 않음)
      -> GameData/data/*.json       <- 변환 결과
      -> DustZeroUnity/Assets/StreamingAssets/data/   <- 게임이 읽는 위치

빌드된 게임은 StreamingAssets 안의 JSON 만 읽으므로 플레이에 엑셀도 파이썬도 필요 없습니다.

검사 대상 테이블은 개수를 적어두지 않고 xlsx_io.WORKBOOKS 의 시트 목록에서 끌어옵니다.
엑셀에 시트가 늘면 여기서도 자동으로 따라갑니다.

옵션
    --no-build   엑셀 변환을 건너뛰고 지금 있는 data/*.json 만 복사합니다.
    --check      복사하지 않고 차이만 보고합니다.
"""
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
DATA_DIR = os.path.join(ROOT, "data")
UNITY_DATA = os.path.abspath(
    os.path.join(ROOT, "..", "DustZeroUnity", "Assets", "StreamingAssets", "data")
)
VERSION_FILE = os.path.join(ROOT, "ACTIVE-VERSION.txt")

sys.path.insert(0, HERE)

# map_entrance / map_tile 은 map 테이블로 합쳐지므로 JSON 으로 따로 나오지 않습니다.
MERGED_AWAY = {"map_entrance", "map_tile"}


def expected_tables():
    """엑셀 시트 목록에서 게임이 읽을 테이블 이름을 끌어옵니다."""
    import xlsx_io
    names = []
    for _, sheets in xlsx_io.WORKBOOKS:
        for s in sheets:
            if s not in MERGED_AWAY and s not in names:
                names.append(s)
    return names


def active_version():
    if os.path.exists(VERSION_FILE):
        with open(VERSION_FILE, encoding="utf-8") as f:
            return f.read().strip()
    return "(미지정)"


def run_build():
    try:
        import build
    except ImportError as exc:
        print(f"  build.py 를 불러오지 못했습니다: {exc}")
        return False
    try:
        build.main()
        return True
    except Exception as exc:  # openpyxl 미설치, 엑셀이 열려 있어 잠김 등
        print(f"  엑셀 변환 실패: {exc}")
        print("  (pip install openpyxl 이 필요하거나, 엑셀 파일이 잠겨 있을 수 있습니다)")
        return False


def main():
    skip_build = "--no-build" in sys.argv
    check_only = "--check" in sys.argv

    print(f"[0/3] 활성 버전: {active_version()}  (GameData/ACTIVE-VERSION.txt)")

    if skip_build:
        print("[1/3] 엑셀 변환 건너뜀 (--no-build)")
    else:
        print("[1/3] 엑셀 -> data/*.json 변환")
        if not run_build():
            print("  변환에 실패해 기존 data/*.json 으로 계속합니다.")

    print("[2/3] 테이블 검사")
    tables = expected_tables()
    missing = [t for t in tables if not os.path.exists(os.path.join(DATA_DIR, f"{t}.json"))]
    if missing:
        print(f"  오류: 다음 테이블이 없습니다 -> {', '.join(missing)}")
        return 1

    bad = []
    for t in tables:
        try:
            with open(os.path.join(DATA_DIR, f"{t}.json"), encoding="utf-8") as f:
                json.load(f)
        except Exception as exc:
            bad.append(f"{t}.json ({exc})")
    if bad:
        print("  오류: JSON 형식이 잘못된 파일이 있습니다 ->")
        for b in bad:
            print(f"    - {b}")
        return 1
    print(f"  테이블 {len(tables)}개 확인")

    if check_only:
        print("[3/3] --check 이므로 복사하지 않습니다.")
        changed = []
        for t in tables:
            src = os.path.join(DATA_DIR, f"{t}.json")
            dst = os.path.join(UNITY_DATA, f"{t}.json")
            if not os.path.exists(dst):
                changed.append(f"{t} (Unity 에 없음)")
            else:
                with open(src, encoding="utf-8") as a, open(dst, encoding="utf-8") as b:
                    if json.load(a) != json.load(b):
                        changed.append(t)
        print("  차이 없음" if not changed else f"  반영 대기 {len(changed)}개: {', '.join(changed)}")
        return 0

    # 기획자에게 넘기는 묶음에는 Unity 프로젝트가 없다. 그래도 엑셀 -> JSON 은 끝까지
    # 돌아야 하고, 복사만 건너뛰면 된다. 조용히 실패하지 말고 왜 건너뛰는지 말한다.
    if not os.path.isdir(os.path.dirname(os.path.dirname(UNITY_DATA))):
        print("[3/3] Unity 프로젝트가 없어 복사를 건너뜁니다 (데이터 편집용 묶음).")
        print(f"  변환 결과는 {DATA_DIR} 에 있습니다.")
        return 0

    print(f"[3/3] StreamingAssets 로 복사 -> {UNITY_DATA}")
    os.makedirs(UNITY_DATA, exist_ok=True)

    # 더 이상 쓰지 않는 테이블이 남아 혼동되지 않도록 먼저 정리합니다.
    for name in os.listdir(UNITY_DATA):
        if name.endswith(".json") and os.path.splitext(name)[0] not in tables:
            os.remove(os.path.join(UNITY_DATA, name))
            print(f"  (제거) {name}")

    for t in tables:
        shutil.copy2(os.path.join(DATA_DIR, f"{t}.json"), os.path.join(UNITY_DATA, f"{t}.json"))

    print(f"완료: 테이블 {len(tables)}개를 Unity 로 복사했습니다.")
    print("Unity 에디터가 켜져 있다면 창을 클릭해 에셋을 새로고침한 뒤 Play 하세요.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
