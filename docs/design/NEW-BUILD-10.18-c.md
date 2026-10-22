# 신규 기준 빌드 — 10.18-c (2026-10-18 분석)

원본 루트: `C:/Users/Master/Downloads/Dustzero_261018_C/dustzero_play`
`README.txt` 변경 기록과 `HANDOFF.md` 머리말에서 **10.18-c** 확인. index.html 안에는
10.16-f ~ 10.18-c 빌드 태그가 섞여 있고 가장 높은 값이 10.18-c다.

이 문서는 **읽기 전용 조사 결과**다. 이 분석으로 프로젝트 데이터/코드를 바꾼 것은 없다.

## 0. 지금 우리 상태

| | 버전 |
|---|---|
| `GameData/ACTIVE-VERSION.txt` | **10.18-c** |
| `GameData/tables/*.xlsx` | **10.18-c** 11권 (`10_League` + 우리 `11_Character`) |
| `GameData/data/*.json` | **10.18-c** 43개 — 공통 41개 전부 원본과 일치 |
| `DustZeroUnity/Assets/StreamingAssets/data/` | 43개, `GameData/data`와 바이트 동일 |
| Unity **코드** | **아직 10.16-n** — 10.17~10.18 기능 미구현 |
| Unity 구현(7탭·전투·대청소) | 10.16-n 기준 재현 |
| 신규 원본 | **10.18-c** |

> 주: `GameData/data`는 10.16-n 원본과 값은 같지만 **빈 칸의 키를 통째로 생략**한다
> (원본 `"summon": ""` ↔ 우리 쪽 키 없음). enemy/gear/shop_product/surv_param/tutorial
> 5개 파일에서 나타난다. 값 차이는 없다.

## 1. 10.16-n → 10.18-c 변경 (원본 변경 기록 기준)

| 빌드 | 내용 |
|---|---|
| 10.17-a | 입구 경고 3초 → 5초, 고정값을 `config.spawnWarnSec`로 분리 |
| 10.17-b | **지원 가전 성장** — 발전기·컴퓨터가 레벨/돌파로 강해지게(`supPow`). 전용장비 4007·4009 기본 능력 변경. 초파리 이동 속도 1.5 → 1.1 |
| 10.17-c | 전투 '나가기' → **일시정지 창**(계속하기·다시 하기·나가기) |
| 10.17-d | 필드 강화 별 배지 개수 수정(강화 1회 ★★) |
| 10.17-e | **리젠 예고** — 입구 말풍선에 5초 내 적 아이콘·마릿수·남은 초 |
| 10.18-a | 난이도 편차 정리(28스테이지 hpMul), 전신 일러스트 글자 자리 복원, **컴퓨터 청록 → 보라**, 세탁기 SD 머리 장식 교체 |
| 10.18-b | 성능 최적화(그림 캐시·거리장 캐시·DOM 갱신 최소화), BGM 80 kbps 재인코딩 |
| 10.18-c | **신규 모드 '가전 대청소 리그'** — 8라운드 오토배틀러 |

index.html 382,150B → 441,866B. 신규 함수 50개, 삭제된 함수 0개.

## 2. 데이터 차이 (파일 단위 실측)

39개 테이블 중 **25개는 내용 완전 동일**. 실제로 바뀐 것은 아래뿐이다.

### 2.1 신규 테이블 6개 — 리그
| 파일 | 행 | 컬럼 |
|---|---|---|
| `league_unit.json` | 12 | id, hp, atk, itv, range, spd, mana, skillKey, descKey |
| `league_param.json` | 32 | unitId, key, value (스킬 수치) |
| `league_enemy.json` | 14 | id, hp, atk, itv, range, spd, armor, fly, split, splitN |
| `league_round.json` | 8 | round, nameKey, squad, rMul, boss |
| `league_tier.json` | 3 | tier, nameKey, hpMul, atkMul, firstCrystal, winCoin, winPart, finishStone |
| `league_syn.json` | 12 | id, group, need, key, value, nameKey, descKey |

엑셀 원본은 `tables/10_League.xlsx`.

