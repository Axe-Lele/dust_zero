# 세션 인계 문서 — 더스트 제로 Unity 이식

새 세션 시작 시 이 문서를 그대로 붙여넣으면 작업을 이어갈 수 있습니다.
작성 시점: 2026-10-15

---

## 0. 한 줄 요약

웹 게임 "오퍼레이션: 더스트 제로"(HTML Canvas)를 Unity 네이티브 2D로 이식 중.
**디펜스 모드의 로비↔전투 루프는 완성되어 실행·검증됨.**
대청소(생존) 모드·뽑기·육성 화면은 규칙 코드만 있고 화면이 없음.

---

## 1. 원래 받은 지시 (요약)

웹 게임을 Unity 네이티브 2D로 이식. WebView로 HTML 실행하는 방식 금지.
원본 위치: `C:\Users\Master\Documents\카카오톡 받은 파일\dustzero_play\dustzero_play` (보존할 것)

### 기술 구성 (지시받은 것)
- Unity 6.3 LTS + C#, Universal 2D 템플릿, URP 2D Renderer
- 전장: Grid/Tilemap · 캐릭터/적: SpriteRenderer · UI: uGUI + TextMeshPro
- 입력: Unity Input System · JSON: `com.unity.nuget.newtonsoft-json`
- 연출: DOTween 기본(무료) 버전, Particle System, Sprite 애니메이션
- 오브젝트 재사용: `UnityEngine.Pool.ObjectPool<T>`
- 기본 타깃: 세로형 모바일, 에디터에서는 마우스/키보드로도 조작
- **초기 구현에 ECS/DOTS, DI 프레임워크, 별도 길찾기 에셋 추가 금지**

### 이식 원칙
- 로직을 C#으로 옮기고 화면은 Unity 오브젝트/UI로 구현
- 원본의 규칙·수치·보상·해금 조건 재현. **임의로 밸런스 바꾸거나 기능 생략 금지**
- 원본의 오류를 발견하면 "이식 과정의 변경 사항"으로 따로 기록
- `index.html`의 거대한 구조를 하나의 C# 파일로 옮기지 말 것. 책임 분리:
  데이터 로딩/검증 · 디펜스 시뮬 · 대청소 시뮬 · 성장/재화/보상 · 저장 · 화면/연출
- 전투 규칙은 가능한 한 **일반 C# 클래스**로, MonoBehaviour는 입력·표시·생명주기 연결만
- 두 전투 모드는 유닛 데이터/성장 정보를 공유하되 모드별 규칙은 분리

### 데이터 구조 유지
- `tables/*.xlsx` → `tools/build.py` → `data/*.json` 흐름 유지
- 엑셀을 밸런스 원본으로 유지, 기존 JSON의 ID/키/테이블 관계 보존
- **실행 시 엑셀이나 파이썬이 필요하지 않게** 구성
- 수치·설명 문구를 C#에 중복 하드코딩하지 않기
- 누락 ID·잘못된 참조·필수 값 오류를 읽기 쉬운 메시지로 검증
- ScriptableObject는 프리팹/스프라이트/사운드 연결용으로만. 밸런스 수치의 별도 원본 금지

### 구현할 기능 (지시 원문 요약)
1. **디펜스**: 타일맵/배치 조건, 편성, 배치·철수·재배치 대기시간, 전력 소비/회복,
   웨이브·복수 입구, 지상/비행 이동 차이, 저지·대기열·과열·냉각,
   일반 공격/자동 스킬/대상 구분, 보스·지형, 일시정지·1배속/2배속,
   승패·별 평가·최초 클리어/별 보상, 전투 진행에 따른 튜토리얼.
   **지상 적은 원본의 BFS 거리장 + 우회·저지 규칙. 비행 적은 지정 경로.
   NavMesh나 물리 충돌로 임의 대체 금지.**
2. **대청소**: 리더 선택, 5분 생존, 터치 드래그 + WASD/방향키, 자동 공격,
   적 생성·보스, 경험치·레벨업 선택지, 동료 합류·강화, 협동기·편성 효과,
   일시정지·종료·결과, 일일 보상 횟수·최고 기록. 다수 오브젝트 풀링.
3. **로비/성장**: 홈·스테이지·보유 유닛·편성·뽑기·상세 팝업,
   레벨업·돌파·중복 처리, 재화/부품/크리스탈, 출석, 방치 보상 상한,
   콘텐츠 해금, 대표 캐릭터, 튜토리얼 다시 보기/초기화.
   테스트 전용 기능은 개발 빌드에서만.
