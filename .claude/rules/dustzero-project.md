---
paths:
  - "DustZeroUnity/**"
  - "GameData/**"
  - "tools/**"
---
# Project-specific port rules

Read CLAUDE.md and docs/DUSTZERO-PORT-BRIEF.md. Unity code belongs beneath
DustZeroUnity/Assets, not the template's src/ or root Assets/.
Preserve the existing port. uGUI + TMP, JSON tables, ordinary C# simulations.
GameData/tables is authoritative; regenerate GameData/data with GameData/tools/build.py.
Keep original_index.html and original_README.txt as preserved references.
Do not apply unrelated hospital/backend/web framework conventions inherited from outside this project.

## 상류에서 파생된 파일은 상류 버전을 따라 올린다

이 저장소에는 **원본 웹 빌드에서 가져오거나 잘라낸 파일**이 섞여 있다
(`GameData/tools/*.py`, `GameData/tables/*.xlsx`, 스프라이트 내보내기 페이지 등).
이것들은 컴파일에도 테스트에도 걸리지 않으므로, 뒤처져도 **아무 신호가 없다.**
2026-10-23 에 `GameData/tools/gen_tables.py` 가 10.14-d 시절 사본(673줄 ·
`CHAPTERS = 3`)으로 남아 있는 것을 발견했다. 상류는 그때 10.22-g(1719줄 · 15챕터)
였다. 한 번만 실행했으면 워크북 16권이 3챕터 내용으로 덮여 썼을 것이다.
같은 날 `tools/convert_art.py` · `tools/export_sprites.py` ·
`GameData/tools/make_enemy_exporter.py` 가 **이미 지워진 `10.16-n` 사본**을
가리키고 있는 것도 같이 나왔다.

규칙:

1. **버전을 소스에 박지 않는다.** 기준은 `GameData/ACTIVE-VERSION.txt` 한 곳이다.
   도구의 `--version` 기본값은 그 파일에서 읽고, 경로에 `versions/10.xx-y` 를
   문자열로 적지 않는다. 사람이 다른 버전을 보고 싶으면 그때 인자로 넘긴다.
2. **새 원본 빌드를 받으면** 버전 사본을 만들고 `ACTIVE-VERSION.txt` 를 올린 뒤,
   상류에서 온 파일을 **상류 것으로 받아 우리 수정만 다시 얹는다.** 반대로
   (우리 파일에 상류 변경을 손으로 옮기기) 하지 않는다 — 베끼면 틀린다.
   우리 수정이 무엇인지는 `tools/check_upstream_drift.py` 의 `LOCAL_EDITS` 에 적는다.
3. **버전 사본을 지우기 전에** 그것을 가리키는 곳이 없는지 확인한다.
4. **`python tools/check_upstream_drift.py` 가 통과해야 한다.** 원본 빌드를
   반영하는 작업에서는 끝내기 전에 돌린다. 통과하지 못하면 올리거나,
   일부러 다르게 두는 것이면 `LOCAL_EDITS` 에 까닭을 적는다. 조용히 넘기지 않는다.

같은 함정의 다른 얼굴: `GameData/spriteexport/export.html` 은 한때 상류의
`drawEnemy` 를 **손으로 베낀 사본**이라 생김새 11종 중 3종만 알고 있었고, 적 19종이
전부 잘린 그림으로 나왔다. 베끼지 말고 `make_enemy_exporter.py` 처럼 **원본에서
잘라내 생성**한다.