### 2.2 config — 키 84 → 118 (+34)
- **리그 28개:** `leagueRounds`(8) `leagueLife`(20) `leagueStartGold`(6) `leagueStartLv`(3)
  `leagueShopSize`(5) `leagueBench`(8) `leagueCost`[1,2,4] `leagueRerollCost`(2)
  `leagueXpBuy/XpCost`(4) `leagueXpPerRound`(2) `leagueLvXp`[0,0,0,0,4,8,14,20,28]
  `leagueOdds`(레벨별 1/2/3코스트 확률 9행) `leaguePoolCopies`[15,12,9]
  `leagueStarMul`[1,1.8,3.2] `leagueRoundTime`(30) `leagueRoundGold`(5) `leagueWinGold`(1)
  `leagueLoseBase`(2) `leagueInterestPer`(10) `leagueInterestMax`(2)
  `leagueManaPerAtk`(10) `leagueManaPerHit`(4) `leagueGrowthMax`(0.3) `leagueGrowthPow`(2.8)
  `leagueExclSkill`(0.25) `leagueDailyRewards`(3) `leagueTrialMin`(6)
- **지원 가전 성장 4개(10.17-b):** `genLvBonus` 0.015 · `genStarBonus` 0.08 ·
  `pcLvBonus` 0.008 · `pcStarBonus` 0.05 → `supPow = 1 + LvBonus×(Lv−1) + StarBonus×★`
- **기타 2개:** `spawnWarnSec`(5, 10.17-a) · `passXpLeaguePerWin`(5)

### 2.3 기존 테이블 실제 변경
| 테이블 | 변경 |
|---|---|
| `content_unlock` | `league` 행 추가 — cond **1016**(3-5 클리어), lockKey `unlock.league` |
| `enemy` | `fly.spd` **1.5 → 1.1** (공중 적 부담 완화) |
| `gear` | **4007**: sp 0→0.15, atk 0.15→0, heat 0.15→0, sk2Key `skillRange`→`buffAtk`, sk2Val 0.4→0.1 · **4009**: sp 0→0.15, atk 0.15→0, heat 0.15→0 |
| `stage` | **28개 스테이지 hpMul 재조정** (3-1 0.95→1.52, 14-2 5.67→9.07, 14-4 5.26→10.51 등 대부분 상향 / 9-2 3.32→2.82, 10-4·10-5 하향) |
| `string_ko` | 529 → 588 (**+59**). `battle.pause.*` 5개, `league.*` 53개, `unlock.league` 1개. 문구 변경 2건(`gear.4007.eff`, `unit.pc.motif` 민트 → 보라) |
| `unit_profile` | `pc` bgColor `#E3F7F1`→`#EFE9FB`, keyColor `#2FBF9A`→`#8A63D2` |

나머지 차이(enemy/gear/shop_product/surv_param/tutorial의 `null` ↔ `""`)는 직렬화 차이이고 값은 같다.

## 3. 아트 · 오디오
- 일러스트 **14개 변경**: `pc_*` 6개 전부(보라 리컬러), `*_full` 8개(ac, boiler, gen, iron,
  micro, robot, stove, washer — 글자 지운 자리 복원). 나머지 59개는 동일.
- 아이콘 **2개 변경**: `shard_pc.webp`, `shard_washer.webp`. 나머지 41개 동일.
- 오디오 3곡은 **같은 곡을 80 kbps로 재인코딩**한 것(3.9MB → 2.4MB). Unity에는 이미
  고비트레이트판이 들어 있으므로 **교체할 이유 없음**.

## 4. '가전 대청소 리그' 구조 (10.18-c)

TFT식 오토배틀러. 로비 콘텐츠 탭에서 입장, **3-5 클리어 해금**, **하루 3판 보상**.

- **판 진행:** 8라운드. 생명 20, 시작 골드 6, 시작 레벨 3.
- **상점:** 5칸, 리롤 2골드. 코스트 1/2/4, 레벨별 등장 확률 `leagueOdds`.
  풀 사본 수 코스트별 15/12/9장. 3장 합체 → ★ (`leagueStarMul` 1 / 1.8 / 3.2).
- **경제:** 라운드당 5골드 + 승리 1골드 + 이자(10골드당 1, 최대 2) + 패배 보상 2.
  XP 구매 4골드에 4XP, 라운드당 2XP.