4. **그래픽/UI**: 원본이 Canvas 코드로 그리므로 이미지 파일 유무 먼저 확인.
   없으면 **원본 그리기 함수로 투명 PNG/스프라이트 시트 추출을 우선 검토**.
   임시 도형으로 최종 결과를 남기지 말 것. 색감·캐릭터 특징·화면 구성 재현.
   Safe Area, 다양한 비율, 한글 폰트, 버튼 반응·피격 반짝임·데미지 숫자·
   스킬 효과·화면 흔들림. **연출 시간과 전투 시뮬 시간을 분리**해 일시정지/배속에서 오류 없게.
   외부 자산 출처·라이선스 기록.
5. **저장**: `Application.persistentDataPath` JSON. 진행도·재화·유닛·편성·
   보상 수령 기록·튜토리얼 상태 보존. 저장 형식 버전 관리.
   임시 파일 저장 후 교체 + 백업으로 손상 대비. 재실행 후 복원.
   방치 보상 시간 계산/상한 유지. PlayerPrefs는 음량 같은 간단한 설정만.
   **브라우저 저장 자동 이전을 가정하지 말고 내보내기/가져오기로 분리.**

### 작업 순서 (지시)
1. 프로젝트 설정, 데이터 로더, 기본 씬 → 2. 첫 스테이지 배치→전투→결과→로비 복귀
→ 3. 전체 디펜스 규칙/스테이지 → 4. 로비·성장·뽑기·보상·저장 → 5. 대청소 모드
→ 6. 그래픽·연출·모바일 검증

### 보고 요구사항 (지시)
Unity 프로젝트 위치/실행 방법, Unity·패키지 버전, 구현/미구현 기능,
**실제로 실행한 검증 결과**, 엑셀 수정 반영 방법, 빌드 방법,
원본과 달라진 동작, 아직 임시 자산인 부분.
**직접 실행하지 못한 검증은 성공했다고 쓰지 말 것. 실행/미확인을 명확히 구분할 것.**

---

## 2. 세션 중 사용자에게 받은 승인 (중요)

이 세 가지는 이미 승인받아 처리했습니다. 다시 물어볼 필요 없음.

| 항목 | 승인 내용 |
|---|---|
| DOTween 다운로드 | dotween.demigiant.com 공식 배포본 1.3.045 받아 `Assets/Plugins/DOTween/`에 설치 |
| 한글 폰트 다운로드 | Google Fonts의 Do Hyeon + IBM Plex Sans KR (OFL 1.1) 받아 설치 |
| Unity 에디터 종료 | 배치모드 테스트가 막혀서, 열려 있던 에디터 프로세스를 종료해도 된다고 승인받음 |

---

## 3. 세션 중 확정된 사실·결정

### 3.1 엔진 버전 (사용자 확인 필요했던 사항)
- **요청: Unity 6.3 LTS / 실제: 설치되어 있지 않음**
- 설치된 버전: 6000.0.77f1, 6000.4.1f1, **6000.4.5f1**
- 기존 프로젝트가 이미 6000.4.5f1이라 **그대로 사용 중**
- 6.3 이행은 "엔진 버전 변경"이라 별도 결정 사항으로 남겨둠 (아직 미결)

### 3.2 패키지 (6000.4.5f1 동봉 2D 템플릿 기준으로 고정)
URP 17.4.0 · Input System 1.19.0 · uGUI 2.0.0 (TMP 포함) ·
Newtonsoft JSON 3.2.2 · Test Framework 1.6.0 · 2D Sprite/Tilemap 1.0.0
→ `DustZeroUnity/Packages/manifest.json`

> 주의: 세션 도중 외부에서 `com.coplaydev.unity-mcp`가 manifest에 추가됨(로컬 file: 참조).
> 의도된 변경이므로 되돌리지 말 것.

### 3.3 CCGS 하네스
세션 도중 CCGS 하네스가 프로젝트에 설치됨. 그에 맞춰 레이아웃을 조정했습니다.
- 테스트 위치: `DustZeroUnity/Assets/Tests/EditMode`, `.../PlayMode` (CLAUDE.md 규칙)
- `tools/ccgs-unity.sh compile|test` 사용 가능
- `.claude/rules/dustzero-project.md`, `docs/DUSTZERO-PORT-BRIEF.md` 참고
- **ccgs-unity.sh 주석에 "기존 에디터를 죽이지 말 것"이 있음** → 에디터가 열려 있으면 사용자에게 물을 것

---

## 4. 현재 코드 구조

