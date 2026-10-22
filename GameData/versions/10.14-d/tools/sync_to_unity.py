"""data/*.json -> Unity StreamingAssets 복사.

엑셀을 고친 뒤 이것 하나만 실행하면 게임 데이터가 갱신됩니다.
    python tools/sync_to_unity.py

내부적으로 build.py 를 먼저 돌려 tables/*.xlsx 를 data/*.json 으로 변환한 뒤,
그 결과를 DustZeroUnity/Assets/StreamingAssets/data/ 로 복사합니다.
빌드된 게임은 StreamingAssets 안의 JSON 만 읽으므로, 플레이에는 엑셀도 파이썬도 필요 없습니다.

옵션
    --no-build   build.py 를 건너뛰고 지금 있는 data/*.json 만 복사합니다.
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

# 게임이 읽는 테이블. 하나라도 없으면 실행 시 로딩에서 막힙니다.
TABLES = [
    "enums", "config", "string_ko", "item", "unit", "unit_levelup", "enemy", "map",
    "stage", "wave", "reward_group", "condition", "content_unlock", "star_reward",
    "skill_param", "gacha", "gacha_pool", "tutorial",
    "surv_param", "surv_combo", "surv_set", "surv_wave",
]


def run_build():
    sys.path.insert(0, HERE)
    try:
        import build  # noqa: F401
    except ImportError as exc:
        print(f"  build.py 를 불러오지 못했습니다: {exc}")
        return False
    try:
        build.main()
        return True
    except Exception as exc:  # openpyxl 미설치, 엑셀 열림 등
        print(f"  엑셀 변환 실패: {exc}")
        print("  (pip install openpyxl 이 필요하거나, 엑셀 파일이 잠겨 있을 수 있습니다)")
        return False


def main():
    skip_build = "--no-build" in sys.argv

    if skip_build:
        print("[1/3] 엑셀 변환 건너뜀 (--no-build)")
    else:
        print("[1/3] 엑셀 -> data/*.json 변환")
        if not run_build():
            print("  변환에 실패해 기존 data/*.json 으로 계속합니다.")

    print("[2/3] 테이블 검사")
    missing = [t for t in TABLES if not os.path.exists(os.path.join(DATA_DIR, f"{t}.json"))]
    if missing:
        print(f"  오류: 다음 테이블이 없습니다 -> {', '.join(missing)}")
        return 1

    bad = []
    for t in TABLES:
        path = os.path.join(DATA_DIR, f"{t}.json")
        try:
            with open(path, encoding="utf-8") as f:
                json.load(f)
        except Exception as exc:
            bad.append(f"{t}.json ({exc})")
    if bad:
        print("  오류: JSON 형식이 잘못된 파일이 있습니다 ->")
        for b in bad:
            print(f"    - {b}")
        return 1

    print(f"[3/3] StreamingAssets 로 복사 -> {UNITY_DATA}")
    os.makedirs(UNITY_DATA, exist_ok=True)

    # 더 이상 쓰지 않는 테이블이 남아 혼동되지 않도록 먼저 정리합니다.
    for name in os.listdir(UNITY_DATA):
        if name.endswith(".json") and os.path.splitext(name)[0] not in TABLES:
            os.remove(os.path.join(UNITY_DATA, name))
            print(f"  (제거) {name}")

    for t in TABLES:
        shutil.copy2(os.path.join(DATA_DIR, f"{t}.json"), os.path.join(UNITY_DATA, f"{t}.json"))

    print(f"완료: 테이블 {len(TABLES)}개를 Unity 로 복사했습니다.")
    print("Unity 에디터가 켜져 있다면 창을 클릭해 에셋을 새로고침한 뒤 Play 하세요.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