- **배치:** 판 21칸(아래 3줄) + 대기석 8칸.
- **전투:** 30초 제한. 마나는 공격 시 +10, 피격 시 +4 → 가전별 고유 **리그 스킬 12종**.
- **시너지 6종:** 생활가전(2/4 → 받는 피해 −15/30%), 주방가전(2/3 → 스킬 피해 +30/60%),
  디지털(2 → 시작 마나 +30), 계절가전(2/3 → 적 공속 감소), 근접(2/4 → 최대 체력),
  원거리(2/4/6 → 공속).
- **계정 반영:** 보유 레벨·돌파가 `leagueGrowthMax` 0.3 / `leagueGrowthPow` 2.8 곡선으로
  최대 +30%, 전용장비는 `leagueExclSkill` 0.25 반영.
- **단계 3개:** 브론즈/실버/골드. 적 hpMul 2.45/3.2/4.8, atkMul 1.72/2.1/2.9.
  첫 클리어 크리스탈 300/500/800, 승리 코인 250/400/600.
- **코드:** 상태 `LG`/`LG.B`, 화면 `#league`. 신규 함수 `lg*` 38개 + `startLeague`,
  `renderLeagueCard`. 난수는 씨앗 고정(`LG.rng`) — 봇 `tools/sim/league.mjs` 재현용.

## 5. Unity 쪽 적용 상태 — 전부 미적용

`DustZeroUnity/Assets` 전체 grep 결과, 아래 중 **구현된 것이 하나도 없다.**

| 기능 | Unity | 비고 |
|---|---|---|
| `spawnWarnSec` / 리젠 예고 | 없음 | `SpawnWarn`·`spawnWarnSec` 검색 0건 |
| 일시정지 창 | 없음 | `PauseMenu`·`pmenu` 0건 |
| 지원 가전 성장 `supPow` | 없음 | `genLvBonus` 0건 |
| 강화 별 배지 개수 수정 | 미확인 | 10.16-n 기준 구현 |
| 리그 모드 전체 | 없음 | `league` 0건 |
| 성능 최적화(10.18-b) | 해당 없음 | 웹 Canvas 전용, Unity는 별개 |
| 아트 14종 + 아이콘 2종 | 구버전 | `Art/Illust` 72장은 10.16-n 판 |
| stage hpMul 28행 | 구버전 | StreamingAssets가 10.16-n |

## 6. 반영할 때 걸리는 것

### 6.1 엑셀 10번 슬롯 충돌 — **해결됨 (2026-10-18)**
우리 `10_Character.xlsx`(`unit_art`, `unit_behaviour` — Unity 전용으로 우리가 만든 테이블)와
신규 `10_League.xlsx`가 같은 번호였다. 우리 쪽을 **`11_Character.xlsx`로 옮기고**
10번을 리그에 내줬다. 처리 내역은 9장.

### 6.2 남은 것
1. **`gen_tables.py` 계보가 갈라져 있다.** 우리 사본은 60KB(10.15-b 흔적까지),
   신규 원본은 115KB. 우리는 **엑셀이 원본**이고 `tools/dz_studio.py`로 편집해 왔다.
   신규 `gen_tables.py`를 그대로 돌리면 **엑셀을 통째로 덮어써** 우리 편집분이 날아간다.
   `build.py`만 쓰거나, 신규 엑셀만 가져와 머지해야 한다.
2. **`xlsx_io.py`는 이번에 머지했지만 `pack_tables.py`는 아직 다르다**(우리 1,088B /
   신규 1,160B). 다음에 비교할 것.
3. **세이브 마이그레이션.** 리그는 세이브에 새 영역(리그 진행·하루 보상 횟수)을 만든다.
   `SaveData`/`SaveService` 버전을 올려야 한다.
4. **stage hpMul 28행 변경은 난이도 전체에 영향**을 준다. 반영하면 기존 전투 회귀 증거
   (`production/qa/evidence`)는 다시 찍어야 한다.
