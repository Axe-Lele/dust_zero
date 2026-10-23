# 이식 기록 — 원본과 달라진 점

원본: `GameData/original_index.html` (테스트 빌드 10.14-d)
이식본: `DustZeroUnity/` (Unity 6000.4.5f1)

---

## 1. 엔진 버전

요청하신 버전은 **Unity 6.3 LTS**였습니다. 이 PC에 설치된 버전은 다음 셋입니다.

| 설치된 버전 | 비고 |
|---|---|
| 6000.0.77f1 | Unity 6.0 LTS |
| 6000.4.1f1 | |
| **6000.4.5f1** | 이 프로젝트가 사용 중 |

6.3(= 6000.3.x)은 설치되어 있지 않고, 기존 프로젝트가 이미 6000.4.5f1로 만들어져 있어
**6000.4.5f1을 그대로 사용**했습니다. 6.3으로 내리는 것은 엔진 버전 변경이라 별도 결정 사항으로 남겨둡니다.

패키지는 Unity가 이 에디터 버전용으로 함께 배포하는 2D 템플릿의 버전에 맞췄습니다
(URP 17.4.0, Input System 1.19.0, uGUI 2.0.0, Newtonsoft JSON 3.2.2, Test Framework 1.6.0).

---

## 2. 원본의 동작을 그대로 남긴 것 (버그로 보이지만 고치지 않음)

고치면 밸런스나 기록이 달라지므로, 원본 동작을 유지하고 여기에 기록만 남깁니다.

### 2.1 대청소 모드의 "오늘 최고 기록"이 실제로는 누적 최고 기록

원본 `survDay()`:

```js
if (!S.surv || S.surv.day !== k)
  S.surv = Object.assign({bestT:0, bestKills:0}, S.surv||{}, {day:k, runs:0});
```

`S.surv`를 나중에 덮어쓰므로 `bestT`/`bestKills`는 **날짜가 바뀌어도 초기화되지 않습니다**.
초기화되는 것은 `day`와 `runs`뿐입니다. 화면 문구는 "오늘 최고"이지만 값은 전체 기간 최고 기록입니다.

→ 이식본도 동일하게 유지했습니다 (`PlayerProfile.SurvivorToday()`).

### 2.2 `skill_param` 누락 시 조용히 0

원본 `SKP()`는 값이 없으면 `console.warn` 후 `0`을 반환합니다. 즉 오타가 나면
해당 가전의 스킬이 아무 일도 하지 않는 상태로 계속 플레이됩니다.

→ **이 부분만 동작을 바꿨습니다.** 이식본은 실행 전에 `TableValidator`가 필수
`skill_param` 키 누락을 오류로 모아 보고하고 게임을 시작하지 않습니다.
밸런스에는 영향이 없고, 데이터 실수를 숨기지 않기 위한 변경입니다.

---

## 3. 의도적으로 다르게 만든 것

| 항목 | 원본 | 이식본 | 이유 |
|---|---|---|---|
| 저장 위치 | 브라우저 `localStorage` | `Application.persistentDataPath/dustzero-save.json` | 네이티브 앱 |
| 저장 방식 | 즉시 덮어쓰기 | 임시 파일 → 교체, 직전 저장본을 `.bak`로 보존 | 쓰기 도중 종료되면 localStorage와 달리 파일은 잘린 채 남을 수 있음 |
| 손상 시 | 해당 없음 | `.bak`에서 복구, 둘 다 깨지면 새로 시작하고 손상본을 `.corrupt`로 보관 | 진행도 유실 방지 |
| 저장 버전 | `v: 2` | `version: 3` | 필드 추가(`tutOpen`, `surv`, `idleBank` 구조화) |
| 브라우저 저장 이전 | `gajeon-save-v1` → v2 자동 이전 | **자동 이전 없음**. 내보내기/가져오기(JSON)로 분리 | 브라우저와 앱은 저장소가 달라 자동 이전이 불가능 |
| 화면 그리기 | Canvas 2D 즉시 모드 | SpriteRenderer + Tilemap + uGUI | 네이티브 2D |
| 캐릭터 그림 | 매 프레임 코드로 그림 | 같은 코드로 **PNG 추출** 후 스프라이트 | 아래 4번 참고 |
| 배속 | `dt`에 배율 | 시뮬레이션을 고정 스텝으로 2번 호출 | 프레임레이트와 무관하게 규칙이 동일하게 동작 |
| 전투 시간 | 프레임 `dt` 그대로 | 고정 1/60초 스텝 | 같은 이유 |

