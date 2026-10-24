# 원본 10.22-g 화면 전수 목록 — Unity 대조표

**원본 코드 출처:** `GameData/versions/10.22-g/index.html` 의 인라인 스크립트
(1268행부터, **519,686바이트 · 5,867줄 · 함수 선언 606개**). 이 본문을 떼어
`scratchpad/rea-10.22-g/game.js` 로 두고 분석했다.

> **REA 정적 분석의 한계를 먼저 적는다.** `analyze_javascript_application` 을
> 10.22-g 폴더에 돌리면 `index.html has 1 inline JavaScript body starting at line
> 1268 that was not analyzed` 라고 분명히 말한다 — **게임 코드 전체가 빠진다.**
> 그래서 아래 목록은 떼어낸 본문에서 직접 뽑은 선언이다. 선언이 있다는 것은
> 코드가 있다는 뜻이지 그 화면이 실제로 그렇게 보인다는 증거가 아니다.
> 실제 모습은 브라우저로 띄워 찍은 것만 증거로 쓴다.

## 상태 표기

`일치` 실행·대조로 확인 / `차이` 대조로 확인된 불일치 / `미구현` Unity 에 코드 없음 /
`미검토` 아직 안 봄

## A. 로비 7탭

| 원본 진입점 | 화면 | Unity | 상태 |
|---|---|---|---|
| `setTab` / `renderLobby` | 탭 전환 틀 | `LobbyScreen` | 미검토 |
| `renderHome` `drawHome` `drawRoom` `roomInit` | 홈(방 로비) | `HomeTab` `RoomView` | **차이** — 퀘스트·우편 버튼 없음 |
| `renderIdle` | 방치 보상 | `HomeTab` | 미검토 |
| `renderAttendBtn` `openAttend` | 출석부 | `AttendanceScreen` | 미검토 |
| `renderWallet` | 재화 띠 | `LobbyScreen` | 일치(눈대중) |
| `renderStages` `renderAreaTabs` `drawHouse` | 스테이지 | `StageTab` | **구역 칩 수정 완료** |
| `renderPsPanel` | 전력 스킬 | `StageTab` | **수정 검증 중** |
| `renderHard` | 심화 75판 | — | **미구현** |
| `renderContent` `renderPoolCard` `renderSurvBtn` | 콘텐츠 | `ContentTab` | **차이** — 이벤트 배너 없음 |
| `renderRoster` | 가전 도감 | `UnitsTab` | **차이** — 역할 칩 `.ak` 없음 |
| `renderDeck` | 편성 | `DeckTab` | **차이** — 역할 칩 `.ak` 없음 |
| `renderGacha` `openOdds` | 소환 | `GachaTab` | **차이** — 픽업 3종 고정 · 천장 없음 · 확률 정보 없음 |
| `renderGearGacha` | 장비 소환 | `GachaTab` | 미검토 |
| `openShop` `renderShop` | 상점 | `ShopTab` | 거의 일치 |
| `renderPassBtn` `openPass` `passCell` | 시즌 패스 | `PassScreen` `PassRow` | 미검토 |

## B. 전투(디펜스)

| 원본 진입점 | 화면 | Unity | 상태 |
|---|---|---|---|
| `showIntro` | 브리핑 | `BattleScreen` | 미검토 |
| `render` `draw` `miniMap` | 전투판 | `BattleScreen` `BoardView` | **차이** — 배치 강조가 항상 켜짐 |
| `drawSpawnWarn` | 리젠 예고 | `SpawnWarnView` | 미검토 |
| `openPauseMenu` | 일시정지 | `BattleScreen` | 미검토 |
| `drawEnemy` `drawChibi` `drawSpriteSD` `drawLook` `drawMarks` | 적·SD 그림 | `EntityView` | 미검토 |
| `tutShow` `tutScript` 외 17함수 | 전투 튜토리얼 | `GuideOverlay` | 미검토 |
| `guideStep` `guideNext` 5함수 | 강제 안내 | `GuideOverlay` | 미검토 |

## C. 대청소(생존)

| 원본 진입점 | 화면 | Unity | 상태 |
|---|---|---|---|
| `openSurvPick` | 리더 선택 | `SurvivorScreen` | 미검토 |
| `survRender` `renderSurvTeam` | 대청소 판 | `SurvivorView` | 미검토 |
| `openLevelUp` `optView` | 레벨업 선택 | `SurvivorOptionCard` | 미검토 |
| — | 결과 | `SurvivorResultScreen` | 미검토 |

## D. 다른 모드

| 원본 진입점 | 화면 | Unity | 상태 |
|---|---|---|---|
| `renderCoopCard` `draw`(5117) | 협동 청소 | `CoopPickScreen` `CoopBars` | 미검토 |
| `renderTowerCard` | 먼지 타워 | `LeagueScreen.Tower` `TowerFloorCard` | 미검토 |
| `renderLeagueCard` `lgRender` | 리그 | `LeagueScreen` `LeagueView` | 미검토 |

## E. 가전·장비 상세

| 원본 진입점 | 화면 | Unity | 상태 |
|---|---|---|---|
| `openDetail` `openSheet` `openModal` | 가전 상세 | `UnitDetailScreen` | 미검토 |
| `openGearBag` `openGearDetail` `openGearPick` | 장비 가방·상세 | `GearBagScreen` `GearCell` | 미검토 |
| `openExclExchange` | 전용장비 교환 | `GearExchangeRow` | 미검토 |
| `openArt` `renderArtTab` | 일러스트 뷰어 | `ArtViewerScreen` | 미검토 |

## F. 10.22-g 신규 — Unity 에 코드가 없다

| 원본 진입점 | 화면 | 비고 |
|---|---|---|
| `renderQBar` `questAlert` | 퀘스트 | 홈 왼쪽 버튼 + 바 |
| `openMissions` | 미션/업적 | 이벤트 배너의 `미션 보기` |
| `renderEventCard` | 이벤트(가을맞이 대청소) | 콘텐츠 탭 상단 배너 |
| `openMail` `mailSt` `mailClaim` 외 7함수 | 우편함 | 홈 오른쪽 버튼 |
| `openCodex` `codexSeen` `codexKill` `codexAlert` | 먼지 도감 | 가전 탭 버튼 |
| `openCollect` | 수집 보너스 | 가전 탭 버튼 |
| `stGame` `stPlay` `stShow` `stNext` `stEnd` `stStage` `stBoxHTML` `stBoxBind` `stTabVisit` `stGameResult` | 스토리 + 미니게임 | 가전 카드 `이야기` 배지 |
| `renderHard` `hardOn` | 심화 75판 | 스테이지 탭 |
| `openPlog` `plogSummary` | 플레이 기록 | |

> 이 9계통은 **지어내지 않는다.** 표·수치·진입점이 원본에 있으므로 옮길 수는 있으나,
> 이번 대조 작업의 범위는 **이미 있는 화면을 원본과 맞추는 것**이다. 신규 계통 이식은
> 범위가 다른 결정이므로 사용자 확인 뒤에 착수한다.
