"""dz_studio.py 를 실행 파일(exe)로 묶는다.

    python tools/build_studio.py

결과: tools/DustZeroStudio.exe  (파이썬이 없는 PC 에서도 더블클릭으로 실행)

왜 이렇게 묶는가
  - 변환기(build.py / sync_to_unity.py / xlsx_io.py)는 **번들에 넣지 않는다.** 그것들은
    자기 __file__ 로 엑셀과 StreamingAssets 경로를 잡기 때문에, 번들 사본을 쓰면 임시
    폴더를 가리켜 아무 파일도 고치지 못한다. exe 는 실행된 자리에서 위로 올라가며
    GameData/tools 를 찾아 디스크의 진짜 변환기를 불러온다.
  - 그래서 exe 는 반드시 프로젝트 폴더 안(루트 또는 tools/)에 있어야 한다. 밖으로
    옮기면 경로를 못 찾았다고 창을 띄우고 멈춘다 — 조용히 빈 창을 띄우지는 않는다.
  - openpyxl 과 tkinter 는 번들에 들어간다. 그쪽은 경로를 쓰지 않는다.

필요: pip install pyinstaller
"""
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
ENTRY = os.path.join(HERE, "dz_studio.py")
NAME = "DustZeroStudio"
WORK = os.path.join(HERE, "_build")
DIST = os.path.join(WORK, "dist")
OUT = os.path.join(HERE, NAME + ".exe")


def main():
    if not os.path.exists(ENTRY):
        print(f"오류: {ENTRY} 가 없습니다.")
        return 1
    try:
        import PyInstaller  # noqa: F401
    except ImportError:
        print("오류: pyinstaller 가 없습니다.\n  pip install pyinstaller")
        return 1

    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm", "--clean",
        "--onefile",                 # 한 파일로. 배포가 쉬운 쪽을 택한다.
        "--windowed",                # 콘솔 창을 띄우지 않는다 (GUI 도구이므로)
        "--name", NAME,
        "--distpath", DIST,
        "--workpath", os.path.join(WORK, "build"),
        "--specpath", WORK,
        # 엑셀 입출력에 필요한 것만. 변환기는 일부러 넣지 않는다 (위 설명 참조).
        "--hidden-import", "openpyxl",
        "--collect-submodules", "openpyxl",
        ENTRY,
    ]
    print("빌드 중…  (1~2분 걸립니다)")
    p = subprocess.run(cmd, cwd=HERE)
    if p.returncode != 0:
        print("빌드 실패. 위 PyInstaller 메시지를 확인하세요.")
        return p.returncode

    built = os.path.join(DIST, NAME + ".exe")
    if not os.path.exists(built):
        print(f"오류: 빌드는 끝났는데 {built} 가 없습니다.")
        return 1

    # tools/ 안으로 옮긴다. 거기서 한 단계만 올라가면 프로젝트 루트다.
    try:
        shutil.copy2(built, OUT)
    except PermissionError:
        # 실행 중인 exe 는 덮어쓸 수 없다. 추적 역추적을 쏟아내는 대신 이유를 말한다.
        print()
        print(f"오류: {os.path.relpath(OUT, ROOT)} 를 덮어쓸 수 없습니다.")
        print("  도구가 실행 중입니다. 창을 닫고 다시 실행하세요.")
        print(f"  (새로 빌드된 파일은 {os.path.relpath(built, ROOT)} 에 있습니다)")
        return 1
    size = os.path.getsize(OUT) / (1024 * 1024)
    print(f"\n완료: {os.path.relpath(OUT, ROOT)}  ({size:.1f} MB)")
    print("더블클릭하면 바로 열립니다. 프로젝트 폴더 밖으로 옮기지 마세요.")
    print("도구를 고친 뒤에는 이 스크립트를 다시 실행해 exe 를 갱신하세요.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