### 저장 데이터 위치

```
%USERPROFILE%\AppData\LocalLow\GEMSS\Operation DustZero\dustzero-save.json
```

---

## 4. 그림 자산

원본에는 이미지 파일이 없고, `drawChibi()` / `drawEnemy()` 등이 Canvas 2D API로 직접 그립니다.

이식본은 **원본의 그리기 함수를 그대로 실행해 투명 PNG로 추출**했습니다.
추출기는 `GameData/spriteexport/one.html`이며, 헤드리스 Chrome으로 투명 배경 캡처합니다.

- 가전 9종: `unit_vac`, `unit_robot`, `unit_fridge`, `unit_ac`, `unit_micro`, `unit_washer`, `unit_pc`, `unit_fan`, `unit_gen`
- 적 8종: `enemy_dust`, `enemy_grease`, `enemy_mold`, `enemy_fly`, `enemy_breaker`, `enemy_boss_dust`, `enemy_boss_grease`, `enemy_boss_mold`
- 모두 256×256 RGBA, 배경 완전 투명
- 위치: `DustZeroUnity/Assets/DustZero/Art/Sprites/`

따라서 캐릭터와 적은 **임시 도형이 아니라 원본과 같은 그림**입니다.

보드 타일도 원본 `paintFloor()` / `buildBoard()`를 옮겨 코드로 굽습니다
(`Scripts/Editor/TilePainter.cs` → `Assets/DustZero/Art/Generated/`).

- 원목 결·이음매(칸마다 3변형), 주방 체커 2변형, 욕실 격자+십자, 벽 사선 해칭
- 가구 블록+그림자, 가스레인지 링, 물웅덩이, 입구 환풍구, 바닥별 목표 오브젝트
- 입구 번호·이름과 목표 이름은 월드 공간 TMP 라벨로 그립니다.

아직 없는 것: 가스레인지의 깜빡이는 주황 링(원본 render 루프의 애니메이션).

### 스프라이트 축척 — 추측하지 말 것

추출 상자는 1칸이 아닙니다. `one.html`이 가전은 `k = px/PAD`(PAD 1.35)로 그리고
게임은 `k = TS*0.92`로 그리므로 이미지 1장 = **1.35 × 0.92 칸**입니다.
적은 `TS = px/2.6`이라 이미지 1장 = **2.6칸**입니다. 또 가전은 `+k*0.19` 만큼
아래로 밀려 캡처되므로 그 거리만큼 되올려야 원본의 기준점이 칸 중심에 놓입니다.
이 세 값은 `BoardView`의 상수로 한곳에 있습니다. 스프라이트 피벗은 **중앙**이며,
"발밑 피벗" 같은 추정값을 넣으면 이 계산과 이중으로 어긋납니다.

### 폰트

| 폰트 | 용도 | 라이선스 |
|---|---|---|
| Do Hyeon | 제목 | SIL Open Font License 1.1 |
| IBM Plex Sans KR (Regular) | 본문 | SIL Open Font License 1.1 |
| Share Tech Mono | 숫자·LED | SIL Open Font License 1.1 (Carrois Type Design) |

출처: Google Fonts (`github.com/google/fonts`). 라이선스 원문은 `Assets/DustZero/Fonts/OFL-*.txt`에 포함했습니다.
원본 `index.html`의 `--f-led`가 Share Tech Mono입니다. IBM Plex SemiBold로 그리면 26px에서
`100`이 42.1 대신 46.8로 11% 넓어져, 내용에 맞춰 늘어나는 읽기칸이 다음 칸을 밀었습니다.
Share Tech Mono는 **라틴 전용**이라 TMP 자산에 본문 글꼴을 fallback으로 달아 두었습니다
(`ProjectBuilder.Fallback`). IBMPlexSansKR-SemiBold.ttf는 쓰지 않지만 보존합니다.
TMP 폰트 에셋은 **Dynamic 아틀라스**입니다. 한글 글리프 수가 많아 미리 구울 수 없기 때문입니다.

