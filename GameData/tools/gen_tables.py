"""오퍼레이션: 더스트 제로 데이터 테이블 생성기 (공식으로 엑셀과 JSON을 처음부터 다시 만든다).

주의: 실행하면 tables/*.xlsx 를 덮어씁니다. 평소 수정은 엑셀에서 하고 tools/build.py 를 쓰세요.


수치 공식은 이 파일 상단 상수에서만 바꾼다. 실행하면 ../data/*.json 을 전부 다시 쓴다.
테이블 규약
  - 모든 테이블의 PK 컬럼 이름은 id (행이 여러 개인 그룹 테이블은 groupId + seq)
  - enum 은 enums.json 하나가 유일한 원본. 다른 테이블은 정수값만 쓴다
  - 사람이 읽는 문자열은 string_ko.json 에만 있고, 다른 테이블은 *Key 로 참조한다
  - 보상은 reward_group(kind/refId/min/max/prob), 비용은 (costItem/costCount) 형식으로 통일
"""
import json, math, os

OUT = os.path.join(os.path.dirname(__file__), '..', 'data')
os.makedirs(OUT, exist_ok=True)

# ---------------- 공식 상수 ----------------
CHAPTERS = 3
STAGES_PER_CHAPTER = 5
# 스테이지별 적 HP 배율. 밸런스 시뮬레이터(기준 덱·기준 레벨 Lv≈스테이지 순번)로 목표 남은 청결도에 맞춰 보정한 값
# 순서: 1-1 ~ 3-5. 웨이브 구성·입구 배치가 스테이지마다 달라서 숫자가 단조 증가하지는 않는다
STAGE_HP_MUL = [0.84, 0.79, 0.9, 0.86, 1.36, 0.94, 1.22, 1.02, 1.05, 0.98, 0.95, 1.15, 1.16, 1.27, 1.25]   # 10.14-k: 강화·멀티탭·후반 HP 상승 반영 재보정(1-1 튜토리얼은 유지)
# 스테이지별 시작 전력(1 = 100W, 0 = config.startPower). 1-1은 첫 먼지(3초)가 들어올 때 딱 800W가 되도록 500W
STAGE_START_POWER = {101: 5}
COUNT_GROWTH_IN_CHAPTER = 0.08                     # 챕터 안에서 스테이지마다 적 수 +8%
IDLE_COIN_BASE, IDLE_COIN_GROWTH = 500, 1.15       # 시급 코인 = 500 * 1.15^(n-1)
IDLE_PART_BASE, IDLE_PART_GROWTH = 3, 1.12         # 시급 부품(계열마다) = 3 * 1.12^(n-1)
IDLE_CRYSTAL_BASE, IDLE_CRYSTAL_STEP = 5, 1        # 시급 크리스탈 = 5 + 1 * (챕터-1)  (10.15-b: 상점 효율에 맞춰 15→5 하향)
FIRST_COIN_BASE = 1000

ENUMS = {
    "Rarity": {"R": 1, "SR": 2, "SSR": 3},
    "Family": {"Living": 1, "Kitchen": 2, "Digital": 3, "Season": 4},
    "Tile": {"Ground": 1, "Furniture": 2},
    "RewardKind": {"Item": 1, "Unit": 2},
    "CondKind": {"None": 0, "StageClear": 1},
    "TargetType": {"Ground": 1, "Air": 2, "All": 3},
    "EnemyAI": {"Detour": 1, "Breaker": 2},
    "TutTrigger": {"Start": 1, "Next": 2, "Time": 3, "Spawn": 4, "Power": 5, "Heat": 6, "Skill": 7, "Boss": 8, "Leak": 9},
    "TutUntil": {"Tap": 1, "Select": 2, "Deploy": 3, "Timer": 4},
    "ShopTab": {"Crystal": 1, "Daily": 2, "Package": 3},
    "PayKind": {"Iap": 1, "Crystal": 2, "Ad": 3},
    "LimitType": {"None": 0, "Daily": 1, "Weekly": 2, "Account": 3},
}
R, F, T = ENUMS["Rarity"], ENUMS["Family"], ENUMS["Tile"]

S = {}  # string_ko
def s(key, text): S[key] = text; return key

# ---------------- item ----------------
items = [
    {"id": 1, "nameKey": s("item.1.name", "코인"), "family": 0},
    {"id": 2, "nameKey": s("item.2.name", "크리스탈"), "family": 0},
    {"id": 101, "nameKey": s("item.101.name", "필터"), "family": F["Living"]},
    {"id": 102, "nameKey": s("item.102.name", "열선"), "family": F["Kitchen"]},
    {"id": 103, "nameKey": s("item.103.name", "칩"), "family": F["Digital"]},
    {"id": 104, "nameKey": s("item.104.name", "모터"), "family": F["Season"]},
]
PART_OF_FAMILY = {F["Living"]: 101, F["Kitchen"]: 102, F["Digital"]: 103, F["Season"]: 104}
for k, v in {1: "생활가전", 2: "주방가전", 3: "디지털", 4: "계절가전"}.items(): s(f"family.{k}.name", v)

# ---------------- unit ----------------
U = [
  # id, rarity, family, tile, cost, heat, atk, itv, range, block, sp, dur, cool, name, short, role, skill, desc, lines
  ("vac", "R", "Living", "Ground", 8, 1000, 75, 1.0, 1.1, 2, 18, 4, 0, "청소기", "청소기", "근접 · 저지 2", "강력 흡입",
   "지상 적이 반경 {pullRadius}칸에 들어오면 발동. {dur}초간 주변 지상 적을 빨아들여 붙잡고 초당 {pullDps} 피해를 줍니다.",
   ["먼지 한 톨도 못 지나가!", "흡입력엔 자신 있어요.", "오늘도 바닥은 내가 지킨다.", "먼지통 비워주실래요?"]),
  ("robot", "R", "Living", "Ground", 5, 550, 40, 0.7, 1.1, 1, 15, 5, 0, "로봇청소기", "로봇", "근접 · 저지 1", "급속 충전",
   "과열이 {triggerHeat_pct}% 이상이면 발동. 과열을 0으로 비우고 {dur}초간 공격력이 +{skillAtkBonus_pct}% 올라요. 싸고 가벼운 저지 담당.",
   ["삐빅. 순찰 중입니다.", "충전 다 했어요!", "구석까지 다 돌았어요.", "양말은 치워주세요… 걸려요."]),
  ("fridge", "SSR", "Kitchen", "Ground", 16, 2400, 55, 1.1, 1.1, 3, 24, 5, 0, "냉장고", "냉장고", "근접 · 저지 3", "급속 냉동",
   "적이 반경 {freezeRadius}칸에 들어오면 발동. 자신의 과열을 {selfCool_pct}% 식히고, {dur}초간 주변 적을 얼려 이동과 공격을 멈추며 초당 {freezeDps} 피해.",
   ["차갑다고요? 원래 이래요.", "열기는 제가 다 식혀드릴게요.", "문 너무 오래 열어두지 마세요.", "유통기한 지난 거 있어요."]),
  ("ac", "SR", "Season", "Furniture", 12, 0, 45, 1.0, 2.6, 0, 22, 6, 0, "에어컨", "에어컨", "원거리 · 둔화", "급속 냉방",
   "{dur}초간 {tick}초마다 사거리 안 적에게 냉기 피해 {skillDmg}을 주고 {skillSlow_pct}% 둔화시켜요. 평타도 {atkSlow_pct}% 둔화를 겁니다.",
   ["실내 온도 24도, 쾌적합니다.", "더위는 맡겨주세요.", "필터 청소 좀 해주실래요?", "제습 모드도 할 줄 알아요."]),
  ("micro", "SR", "Kitchen", "Furniture", 14, 0, 170, 2.4, 2.4, 0, 20, 2, 0, "전자레인지", "레인지", "원거리 · 단일 고화력", "띵!",
   "{dur}초간 데운 뒤 가장 앞선 적 주변 {blastRadius}칸을 폭발시켜 {blastDmg} 피해. 데우는 동안은 평타를 쉬어요.",
   ["띵! 다 데워졌어요.", "30초만 기다려줘요.", "금속은 넣으면 안 돼요!", "돌아가는 건 제 취미예요."]),
  ("washer", "SSR", "Living", "Furniture", 15, 0, 70, 1.4, 2.2, 0, 22, 5, 0, "세탁기", "세탁기", "원거리 · 광역", "탈수 회오리",
   "{dur}초간 {tick}초마다 사거리 안 적에게 {skillDmg} 피해를 주고 {knockback}칸 밀어냅니다. 평타는 착탄 지점 {splashRadius}칸 광역.",
   ["빙글빙글, 탈수 시작!", "얼룩은 전부 빼드릴게요.", "헹굼 한 번 더 할까요?", "양말 한 짝은 제가 안 먹었어요."]),
  ("pc", "SR", "Digital", "Furniture", 10, 0, 0, 0, 1.6, 0, 25, 10, 0, "컴퓨터", "컴퓨터", "지원 · 버프", "오버클럭",
   "{dur}초간 버프 범위가 {skillRange}칸으로 넓어지고 주변 가전 공격력 +{skillAtk_pct}%, 공격 속도 {speedMul}배. 평소엔 {buffRange}칸 안 가전 공격력 +{buffAtk_pct}%.",
   ["업데이트는 나중에 할게요.", "오버클럭 준비 완료.", "작업 관리자는 열지 마세요.", "팬 소리는 신경 쓰지 마세요."]),
  ("fan", "R", "Season", "Furniture", 9, 0, 0, 0, 1.6, 0, 20, 6, 60, "선풍기", "선풍기", "지원 · 냉각", "터보 바람",
   "주변 가전 과열이 {triggerHeat_pct}% 이상이면 발동. 과열을 즉시 {skillBurst_pct}% 식히고 {dur}초간 냉각 {skillCoolMul}배. 평소엔 {coolRange}칸 안 가전을 초당 {cool}씩 식혀요.",
   ["바람 필요하세요?", "회전 모드 켤까요?", "강풍은 조심하세요!", "아아아— 목소리 떨리죠?"]),
  ("gen", "R", "Digital", "Furniture", 6, 0, 0, 0, 1.0, 0, 20, 6, 0, "발전기", "발전기", "지원 · 전력", "풀가동",
   "배치해 두면 전력 회복이 초당 +{regenPerSec_w}W 늘어요. 스킬: 전력 {burst_w}W를 즉시 채우고 {dur}초간 추가 회복이 {skillRegenMul}배가 됩니다.",
   ["전력 공급 시작합니다!", "콘센트는 충분하죠?", "풀가동 들어갑니다!", "기름 냄새는 신경 쓰지 마세요."]),
]
TT = ENUMS["TargetType"]
TARGETS = {"vac": ("Ground", "Ground"), "robot": ("Ground", "Ground"), "fridge": ("Ground", "All"),
           "ac": ("All", "All"), "micro": ("All", "All"), "washer": ("All", "All"), "pc": ("All", "All"), "fan": ("All", "All"), "gen": ("All", "All")}
