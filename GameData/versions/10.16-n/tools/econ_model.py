"""무과금 기준 경제 모델 (10.16-f) — 레벨·돌파 비용과 수입으로 '몇 일째에 몇 스테이지'를 추정한다.

    python tools/econ_model.py

data/*.json 을 읽어서(먼저 gen_tables.py 또는 build.py 실행) 하루 단위로 굴린다.
가정(플레이어): 하루 자동 청소 12시간 + 광고 무료 보상 + 대청소 3판 + 출석, 크리스탈은 75% 가전 소환 · 나머지 장비 소환,
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


def main(seed=1):
    random.seed(seed)
    cfg = J("config"); stages = sorted(J("stage"), key=lambda s: s["order"]); lvt = J("unit_levelup"); stars = sorted(J("unit_star"), key=lambda r: r["star"])
    rg = {}
    for r in J("reward_group"): rg.setdefault(r["groupId"], []).append(r)
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
        coin += st["idleCoin"] * HOURS + 9000 + 2000 + 2140
        cry += st["idleCrystal"] * HOURS + 90 + 150 + 50 + 40
        for f in range(4): parts[f] += st["idlePart"] * HOURS + 10 + 30 + 1.4
        stones += 36
        pulls = int(cry * PULL_SHARE // 150); cry -= pulls * 150; cry -= int(cry // 100) * 100
        for i in range(6):
            p = 0.8 / 4 if rar[i] == "R" else 0.17 / 5
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