```
clean-girls/
├─ GameData/                         밸런스 원본 (원본에서 복사)
│  ├─ tables/*.xlsx                  ← 밸런스 수정은 여기
│  ├─ tools/build.py                 원본 변환기 (수정하지 않음)
│  ├─ tools/sync_to_unity.py         ★ 내가 추가: build.py 실행 + StreamingAssets 복사
│  ├─ data/*.json                    변환 결과 22개
│  ├─ spriteexport/one.html          ★ 내가 추가: 원본 그리기 코드로 스프라이트 추출
│  └─ original_index.html            원본 보존 사본
│
├─ DustZeroUnity/                    Unity 프로젝트 (Hub에서 이 폴더를 연다)
│  └─ Assets/
│     ├─ StreamingAssets/data/*.json 게임이 실제로 읽는 테이블
│     ├─ Plugins/DOTween/            무료판 1.3.045
│     ├─ TextMesh Pro/               TMP 필수 리소스 (unitypackage에서 추출)
│     ├─ Tests/EditMode/             DataTests, BattleRuleTests, ProgressionTests, SceneWiringTests
│     ├─ Tests/PlayMode/             PlayModeSmoke
│     └─ DustZero/
│        ├─ Art/Sprites/             ★ 원본에서 추출한 PNG 17장 (가전9 + 적8)
│        ├─ Art/Generated/           코드 생성 도형 (사각/원/외곽선)
│        ├─ Fonts/                   DoHyeon, IBMPlexSansKR + TMP SDF 3종 + OFL 라이선스
│        ├─ Prefabs/                 Entity, DeckCard, StageRow, FxParticle, FxLabel, TileHighlight
│        ├─ Scenes/Main.unity        ★ 전부 스크립트로 생성됨
│        ├─ Settings/                SpriteLibrary + 타일 8종
│        └─ Scripts/
│           ├─ Data/     (asmdef DustZero.Data)   TableRows, TableLoader, GameDatabase, TableValidator
│           ├─ Core/     (asmdef DustZero.Core)   엔진 비의존 규칙
│           │   ├─ Util/      Primitives(Vec2,IRandom), PresentationEvents(PresentationBus)
│           │   ├─ Defense/   BattleField, BattleEntities, BattleSim, UnitBehaviours, TutorialDirector
│           │   ├─ Survivor/  SurvivorEntities, SurvivorSim
│           │   └─ Meta/      SaveData, SaveService, PlayerProfile
│           ├─ Game/    (asmdef DustZero.Game)    MonoBehaviour 계층
│           │   GameServices, AppRoot, LobbyScreen, StageRow, BattleScreen, DeckCard,
│           │   BoardView, EntityView, FxDirector, FxParticle, FxLabel, SpriteLibrary, SafeAreaFitter
│           └─ Editor/  (asmdef DustZero.Editor)
│               ProjectBuilder(★씬/프리팹/폰트/타일 전부 생성), GeneratedArt,
│               SpriteImportSettings, TmpEssentials
│
├─ tools/unity.sh                    배치모드 실행 (GRAPHICS=1 이면 -nographics 제외)
├─ tools/ccgs-unity.sh               CCGS 진입점
├─ build/                            Unity 로그 + 테스트 XML
├─ production/qa/evidence/*.png      ★ 실행 화면 증거 6장
└─ docs/PORTING-NOTES.md             ★ 원본과 달라진 점 / 검증 결과 / 미구현 목록
```

### 설계 원칙 (유지할 것)
- `DustZero.Core`는 **UnityEngine 타입을 쓰지 않음** (Vec2/IRandom 자체 정의).
  시뮬레이션은 `Tick(dt)`만 호출하면 진행됨.
- 시뮬 → 화면 통신은 `PresentationBus` (FxEvent/BurstEvent/FloatTextEvent/Banner/Shake).
  시뮬은 GameObject를 모름. 뷰가 매 프레임 drain.
- **전투 시간과 연출 시간 분리**: 시뮬은 고정 1/60초 스텝,
  2배속은 한 프레임에 두 번 Tick. 연출은 `Time.unscaledDeltaTime`.
  → 일시정지/배속에서 규칙이 흔들리지 않음.
- 밸런스 수치는 전부 테이블에서 읽음. `skill_param`의 `{키}` 자리표시자도 런타임 치환.

---

## 5. 검증 현황 (실제로 실행한 것만)

```
EditMode  44 / 44 통과
PlayMode   3 /  3 통과
```

실행 명령:
```bash
LABEL=em bash tools/unity.sh -runTests -testPlatform EditMode  -testResults "<절대경로>/build/em.xml"
GRAPHICS=1 LABEL=pm bash tools/unity.sh -runTests -testPlatform PlayMode -testResults "<절대경로>/build/pm.xml"
```

