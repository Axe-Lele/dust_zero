# 전력 스킬 — 전투에 구현되어 있지 않다

2026-10-19 확인. 결과 화면 작업 중에 드러났다.

## 지금 상태

| 있는 것 | 없는 것 |
| --- | --- |
| `power_skill` 테이블 11행 (id · lv · unlockStage · cost · cd · radius · dmg · stun · dur · atkMul · itvMul · spMul) | **전투에서 쓰는 코드 전부** |
| 스테이지 탭의 전력 스킬 목록 (`StageTab.BuildPowerSkills`) | 전투 화면의 스킬 버튼 바 |
| 해금 레벨 계산 (`PlayerProfile.PowerSkillLevel`) | 쿨다운 상태 · 전력 소모 |
| 결과 화면의 해금/강화 안내 | 조준(과전류 방출은 터치한 칸에 떨어진다) |
| | 효과 적용(피해 · 기절 · 전체 버프) |

`grep -rn PowerSkill DustZeroUnity/Assets/DustZero/Scripts/Core/Defense/` → **0건.**

## 원본은 어떻게 쓰나

```js
G.ps = psState()                  // 해금된 스킬마다 {row, cd, used}
psStorm()  // 과전류 방출: 누른 칸 반경 1.5칸에 피해 + 1.6초 기절, 비행 적도 맞는다
psBoost()  // 전력 증폭: 8초간 모든 가전 공격력 +25%, 공격 속도 +25%, 스킬 충전 1.5배
for (const k in G.ps) G.ps[k].cd -= dt      // 매 틱 쿨다운
$('psBar')                        // 전투 화면 아래 버튼 바, 쿨다운/전력 부족 표시
```

둘 다 **남는 전력으로 쓰는 액티브 스킬**이다. 배치와 같은 전력을 쓰므로, 전력을
가전에 쓸지 스킬에 쓸지가 이 게임의 핵심 선택 하나다. 그게 통째로 빠져 있다.

## 그래서 "전력 스킬 플레이 검증" 은 할 수 없다

검증할 것이 없다. 이것은 검사 실패가 아니라 **미구현**이다. 통과로 적지 않는다.

## 만들려면

1. `BattleSim` 에 `PowerSkillState`(row · cd · used) 와 `Tick` 의 쿨다운 감소
2. `CastStorm(c, r)` / `CastBoost()` — 전력 차감, 쿨다운 시작, 효과 적용
   (storm 은 반경 내 적에게 dmg + stun, boost 는 기존 `BoostMul` 통로를 그대로 쓸 수 있다)
3. 전투 화면에 버튼 바 + 과전류 방출의 조준 모드(칸을 누르면 그 자리에 떨어진다)
4. 테스트: 전력이 모자라면 못 쓴다 / 쿨다운 중에는 못 쓴다 / 반경 밖 적은 안 맞는다 /
   증폭 중에는 공격력이 실제로 오른다

`unlockStage` 가 3-5, 6-5 … 인 것으로 보아 초반 판에서는 안 열린다. 1-1 만 돌리는
스모크 테스트로는 영영 안 걸린다는 뜻이기도 하다.