units = []
for i, (uid, rar, fam, tile, cost, heat, atk, itv, rng, block, sp, dur, cool, name, short, role, skill, desc, lines) in enumerate(U):
    for j, ln in enumerate(lines): s(f"unit.{uid}.line{j+1}", ln)
    units.append({"id": uid, "order": i + 1, "rarity": R[rar], "family": F[fam], "tile": T[tile],
                  "cost": cost, "heat": heat, "atk": atk, "itv": itv, "range": rng, "block": block,
                  "sp": sp, "dur": dur, "cool": cool, "lineCount": len(lines),
                  "atkTarget": TT[TARGETS[uid][0]], "skillTarget": TT[TARGETS[uid][1]],
                  "nameKey": s(f"unit.{uid}.name", name), "shortKey": s(f"unit.{uid}.short", short),
                  "roleKey": s(f"unit.{uid}.role", role), "skillKey": s(f"unit.{uid}.skill", skill),
                  "descKey": s(f"unit.{uid}.desc", desc)})

# 가전 조각: 가전마다 하나(item id = 300 + 도감 순서). 중복 소환·상점·대청소·패스로 모아 돌파하거나, 없는 가전은 영입
SHARD_ID = {}
for u in units:
    sid = 300 + u["order"]; SHARD_ID[u["id"]] = sid
    items.append({"id": sid, "nameKey": s(f"item.{sid}.name", f"{S[u['nameKey']]} 조각"), "family": 0, "unitId": u["id"]})
# 돌파(unit_star): ★n 으로 올리는 데 드는 조각·코인, 돌파 후 레벨 상한. ★0 상한은 config lvCapBase
unit_star = [{"star": n, "shards": sh, "coin": co, "lvCap": cap} for n, sh, co, cap in
             [(1, 10, 2000, 25), (2, 20, 5000, 30), (3, 30, 10000, 35), (4, 40, 20000, 40), (5, 50, 40000, 45)]]

# 레벨업: L -> L+1 비용 = (코인, 그 가전 계열 부품) 2단 비용
# 스킬·평타 부가 계수. 코드는 여기 있는 값만 읽는다. (unitId, key, value, 설명)
SKILL_PARAM = {
    "vac": [("pullRadius", 2.5, "강력 흡입 반경(칸). 이 안에 지상 적이 있으면 발동"), ("pullSpeed", 3.2, "끌어당기는 속도(칸/초)"),
            ("pullDps", 50, "흡입 중 초당 피해(성장 배율 적용)"), ("holdDist", 0.6, "이 거리 안으로 끌려오면 저지 정원과 무관하게 붙잡음")],
    "robot": [("triggerHeat", 0.5, "자기 과열이 이 비율 이상이면 발동"), ("skillAtkBonus", 1.0, "스킬 중 공격력 증가(+100%)")],
    "fridge": [("freezeRadius", 2.0, "급속 냉동 반경(칸)"), ("freezeDps", 22, "빙결 중 초당 피해"), ("selfCool", 0.5, "발동 즉시 자기 과열 감소 비율")],
    "ac": [("atkSlow", 0.35, "평타 둔화율"), ("atkSlowSec", 1.5, "평타 둔화 시간(초)"), ("tick", 0.5, "급속 냉방 피해 간격(초)"),
           ("skillDmg", 30, "급속 냉방 1회 피해"), ("skillSlow", 0.6, "급속 냉방 둔화율"), ("skillSlowSec", 2.0, "급속 냉방 둔화 시간(초)")],
    "micro": [("blastRadius", 1.2, "띵! 폭발 반경(칸)"), ("blastDmg", 450, "띵! 폭발 피해"), ("rangeBonus", 0.5, "폭발 대상 탐색 추가 사거리"),
              ("noTargetRefund", 0.6, "충전이 끝났을 때 대상이 없으면 돌려받는 스킬 게이지 비율")],
    "washer": [("splashRadius", 0.9, "평타 착탄 광역 반경(칸)"), ("tick", 0.5, "탈수 회오리 피해 간격(초)"), ("skillDmg", 35, "탈수 회오리 1회 피해"),
               ("knockback", 0.3, "밀어내는 거리(칸)"), ("bossKnockback", 0.12, "보스를 밀어내는 거리(칸)")],
    "pc": [("buffRange", 1.6, "평소 버프 범위(칸)"), ("buffAtk", 0.25, "평소 공격력 증가"), ("skillRange", 2.4, "오버클럭 범위(칸)"),
           ("skillAtk", 0.6, "오버클럭 공격력 증가"), ("skillIntervalMul", 0.5, "오버클럭 중 공격 간격 배율(0.5 = 공속 2배)")],
    "fan": [("coolRange", 1.6, "평소 냉각 범위(칸). 초당 냉각량은 unit.cool"), ("skillRange", 2.2, "터보 바람 범위(칸)"),
            ("skillCoolMul", 3.0, "터보 바람 중 냉각 배율"), ("skillBurst", 0.4, "발동 즉시 과열 감소 비율"),
            ("triggerHeat", 0.4, "범위 안 가전 과열이 이 비율 이상이면 발동")],
    "gen": [("regenPerSec", 0.35, "배치 중 추가 전력 회복(초당, 1 = 100W)"), ("burst", 8, "풀가동 즉시 충전량(1 = 100W)"),
            ("skillRegenMul", 2.0, "풀가동 중 추가 회복 배율")],
}
# ---------------- unit_boost (전투 중 강화) ----------------
# 놓인 가전을 눌러 남는 전력으로 그 판에서만 강화한다. 1단계(배치) → 2단계 → 3단계
#   costMul : 강화 비용 = unit.cost × costMul (1 = 100W)
#   statMul : 배치 때 대비 공격력·스킬 피해·과열 한도·냉각량·발전량·버프량 배율(누적이 아니라 그 단계의 최종 배율)
#   blockAdd / rangeAdd : 저지 수·사거리 추가,  spMul : 스킬 충전 시간 배율
UNIT_BOOST = {
    # 2단계 (costMul, statMul, blockAdd, rangeAdd, spMul) / 3단계
    "vac":    [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 1, 0, 0.9)],
    "robot":  [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 1, 0, 0.9)],
    "fridge": [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 1, 0, 0.9)],
    "ac":     [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 0, 0.4, 0.9)],
    "micro":  [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 0, 0.4, 0.9)],
    "washer": [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 0, 0.4, 0.9)],
    "pc":     [(1.0, 1.25, 0, 0, 1.0), (1.5, 1.5, 0, 0.4, 0.8)],
    "fan":    [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 0, 0.4, 0.8)],
    "gen":    [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 0, 0, 0.8)],
}
unit_boost = [{"unitId": uid, "tier": t + 2, "costMul": r[0], "statMul": r[1], "blockAdd": r[2], "rangeAdd": r[3], "spMul": r[4]}
              for uid, rows in UNIT_BOOST.items() for t, r in enumerate(rows)]
skill_param = [{"unitId": uid, "key": k, "value": v, "note": n} for uid, rows in SKILL_PARAM.items() for (k, v, n) in rows]

# 스킬 설명은 {pullRadius} 같은 자리표시자를 가진 템플릿으로 저장한다. 게임이 skill_param·unit 값으로 채운다
def _num(v):
    return str(int(v)) if float(v).is_integer() else str(round(v, 2))
for u in units:
    ctx = {"dur": _num(u["dur"]), "cool": _num(u["cool"])}
    for (k, v, n) in SKILL_PARAM.get(u["id"], []):
        ctx[k] = _num(v); ctx[k + "_pct"] = _num(v * 100); ctx[k + "_w"] = _num(v * 100)
    if u["id"] == "pc":
        ctx["speedMul"] = _num(1 / dict((k, v) for k, v, n in SKILL_PARAM["pc"])["skillIntervalMul"])
    S[u["descKey"]].format(**ctx)  # 템플릿 검증만 한다. 실제 문구는 게임이 skill_param 값으로 채운다

unit_levelup = [
    {"id": 1, "lvMin": 1, "lvMax": 9, "coinBase": 100, "coinStep": 100, "partBase": 2, "partStep": 1},
    {"id": 2, "lvMin": 10, "lvMax": 19, "coinBase": 1200, "coinStep": 150, "partBase": 12, "partStep": 2},
    {"id": 3, "lvMin": 20, "lvMax": 29, "coinBase": 3000, "coinStep": 250, "partBase": 35, "partStep": 3},
    {"id": 4, "lvMin": 30, "lvMax": 44, "coinBase": 6000, "coinStep": 400, "partBase": 65, "partStep": 4},
]