5. **빈 칸 직렬화 차이.** 우리 `xlsx_io`는 빈 셀의 **키를 통째로 생략**하고 원본은 `""`를
   쓴다(`league_enemy.split` 등). 값은 같고 기존 5개 테이블에서도 똑같이 동작해 온
   방식이라 그대로 뒀다. Unity에서 `null`과 `""`를 같게 다루는지는 league 구현 때 확인한다.

## 7. 남은 순서 (제안)

1. ~~`GameData/versions/10.18-c/` 원본 사본 + 해시~~ → **완료**
2. ~~엑셀 번호 충돌 해소 + `10_League.xlsx` 도입~~ → **완료**
3. ~~데이터 본반영(config·string_ko·stage·enemy·gear·content_unlock·unit_profile) + Unity 동기화~~ → **완료**
4. 작은 기능부터: `spawnWarnSec`+리젠 예고 → 일시정지 창 → `supPow` → 별 배지 → 아트 14종 재추출
5. 리그 모드는 독립 에픽(`Core/League/` 시뮬 + `Game/LeagueScreen`), `TableRows.Names`에 6테이블 등록부터

## 8. (완료) 데이터 동기화

43테이블 전부 StreamingAssets로 복사했다(10장). league 6개는 파일만 있고
`TableRows.Names` 적재 목록에 없어 **Unity가 읽지 않는다** — 리그 에픽에서 등록한다.

## 9. 2026-10-18 작업 기록 — 사본 + 번호 충돌

### 원본 사본
`GameData/versions/10.18-c/` — 209파일 전부 복사, `INVENTORY.json`에 sha256·바이트 기록.
복사본을 다시 해싱해 **209/209 일치, 불일치 0** 확인. 다운로드 원본은 읽기만 했다.

### 번호 충돌 해소
| 바뀐 것 | 내용 |
|---|---|
| `GameData/tables/10_Character.xlsx` | → `11_Character.xlsx` (파일 내용 그대로) |
| `GameData/tables/10_League.xlsx` | 10.18-c 사본에서 새로 들임 |
| `GameData/tools/xlsx_io.py` | `WORKBOOKS` 10권 → **11권**, `DESC` league 6종, `CONFIG_NOTE` league 설명 추가. **상류 파일에서 그대로 뽑아 넣었고 손으로 베끼지 않았다.** 백업 `xlsx_io.py.10.16-n.bak` |
| `GameData/data/` | 37 → **43개** (league 6개 생성) |
| `GameData/ACTIVE-VERSION.txt` | `10.16-n` → `10.16-n +10.18-c(league)` |
| 참조 갱신 | `seed_character_table.py`(3) · `TableValidator.cs`(2) · `CharacterDataTests.cs`(1) · `tools/dz_studio.py`(2) · `docs/design/ADD-CONTENT.md`(2) |

세션 로그·`active.md`의 과거 기록에 남은 `10_Character.xlsx`는 그때의 사실이라 그대로 뒀다.

### 검증 (전부 실제로 돌림)
- **반영 전 멱등성 확인:** 엑셀 → data 재생성이 기존 37테이블을 **하나도 바꾸지 않음**을
  먼저 확인한 뒤에 league를 넣었다. 그래야 league 추가분만 떼어 볼 수 있다.
- `build.py` → 43테이블. 생성된 league JSON을 10.18-c 원본과 대조:
  **6개 중 5개 완전 일치**, `league_enemy`만 빈 칸 직렬화 차이(6.2-5), 값은 동일.
- **기존 37테이블은 10.16-n 대비 변경 0건.**
- `tools/studio_roundtrip.py` → PASS (가전 12종·상품 29개 왕복, 테이블 불변).
- `dz_studio.py --selftest` → PASS / `DustZeroStudio.exe --selftest` → **PASS**
  (exe는 `xlsx_io.py`를 디스크에서 읽으므로 재빌드 불필요. 11 workbooks · 43 tables 인식).
- `sync_to_unity.py --check` → 43테이블 확인, 반영 대기 6개(league).

### Unity 컴파일은 아직 확인하지 않았다
`TableValidator.cs`·`CharacterDataTests.cs` 변경은 **에러 메시지 문자열과 주석뿐**이라
동작에 영향이 없지만, Unity 에디터에서 컴파일/테스트를 돌린 것은 아니다. 미확인으로 남긴다.

