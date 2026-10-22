# 10.16-n 이식 기준 — 10.15-h 대비 차이

사용자가 2026-10-17에 **디자인 + 데이터 전면 이식**을 지시했다.
이전 세션의 "10.16-n은 분석만 하고 적용 보류" 지침은 이 지시로 대체되었다.

- 원본 다운로드: `C:/Users/Master/Downloads/Dustzero_261016_N/dustzero_play` (수정 금지)
- 프로젝트 내 버전 사본: `GameData/versions/10.16-n/` (191파일, 해시는 같은 폴더 `INVENTORY.json`)
- 비교 대상: `GameData/versions/10.15-h/`
- 원본 html 비교 서버: `python -m http.server 8777 --bind 127.0.0.1 --directory "<원본경로>"`
  → `http://127.0.0.1:8777/index.html`. 화면 캡처는 `production/qa/evidence/browser/n16-*.png`.

**아직 `GameData/tables`, `GameData/data`, `ACTIVE-VERSION.txt` 는 10.15-h 그대로다.**
버전 사본만 만들었을 뿐, 활성 편집 원본을 바꾸는 것은 데이터 계층 이식과 함께 수행한다.

## 오디오

10.16-n의 `audio/` 3곡은 10.15-h와 **바이트 동일**(sha256 대조 완료).
이미 완료한 BGM 작업을 다시 할 필요가 없다.

## 화면 구조 차이 (실제 실행 대조)

| 항목 | 현재 Unity (10.15-h) | 10.16-n |
| --- | --- | --- |
| 테마 | 밝은 크림 | **어두운 차콜** (모달·카드 일부는 밝은 면) |
| 하단 탭 | 5개 | **7개**: 홈·스테이지·콘텐츠·가전·편성·소환·상점 |
| 가전 | 9종 | **12종** (보일러·다리미·가스레인지 추가) |
| 스테이지 | 15 | **75** (5구역 × 15챕터) |
| 소환 | 가전만 | 가전 + **장비 소환** 2종 세그먼트 |
| 가전 탭 | 보유 목록 | **도감**(미보유 실루엣) + 일러스트 + 장비 가방 |
| 신규 화면 | — | **일러스트 정보창**(프로필·표정·설정화 3탭) |
| 스테이지 탭 | 목록 | **전력 스킬** 섹션 + 출전 편성 + 5구역 탭 + 챕터 카드 |

## 데이터: 29 → 35 테이블

### 신규 6종

| 테이블 | 행 | 컬럼 |
| --- | --- | --- |
| `area` | 5 | id, nameKey, shortKey, subKey, descKey, chFrom, chTo, bg, color |
| `gear` | 21 | id, type, grade, unitId, nameKey, effKey, atk, heat, sp, sk1Key/Op/Val, sk2Key/Op/Val |
| `gear_gacha` | 2 | id, nameKey, costItem, costCount, pulls, guaranteeGrade, w1~w4 |
| `gear_surv` | 30 | gearId, unitId, slot, key, op, val |
| `power_skill` | 6 | id, lv, unlockStage, unlockCond, cost, cd, dur, dmg, radius, stun, atkMul, spMul, itvMul |
| `unit_profile` | 12 | unitId, art, keyColor, bgColor, taglineKey, profileKey, motifKey |

`unit_profile.art` 가 일러스트 경로 접두어다. 비어 있으면 원본은 SD 캔버스로 대체한다
(`hasArt(t)` 분기). 즉 일러스트가 없는 가전도 화면이 깨지지 않아야 한다.

### 스키마가 바뀐 테이블

| 테이블 | 행 변화 | 추가된 컬럼 |
| --- | --- | --- |
| `config` | 1 → 1 | armorMax, deployCooldown, exclDupeEssence, exclExchange, exclMaxStar, exclStarBonus, gearCoinPerLv, gearDismantle, gearEssencePerPull, gearFuseNeed, gearMaxLv, gearRefund, gearStatPerLv, gearStonePerLv, oilTileEnemySpeedMul, oilTileHeatPerSec, rugTileEnemySpeedMul, stunBossMul, survStonePerMin, tutGuestMax |
| `enemy` | 8 → 19 | armor, split, splitN, summon, summonEvery, summonN |
| `stage` | 15 → 75 | area, dmgMul, refGear, refLv, refStar |
| `enums` | 12 → 12 | Over |
| `string_ko` | — | 약 230키 추가 (area/gear/map/ps/unit profile·tagline·motif/신규 가전 3종/신규 적 6종) |

### 행만 늘어난 테이블

`condition` 15→76, `gacha_pool` 9→12, `item` 15→20, `map` 3→15,
`reward_group` 303→656, `shop_product` 23→29, `skill_param` 37→55,
`star_reward` 9→45, `surv_param` 98→121, `surv_wave` 7→9, `tutorial` 20→31,
`unit` 9→12, `unit_boost` 18→24, `unit_levelup` 4→4(값), `wave` 285→1356.

### 엑셀

`09_Gear.xlsx` 신규. 나머지 8개는 이름 동일(내용 변경).

## 아트: webp 116종

**Unity는 webp를 네이티브로 임포트하지 못한다.** PNG 변환이 전제다.

- `art/icon/*.webp` 43종 — UI 아이콘. 원본이 `art/icon/${n}.webp` 로 동적 참조하므로
  이름을 그대로 유지해야 한다.
- `art/*.webp` 72종 — 12가전 × 6종(`full`, `face1~3`, `turn`, `keys`).
  **전투/보드에는 쓰이지 않는다** — 보드는 여전히 캔버스 SD 드로잉(`DRAW`/`OBJ_DRAW`)이다.
  이 72종은 일러스트 정보창 전용이다. `vac_full.webp` 는 516×1080 **RGB(알파 없음)** 으로
  확인됐다. 파일마다 알파 유무가 다를 수 있으므로 변환 시 개별 확인한다.

## 이식 순서

1. 원본 버전 보존 + diff 기록 ← **이 문서 (완료)**
2. webp → PNG 변환 + Unity 임포트 규칙
3. 데이터 계층 35테이블 (TableRows/GameDatabase/TableValidator, build·sync 경로 전환)
4. UiTheme 다크 테마
5. 로비 7탭 재구성 + 일러스트 정보창
6. 신규 시스템 (장비, 전력 스킬, 75스테이지, 가전 3종, 적 신규 능력)
7. 세이브 v4 → v5 마이그레이션 (사용자 실제 세이브로 복원 검증)

각 단계마다 컴파일 → 테스트 → 실제 화면 확인. 문서만 쓰고 완료로 보고하지 않는다.