### DOTween

DOTween 무료 버전 1.3.045 (`Assets/Plugins/DOTween/`). 출처: dotween.demigiant.com 공식 배포본.
무료 버전은 상업적 사용이 가능하며 저작권 표기를 유지해야 합니다 (`DOTween/readme.txt` 동봉).

---

## 5. 데이터 파이프라인

엑셀 → JSON 흐름은 원본 그대로 유지했습니다. ID·키·테이블 관계도 그대로입니다.

```
GameData/tables/*.xlsx
        │  python tools/build.py        (원본 변환기, 수정하지 않음)
        ▼
GameData/data/*.json
        │  python tools/sync_to_unity.py
        ▼
DustZeroUnity/Assets/StreamingAssets/data/*.json   ← 게임이 읽는 곳
```

`sync_to_unity.py`가 `build.py`를 먼저 실행하므로, 밸런스를 고친 뒤 실행할 명령은 하나뿐입니다.

```bash
cd GameData && python tools/sync_to_unity.py
```

**검증:** 변환기를 다시 돌린 결과가 원본 `data/*.json` 22개와 의미상 완전히 동일함을 확인했습니다
(차이는 줄바꿈 문자 LF/CRLF뿐). 게임 실행에는 엑셀도 파이썬도 필요하지 않습니다.

수치와 문구는 C#에 중복 정의하지 않았습니다. 스킬 설명의 `{pullRadius}` 같은 자리표시자도
원본과 동일하게 `skill_param` 값으로 런타임에 채웁니다.

---

## 6. 아직 구현하지 않은 것

시뮬레이션·규칙 계층은 구현되어 테스트로 검증되지만, **화면이 아직 없는** 기능입니다.

| 기능 | 규칙 구현 | 화면 |
|---|---|---|
| 디펜스 전투 전체 | ✅ `BattleSim`, `UnitBehaviours` | ✅ |
| 전투 튜토리얼 | ✅ `TutorialDirector` | ✅ |
| 스테이지 선택·진행도·별 | ✅ | ✅ |
| 자동 청소(방치 보상) | ✅ | ✅ |
| 출석 보상 | ✅ | ✅ |
| 저장·복원 | ✅ | ✅ |
| 대청소(생존) 모드 | ✅ `SurvivorSim` 전체 | ❌ 화면 없음 |
| 뽑기 | ✅ `PlayerProfile.Pull` (확정 등급 포함) | ❌ 화면 없음 |
| 유닛 레벨업·돌파 | ✅ `LevelUp`, `AddUnit` | ❌ 화면 없음 |
| 편성 변경 | ✅ `ToggleDeck` | ❌ 화면 없음 |
| 가전 도감·상세 팝업 | — | ❌ |
| 별 보상 수령 | ✅ `ClaimStarReward` | ❌ 버튼 없음 |
| 대표 캐릭터 변경 | — | ❌ |

즉 현재 **플레이 가능한 범위는 "로비 → 스테이지 선택 → 배치 → 전투 → 결과 → 로비"**이며,
디펜스 15개 스테이지 전부를 이 흐름으로 플레이할 수 있습니다.

대청소 모드와 성장/뽑기 화면은 다음 작업 대상입니다. 규칙이 이미 테스트까지 끝나 있으므로
남은 일은 uGUI 화면과 `ProjectBuilder`에 추가하는 것입니다.

---

## 7. 실행·빌드

### 에디터에서 실행
1. Unity Hub에서 `DustZeroUnity/` 열기 (6000.4.5f1)
2. `Assets/DustZero/Scenes/Main.unity` 열기
3. Game 뷰 해상도를 세로(예: 1080×1920)로 맞추고 Play

에디터에서는 마우스로 조작합니다(카드 선택 → 타일 클릭).

### 에셋 재생성
씬·프리팹·폰트·타일은 전부 스크립트로 생성됩니다. 메뉴 `DustZero > Rebuild Project Assets`
또는:

```bash
bash tools/unity.sh -quit -executeMethod DustZero.Editor.ProjectBuilder.RebuildAll
```

TMP 필수 리소스가 없는 새 클론에서는 먼저 `DustZero > Import TMP Essentials`가 필요합니다.

### 테스트
```bash
bash tools/ccgs-unity.sh test
```