### 검증된 것
- 22개 테이블 로딩·교차 참조 검증, 누락/오타가 테이블명과 함께 보고됨
- 전 스테이지 웨이브 전개·입구 인덱스·BFS 목표 도달 가능성
- 배치 규칙(근접=바닥/원거리=가구/입구·목표 금지구역/중복/한도 8대)
- 전력 소모·철수 환급·재배치 대기시간
- 저지·대기열·저지 정원 초과 방지·과열 시 전원 해제
- 비행 적이 저지되지 않고 경로 따라감
- **보상 중복 지급 방지**(별 보상, 최초 클리어 재도전, 출석 1일 1회)
- 중복 획득 → 돌파 → 전환, 레벨/돌파 배율
- 방치 보상 상한·소수점 이월·시급 상승 전 옛 시급 확정
- 저장 왕복 / 손상 시 `.bak` 복구 / 둘 다 깨지면 새로 시작 + `.corrupt` 보관 / 내보내기·가져오기
- 뽑기 비용 1회 차감·확정 등급 보장·재화 부족 시 거부
- 씬의 모든 직렬화 참조가 비어 있지 않음, 전 가전/전 적 스프라이트 매핑
- **실제 Play Mode**: 씬 로드 → 부팅 → 로비 → 1-1 → 시작 → 배치 → 전투 → 결과 → 저장 복원
- **튜토리얼**: 첫 전투에서 표시, `{unit}`→"청소기" 치환, pause 단계가 시뮬 정지,
  카드 선택 시 다음 단계로 진행

### 증거 화면 (`production/qa/evidence/`, 1080×1920)
`01-lobby` `02-battle-intro` `03-battle-deployed` `04-battle-result` `05-board-grid` `06-tutorial`

### 아직 확인 못 한 것 (미확인으로 유지)
- 실기(Android/iOS) 동작·성능 — 에디터 Play Mode만 실행
- 터치 입력 — 코드는 Touchscreen 우선 처리하나 실기 미시험
- 플레이어 빌드 — 빌드 메서드 미작성, APK/IPA 만들어보지 않음
- 대청소 모드 실제 플레이 — 화면이 없음. `SurvivorSim` 전용 테스트도 없음
- 뽑기/육성/편성 화면 — 규칙은 테스트됐으나 화면 없음
- 원본과 프레임 단위 수치 1:1 대조 — 안 함

---

## 6. 구현 상태표

| 기능 | 규칙 | 화면 |
|---|---|---|
| 디펜스 전투 전체 (저지/과열/스킬/보스/지형/배속) | ✅ | ✅ |
| 전투 튜토리얼 | ✅ | ✅ |
| 스테이지 선택·진행도·별 | ✅ | ✅ |
| 자동 청소(방치 보상) | ✅ | ✅ |
| 출석 보상 | ✅ | ✅ |
| 저장·복원·백업 | ✅ | ✅ |
| 대청소(생존) 모드 | ✅ `SurvivorSim` 전체 | ❌ |
| 뽑기 | ✅ 확정 등급 포함 | ❌ |
| 유닛 레벨업·돌파 | ✅ | ❌ |
| 편성 변경 | ✅ `ToggleDeck` | ❌ |
| 가전 도감·상세 팝업 | — | ❌ |
| 별 보상 수령 | ✅ | ❌ 버튼 없음 |
| 대표 캐릭터 변경 | — | ❌ |

---

## 7. 다음에 할 일 (우선순위)

1. **대청소 모드 화면** — `SurvivorSim`은 완성되어 있음. 필요한 것:
   리더 선택 팝업, 아레나 뷰(풀링), 드래그/WASD 입력, 레벨업 카드 3장, 결과 화면.
   `ProjectBuilder`에 UI 생성 코드 추가.
2. **육성/편성/뽑기 화면** — `PlayerProfile`에 규칙 전부 있음. uGUI만 붙이면 됨.
3. **별 보상 수령 버튼** — `ClaimStarReward` 호출만 연결.
4. **타일 그래픽** — 현재 단색. 원본 `paintFloor()` 참고해 질감 추가.
5. **플레이어 빌드 메서드** — `tools/ccgs-unity.sh build`가 의도적으로 막혀 있음.
6. **덱 카드 레이아웃** — 이름/비용 문구가 서로 가깝게 겹쳐 보임.

---

## 8. 반드시 알아야 할 함정 (이번 세션에서 실제로 겪은 것)