## 10. 2026-10-18 작업 기록 — 데이터 본반영

### 한 일
엑셀(편집 원본)에 10.18-c 값을 넣고 다시 변환했다. **JSON을 직접 고치지 않았다** —
그러면 다음 `build.py`에서 엑셀 값으로 되돌아간다.

| 테이블 | 반영 |
|---|---|
| `config` | 84 → **118키** (리그 28 · 지원가전 성장 4 · `spawnWarnSec` · `passXpLeaguePerWin`) |
| `string_ko` | 529 → **588키** + 문구 2건(`gear.4007.eff`, `unit.pc.motif`) |
| `stage` | **28행 hpMul** (3-1 0.95→1.52 … 14-4 5.26→10.51) |
| `enemy` | `fly.spd` 1.5 → **1.1** |
| `gear` | 4007 · 4009 — atk/heat 0.15 → 0, sp 0 → 0.15, 4007 sk2 `skillRange`→`buffAtk` 0.1 |
| `content_unlock` | `league` 행 (cond 1016 = 3-5) |
| `unit_profile` | `pc` `#2FBF9A`→`#8A63D2`, `#E3F7F1`→`#EFE9FB` |

값은 전부 `versions/10.18-c/data/*.json`에서 **프로그램으로 읽어 넣었다.** 손으로 옮기지 않았다.
우리 전용 `unit_art`·`unit_behaviour` 12행은 그대로 보존했다.

### 검증
- **먼저 `write_workbooks` 무손실 확인:** 11권 전부 읽기→쓰기→읽기 해도 43테이블 **전부 동일**.
  `04_Stage.xlsx`는 `map`을 `map_entrance`/`map_tile`로 되쪼개 쓰므로 이걸 확인하지 않고
  덮어쓰면 맵이 깨져도 모른다.
- 적용 스크립트가 **의도한 7개 외 테이블이 바뀌면 중단**하도록 단언을 걸었다. 통과.
- `build.py` 후 `data/` 전체를 10.18-c 원본과 대조 →
  **공통 41개 전부 일치**, 우리에만 있는 것은 `unit_art`·`unit_behaviour` 2개뿐.
- 값 직접 확인: `spawnWarnSec`=5 · `fly.spd`=1.1 · 3-1 hpMul=1.52 · 14-4=10.51 ·
  `content_unlock league` cond 1016 · `pc` 보라 · gear 4007 `buffAtk` 0.1.
- `sync_to_unity.py` → StreamingAssets 43개, `GameData/data`와 **바이트 동일**.

### 코드를 안 고치고도 되는지 확인한 것
- `content_unlock`의 새 `league` 행: Unity는 `Unlocks`를 `"idle"`·`"survivor"` 키로만
  꺼내 쓰고 전체를 순회하지 않는다. `TableValidator`는 cond 1016이 `condition`에 있고
  `unlock.league`가 `string_ko`에 있으면 통과 — 둘 다 있다.
- gear 4007의 `sk2Key` `buffAtk`: 장비는 `Edit(key, op, val)`로 skill_param을 일반적으로
  덮어쓰고 `BattleSim`이 `Skp("buffAtk")`를 읽는다. 전용 분기가 없어 코드 변경 불필요.
- config 신규 34키·string_ko 신규 59키는 읽는 쪽이 없으면 그냥 쓰이지 않는다.

### 기능은 아직 10.16-n이다
데이터만 10.18-c다. `spawnWarnSec`가 들어왔다고 리젠 예고가 생기는 게 아니고,
`genLvBonus`가 있다고 지원 가전이 성장하지 않는다. **5장 표의 기능 구현은 그대로 남아 있다.**
`stage` hpMul 28행이 바뀌었으므로 기존 전투 증거는 무효다.

### 검증 — Unity 실행 (2026-10-19 00:05)
Unity 에디터가 열려 있어 batchmode(`tools/unity.sh`)는 **프로젝트 잠금으로 거부**됐다.
Unity MCP로 돌렸다. 43개 임포트 뒤 도메인 리로드가 길어 브리지가 여러 번 끊겼다가 돌아왔다.

