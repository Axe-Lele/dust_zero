# 원본 기준선 — 어떻게 떴는지

**버전 10.19-e · 뷰포트 430×932 · 수집 2026-10-19 16:00**

출처는 `C:/Users/Master/Downloads/Dustzero_261019_E/dustzero_play` 이고,
프로젝트 사본 `GameData/versions/10.19-e/` 와 **230/230 파일 바이트 동일**함을 확인했다.
따라서 사본을 서빙해서 떠도 같은 결과다.

## 게임 상태 — 이걸 맞추지 않으면 짝이 어긋난다

brand-new 세이브에서 시작해 **`best=2`, `stars{101:3, 102:3}` 만** 심었다.
**10.19-e 가 대청소 해금을 1-1 에서 1-2 로 옮겼기 때문이다**(`content_unlock` survivor
cond 1 → 1003 = 1-2 클리어). 그 이상은 건드리지 않았다.

```js
const s = JSON.parse(localStorage.getItem('gajeon-save-v2'));
s.best = 2; s.stars = { "101": 3, "102": 3 };
localStorage.setItem('gajeon-save-v2', JSON.stringify(s));
```

**이식본도 같은 상태로 맞춰 두었다.** `PlayModeSmoke.ResetSaveOnDisk()` 가 씬을 띄우기 전에
세이브 파일을 지우고, `SeedAuditState()` 가 위와 똑같이 `ClearedStageIds = {101, 102}` 를 심는다.
**한쪽을 바꾸면 다른 쪽도 바꿔야 한다** — 안 그러면 두 쪽이 비교 대상이 아니게 된다.

맞추기 전에는 같은 코드로 두 번 돌려 **짝 216/차이 133 → 짝 214/차이 131** 로 움직였다.
맞춘 뒤에는 두 번 모두 **짝 223/차이 136** 으로 같다.

## 다시 뜨는 방법

```bash
python -m http.server 8774 --bind 127.0.0.1        # 저장소 루트에서
```

Playwright 를 **430×932** 로 맞추고
`http://127.0.0.1:8774/GameData/versions/10.19-e/index.html` 로 간 뒤,
`/tools/audit_extract_original.js` 를 fetch + eval 해서 `window.DZ_AUDIT(root)` 를 부른다.
저장소 루트를 서빙하는 이유는 추출기를 **같은 출처**로 불러오기 위해서다 —
원본 사본 폴더에 파일을 넣지 않는다.

| 화면 | root | 가는 길 |
| --- | --- | --- |
| lobby-home / stage / content / unit / deck / gacha / shop | `.app` | `[data-tab=...]` 클릭 |
| battle-brief | `#ovCard` | 스테이지 → `.sbtn`(도전) |
| battle | `#battle` | 브리핑 → 시작 → 카드 선택 → 보드 클릭 |
| surv-pick | `#mbox` | 콘텐츠 → 대청소 |
| surv | `#surv` | 리더 선택 → 출동 |

## 10.19-e 로 올리면서 달라진 화면

| 화면 | 10.18-c | 10.19-e |
|---|---:|---:|
| 콘텐츠 | 106 | **156** (타워·협동이 들어옴) |
| 소환 | 62 | **64** (무료 첫 소환) |
| 대청소 리더 선택 | 37 | **36** |

나머지 8화면은 상자 수가 같다. 10.19-e 는 기존 CSS 규칙을 하나도 바꾸지 않았다
(규칙 734 → 878, 추가만 144).

## 이전 기준선

- `backup-10.18-c/` — 직전 기준선 11개
- `backup-10.16-n/` — 그 전. **뷰포트가 섞여 있었다**(로비 7화면 430×932, 전투·브리핑·
  대청소·리더선택 4화면 390×844). 추출기가 지금은 뷰포트가 다르면 수집을 거부한다.