### 빌드
Android/iOS 세로 전용으로 설정되어 있습니다. 플레이어 빌드 메서드는 아직 작성하지 않았습니다
(`tools/ccgs-unity.sh build`는 의도적으로 미구현 상태로 막아둠).

---

## 8. 실제 실행한 검증 (2026-10-15)

### 자동 테스트

| 구분 | 결과 | 비고 |
|---|---|---|
| EditMode | **44 / 44 통과** | 실제 배포 테이블(StreamingAssets)로 실행 |
| PlayMode | **2 / 2 통과** | 실제 씬을 Play Mode로 구동 |

EditMode가 검증하는 것:
- 22개 테이블 로딩·검증, 누락 테이블/잘못된 mapId가 **이름과 함께** 보고되는지
- 모든 스테이지의 웨이브 전개·입구 인덱스·목표 도달 가능성(BFS)
- 배치 규칙(근접=바닥, 원거리=가구, 입구/목표 금지 구역, 중복 배치 불가, 배치 한도)
- 전력 소모/철수 환급/재배치 대기시간
- 저지·대기열·저지 정원 초과 방지·과열 시 전원 해제
- 비행 적이 저지되지 않고 경로를 따라가는지
- 별 보상·최초 클리어 보상이 **두 번 지급되지 않는지**
- 출석 보상 1일 1회, 중복 획득 → 돌파 → 전환
- 방치 보상 상한, 소수점 이월, 시급 상승 전 옛 시급 확정
- 저장 왕복, 손상 시 백업 복구, 둘 다 깨졌을 때 새로 시작, 내보내기/가져오기
- 뽑기 비용 1회 차감·확정 등급 보장·재화 부족 시 거부
- 씬의 모든 직렬화 참조가 비어 있지 않은지, 스프라이트가 전 가전/전 적에 매핑되는지

PlayMode가 검증하는 것:
- 실제 씬 로드 → 서비스 부팅 → 로비 표시
- 1-1 진입 → 시작 버튼 → 배치 → 전투 진행 → 결과 도달
- 전투 종료 후 저장 → 디스크에서 다시 읽어 진행도·재화 일치
- 타일 격자와 배치 규칙이 화면상에서도 일치 (바닥 20칸 / 가구 32칸)

### 실행 화면 증거

`production/qa/evidence/` 에 PNG 5장을 남겼습니다 (1080×1920).

| 파일 | 내용 |
|---|---|
| `01-lobby.png` | 로비: 재화, 대표 가전, 자동 청소, 스테이지 15개 목록 |
| `02-battle-intro.png` | 전투 시작 안내 오버레이 |
| `03-battle-deployed.png` | 전투 중: 타일맵, 배치된 청소기, 먼지 적, 덱 카드, HUD |
| `04-battle-result.png` | 결과 오버레이 |
| `05-board-grid.png` | 1-1 격자 |

배치모드에서는 `ScreenCapture`가 동작하지 않아, 카메라와 오버레이 캔버스를
RenderTexture로 직접 렌더링해 캡처합니다 (`PlayModeSmoke.Shot`).

### 아직 확인하지 못한 것

- **실기(안드로이드/iOS) 동작·성능**: 미확인. 에디터 Play Mode만 실행했습니다.
- **터치 입력**: 코드는 Touchscreen을 우선 처리하지만 실기에서 시험하지 못했습니다.
- **플레이어 빌드**: 빌드 메서드를 작성하지 않아 APK/IPA를 만들어 보지 않았습니다.
- **대청소 모드 실행**: 화면이 없어 `SurvivorSim`을 실제로 플레이해 보지 못했습니다.
  (규칙 코드는 컴파일되지만 전용 테스트도 아직 없습니다.)
- **육성/뽑기/편성 화면**: 규칙은 테스트로 검증되었으나 화면이 없어 플레이 확인 불가.
- **원본과의 1:1 수치 대조**: 자동 비교는 하지 않았습니다. 테이블이 동일하고
  규칙을 옮겼으므로 같아야 하지만, 프레임 단위 동작까지 대조한 것은 아닙니다.

### 알려진 UI 문제

- 덱 카드에서 가전 이름과 비용 문구가 서로 가깝게 겹쳐 보입니다.
- 타일은 아직 단색입니다 (3번 항목 참고).
