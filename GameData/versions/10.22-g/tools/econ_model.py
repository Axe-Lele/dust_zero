"""무과금 기준 경제 모델 (10.16-f) — 레벨·돌파 비용과 수입으로 '몇 일째에 몇 스테이지'를 추정한다.

    python tools/econ_model.py

data/*.json 을 읽어서(먼저 gen_tables.py 또는 build.py 실행) 하루 단위로 굴린다.
가정(플레이어): 하루 자동 청소 12시간 + 광고 무료 보상 + 대청소 3판 + 리그 브론즈 3판(6승, 3-5 이후) + 출석 + 일일·주간 퀘스트 전부(10.21-c) + 업적(스테이지·도감)
             + 우편(환영·진행 축하) · 편성 가전 호감도(하루 말 걸기 + 전투 10판) · 수집 보너스(자동 청소 시급, 10.21-d), 크리스탈은 75% 가전 소환 · 나머지 장비 소환,
             편성 6명(R 4 · SR 2)을 고르게 레벨 업, 조각이 모이면 돌파, 공용장비는 코인의 20%까지 강화.
스테이지는 '기준 계정'(stage.refLv / refStar / refGear)의 전투력 98% 이상이면 깬 것으로 본다.
전투 쪽은 tools/sim 의 시뮬레이터가 기준 계정으로 별 2개가 나오게 HP를 맞춰 두었으므로, 이 모델의 날짜 = 무과금 진행 속도.
목표(10.16-f): 15스테이지 ≈ 2일, 30 ≈ 5일, 45 ≈ 9일, 60 ≈ 13일, 75 ≈ 4주.
"""
import json, os, random

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data")
J = lambda n: json.load(open(os.path.join(DATA, n + ".json"), encoding="utf-8"))
HOURS = 12            # 하루 자동 청소 시간(12시간 상한, 하루 두 번쯤 받는다고 봄)
PULL_SHARE = 0.75     # 크리스탈 중 가전 소환에 쓰는 비율
_UR = [u["rarity"] for u in J("unit")]; N_R, N_SR = _UR.count(1), _UR.count(2)   # 등급별 가전 수(소환 확률이 나뉨)
# 10.21-c 퀘스트(하루치 = 일일 + 주간 ÷ 7, 전부 한다고 봄) · 업적(스테이지 진행 줄기) · 먼지 도감(19종 × 10, 60판까지 고르게)
QUEST_COIN = 500 + 500 + 500 + (3000 + 3000) / 7
QUEST_CRY = 20 + 80 / 7
QUEST_STONE = 5 + 5 + 10 + (20 + 10 + 30) / 7
QUEST_PART = 3 + 2 + 3 + 5 + (15 + 15 + 10 + 20) / 7
ACH_BEST = {5: 20, 15: 30, 30: 50, 45: 50, 60: 80, 75: 150}
CODEX_PER_STAGE = 190 / 60
# 10.21-d 우편·호감도·수집 보너스
MAIL_BEST = {1: (100, 5000, 0), 10: (0, 0, 30), 25: (100, 0, 0)}   # 진행 → (크리스탈, 코인, 강화석)
AFF_XP_DAY = 5 + 3 * 10                                             # 편성 가전 하나가 하루 받는 호감도(말 걸기 + 클리어 10판)
AFF_LV = [(2, 30, 0, 2000, 0), (3, 80, 10, 0, 0), (4, 150, 0, 0, 3), (5, 250, 20, 0, 0), (6, 380, 0, 0, 0), (7, 540, 0, 0, 5), (8, 740, 30, 0, 0), (10, 1260, 50, 0, 5)]   # (레벨, 누적 xp, 크리스탈, 코인, 조각)
# 10.21-e 가전 스토리: 탭 첫 방문 크리스탈 30, 1화(바로) 코인 2000 · 호감도 20, 2화(호감도 4) 크리스탈 10 · 30 + 미니게임 첫 성공 20, 3화(호감도 8) 크리스탈 20 · 40
STORY_TAB_CRY = 30
# 10.21-h 심화 모드: 보통 b판을 깼으면 심화 (b−25)판까지 깬다고 봄(심화 n 기준 계정 = 보통 n+25). 첫 클리어 크리스탈 20(보스 50) · 강화석 10(20) · 보스 부품 20
# 주간 시즌: 리그 단계별 이번 주 첫 완주 60/100/150(리그 30판 · 실버 45판 · 골드 60판쯤부터), 타워 최근 5층 재청소 20씩(25판부터) → 하루치 = ÷7
# 10.21-i 이벤트가 1년 내내 4주씩 이어짐: 4주에 크리스탈 400 + 우편 50 · 코인 1만(3만이면 75판 24일이라 줄임) · 강화석 40 · 부품 20 → 하루치 = ÷28
def week_cry(best): return (60 if best >= 30 else 0) / 7 + (100 if best >= 45 else 0) / 7 + (150 if best >= 60 else 0) / 7 + (100 / 7 if best >= 25 else 0)
EV_CRY, EV_COIN, EV_STONE, EV_PART = 450 / 28, 10000 / 28, 40 / 28, 20 / 28
def aff_xp(d):   # d 일째 끝 누적 호감도(스토리 보너스 포함)
    x = AFF_XP_DAY * d + 20
    if x >= 150: x += 50
    if x >= 740: x += 40
    return x