# ---------------- enemy ----------------
E = [
    ("dust", "먼지뭉치", 160, 1.0, 35, 1.0, .22, 1, 0, "dust", 0),
    ("grease", "기름때", 520, .6, 80, 1.0, .28, 1, 0, "grease", 0),
    ("mold", "곰팡이", 700, .45, 90, 1.2, .30, 1, 0, "mold", 0),
    ("fly", "초파리", 110, 1.5, 0, 1.0, .17, 1, 1, "fly", 0),
    ("spore", "포자 곰팡이", 420, .5, 60, 1.2, .26, 1, 0, "spore", 0),   # 대청소 모드 전용 원거리 적
    ("boss_dust", "먼지 대왕", 3200, .35, 180, 1.5, .42, 2, 0, "dust", 1),
    ("boss_grease", "기름 대왕", 3400, .35, 200, 1.5, .42, 2, 0, "grease", 1),
    ("boss_mold", "곰팡이 여왕", 3800, .30, 200, 1.6, .44, 2, 0, "mold", 1),
]
# 이동 성향: Detour = 돌아갈 길이 있으면 우회하고 막혔을 때만 공격, Breaker = 길을 막은 가전이 있으면 그대로 부수며 직진
ENEMY_AI = {"dust": "Detour", "grease": "Breaker", "mold": "Breaker", "fly": "Detour", "spore": "Breaker",
            "boss_dust": "Breaker", "boss_grease": "Breaker", "boss_mold": "Breaker"}
enemies = [{"id": e[0], "nameKey": s(f"enemy.{e[0]}.name", e[1]), "hp": e[2], "spd": e[3], "dmg": e[4], "itv": e[5],
            "radius": e[6], "weight": e[7], "fly": e[8], "look": e[9], "boss": e[10],
            "ai": ENUMS["EnemyAI"][ENEMY_AI[e[0]]]} for e in E]