1. **MonoBehaviour는 클래스명과 파일명이 같아야 함.**
   `EntityView`/`FxParticle`/`FxLabel`을 다른 파일 안에 선언했더니
   프리팹의 스크립트 참조가 `{fileID: 0}`으로 조용히 깨짐. 각각 별도 파일로 분리해 해결.
2. **한 GameObject에 Graphic은 하나만.**
   TextMeshProUGUI가 있는 오브젝트에 `AddComponent<Image>()`는 **null을 반환**.
   배경과 텍스트를 부모/자식으로 분리할 것.
3. **`EditorSceneManager.NewScene` 이후 기존 프리팹 참조는 무효.**
   `ProjectBuilder`는 씬 생성 후 `ReloadPrefabs()`로 디스크에서 다시 읽음.
4. **TMP 필수 리소스는 배치모드에서 자동 생성 안 됨.**
   `AssetDatabase.ImportPackage`는 비동기라 `-quit`에 안 걸림.
   → unitypackage(tar.gz)를 파이썬으로 직접 풀어 `Assets/TextMesh Pro/`에 넣음.
5. **배치모드에서 `WaitForEndOfFrame`은 영원히 재개되지 않음** → 테스트가 멈춤. 쓰지 말 것.
6. **`ScreenCapture`는 헤드리스에서 동작 안 함.**
   → 카메라 + 오버레이 캔버스를 RenderTexture에 직접 렌더링 (`PlayModeSmoke.Shot`).
7. **Unity 에디터가 열려 있으면 배치모드가 전부 실패.**
   "another Unity instance is running". 프로젝트 규칙상 임의로 죽이지 말고 사용자에게 물을 것.
8. **bash에서 `"$VAR\\$NAME"`은 `\$`가 escape로 먹혀 리터럴 `$NAME`이 됨.**
   Windows 경로는 슬래시(`C:/...`)로 쓸 것. 스프라이트 17장이 한 파일에 덮어써진 원인.
9. **PlayMode 테스트 asmdef에 `includePlatforms: ["Editor"]`를 주면 테스트가 0개로 발견됨.**
   PlayMode는 비워둘 것. UnityEditor API가 필요하면 EditMode로 보낼 것.
10. **튜토리얼 1단계는 카드 선택까지 시뮬을 멈춤.**
    자동 테스트에서 전투를 돌리려면 `Data.tutOff = 1`로 꺼야 함
    (단, 그러면 튜토리얼 자체는 검증되지 않으니 전용 테스트를 따로 둘 것 — 이미 있음).

---

## 9. 자주 쓰는 명령

```bash
# 밸런스 수정 반영 (엑셀 → JSON → StreamingAssets)
cd GameData && python tools/sync_to_unity.py

# 컴파일만
bash tools/unity.sh -quit

# 씬/프리팹/폰트/타일 전부 재생성
bash tools/unity.sh -quit -executeMethod DustZero.Editor.ProjectBuilder.RebuildAll

# 테스트
LABEL=em bash tools/unity.sh -runTests -testPlatform EditMode -testResults "C:/Users/Master/Documents/Project/clean-girls/build/em.xml"
GRAPHICS=1 LABEL=pm bash tools/unity.sh -runTests -testPlatform PlayMode -testResults "C:/Users/Master/Documents/Project/clean-girls/build/pm.xml"

# 스프라이트 재추출 (헤드리스 Chrome, 경로는 반드시 슬래시)
# GameData/spriteexport/one.html?s=unit_vac&px=256
```

### 에디터에서 실행
Unity Hub → `DustZeroUnity/` (6000.4.5f1) → `Assets/DustZero/Scenes/Main.unity`
→ Game 뷰 1080×1920 세로 → Play. 마우스로 카드 선택 후 타일 클릭.

### 저장 파일 위치
`%USERPROFILE%\AppData\LocalLow\GEMSS\Operation DustZero\dustzero-save.json`

---

## 10. 원본 버그 (고치지 않고 기록만 — 지시대로)

1. **대청소 "오늘 최고 기록"이 실제로는 누적 최고 기록.**
   원본 `survDay()`의 `Object.assign` 순서 때문에 `bestT`/`bestKills`가 날짜가 바뀌어도
   초기화되지 않음. 이식본도 동일하게 유지 (`PlayerProfile.SurvivorToday()`).
2. **`skill_param` 누락 시 조용히 0.**
   원본은 `console.warn` 후 0 반환 → 스킬이 아무것도 안 하는 채로 플레이됨.
   **이 부분만 동작을 바꿔서** `TableValidator`가 실행 전에 오류로 보고하고 게임을 막음.
   밸런스 영향 없음.

자세한 내용은 `docs/PORTING-NOTES.md` 참고.
