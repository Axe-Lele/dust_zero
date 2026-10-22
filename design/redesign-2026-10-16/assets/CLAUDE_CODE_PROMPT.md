# Claude Code에 붙여 넣을 명령어

아래 블록 전체를 Unity 프로젝트 루트에서 Claude Code에 그대로 붙여 넣으세요.
그 전에 이 `assets/` 폴더를 `Assets/Art/UI_DZ/`로 복사하고, 디자인 문서 2개(`더스트 제로 UI 리디자인.dc.html`, `구현 가이드.dc.html`)를 `Docs/UI/`에 넣어 두세요.

```
너는 Unity(uGUI + TextMeshPro) 2D 모바일 게임 「오퍼레이션: 더스트 제로」의 UI 구현 담당이야.
디자인 시안은 Docs/UI/ 의 HTML 2개, 에셋은 Assets/Art/UI_DZ/ 에 있고 Assets/Art/UI_DZ/manifest.json 에 각 PNG의 크기·9-slice border([left,right,top,bottom] 순서, px)·tint 여부·temp 여부가 적혀 있어.
기존 게임 규칙·로직 코드는 바꾸지 말고 UI 레이어만 교체해. 모르는 규칙(멀티탭 효과, 과열 조건, 별 조건 등)은 TODO 주석으로 남기고 추측으로 구현하지 마.

1단계 — 임포트 자동화
- Assets/Editor/DZUIImporter.cs (AssetPostprocessor) 작성: Assets/Art/UI_DZ/** 는 Sprite(Single), PPU 100, Mesh Type Full Rect, Compression ASTC 6x6(아이콘은 4x4), Mip 끔, manifest.json의 border를 spriteBorder에 적용(Vector4(left,bottom,right,top) 변환 주의).
- SpriteAtlas 4개: UI_Common(ui_common, icons, currency), UI_Battle(battle), UI_Collection(collection), Characters(characters_temp, enemies_temp).

2단계 — 공통
- Canvas Scaler: Scale With Screen Size, 1080x1920, Match 0.5.
- SafeAreaRoot.cs: Screen.safeArea → RectTransform anchorMin/Max, 해상도 변경 시 재계산. 배경·하단바 바탕은 SafeArea 밖까지, 터치 요소만 안쪽.
- 색 상수 DZColors.cs: Primary #E8701A, PrimaryEdge #B9560F, Navy #2F4A7A, Success #3FA66B, Danger #E0533D, Power #F5C028, Premium #8E5BD6, Ink #2B2420, Ink2 #5E5046, Panel #FFFDF8, Panel2 #F1E8DB, Disabled #E4DCD1. 등급 N #9AA0A6 R #3E8ED0 SR #8E5BD6 SSR #E6A817.
- 폰트: Pretendard(본문), Jua(제목·큰 버튼) TMP Font Asset 생성, KS X 1001 2,350자 + 영숫자, Dynamic 폴백.
- 재사용 프리팹 + 상태 enum (구현 가이드 7장 표 그대로):
  UIButton(Normal/Pressed/Selected/Disabled/Locked/Claimable; btn_*.png 스왑, 눌림 시 Y -8px 60ms),
  CurrencyChip(K/M/B 축약: 1만 미만 쉼표 전체, 이상 유효숫자 3자리),
  TabItem, SegmentedTab, RailButton, AutoCleanChip, DeployCard(Normal/Selected/LowPower/Cooldown/Locked/Empty),
  FieldTile, UnitView(Idle/Selected/Heating/Overheated/MultitapLinked), UnitActionBar, CharacterCard(등급×Owned/NotOwned), BottomSheet, Toast.
- 최소 터치 영역 121px(44pt). 작은 아이콘은 투명 Image(Raycast Target)로 영역 확장.

3단계 — 화면 (시안 id 기준, 1080 기준 px = 시안 pt × 2.77)
- 홈 = 시안 1a: 방 배경 + 캐릭터 배회, 상단바(프로필·재화), 우측 레일(출석·패스·설정), 자동청소 칩, 대청소+다음 스테이지 버튼, 하단 5탭(홈/스테이지/가전/소환/상점). 스테이지 탭 안에 [스테이지|콘텐츠], 가전 탭 안에 [보유 가전|편성] 하위 탭.
- 전투 HUD = 시안 1c: 브리핑(상단 패널+하단 시작) → 플레이. 상단 일시정지/웨이브/남은 적/배속 + 청결도 바, 하단 트레이(전력 바 + 카드 5장 ↔ 유닛 선택 시 강화·멀티탭·철수 패널). 카드 탭→칸 탭 배치, 가능 칸만 tile_hl_valid, 나머지 알파 0.45. 나가기는 일시정지 메뉴 안.
- 2a 스테이지, 2b 가전 상세, 2c 편성, 2d 소환, 2e 상점, 2f 시즌 패스, 2g 결과.
- 전장은 SpriteRenderer + 별도 카메라, HUD는 Screen Space Overlay. 입력은 HUD 먼저 → Physics2D Raycaster로 칸.

4단계 — 검증 후 보고
- 해상도 1080x1920, 1170x2532, 720x1600, 1440x3200(Device Simulator)에서 스크린샷 캡처해 Docs/UI/verify/ 에 저장.
- 시안과 다른 점, TODO, 미검증 항목을 Docs/UI/IMPLEMENTATION_NOTES.md 에 정리.
- characters_temp / enemies_temp / backgrounds_temp 는 임시. 실제 아트가 오면 같은 파일명으로 교체만 하면 되게 참조를 파일명 기준으로 둬.

한 번에 다 하지 말고 1단계 → 2단계 → 홈 → 전투 HUD 순서로 진행하고, 각 단계 끝날 때마다 무엇을 만들었는지 짧게 보고해.
```

## 에셋 요약 (108개, manifest.json 참고)
- ui_common: 패널·필·막대·버튼(5색×일반/눌림 + 비활성·선택)·배지·탭 표시·딤 — 9-slice
- battle: 카드 프레임 5종(9-slice), 타일 8종(144px), 사거리·선택·과열 링
- collection: 등급 프레임 N/R/SR/SSR/미보유(9-slice) + 등급 배지
- icons: 흰색 단색 40종(96px, Tint) — Material Symbols Rounded(Apache 2.0)
- currency: 코인·크리스탈·부품·소환권·전력·EXP·별
- *_temp: 임시 캐릭터 4종×3크기, 적 2종, 방 배경 1080×1920 — 실제 아트로 교체 필요