# ---------------- map ----------------
# 입구 3곳. 경로는 목표물 바로 앞 칸에서만 합쳐진다(합류 칸과 입구 앞 2칸은 배치 불가).
# 타일은 경로에서 자동 생성: 경로=P, 목표=E, 경로와 맞닿은 칸=H(가구), 나머지=X. 특수 칸은 overrides로 덮어쓴다.
def build_tiles(paths, overrides, holes=()):
    g = [["X"] * 7 for _ in range(10)]
    cells = set()
    for wp in paths:
        for (x1, y1), (x2, y2) in zip(wp, wp[1:]):
            dx, dy = (x2 > x1) - (x2 < x1), (y2 > y1) - (y2 < y1)
            x, y = x1, y1
            cells.add((x, y))
            while (x, y) != (x2, y2):
                x += dx; y += dy; cells.add((x, y))
    for (x, y) in cells: g[y][x] = "P"
    gx, gy = paths[0][-1]; g[gy][gx] = "E"
    for y in range(10):
        for x in range(7):
            if g[y][x] == "X" and any((x + a, y + b) in cells for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                g[y][x] = "H"
    for (x, y) in holes: g[y][x] = "X"
    for (x, y), ch in overrides.items(): g[y][x] = ch
    return ["".join(r) for r in g]

MAP_DEF = [
    ("living", "wood", "거실", ["창문", "베란다", "현관"], "소파", "",
     "창문, 베란다, 현관 세 곳으로 먼지가 들어와요. 웨이브마다 열리는 입구가 바뀌니 경고를 보고 대응하세요.",
     [[[0, 2], [1, 2], [1, 8], [3, 8], [3, 9]],
      [[3, 0], [3, 3], [2, 3], [2, 6], [3, 6], [3, 9]],
      [[6, 4], [5, 4], [5, 8], [3, 8], [3, 9]]], {}, [(6, 0), (0, 9), (6, 9)]),
    ("kitchen", "kitchen", "주방", ["환풍구", "뒷문", "창문"], "식탁", "가스레인지 칸의 근접 가전은 초당 과열 +40",
     "환풍구, 뒷문, 창문으로 기름때가 쏟아져요. 기름때는 단단하고 과열을 빨리 쌓습니다.",
     [[[3, 0], [3, 1], [2, 1], [2, 9]],
      [[0, 5], [1, 5], [1, 8], [2, 8], [2, 9]],
      [[6, 3], [5, 3], [5, 6], [3, 6], [3, 8], [2, 8], [2, 9]]], {(2, 4): "F", (4, 6): "F"}, [(6, 0), (6, 9)]),
    ("bath", "bath", "욕실", ["환풍기", "배수구", "욕실 문"], "욕조", "물기 칸: 적 이동 +35%, 그 칸 근접 가전은 냉각 +60",
     "배수구는 방 한가운데 있어요. 환풍기와 문까지 세 곳에서 곰팡이 떼가 올라옵니다.",
     [[[5, 0], [5, 8], [4, 8], [4, 9]],
      [[1, 3], [1, 7], [3, 7], [3, 8], [4, 8], [4, 9]],
      [[2, 0], [2, 2], [3, 2], [3, 5], [4, 5], [4, 9]]], {(5, 4): "W", (4, 6): "W", (1, 6): "W"}, [(0, 9), (6, 9)]),
]
maps = []
for mid, floor, name, spawns, goal, gim, desc, paths, ov, holes in MAP_DEF:
    maps.append({"id": mid, "floor": floor, "nameKey": s(f"map.{mid}.name", name),
                 "spawnKeys": [s(f"map.{mid}.spawn{i+1}", sp) for i, sp in enumerate(spawns)],
                 "goalKey": s(f"map.{mid}.goal", goal), "gimKey": s(f"map.{mid}.gim", gim) if gim else "",
                 "descKey": s(f"map.{mid}.desc", desc), "tiles": build_tiles(paths, ov, holes), "paths": paths,
                 "noDeployFromEntrance": 2, "noDeployBeforeGoal": 1})
CHAPTER_MAP = {1: "living", 2: "kitchen", 3: "bath"}
MAP_PATHS = {m[0]: m[7] for m in MAP_DEF}
CHAPTER_BOSS = {1: "boss_dust", 2: "boss_grease", 3: "boss_mold"}
# 챕터별 기본 웨이브 (t초, 적, 마릿수, 간격초, 입구 1~3). 입구 번호는 '열리는 순서'이고 실제 입구는 스테이지마다 바뀐다.
# 웨이브는 5~6초마다 시작해 쉬는 시간이 거의 없다. 처음 18초는 입구 1곳, 30초부터 3곳 모두.
# 같은 웨이브 안 간격은 0.8초 이상: 더 촘촘하면 단일 공격 가전의 공격 속도로는 다 맞히지 못해 체력과 무관하게 샌다.
BASE_WAVE = {
    1: [(2, "dust", 3, 1.2, 1), (8, "dust", 4, 1.0, 1), (14, "dust", 4, 1.0, 1), (19, "fly", 3, 1.0, 2),
        (24, "dust", 4, 1.0, 1), (25, "mold", 1, 0, 2), (30, "fly", 3, .9, 3), (31, "dust", 4, 1.0, 2),
        (36, "dust", 5, .9, 3), (37, "mold", 1, 0, 1), (42, "dust", 5, .9, 1), (43, "fly", 3, .9, 2),
        (48, "dust", 5, .9, 3), (49, "mold", 2, 2.5, 2), (54, "dust", 6, .8, 1), (55, "fly", 3, .9, 3)],
    2: [(2, "dust", 4, 1.0, 1), (8, "dust", 4, 1.0, 1), (14, "grease", 1, 0, 1),
        (19, "dust", 5, .9, 2), (20, "fly", 2, 1.0, 1), (24, "fly", 3, .9, 2), (25, "dust", 4, .9, 1), (30, "grease", 2, 2.5, 3), (31, "dust", 4, .9, 2),
        (36, "dust", 5, .9, 3), (37, "fly", 3, .9, 1), (42, "grease", 2, 2.2, 2), (43, "dust", 5, .9, 1),
        (48, "dust", 5, .8, 3), (49, "fly", 3, .8, 2), (54, "grease", 2, 2.0, 1), (55, "dust", 5, .8, 2),
        (60, "dust", 6, .8, 3), (61, "fly", 3, .8, 1)],
    3: [(2, "dust", 4, 1.0, 1), (7, "mold", 1, 0, 1), (8, "dust", 3, 1.0, 1), (13, "dust", 3, .9, 1),
        (18, "dust", 4, .9, 2), (19, "mold", 1, 0, 1), (20, "fly", 3, .9, 1), (24, "fly", 3, .9, 2), (25, "dust", 4, .9, 1),
        (30, "mold", 2, 2.5, 3), (31, "dust", 4, .9, 2), (36, "fly", 4, .8, 3), (37, "dust", 4, .9, 1),
        (42, "mold", 2, 2.2, 2), (43, "dust", 5, .8, 3), (48, "fly", 4, .8, 1), (49, "dust", 5, .8, 2),
        (54, "mold", 2, 2.0, 3), (55, "dust", 5, .8, 1), (60, "fly", 4, .8, 2), (61, "dust", 6, .8, 3), (66, "mold", 2, 2.0, 1)],
}
# 1-1은 튜토리얼: 30초대로 짧고 가볍게, 입구 2곳만
STAGE_WAVE_OVERRIDE = {
    101: [(3, "dust", 3, 1.0, 1), (9, "dust", 4, .9, 1), (15, "dust", 3, .9, 1), (20, "fly", 2, .9, 2),
          (26, "dust", 4, .8, 1), (27, "fly", 2, .8, 2), (32, "mold", 1, 0, 1)],
}
LATE_RAMP_START, LATE_RAMP_HP = 0.4, 2.0   # 마지막 웨이브 시각의 40%부터 HP가 올라 마지막 웨이브는 ×3.0 (wave.hpMul)
ENEMY_BOSS = {"boss_dust": 1, "boss_grease": 1, "boss_mold": 1}
BOSS_DELAY, BOSS_ESCORT = 5, (4, 1.0)   # 마지막 웨이브 시작 후 보스 등장까지(초), 호위 먼지(마릿수, 간격)

# ---------------- reward_group / condition / stage / wave ----------------
reward_group, conditions, stages, waves = [], [], [], []
def rg(group_id, rows):
    for i, (kind, ref, mn, mx, prob) in enumerate(rows):
        reward_group.append({"groupId": group_id, "seq": i + 1, "kind": kind, "refId": ref, "min": mn, "max": mx, "prob": prob})
    return group_id
ITEM, UNIT = ENUMS["RewardKind"]["Item"], ENUMS["RewardKind"]["Unit"]

n = 0
prev_stage = 0
for ch in range(1, CHAPTERS + 1):
    s(f"chapter.{ch}.name", {1: "거실 사수", 2: "주방 탈환", 3: "욕실 대청소"}[ch])
    for k in range(1, STAGES_PER_CHAPTER + 1):
        n += 1
        sid = ch * 100 + k
        boss = k == STAGES_PER_CHAPTER
        cond_id = 0
        if prev_stage:
            cond_id = 1000 + n
            conditions.append({"id": cond_id, "kind": ENUMS["CondKind"]["StageClear"], "p1": prev_stage, "p2": 0})
        # 웨이브
        cm = 1 + COUNT_GROWTH_IN_CHAPTER * (k - 1)
        # 입구 배정: 처음 열리는 입구(웨이브 표의 입구 1)는 경로가 긴 두 곳을 번갈아 쓰고, 나머지 둘은 스테이지마다 순서를 바꾼다
        plen = [sum(abs(x2 - x1) + abs(y2 - y1) for (x1, y1), (x2, y2) in zip(wp, wp[1:])) for wp in MAP_PATHS[CHAPTER_MAP[ch]]]
        by_len = sorted(range(3), key=lambda i: -plen[i])
        opener = by_len[(k - 1) % 2]
        rest = [i for i in range(3) if i != opener]
        if ((k - 1) // 2) % 2: rest.reverse()
        perm = [opener] + rest
        rot = lambda e, perm=perm: perm[e - 1] + 1
        if sid in STAGE_WAVE_OVERRIDE:
            rows = list(STAGE_WAVE_OVERRIDE[sid])
        else:
            rows = [(t, en, max(1, round(c * cm)), g, rot(e)) for (t, en, c, g, e) in BASE_WAVE[ch]]
        if boss:
            last = max(t for (t, en, c, g, e) in rows)
            rows.append((last + BOSS_DELAY, CHAPTER_BOSS[ch], 1, 0, rot(2)))
            rows.append((last + BOSS_DELAY + 2, "dust", BOSS_ESCORT[0], BOSS_ESCORT[1], rot(1)))
            rows.append((last + BOSS_DELAY + 2, "dust", BOSS_ESCORT[0], BOSS_ESCORT[1], rot(3)))
        # 후반 압박: 웨이브 시각이 뒤로 갈수록 적 HP를 올린다(배치가 다 찬 뒤 남는 전력을 강화에 쓰게 만드는 장치)
        t_last = max(t for (t, en, c, g, e) in rows if not ENEMY_BOSS.get(en))
        for seq, (t, en, c, g, e) in enumerate(rows):
            x = max(0.0, (t - LATE_RAMP_START * t_last) / ((1 - LATE_RAMP_START) * t_last)) if sid not in STAGE_WAVE_OVERRIDE else 0
            hm = 1.0 if ENEMY_BOSS.get(en) else round(1 + LATE_RAMP_HP * min(1.0, x) ** 1.5, 2)
            waves.append({"groupId": sid, "seq": seq + 1, "t": t, "enemy": en, "count": c, "gap": g, "entrance": e, "hpMul": hm})
        # 첫 클리어 보상
        first_rows = [(ITEM, 1, FIRST_COIN_BASE + 400 * (n - 1), FIRST_COIN_BASE + 400 * (n - 1), 10000),
                      (ITEM, 2, 150 if boss else 50, 150 if boss else 50, 10000)]   # 첫 클리어 크리스탈(10.15-b: 100/300 → 50/150)
        if boss:
            first_rows += [(ITEM, p, 15, 15, 10000) for p in (101, 102, 103, 104)]
        first_group = rg(10000 + sid, first_rows)
        stages.append({
            "id": sid, "order": n, "chapter": ch, "no": f"{ch}-{k}", "mapId": CHAPTER_MAP[ch], "waveGroup": sid,
            "boss": 1 if boss else 0, "hpMul": STAGE_HP_MUL[n - 1],
            "unlockCond": cond_id, "firstRewardGroup": first_group,
            "idleCoin": int(round(IDLE_COIN_BASE * IDLE_COIN_GROWTH ** (n - 1), -1)),
            "idlePart": int(round(IDLE_PART_BASE * IDLE_PART_GROWTH ** (n - 1))),
            "idleCrystal": IDLE_CRYSTAL_BASE + IDLE_CRYSTAL_STEP * (ch - 1),
            "startPower": STAGE_START_POWER.get(sid, 0),
        })
        prev_stage = sid

# 고정 보상 그룹
rg(9000, [(ITEM, 1, 2000, 2000, 10000), (ITEM, 2, 1500, 1500, 10000)] + [(ITEM, p, 20, 20, 10000) for p in (101, 102, 103, 104)]
   + [(UNIT, "vac", 1, 1, 10000), (UNIT, "ac", 1, 1, 10000), (UNIT, "fan", 1, 1, 10000), (UNIT, "gen", 1, 1, 10000)])   # 시작 지급
rg(9001, [(ITEM, 2, 50, 50, 10000)])   # (구) 매일 출석. 10.15-b부터 attendance 테이블(7일 출석부)로 대체
# 출석부: 하루 한 번 접속하면 다음 칸을 받는다(연속 아니어도 됨). 7일째 다음은 1일로 돌아감
ATTEND = [(1, [(ITEM, 2, 50, 50, 10000)]), (2, [(ITEM, 1, 5000, 5000, 10000)]), (3, [(ITEM, 2, 50, 50, 10000)]),
          (4, [(ITEM, p, 10, 10, 10000) for p in (101, 102, 103, 104)]), (5, [(ITEM, 2, 50, 50, 10000)]),
          (6, [(ITEM, 1, 10000, 10000, 10000)]), (7, [(ITEM, 2, 200, 200, 10000)])]
attendance = [{"day": d, "rewardGroup": rg(9100 + d, rows), "big": 1 if d == 7 else 0} for d, rows in ATTEND]
rg(9002, [(ITEM, 1, 1000, 1000, 10000)])                                                     # 돌파 최대 이후 중복 전환

# 챕터 별 누적 보상 (챕터당 최대 별 = 스테이지 수 x 3)
star_reward = []
STAR_MILESTONES = [(5, [(ITEM, 2, 100), (ITEM, 1, 3000)]),
                   (10, [(ITEM, p, 20) for p in (101, 102, 103, 104)]),
                   (15, [(ITEM, 2, 150)])]   # 10.15-b: 300 → 150
for ch in range(1, CHAPTERS + 1):
    for i, (need, rows) in enumerate(STAR_MILESTONES):
        gid = rg(20000 + ch * 10 + i + 1, [(k, ref, int(c * (1 + 0.5 * (ch - 1))), int(c * (1 + 0.5 * (ch - 1))), 10000) for (k, ref, c) in rows])
        star_reward.append({"id": ch * 10 + i + 1, "chapter": ch, "needStars": need, "rewardGroup": gid})

# ---------------- tutorial ----------------
# 규칙을 한 가지씩 나눠 가르친다. 스테이지가 아니라 '몇 번째 전투인지'에 묶는다:
#   cond 가 빈 그룹은 순서대로 전투 1판에 1그룹씩 열린다(이미 진행한 저장 데이터도 다음 전투부터 1그룹).
#   그 판에 조건이 안 와서 못 본 스텝은 다음 판들로 넘어가 조건이 오면 뜬다.
#   cond 가 있는 그룹(boss / map:kitchen / map:bath)은 그 조건의 전투에서 처음 한 번.
# 스텝마다 한 번만 뜨고(저장), 편성 탭에서 다시 보기 가능.
# trigger: Start 전투 시작 / Next 같은 그룹 앞 스텝 끝난 직후 / Time 경과 초 / Spawn 그 적 첫 등장 / Power 전력(1=100W) 이상
#          Heat 근접 가전 과열 비율 이상 / Skill 스킬 첫 발동(가전 ID, 비우면 아무거나) / Boss 보스 등장 / Leak 첫 누수
# until:   Tap 확인 버튼 / Select 그 카드 선택 / Deploy 그 가전 배치 / Timer 초 뒤 자동으로 닫힘
#          대상은 가전 ID 또는 역할: melee(덱의 첫 근접 가전) / ranged(덱의 첫 공중 공격 가능한 원거리 가전)
#          덱에 대상이 없으면 그 스텝은 미뤄진다. 문구의 {unit} 은 대상 가전 이름으로 바뀐다.
# pause:   1이면 안내가 떠 있는 동안 전투 시간이 멈춘다(배치·선택은 가능)
# focus:   card:가전ID 또는 card:melee/ranged / hud:life / hud:power / btn:speed / unit:heat (가장 뜨거운 근접 가전)
# afterPrev: 1이면 같은 그룹 앞 스텝을 끝낸 뒤에만 조건을 본다(앞 스텝 대상이 덱에 없으면 건너뜀)
# tile:    배치 스텝에서 그 칸에만 놓을 수 있게 고정(빛나는 칸). tileStage 스테이지에서만 적용, 다른 판에선 자유 배치
#          고정 배치 스텝 동안에는 해당 카드만 고를 수 있다
TT_, TU_ = ENUMS["TutTrigger"], ENUMS["TutUntil"]
TUT = [
    # group, cond, trigger, param, until, untilParam, pause, focus, text, tile("x,y" 배치 칸 고정), tileStage(그 스테이지에서만 칸 고정)
    # 1-1은 전력에 맞춰 딱딱 진행: 먼지 등장 = 800W(청소기) → 600W(발전기) → 1,200W(에어컨, 곧 초파리)
    (1, "", "Spawn", "dust", "Deploy", "melee", 1, "card:melee", "먼지가 들어와요! {unit} 카드를 누르고 빛나는 칸에 놓아 길목을 막아 보세요.", "1,3", 101),
    (1, "", "Next", "", "Timer", 6, 0, "", "먼지뭉치는 우회형이에요. 막힌 길은 피해 돌아가니, 놓친 먼지는 원거리 가전이 잡아야 해요.", "", 0),
    (1, "", "Power", 6, "Deploy", "gen", 1, "card:gen", "전력 600W가 모였어요. 발전기를 놓으면 전력이 더 빨리 차요. 빛나는 가구 칸에 놓아 보세요.", "0,1", 101, 1),
    (1, "", "Power", 12, "Deploy", "ranged", 1, "card:ranged", "전력 1,200W! 곧 초파리가 날아와요. 근접 가전은 공중을 못 때리니 {unit} 카드로 빛나는 칸에 놓아 잡아 보세요.", "3,4", 101, 1),
    (1, "", "Spawn", "fly", "Timer", 5, 0, "", "초파리는 날아다녀요. 공중은 원거리 가전만 칠 수 있어요.", "", 0),
    (1, "", "Skill", "", "Timer", 4, 0, "", "게이지가 차면 스킬이 저절로 나가요. 가전을 누르면 스킬 설명과 게이지를 볼 수 있어요.", "", 0),
    (1, "", "Spawn", "mold", "Deploy", "fan", 1, "card:fan", "곰팡이는 직진형(발밑 붉은 원)이라 피하지 않고 청소기를 밀어붙여 과열시켜요. 선풍기를 청소기 옆 빛나는 칸에 놓아 식혀 주세요.", "0,3", 101),
    (1, "", "Next", "", "Timer", 6, 0, "", "기본은 다 익혔어요! 이제 남은 전력으로 원하는 가전을 자유롭게 더 놓아 보세요.", "", 0),
    (1, "", "Leak", "", "Timer", 6, 0, "hud:life", "적이 목표물에 닿으면 청결도가 줄어요. 0이 되면 실패, 하나도 새지 않으면 별 3개예요.", "", 0),
    (2, "", "Time", 4, "Tap", "", 1, "hud:power", "같은 가전도 전력만 있으면 여러 대 놓을 수 있어요. 한 번에 최대 8대, 아래 멀티탭을 사면 10대까지 늘어나요.", "", 0),
    (2, "", "Skill", "gen", "Timer", 5, 0, "", "발전기 스킬 풀가동은 전력을 한 번에 채워 줘요.", "", 0),
    (2, "", "Power", 25, "Tap", "", 1, "", "전력이 남아요! 놓인 가전을 누르면 남는 전력으로 그 판에서만 강화할 수 있어요(3단계까지). 3단계는 저지 +1, 사거리 증가 같은 보너스가 붙어요.", "", 0),
    (3, "", "Heat", 0.5, "Tap", "", 1, "unit:heat", "근접 가전이 적을 막으면 과열이 쌓여요. 가득 차면 5초 동안 멈추고 막던 적을 놓쳐요.", "", 0),
    (3, "", "Next", "", "Tap", "", 1, "card:fan", "선풍기를 근접 가전 옆에 두면 주변 가전을 식혀 줘요. 과열이 쌓이는 자리 옆에 놓아 보세요.", "", 0),
    (4, "", "Time", 12, "Tap", "", 1, "", "근접 가전 아래 주황 점은 저지 정원이에요. 정원이 차면 적이 뒤에 줄을 서고, 줄 선 적도 과열을 쌓아요.", "", 0),
    (4, "", "Time", 30, "Timer", 7, 0, "", "가전을 누르면 철수할 수 있어요. 비용 절반을 돌려받고, 12초 뒤 다시 놓을 수 있어요.", "", 0),
    (4, "", "Time", 45, "Timer", 5, 0, "btn:speed", "익숙해졌다면 오른쪽 위 1× 버튼으로 2배속을 써 보세요.", "", 0),
    (10, "boss", "Boss", "", "Tap", "", 1, "", "보스는 단단하고, 목표물에 닿으면 청결도를 2 깎아요. 근접 가전으로 막아 세우고 화력을 모으세요.", "", 0),
    (11, "map:kitchen", "Start", "", "Tap", "", 1, "", "주방의 가스레인지 칸(불꽃)에 선 근접 가전은 과열이 훨씬 빨리 쌓여요. 기름때는 직진형이니 불꽃 칸을 피해 막아 보세요.", "", 0),
    (12, "map:bath", "Start", "", "Tap", "", 1, "", "욕실의 물기 칸에서는 적이 빨라지지만, 그 칸에 선 근접 가전은 더 잘 식어요.", "", 0),
]
tutorial, _seq = [], {}
for row in TUT:
    grp, cond, trg, prm, unt, uprm, pause, focus, text, tile, tstage = row[:11]
    after = row[11] if len(row) > 11 else 0
    _seq[grp] = _seq.get(grp, 0) + 1
    tid = 20000 + grp * 100 + _seq[grp]   # 2만 번대: 1-1 고정 진행판(이전 저장의 '본 안내' 기록과 겹치지 않게)
    tutorial.append({"id": tid, "group": grp, "seq": _seq[grp], "cond": cond, "trigger": TT_[trg], "param": prm, "until": TU_[unt],
                     "untilParam": uprm, "pause": pause, "focus": focus, "tile": tile, "tileStage": tstage, "afterPrev": after, "textKey": s(f"tut.{tid}", text)})

# ---------------- guide (로비·화면 안내) ----------------
# 전투 밖에서 버튼 하나만 밝혀 두고 나머지는 어둡게 막는 강제 안내. flow 단위로 순서대로 진행
#   first   : 새 계정·게임 초기화 직후 → 1-1 입장
#   levelup : 1-1 첫 클리어 직후 → 마을(로비)로 → 가전 탭 → 청소기 → 레벨 업 → 닫기 → 홈 → 1-2
# target 은 화면 요소 키(코드의 GUIDE_TARGET 표에 매핑). roster.unit:가전ID 처럼 가전을 지정할 수 있다
guide = []
for flow, rows in [
    ("first", [("home.cta", "첫 출동이에요! 거실에 먼지가 몰려오고 있어요. 1-1을 눌러 지켜 주세요."),
               ("battle.start", "준비됐으면 시작! 전투 중에는 필요한 순간마다 하나씩 알려 드릴게요.")]),
    ("levelup", [("result.lobby", "첫 청소 성공! 마을로 돌아가 가전을 키워 볼까요?"),
                 ("tab.roster", "가전 탭에서 가전을 키울 수 있어요."),
                 ("roster.unit:vac", "오늘 활약한 청소기를 눌러 보세요."),
                 ("detail.levelup", "레벨 업! 코인과 같은 계열 부품을 쓰면 공격력과 과열 한도가 올라가요."),
                 ("detail.close", "더 튼튼해졌어요. 닫고 다음 스테이지로 가 볼까요?"),
                 ("tab.home", "홈으로 돌아가요."),
                 ("home.cta", "이제 1-2 도전! 막히면 가전을 더 키우거나 자동 청소 보상을 받아 오세요.")]),
]:
    for i, (tgt, text) in enumerate(rows):
        guide.append({"flow": flow, "seq": i + 1, "target": tgt, "textKey": s(f"guide.{flow}.{i+1}", text)})

conditions.append({"id": 1, "kind": ENUMS["CondKind"]["StageClear"], "p1": 101, "p2": 0})
content_unlock = [
    {"id": "idle", "cond": 1, "lockKey": s("unlock.idle", "1-1을 클리어하면 자동 청소가 시작돼요.")},
    # 대청소 모드. 프로토타입 테스트 편의로 1-1 클리어(cond 1)에 열어 둠. 기획 의도는 1-5 클리어(cond 1006)
    {"id": "survivor", "cond": 1, "lockKey": s("unlock.survivor", "1-1을 클리어하면 대청소 모드가 열려요.")},
]

# ---------------- 대청소 모드 (서바이벌) ----------------
# 리더 1명을 조종하고, 레벨업 때마다 덱의 가전이 동료로 합류하거나 강해진다.
# surv_param: (id, slot, key, value, note). slot = leader(리더 주무기) / comp(동료) / combo(협동기) / enemy / passive
#   leader 성장: 피해 = dmg × (1 + dmgPerLv × (Lv-1)), 개수 = count + floor((Lv-1) × countPerLv)
#   comp 성장: 피해·효과 = 값 × (1 + perLv × (Lv-1))
SURV_PARAM = [
    # 리더 주무기 (Lv 1~5)
    ("vac", "leader", "radius", 1.5, "흡입 소용돌이 반경(칸)"), ("vac", "leader", "radiusPerLv", 0.18, "레벨당 반경 증가"),
    ("vac", "leader", "dps", 34, "초당 피해"), ("vac", "leader", "dmgPerLv", 0.3, "레벨당 피해 증가 비율"),
    ("robot", "leader", "count", 3, "회전 브러시 개수"), ("robot", "leader", "countPerLv", 0.5, "레벨당 개수 증가(내림)"),
    ("robot", "leader", "radius", 1.55, "회전 반경(칸). 브러시는 이 반경의 55~100% 사이를 오가며 안쪽도 쓴다"), ("robot", "leader", "dmg", 40, "브러시 1회 피해"),
    ("robot", "leader", "dmgPerLv", 0.25, "레벨당 피해 증가 비율"), ("robot", "leader", "spin", 4.0, "회전 속도(rad/초)"),
    ("robot", "leader", "hitGap", 0.4, "같은 적을 다시 때리기까지(초)"),
    ("ac", "leader", "dmg", 31, "냉기 탄 피해"), ("ac", "leader", "dmgPerLv", 0.25, "레벨당 피해 증가 비율"),
    ("ac", "leader", "cd", 0.6, "발사 간격(초)"), ("ac", "leader", "count", 1, "한 번에 쏘는 수"), ("ac", "leader", "countPerLv", 0.5, "레벨당 개수 증가(내림)"),
    ("ac", "leader", "speed", 8, "탄속(칸/초)"), ("ac", "leader", "range", 6, "사거리(칸)"), ("ac", "leader", "slow", 0.35, "둔화율"), ("ac", "leader", "slowSec", 1.5, "둔화 시간"),
    # 동료 (Lv 1~3)
    ("vac", "comp", "cd", 7, "흡입 폭풍 간격(초)"), ("vac", "comp", "radius", 2.2, "반경"), ("vac", "comp", "dur", 2, "지속(초)"), ("vac", "comp", "dps", 30, "초당 피해"), ("vac", "comp", "perLv", 0.4, "레벨당 강화"),
    ("robot", "comp", "cd", 2.6, "돌격 간격(초)"), ("robot", "comp", "dmg", 55, "돌격 피해(경로 위 모든 적)"), ("robot", "comp", "range", 4, "사거리"), ("robot", "comp", "perLv", 0.4, "레벨당 강화"),
    ("fridge", "comp", "cd", 8, "급속 냉동 간격(초)"), ("fridge", "comp", "radius", 2.2, "반경"), ("fridge", "comp", "freeze", 1.5, "빙결(초)"), ("fridge", "comp", "dmg", 40, "피해"), ("fridge", "comp", "perLv", 0.4, "레벨당 강화"),
    ("ac", "comp", "cd", 1.1, "냉기 탄 간격(초)"), ("ac", "comp", "dmg", 22, "피해"), ("ac", "comp", "slow", 0.3, "둔화율"), ("ac", "comp", "perLv", 0.4, "레벨당 강화"),
    ("micro", "comp", "cd", 3.6, "띵! 간격(초)"), ("micro", "comp", "radius", 1.4, "폭발 반경"), ("micro", "comp", "dmg", 110, "폭발 피해"), ("micro", "comp", "range", 6, "사거리"), ("micro", "comp", "perLv", 0.4, "레벨당 강화"),
    ("washer", "comp", "cd", 2.2, "거품 탄 간격(초)"), ("washer", "comp", "count", 3, "부채꼴 발사 수"), ("washer", "comp", "dmg", 24, "피해"), ("washer", "comp", "splash", 0.9, "터지는 반경"), ("washer", "comp", "perLv", 0.4, "레벨당 강화"),
    ("pc", "comp", "cdMul", 0.12, "모든 공격 간격 감소(레벨당)"), ("pc", "comp", "dmgMul", 0.08, "모든 피해 증가(레벨당)"),
    ("fan", "comp", "cd", 4, "바람 고리 간격(초)"), ("fan", "comp", "radius", 2.2, "반경"), ("fan", "comp", "knock", 1.3, "밀쳐내는 거리(칸)"), ("fan", "comp", "dmg", 14, "피해"), ("fan", "comp", "perLv", 0.4, "레벨당 강화"),
    ("gen", "comp", "pickup", 0.4, "먼지 조각 흡입 범위 증가(레벨당)"), ("gen", "comp", "xp", 0.15, "경험치 증가(레벨당)"),
    # 협동기
    ("tornado", "combo", "radiusMul", 1.5, "흡입 소용돌이 반경 배율"), ("tornado", "combo", "dpsMul", 2.0, "피해 배율"), ("tornado", "combo", "slow", 0.4, "안쪽 적 둔화"),
    ("blizzard", "combo", "freeze", 1.0, "냉기 탄 명중 시 빙결(초)"), ("blizzard", "combo", "pierce", 2, "관통 수"),
    ("turbo", "combo", "countAdd", 2, "브러시 추가 개수"), ("turbo", "combo", "spinMul", 1.6, "회전 속도 배율"), ("turbo", "combo", "dmgMul", 1.5, "피해 배율"),
    # 적 (서바이벌 전용 값. HP는 enemy.hp × surv_wave 배율)
    ("dust", "enemy", "contactDps", 6, "닿아 있을 때 초당 내구도 피해"), ("dust", "enemy", "xp", 1, "먼지 조각 경험치"), ("dust", "enemy", "speed", 1.25, "이동 속도"),
    ("fly", "enemy", "contactDps", 4, ""), ("fly", "enemy", "xp", 1, ""), ("fly", "enemy", "speed", 1.9, ""),
    ("grease", "enemy", "contactDps", 12, ""), ("grease", "enemy", "xp", 3, ""), ("grease", "enemy", "speed", 0.9, ""),
    ("mold", "enemy", "contactDps", 14, ""), ("mold", "enemy", "xp", 4, ""), ("mold", "enemy", "speed", 0.75, ""),
    ("spore", "enemy", "contactDps", 8, ""), ("spore", "enemy", "xp", 3, ""), ("spore", "enemy", "speed", 1.0, ""),
    ("spore", "enemy", "range", 4.6, "원거리: 이 거리까지 다가와 멈추고 포자를 쏜다(칸)"), ("spore", "enemy", "shotCd", 2.6, "발사 간격(초)"),
    ("spore", "enemy", "shotDmg", 9, "포자 1발 피해(무한 강화의 피해 배율 적용)"), ("spore", "enemy", "shotSpeed", 3.6, "포자 속도(칸/초). 리더(3칸/초)와 비슷해 옆으로 비켜서 피한다"),
    ("boss_dust", "enemy", "contactDps", 30, ""), ("boss_dust", "enemy", "xp", 40, ""), ("boss_dust", "enemy", "speed", 0.9, ""),
    ("boss_grease", "enemy", "contactDps", 34, ""), ("boss_grease", "enemy", "xp", 45, ""), ("boss_grease", "enemy", "speed", 0.85, ""),
    ("boss_mold", "enemy", "contactDps", 38, ""), ("boss_mold", "enemy", "xp", 50, ""), ("boss_mold", "enemy", "speed", 0.8, ""),
    # 공용 강화(가전 카드가 바닥나면 섞여 나옴)
    ("move", "passive", "value", 0.1, "이동 속도 증가(단계당)"), ("move", "passive", "max", 3, "최대 단계"),
    ("hp", "passive", "value", 25, "최대 내구도 증가(단계당, 증가분만큼 회복)"), ("hp", "passive", "max", 3, "최대 단계"),
    ("bonus", "passive", "value", 25, "뽑을 강화가 다 떨어진 뒤 레벨 업 보상: 코인(회복 없음. 무한 회복으로 영원히 버티는 걸 막는다)"),
]
surv_param = [{"id": i, "slot": sl, "key": k, "value": v, "note": n} for (i, sl, k, v, n) in SURV_PARAM]
for uid, nm, ds in [
    ("vac", "흡입 소용돌이", "주변 {radius}칸 안 적에게 초당 {dps} 피해. 레벨마다 범위와 피해 증가"),
    ("robot", "회전 브러시", "브러시 {count}개가 주위를 돌며 닿는 적에게 {dmg} 피해. 레벨이 오르면 개수 증가"),
    ("ac", "냉기 탄", "{cd}초마다 가까운 적에게 냉기 탄({dmg} 피해, {slow_pct}% 둔화). 레벨이 오르면 발사 수 증가")]:
    s(f"surv.leader.{uid}.name", nm); s(f"surv.leader.{uid}.desc", ds)
for uid, nm, ds in [
    ("vac", "흡입 폭풍", "{cd}초마다 {dur}초 동안 주변 적을 끌어당기며 초당 {dps} 피해"),
    ("robot", "돌격 청소", "{cd}초마다 가까운 적에게 돌진, 지나간 길의 적에게 {dmg} 피해"),
    ("fridge", "급속 냉동", "{cd}초마다 주변 {radius}칸을 {freeze}초 얼리고 {dmg} 피해"),
    ("ac", "냉기 지원", "{cd}초마다 냉기 탄({dmg} 피해, {slow_pct}% 둔화)"),
    ("micro", "띵!", "{cd}초마다 적 무리에 폭발({dmg} 피해)"),
    ("washer", "거품 탄", "{cd}초마다 거품 {count}발을 부채꼴로 발사({dmg} 피해, 터지며 광역)"),
    ("pc", "오버클럭 링크", "리더에 붙어 모든 공격 간격 {cdMul_pct}% 감소, 피해 {dmgMul_pct}% 증가(레벨당)"),
    ("fan", "바람 고리", "리더에 붙어 {cd}초마다 주변 적을 밀쳐내고 {dmg} 피해"),
    ("gen", "자가 발전", "리더에 붙어 먼지 조각 흡입 범위 +{pickup_pct}%, 경험치 +{xp_pct}%(레벨당)")]:
    s(f"surv.comp.{uid}.name", nm); s(f"surv.comp.{uid}.desc", ds)
surv_combo = []
for cid, leader, comp, nm, ds in [
    ("tornado", "vac", "fan", "토네이도 흡입", "흡입 소용돌이가 {radiusMul}배로 넓어지고 피해 {dpsMul}배, 안쪽 적 {slow_pct}% 둔화"),
    ("blizzard", "ac", "fridge", "블리자드", "냉기 탄이 적을 {freeze}초 얼리고 {pierce}마리까지 관통"),
    ("turbo", "robot", "gen", "터보 충전", "브러시 +{countAdd}개, 회전 속도 {spinMul}배, 피해 {dmgMul}배")]:
    surv_combo.append({"id": cid, "leader": leader, "comp": comp, "nameKey": s(f"surv.combo.{cid}.name", nm), "descKey": s(f"surv.combo.{cid}.desc", ds)})
# 계열 세트: 리더 + 동료 중 같은 계열이 need명 이상이면 발동
surv_set = []
for fam, need, key, val, ds in [
    ("Living", 2, "pickupMul", 0.3, "생활가전 2: 먼지 조각 흡입 범위 +30%"),
    ("Kitchen", 2, "dmgMul", 0.15, "주방가전 2: 모든 피해 +15%"),
    ("Digital", 2, "cdMul", 0.15, "디지털 2: 모든 공격 간격 -15%"),
    ("Season", 2, "enemySlow", 0.12, "계절가전 2: 모든 적 이동 속도 -12%")]:
    surv_set.append({"family": F[fam], "need": need, "key": key, "value": val, "descKey": s(f"surv.set.{fam.lower()}", ds)})
for k, v in [("move", "이동 속도 +10%"), ("hp", "최대 내구도 +25"), ("bonus", "보너스 코인 +25")]:
    s(f"surv.passive.{k}", v)
# 스폰: t0~t1 동안 초당 rate0→rate1 마리, HP 배율 hp0→hp1 (enemy.hp 기준)로 늘고, t1 이후에도 rate1·hp1로 계속 나오며(무한), 그 위에 survScaleStart부터 무한 강화가 곱해진다. 진행도 f의 curve 제곱으로 올라간다(1.6 = 초반 완만, 후반 가파름). burst > 0 이면 t0에 그 수만큼 리더를 둘러싸며 한 번에 등장(한쪽은 빠져나갈 틈을 둔다). every > 0 이면 그 간격으로 반복
surv_wave = []
for i, (t0, t1, en, r0, r1, h0, h1, burst) in enumerate([
    (0, 360, "dust", 1.2, 7.0, 0.25, 1.2, 0),
    (25, 360, "fly", 0.3, 2.6, 0.3, 1.3, 0),
    (70, 360, "grease", 0.15, 1.1, 0.22, 1.0, 0),
    (130, 360, "mold", 0.12, 0.9, 0.22, 0.9, 0),
    (100, 360, "spore", 0.04, 0.45, 0.35, 1.0, 0),
    (100, 0, "dust", 0, 0, 0.5, 0.5, 26),
    (190, 0, "fly", 0, 0, 0.8, 0.8, 30),
]):
    every = {100: 90, 190: 90}.get(t0, 0) if burst else 0
    surv_wave.append({"id": i + 1, "t0": t0, "t1": t1, "enemy": en, "rate0": r0, "rate1": r1, "hp0": h0, "hp1": h1, "curve": 1.6, "burst": burst, "every": every})
# 보스는 config의 survBoss* 간격으로 먼지 대왕 → 기름 대왕 → 곰팡이 여왕 순서로 돌아가며 나온다
SURV_BOSSES = ["boss_dust", "boss_grease", "boss_mold"]
# 버틴 시간 보상: 하루 동안 처음 넘긴 구간마다 크리스탈(유료 재화). 같은 날 같은 구간은 한 번만
surv_reward = [{"sec": sec, "crystal": cr} for sec, cr in [(60, 5), (120, 10), (180, 15), (240, 15), (300, 20), (420, 20), (600, 25), (900, 40)]]   # 하루 최대 150 (10.15-b: 300 → 150)

# ---------------- gacha ----------------
gacha = [
    {"id": 1, "poolId": 1, "costItem": 2, "costCount": 150, "pulls": 1, "guaranteeRarity": 0, "unlockCond": 0,
     "nameKey": s("gacha.1.name", "1회 소환")},
    {"id": 2, "poolId": 1, "costItem": 2, "costCount": 1500, "pulls": 10, "guaranteeRarity": R["SR"], "unlockCond": 0,
     "nameKey": s("gacha.2.name", "10회 소환")},
]
s("banner.1.title", "신상 가전 입고")
s("banner.1.desc", "SSR 냉장고와 세탁기가 입고됐어요. 10회 소환은 SR 이상 1개가 확정입니다.")
# 등급 확률 SSR 3% / SR 17% / R 80% 를 가전별 가중치(합 10000)로 분배
gacha_pool = []
for rar, total in (("SSR", 300), ("SR", 1700), ("R", 8000)):
    ids = [u["id"] for u in units if u["rarity"] == R[rar]]
    base = total // len(ids)
    for i, uid in enumerate(ids):
        gacha_pool.append({"poolId": 1, "unitId": uid, "weight": base + (1 if i < total - base * len(ids) else 0)})

config = {
    "deckMax": 6, "lvMax": 45, "lvCapBase": 20, "starMax": 5,
    "dupeShards": [10, 15, 25],      # 이미 가진 가전이 또 나오면 그 가전 조각(R / SR / SSR). ★5 이후 중복은 dupeOverflowRewardGroup
    "shardRecruit": [20, 30, 50],    # 없는 가전을 조각으로 영입할 때 필요한 수(R / SR / SSR)
    "survShardPerMin": 1, "survShardMax": 5,   # 대청소 보상 판: 버틴 1분마다 리더 조각, 한 판 최대
    "lvBonus": 0.05, "starBonus": 0.12,
    "idleCapHours": 12, "maxUnits": 8, "startPower": 10, "regenPerSec": 1, "redeployCooldown": 12,
    "overheatSec": 5, "lifeMax": 10, "startRewardGroup": 9000, "attendanceRewardGroup": 9001,
    "dupeOverflowRewardGroup": 9002, "repRarityAutoSwap": 1,
    # 대청소 모드는 무한: 쓰러질 때까지. survScaleStart 이후 분당 비율로 적이 계속 강해진다
    "survScaleStart": 150,       # 이 시각(초)부터 HP·피해 무한 강화 시작
    "survHpGrowthPerMin": 0.4,   # 분당 적 HP 증가(복리)
    "survDmgGrowthPerMin": 0.25, # 분당 적 접촉·포자 피해 증가(복리)
    "survSpdGrowthPerMin": 0.07, "survSpdMax": 1.8,   # 이동 속도는 시작부터 분당 +7%, 최대 1.8배(초파리가 리더를 따라잡기 시작)
    "survBossFirst": 120, "survBossEvery": 120,       # 보스 첫 등장(초), 이후 간격
    "survBossHp": 2.2, "survBossHpStep": 0.5,         # 보스 HP 배율(enemy.hp 기준) = survBossHp × (1 + step × 회차) × 무한 강화
    "survLeaderHp": 100,         # 리더 내구도
    "survMoveSpeed": 3.0,        # 리더 이동 속도(칸/초)
    "survPickup": 1.6,           # 먼지 조각 흡입 범위(칸)
    "survXpBase": 5, "survXpStep": 5, "survXpQuad": 0.35,   # 다음 레벨 경험치 = base + step × (레벨-1) + quad × (레벨-1)²
    "survMaxComp": 4, "survCompMaxLv": 3, "survWeaponMaxLv": 5,
    "survArena": 22,             # 방 크기(칸, 정사각형)
    "survDailyRewards": 3,       # 하루 보상 받는 판 수
    "survPartPer30s": 1,         # 30초 버틸 때마다 부품(계열마다)
    "survCoinPerKill": 1,        # 처치당 코인
    "starLifeMin": [1, 6, 10],   # 남은 청결도가 이 값 이상이면 별 1/2/3개
    "queueHeatRatio": 0.5,       # 저지 정원이 차서 줄 선 적이 가전에 주는 과열 비율
    "powerMax": 99,              # 전력 최대치(1 = 100W)
    "retreatRefund": 0.5,        # 철수 시 환급 비율(배치 + 강화에 쓴 전력 전체 기준)
    "multitapCost": [20, 35],
    "passSeasonStart": "2026-10-12", "passSeasonDays": 28,   # 시즌 패스: 1시즌 시작일(월요일), 시즌 길이(일). 이후 같은 길이로 계속 돌아감
    "passXpStageFirst": 100, "passXpStageRepeat": 30,       # 패스 경험치: 스테이지 첫 클리어 / 다시 클리어
    "passXpSurvPer30s": 10, "passXpSurvMax": 80,            # 대청소 30초마다, 한 판 최대
    "passXpAttend": 50, "passXpAd": 10,                     # 출석 받기 / 광고 1회 시청
    "passLevelBuyCrystal": 150,                             # 크리스탈로 패스 1레벨 올리기
    "adWatchSec": 5,             # 보상형 광고 길이(초). 프로토타입 테스트용으로 짧게(실서비스 15~30초)
    "monthPassDays": 30, "monthPassDailyGroup": 32001,   # 구독: 기간(일), 매일 받는 보상 그룹    # 멀티탭: 배치 칸 +1 비용(1 = 100W). 개수만큼 살 수 있다(8 → 9 → 10)
    "naturalCoolPerSec": 25,     # 근접 가전 자연 냉각(초당)
    "fireTileHeatPerSec": 40,    # 가스레인지(F) 칸 근접 가전 과열(초당)
    "wetTileCoolPerSec": 60,     # 물기(W) 칸 근접 가전 추가 냉각(초당)
    "wetTileEnemySpeedMul": 1.35,  # 물기(W) 칸 지상 적 이동 배율
}

# ---------------- shop (상점 · BM) ----------------
# payKind: Iap = 현금 결제(프로토타입은 테스트 결제창만 뜨고 실제 결제 없음), Crystal = 크리스탈 차감, Ad = 보상형 광고 시청 후 지급
# price  : Iap = 원(KRW), Crystal = 크리스탈 개수, Ad = 0
# limit  : None 무제한 / Daily 하루 n회(자정 초기화) / Weekly 주 n회(월요일 초기화) / Account 계정당 n회
# first  : 첫 구매 보너스 보상(크리스탈 상품의 '첫 구매 2배'), cooldown: 광고 상품 재시청 대기(초)
# special: idle:H = 현재 자동 청소 시급으로 H시간치 즉시 지급 / pass:month = 구독(즉시 지급 + 매일 받기) / pass:noads = 광고 제거
# unlockCond: condition id (0 = 항상)
SH, PK, LT = ENUMS["ShopTab"], ENUMS["PayKind"], ENUMS["LimitType"]
PARTS = lambda n: [(ITEM, 101, n, n, 10000), (ITEM, 102, n, n, 10000), (ITEM, 103, n, n, 10000), (ITEM, 104, n, n, 10000)]
CRY = lambda n: [(ITEM, 2, n, n, 10000)]
COIN = lambda n: [(ITEM, 1, n, n, 10000)]
SHOP = [
    # id, tab, pay, price, rewards, limit, limitCount, first bonus, special, cooldown, unlockCond, badge, name, desc
    (100, "Crystal", "Ad", 0, CRY(30), "Daily", 3, None, "", 600, 0, "무료", "무료 크리스탈", "광고를 보면 크리스탈 30. 하루 3번, 10분마다"),
    (101, "Crystal", "Iap", 1200, CRY(60), "None", 0, CRY(60), "", 0, 0, "", "크리스탈 60", ""),
    (102, "Crystal", "Iap", 5900, CRY(300), "None", 0, CRY(300), "", 0, 0, "", "크리스탈 300", ""),
    (103, "Crystal", "Iap", 12000, CRY(630), "None", 0, CRY(600), "", 0, 0, "보너스 5%", "크리스탈 630", ""),
    (104, "Crystal", "Iap", 33000, CRY(1800), "None", 0, CRY(1650), "", 0, 0, "보너스 9%", "크리스탈 1,800", "10회 소환 1번 분량"),
    (105, "Crystal", "Iap", 65000, CRY(3700), "None", 0, CRY(3250), "", 0, 0, "보너스 14%", "크리스탈 3,700", ""),
    (106, "Crystal", "Iap", 119000, CRY(7000), "None", 0, CRY(5950), "", 0, 0, "보너스 18%", "크리스탈 7,000", ""),
    (201, "Daily", "Ad", 0, COIN(3000), "Daily", 3, None, "", 300, 0, "무료", "코인 3,000", "광고 보고 받기. 하루 3번, 5분마다"),
    (202, "Daily", "Ad", 0, PARTS(5), "Daily", 2, None, "", 600, 0, "무료", "부품 상자", "모든 계열 부품 5개씩. 하루 2번"),
    (203, "Daily", "Ad", 0, [], "Daily", 2, None, "idle:2", 0, 1, "무료", "자동 청소 2시간 즉시", "지금 시급으로 2시간치 코인·부품·크리스탈을 바로 받아요"),
    (204, "Daily", "Ad", 0, [], "Daily", 1, None, "shard:0:5", 0, 0, "무료", "오늘의 무료 조각", "보유한 가전 중 하나의 조각 5개(매일 바뀜)"),
    (221, "Daily", "Crystal", 100, [], "Daily", 1, None, "shard:1:10", 0, 0, "R", "오늘의 R 조각", "R 가전 조각 10개(매일 바뀜)"),
    (222, "Daily", "Crystal", 200, [], "Daily", 1, None, "shard:2:10", 0, 0, "SR", "오늘의 SR 조각", "SR 가전 조각 10개(매일 바뀜)"),
    (223, "Daily", "Crystal", 400, [], "Daily", 1, None, "shard:3:10", 0, 0, "SSR", "오늘의 SSR 조각", "SSR 가전 조각 10개(매일 바뀜)"),
    (211, "Daily", "Crystal", 200, COIN(20000), "Daily", 3, None, "", 0, 0, "", "코인 20,000", ""),
    (212, "Daily", "Crystal", 300, PARTS(30), "Daily", 2, None, "", 0, 0, "", "부품 묶음", "모든 계열 부품 30개씩"),
    (213, "Daily", "Crystal", 150, [], "Daily", 1, None, "idle:4", 0, 1, "", "자동 청소 4시간 즉시", "지금 시급으로 4시간치를 바로 받아요"),
    (301, "Package", "Iap", 4900, CRY(500) + COIN(10000) + PARTS(20), "Account", 1, None, "", 0, 0, "1회 한정", "첫 출동 패키지", "처음 한 번만. 크리스탈 500 · 코인 10,000 · 부품 각 20"),
    (302, "Package", "Iap", 5900, CRY(300), "None", 0, None, "pass:month", 0, 0, "30일", "가전 관리 구독", "바로 크리스탈 300, 30일 동안 매일 상점에서 크리스탈 100 (총 3,300)"),
    (303, "Package", "Iap", 9900, CRY(300), "Account", 1, None, "pass:noads", 0, 0, "영구", "광고 제거 패스", "광고 없이 무료 보상을 바로 받아요. 크리스탈 300 포함"),
    (304, "Package", "Iap", 9900, CRY(300) + COIN(30000) + PARTS(40), "Weekly", 1, None, "", 0, 0, "주 1회", "주간 성장 패키지", "크리스탈 300 · 코인 30,000 · 부품 각 40"),
    (306, "Package", "Iap", 11000, [], "None", 0, None, "pass:season", 0, 0, "시즌", "프리미엄 패스", "이번 시즌 패스의 유료 보상 줄을 열어요. 이미 지난 레벨 보상도 바로 받을 수 있어요"),
    (305, "Package", "Iap", 19900, CRY(1200) + COIN(30000) + PARTS(50), "Account", 1, None, "", 0, 1006, "1-5 클리어", "거실 사수 기념 패키지", "1-5를 깨면 열려요. 크리스탈 1,200 · 코인 30,000 · 부품 각 50"),
]
shop_product = []
for i, (pid, tab, pay, price, rws, lim, limn, first, special, cd, cond, badge, name, desc) in enumerate(SHOP):
    gid = rg(30000 + pid, rws) if rws else 0
    fid = rg(31000 + pid, first) if first else 0
    shop_product.append({"id": pid, "tab": SH[tab], "order": i + 1, "payKind": PK[pay], "price": price, "rewardGroup": gid,
                         "limitType": LT[lim], "limitCount": limn, "firstBonusGroup": fid, "special": special, "cooldown": cd,
                         "unlockCond": cond, "badgeKey": s(f"shop.{pid}.badge", badge) if badge else "",
                         "nameKey": s(f"shop.{pid}.name", name), "descKey": s(f"shop.{pid}.desc", desc) if desc else ""})
MONTH_PASS_DAILY = rg(32001, CRY(100))

# ---------------- pass_level (시즌 패스) ----------------
# 시즌(config passSeasonDays일)마다 처음부터. 패스 경험치로 레벨이 오르고, 레벨마다 무료 줄 / 유료(프리미엄 패스) 줄 보상
# xp = 그 레벨에 도달하는 누적 경험치(1레벨은 0 = 시작하자마자 받음)
# 경험치 획득처는 config 의 passXp* (첫 클리어 / 다시 클리어 / 대청소 30초마다 / 출석 / 광고 시청)
PASS_PER_LV, PASS_LV = 100, 30
def _pass_free(lv):
    if lv % 7 == 0: return [(ITEM, SHARD_ID["ac"], 5, 5, 10000)]      # 7·14·21·28: 에어컨 조각
    if lv % 5 == 0: return CRY(30)
    if lv % 3 == 0: return PARTS(5)
    return COIN(1000 + 100 * lv)
def _pass_paid(lv):
    if lv == PASS_LV: return [(UNIT, "washer", 1, 1, 10000)]          # 마지막: SSR 세탁기(보유 시 돌파)
    if lv == 15: return [(UNIT, "micro", 1, 1, 10000)] + CRY(100)      # 중간: SR 전자레인지
    if lv == PASS_LV - 1: return [(ITEM, SHARD_ID["washer"], 10, 10, 10000)]  # 29: 세탁기 조각
    if lv % 5 == 0: return CRY(150)
    if lv % 2 == 0: return CRY(60) + PARTS(5)
    return COIN(3000 + 200 * lv) + PARTS(5)
pass_level = [{"level": lv, "xp": (lv - 1) * PASS_PER_LV, "freeGroup": rg(33000 + lv, _pass_free(lv)), "paidGroup": rg(34000 + lv, _pass_paid(lv))}
              for lv in range(1, PASS_LV + 1)]

TABLES = {
    "enums": ENUMS, "config": config, "string_ko": S, "item": items, "unit": units, "unit_star": unit_star, "unit_levelup": unit_levelup,
    "enemy": enemies, "map": maps, "stage": stages, "wave": waves, "reward_group": reward_group,
    "condition": conditions, "content_unlock": content_unlock, "star_reward": star_reward, "tutorial": tutorial, "skill_param": skill_param, "unit_boost": unit_boost,
    "guide": guide, "shop_product": shop_product, "attendance": attendance, "pass_level": pass_level, "surv_param": surv_param, "surv_combo": surv_combo, "surv_set": surv_set, "surv_wave": surv_wave, "surv_reward": surv_reward, "gacha": gacha, "gacha_pool": gacha_pool,
}
for name, data in TABLES.items():
    with open(os.path.join(OUT, f"{name}.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
# 브라우저가 파일을 바로 열어도(file://) 읽을 수 있도록 tables.js로도 묶는다
import pack_tables, xlsx_io
pack_tables.pack(OUT)
# 엑셀 원본도 함께 다시 쓴다(주의: tables/*.xlsx 에서 손으로 고친 내용은 덮어써짐)
xlsx_io.write_workbooks(TABLES, os.path.join(os.path.dirname(__file__), "..", "tables"))
print({k: (len(v) if isinstance(v, list) else len(v.keys())) for k, v in TABLES.items()})
for st in stages:
    print(st["no"], st["hpMul"], st["idleCoin"], st["idlePart"], st["idleCrystal"])