| 항목 | 결과 |
|---|---|
| 컴파일 | 에러 0. `DustZero.Tests.EditMode.dll` 재생성 확인 |
| EditMode | **112/112 통과** (6.7초) |
| PlayMode | **5/5 통과** (27.3초) |
| 증거 스크린샷 | **28장 재생성** (`production/qa/evidence/`) |

**처음 두 번의 테스트 실행은 `total: 0` 이었다.** 상태는 `Passed` 였지만 **아무것도 돌지 않은 것**이다.
`Window/General/Test Runner` 를 열어 Unity가 테스트 목록을 만들게 한 뒤에야 112개가 잡혔다.
MCP로 테스트를 돌릴 때는 **total 이 0이 아닌지 반드시 확인해야 한다.** 0은 통과가 아니다.

### 지금 눈에 보이는 불일치 — 컴퓨터(pc) 색
`unit_profile.pc`는 **보라**(`#8A63D2`)로 바뀌었는데 Unity의 `Art/Illust/pc_*.png` 6장은
아직 10.16-n **민트** 일러스트다. 정보창 테마색과 그림 색이 어긋난다.
10.18-a 아트 14종 + 아이콘 2종 재추출(7장 4단계)을 해야 맞는다.

## 11. 2026-10-19 — 리젠 예고(10.17-a·e) · 일시정지 창(10.17-c)

데이터만 들어와 있던 두 기능을 실제로 구현했다.