def col_bonus(day, best):   # 자동 청소 (코인, 부품) 보너스 — 보유 6종(시작) · 9종(10일쯤) · 호감도 합 20(3일)·50(15일) / 돌파 합 10(6일)·30(20일) · 도감 10(25판)·19(70판)
    c = .03 + (.03 if day >= 10 else 0) + (.02 if day >= 3 else 0) + (.03 if day >= 15 else 0)
    p = (.03 if day >= 6 else 0) + (.03 if day >= 20 else 0) + (.05 if best >= 25 else 0) + (.05 if best >= 70 else 0)
    return c, p


def main(seed=1):
    random.seed(seed)
    cfg = J("config"); stages = sorted(J("stage"), key=lambda s: s["order"]); lvt = J("unit_levelup"); stars = sorted(J("unit_star"), key=lambda r: r["star"])
    rg = {}
    for r in J("reward_group"): rg.setdefault(r["groupId"], []).append(r)
    # 10.22-a 메인 퀘스트: 장 c 의 보상은 보통 c장 끝(5c판)을 깰 때 다 받는다고 봄(16장 = 엔드는 빼고). 서브는 크리스탈·강화석 위주라 뺌
    qmain = {}
    for q in J("quest"):
        if q["line"] == 0 and 1 <= q["chapter"] <= 15: qmain.setdefault(5 * q["chapter"], []).append(q["rewardGroup"])
    def lvcost(lv):
        for r in lvt:
            if r["lvMin"] <= lv <= r["lvMax"]: return r["coinBase"] + r["coinStep"] * (lv - r["lvMin"]), r["partBase"] + r["partStep"] * (lv - r["lvMin"])
        return None
    pw = lambda lv, st: 1 + cfg["lvBonus"] * (lv - 1) + cfg["starBonus"] * st
    lv = [1] * 6; star = [0] * 6; rar = ["R"] * 4 + ["SR"] * 2; fam = [0, 0, 1, 1, 2, 3]
    coin, cry, parts, shards, stones = 2000, 1500, [20] * 4, [0] * 6, 0
    grade = gear_lv = 0; best = 0; day = 0; reached = {}
    while best < len(stages) and day < 200:
        st = stages[max(0, best - 1)]
        cb, pb = col_bonus(day, best)
        coin += st["idleCoin"] * HOURS * (1 + cb) + 9000 + 2000 + 2140
        axp_prev, axp = (aff_xp(day - 1) if day else 0), aff_xp(day)
        if day == 0: cry += STORY_TAB_CRY * 6; coin += 2000 * 6
        if axp_prev < 150 <= axp: cry += 10 * 6
        if axp_prev < 740 <= axp: cry += 20 * 6
        for _alv, need, ac, aco, ash in AFF_LV:
            if axp_prev < need <= axp:
                cry += ac * 6; coin += aco * 6
                for i in range(6): shards[i] += ash
        if best >= 30: coin += 250 * 18                      # 10.18-c 리그 브론즈 하루 3판 × 6승(10.21-g 6-5 클리어부터)
        cry += st["idleCrystal"] * HOURS + 90 + 150 + 50 + 40
        cry += week_cry(best) + EV_CRY; coin += EV_COIN; stones += EV_STONE   # 10.21-h·i 주간 시즌 · 이벤트
        coin += QUEST_COIN; cry += QUEST_CRY; stones += QUEST_STONE   # 10.21-c 일일 + 주간/7
        for f in range(4): parts[f] += st["idlePart"] * HOURS * (1 + pb) + 10 + 30 + 1.4 + (18 if best >= 30 else 0)
        stones += 36
        for i in range(4): parts[i] += EV_PART
        for i in range(4): parts[i] += QUEST_PART
        pulls = int(cry * PULL_SHARE // 150); cry -= pulls * 150; cry -= int(cry // 100) * 100
        for i in range(6):
            p = 0.8 / N_R if rar[i] == "R" else 0.17 / N_SR   # 10.21-f 가전 16종: 등급 안 가전 수로 나눔(R 5 · SR 7)
            shards[i] += pulls * p * (10 if rar[i] == "R" else 15)
        for i in range(4): shards[i] += 1.0
        grade = 3 if day >= 13 else 2 if day >= 6 else 1 if day >= 2 else 0
        for i in range(6):
            nx = next((r for r in stars if r["star"] == star[i] + 1), None)
            if nx and shards[i] >= nx["shards"] and coin >= nx["coin"]: shards[i] -= nx["shards"]; coin -= nx["coin"]; star[i] += 1
        base = {0: 0, 1: .07, 2: .14, 3: .23}[grade]; gmax = cfg["gearMaxLv"][grade - 1] if grade else 0
        while gear_lv < gmax:
            c = cfg["gearCoinPerLv"][grade - 1] * (gear_lv + 1) * 6; sn = cfg["gearStonePerLv"][grade - 1] * (gear_lv + 1) * 6
            if coin * .2 < c or stones < sn: break
            coin -= c; stones -= sn; gear_lv += 1
        gear = base * (1 + cfg["gearStatPerLv"] * gear_lv)
        while True:
            cand = [k for k in range(6) if lv[k] < min(cfg["lvMax"], cfg["lvCapBase"] + 5 * star[k])]
            if not cand: break
            i = min(cand, key=lambda k: lv[k]); c = lvcost(lv[i])
            if not c or coin < c[0] or parts[fam[i]] < c[1]: break
            coin -= c[0]; parts[fam[i]] -= c[1]; lv[i] += 1
        mine = sum(pw(a, b) for a, b in zip(lv, star)) / 6 * (1 + gear)
        while best < len(stages):
            s = stages[best]
            if mine < pw(s["refLv"], s["refStar"]) * (1 + s["refGear"]) * .98: break
            best += 1; reached[best] = day
            for g in qmain.get(best, []):
                for r in rg.get(g, []):
                    if r["kind"] != 1: continue
                    if r["refId"] == 1: coin += r["max"]
                    elif r["refId"] == 2: cry += r["max"]
                    elif r["refId"] == 401: stones += r["max"]
                    elif 101 <= r["refId"] <= 104: parts[r["refId"] - 101] += r["max"]
            nh = best - 25
            if 1 <= nh <= 50: hb = nh % 5 == 0; cry += 50 if hb else 20; stones += 20 if hb else 10; parts = [x + (20 if hb else 0) for x in parts]   # 10.21-h 심화
            cry += ACH_BEST.get(best, 0) + (CODEX_PER_STAGE if best <= 60 else 0)
            mc, mco, mst = MAIL_BEST.get(best, (0, 0, 0)); cry += mc; coin += mco; stones += mst   # 10.21-c 업적(스테이지) · 먼지 도감 발견(대략 진행에 따라)
            for r in rg.get(s["firstRewardGroup"], []):
                if r["kind"] != 1: continue
                if r["refId"] == 1: coin += r["max"]
                elif r["refId"] == 2: cry += r["max"]
                elif r["refId"] == 401: stones += r["max"]
                elif 101 <= r["refId"] <= 104: parts[r["refId"] - 101] += r["max"]
        day += 1
    return reached, lv, star


if __name__ == "__main__":
    reached, lv, star = main()
    print("스테이지 → 도달 일차(무과금 기준)")
    for n in (5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75):
        print(f"  {n:>2}: {reached.get(n, '—')}일째")
    print("마지막 편성 레벨", lv, "돌파", star)