### 리젠 예고
| 파일 | 역할 |
|---|---|
| `Core/Defense/SpawnWarnings.cs` | **새 파일.** 묶음·정렬 규칙(순수 C#). `SpawnWarningBoard`는 스크래치 리스트를 재사용해 매 프레임 호출돼도 할당하지 않는다 |
| `Core/Defense/BattleSim.cs` | `ScheduleCursor` 공개 — 이미 나온 적을 다시 예고하지 않기 위한 기준점 |
| `Data/TableRows.cs` | `ConfigRow.spawnWarnSec` |
| `Game/SpawnWarnView.cs` | **새 파일.** 월드 공간 말풍선. 타일 단위로 배치해 보드 프레이밍을 따라간다 |
| `Game/BoardView.cs` | 입구마다 뷰 하나를 Build 때 만들고 `SyncSpawnWarnings()`로 매 프레임 갱신 |
| `Game/BattleScreen.cs` | **표현 시간**으로 호출 — 일시정지 중에도 테두리가 깜빡인다(원본과 같음) |
| `Editor/GeneratedArt.cs` | `Triangle`(꼬리) · `AirDot`(공중 표시: 원에서 날개 모양을 **뚫어** 어떤 배경색에도 맞음) |
| `Editor/ProjectBuilder.cs` | `SpawnWarn` 프리팹 + 보드 배선 |

원본에서 옮긴 규칙: 보스 금색 `#C98A12` / 공중 파랑 `#2F86D6` / 지상 빨강 `#E0524A`,
칸 최대 2개(넘치면 `+`), 보스→공중→지상 **안정 정렬**, 카운트다운은 올림,
맨 윗줄 입구는 말풍선을 옆으로 띄움, 테두리 깜빡임 `0.55 + 0.4·sin(12t)`.

**묶음 창의 기준점이 핵심이다.** 창은 *지금*이 아니라 **그 입구의 첫 등장 시각**에서 잰다.
지금 기준으로 자르면 창 경계에 걸친 웨이브가 중간에 잘려 마릿수가 틀린다. 테스트로 고정했다.

### 일시정지 창
전투 중 `나가기`가 **'포기?' 두 번 누르기 → 일시정지 창**으로 바뀌었다.
제목·설명·세 버튼 문구는 전부 `string_ko`의 `battle.pause.*`에서 읽는다.
`계속하기`(기본) 아래 `다시 하기` / `나가기`를 나란히 둔 원본 배치다.
`다시 하기`는 같은 스테이지를 처음부터 다시 시작한다(진행 중인 판은 버린다).
창이 떠 있는 동안 트레이 전체가 `CanvasGroup`으로 반투명·입력 차단된다(`.battle.pmenu`에 해당).
전투 시작 전·결과 화면에서는 예전처럼 바로 로비로 나간다.

### 테스트
`Assets/Tests/EditMode/SpawnWarningTests.cs` **6개 신규**:
창 설정/0이면 꺼짐 · 카운트다운 음수 금지와 소비된 일정 재예고 금지 · 입구별 격리 ·
보스→공중 정렬과 색 우선순위 · 같은 종류는 묶어서 세기 · **창 기준점이 '첫 등장'인지**.
`StageDef`에 테스트 전용 Clone을 추가하지 않으려고 리플렉션으로 복사해 합성 스테이지를 만든다.

### 이번에 밟은 함정 셋 (전부 조용한 실패였다)
1. **`ReloadPrefabs`의 수기 목록.** `Prefabs`에 필드를 넣고 프리팹을 제대로 만들어도
   이 목록에 없으면 **null로 조용히 배선**된다. `SpawnWarn`이 그랬다.
   → `AssertAllLoaded`를 추가해 이제 예외를 던진다.
2. **`GetComponent<T>() ?? AddComponent<T>()`.** `??`는 **Unity의 `==` 오버로드를 안 거친다.**
   없는 컴포넌트가 가짜 null로 돌아와 `??`를 통과했고, 컴포넌트는 추가되지 않았으며
   필드는 빈 채로 배선됐다. → `TryGetComponent`로 교체.
3. **빌더 실행 타이밍.** 스크립트를 고친 직후 리빌드를 돌리면 **컴파일 전 어셈블리로** 씬이
   생성된다. 두 번은 그래서 헛돌았다. 씬 시각 > Editor.dll 시각인지 확인해야 한다.
   그리고 Unity가 **PlayMode에 멈춰 있으면** 리빌드가 `This cannot be used during play mode`로
   거부된다 — 로그를 보기 전까지는 성공한 것처럼 보였다.

### 검증 (2026-10-19)
| 항목 | 결과 |
|---|---|
| 컴파일 | 에러 0 |
| EditMode | **118/118 통과** (112 → +6 신규) |
| PlayMode | **6/6 통과** (5 → +1 신규 `PauseMenuOpensFromExitAndDimsTheTray`) |
| 증거 | `02-battle-intro.png`·`03-battle-deployed.png`(리젠 예고) · **`05b-battle-pause-menu.png`**(신규) |

**화면으로 확인한 것:** 입구 1 위에 흰 말풍선 + 빨간 테두리 + 먼지 아이콘 + `×3` +
아래를 가리키는 꼬리 + 우상단 빨간 원에 남은 초(`3`, `2`), 입구 타일 테두리 깜빡임.
전투 중에도 **이미 나온 적이 아니라 다음 그룹**을 예고한다.
일시정지 창은 제목·설명·`계속하기`/`다시 하기`/`나가기`가 모두 보이고 트레이가 흐려진다.

**두 번 고쳐야 했던 것(둘 다 테스트는 통과하는데 눈으로는 틀렸다):**
- 카운트다운 배지가 `art.Circle`(반지름 82%부터 흐려지는 스프라이트)이라 흰 숫자가 묻혔다 →
  `GeneratedArt.Disc`로 교체.
- 일시정지 설명 줄이 **버튼 뒤에 깔려 안 보였다.** 라벨에 글자는 있어서
  `AssertNoBlankLabels`도 통과했다 → 카드 높이를 고정값이 아니라 **내용에서 계산**하도록 변경.

## 12. 2026-10-19 — 지원 가전 성장(10.17-b) · 강화 별 배지(10.17-d)

### 지원 가전 성장
발전기·컴퓨터는 레벨·돌파를 올려도 **하는 일이 전혀 세지지 않았다.** `powOf`는 공격력을
키우는데 이 둘은 공격을 하지 않기 때문이다. 원본의 `supPow`를 옮겼다.

| 파일 | 내용 |
|---|---|
| `Data/TableRows.cs` | `ConfigRow`에 `[JsonExtensionData]` + `TryNum(key)`. `<id>LvBonus` 처럼 **이름을 조합해 찾는 키**를 위해 필요하다. 필드로 박으면 테이블이 정하는 것을 C#이 정하게 된다 |
| `Core/Defense/BattleSim.cs` | `IUnitPowerSource.SupportPowerOf` 추가, `BattleSim.LevelOf` 노출 |
| `Core/Meta/PlayerProfile.cs` | `1 + <id>LvBonus×(Lv−1) + <id>StarBonus×★`, 키가 없으면 1 |
| `Core/Defense/BattleEntities.cs` | `BattleUnit.Support` · `BattleUnit.BoostMul` |
| `BattleSim` · `UnitBehaviours` | 전력 회복 · pc 버프 · 발전기 풀가동(충전 가능 판정 + 충전량)에 곱한다 |
| `Game/UnitDetailScreen.cs` | 상세 창에 한 줄: `전력 회복 +49W/초 · 풀가동 1,120W · 레벨업 +0.5W/초` |

**함께 고친 것:** 원본은 이 셋에 `sup`과 **`bst`(강화 배율)를 같이** 곱하는데 우리 쪽은
`bst`가 아예 빠져 있었다. 강화해도 발전기 출력이 그대로였다. `BoostMul`로 맞췄다.

### 강화 별 배지
Unity에는 **필드 배지가 아예 없었다.** `EntityView.TierBadge`(월드 TMP)를 만들고
`BoardView.SyncUnits`가 `Tier > 1`일 때 `★ × Tier`를 쓴다 — 배치 직후는 없음, 강화 1회 ★★.
액션바도 같은 수를 쓰도록 이름을 `청소기 ★★`로 바꿨고, **`Lv.{Tier}` → `Lv.{보유 레벨}`**로
고쳤다(강화 2단계 가전이 "Lv.2"로 보이고 있었다).

**원본과 다른 점 둘:**
- 강화 버튼에는 별을 넣지 못했다. 원본은 전체 폭 버튼에 `강화 ★★ · 2,000W`를 쓰지만
  여기 버튼은 작은 정사각형이라 `강화 ★★`는 `강화 …`로 잘리고, 보조줄 `★★ 500W`도
  133px 공간에 148px가 필요했다. 별은 **보드 배지와 패널 이름**에만 둔다.
- 배지에 어두운 외곽선이 없다. `outlineWidth`는 머티리얼 인스턴스가 있어야 먹고,
  공유 폰트 머티리얼을 건드리면 **게임의 모든 글자에 외곽선이 생긴다.**

### 테스트 · 증거
`Assets/Tests/EditMode/SupportGrowthTests.cs` **6개 신규** — 어떤 가전이 대상인지는 테이블이
정한다 / 레벨 올린 발전기가 더 회복한다(공식까지 대조) / 돌파도 반영 / 공격형은 배율 1 /
배치 직후 단계 1이고 강화 1회면 2 / 강화가 지원 출력도 올린다.

| 항목 | 결과 |
|---|---|
| EditMode | **131/131 통과** |
| PlayMode | **6/6 통과** |
| 신규 증거 | `03b-battle-boosted-badge.png`(★★) · `13b-unit-detail-support.png`(지원 성장 줄) |

### 또 나온 조용한 실패 셋
1. **죽어 있던 게이트.** 버튼 비용 잘림 검사가 `Face/Sub`를 찾고 있었는데 `UiKit`이 만드는
   경로는 `Content/Sub`다. **한 번도 실행된 적이 없었다.** 고치자마자 내가 넣은
   `★★ 500W`를 바로 잡아냈다. 본문 라벨 잘림 게이트도 없어서 새로 넣었다.
2. **그리드에 안 들어가는 줄.** 지원 성장을 칸 2개로 넣었더니 4칸=2줄에 딱 맞던 그리드가
   3줄이 되어 **시트 밖으로 밀려나** 글자가 잘렸다. 그리드 밖 한 줄로 바꿨다.
3. **`PlayerProfile.ClaimStarReward`에 다른 메서드 본문 조각이 들어 있었다**
   (`won`·`firstClear`·`psBefore` 미정의). 컴파일 자체가 막혀서 발견했고 제거했다.
   이 세션에서 생긴 것인지 이전부터인지는 확정하지 못했다.
