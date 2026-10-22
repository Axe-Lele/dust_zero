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
STAGE_HP_MUL = [0.84, 0.79, 1.16, 0.88, 1.70, 1.02, 1.22, 1.08, 1.05, 1.18, 1.04, 1.17, 1.18, 1.27, 1.18]
COUNT_GROWTH_IN_CHAPTER = 0.08                     # 챕터 안에서 스테이지마다 적 수 +8%
IDLE_COIN_BASE, IDLE_COIN_GROWTH = 500, 1.15       # 시급 코인 = 500 * 1.15^(n-1)
IDLE_PART_BASE, IDLE_PART_GROWTH = 3, 1.12         # 시급 부품(계열마다) = 3 * 1.12^(n-1)
IDLE_CRYSTAL_BASE, IDLE_CRYSTAL_STEP = 15, 5       # 시급 크리스탈 = 15 + 5 * (챕터-1)
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
]

# ---------------- enemy ----------------
E = [
    ("dust", "먼지뭉치", 160, 1.0, 35, 1.0, .22, 1, 0, "dust", 0),
    ("grease", "기름때", 520, .6, 80, 1.0, .28, 1, 0, "grease", 0),
    ("mold", "곰팡이", 700, .45, 90, 1.2, .30, 1, 0, "mold", 0),
    ("fly", "초파리", 110, 1.5, 0, 1.0, .17, 1, 1, "fly", 0),
    ("boss_dust", "먼지 대왕", 3200, .35, 180, 1.5, .42, 2, 0, "dust", 1),
    ("boss_grease", "기름 대왕", 3400, .35, 200, 1.5, .42, 2, 0, "grease", 1),
    ("boss_mold", "곰팡이 여왕", 3800, .30, 200, 1.6, .44, 2, 0, "mold", 1),
]
# 이동 성향: Detour = 돌아갈 길이 있으면 우회하고 막혔을 때만 공격, Breaker = 길을 막은 가전이 있으면 그대로 부수며 직진
ENEMY_AI = {"dust": "Detour", "grease": "Breaker", "mold": "Breaker", "fly": "Detour",
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
        for seq, (t, en, c, g, e) in enumerate(rows):
            waves.append({"groupId": sid, "seq": seq + 1, "t": t, "enemy": en, "count": c, "gap": g, "entrance": e})
        # 첫 클리어 보상
        first_rows = [(ITEM, 1, FIRST_COIN_BASE + 400 * (n - 1), FIRST_COIN_BASE + 400 * (n - 1), 10000),
                      (ITEM, 2, 300 if boss else 100, 300 if boss else 100, 10000)]
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
        })
        prev_stage = sid

# 고정 보상 그룹
rg(9000, [(ITEM, 1, 2000, 2000, 10000), (ITEM, 2, 1500, 1500, 10000)] + [(ITEM, p, 20, 20, 10000) for p in (101, 102, 103, 104)]
   + [(UNIT, "vac", 1, 1, 10000), (UNIT, "ac", 1, 1, 10000), (UNIT, "fan", 1, 1, 10000), (UNIT, "gen", 1, 1, 10000)])   # 시작 지급
rg(9001, [(ITEM, 2, 300, 300, 10000)])                                                       # 출석
rg(9002, [(ITEM, 1, 1000, 1000, 10000)])                                                     # 돌파 최대 이후 중복 전환

# 챕터 별 누적 보상 (챕터당 최대 별 = 스테이지 수 x 3)
star_reward = []
STAR_MILESTONES = [(5, [(ITEM, 2, 100), (ITEM, 1, 3000)]),
                   (10, [(ITEM, p, 20) for p in (101, 102, 103, 104)]),
                   (15, [(ITEM, 2, 300)])]
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
TT_, TU_ = ENUMS["TutTrigger"], ENUMS["TutUntil"]
TUT = [
    # group, cond, trigger, param, until, untilParam, pause, focus, text
    (1, "", "Start", "", "Select", "melee", 1, "card:melee", "적이 오기 전에 가전을 놓아요. {unit} 카드를 눌러 보세요."),
    (1, "", "Next", "", "Deploy", "melee", 1, "", "빛나는 바닥 칸에 놓을 수 있어요. 적이 지나갈 길목을 눌러 주세요."),
    (1, "", "Spawn", "dust", "Timer", 6, 0, "", "먼지뭉치는 우회형이에요. 근접 가전을 피해 돌아가니 원거리 가전으로 잡아야 해요."),
    (1, "", "Time", 1, "Deploy", "ranged", 1, "card:ranged", "전력이 모였어요. {unit} 카드로 흰 가구 칸에 원거리 가전을 놓아 보세요."),
    (1, "", "Spawn", "fly", "Timer", 6, 0, "", "초파리는 날아다녀요. 근접 가전은 지상만 때리니 공중을 칠 수 있는 원거리 가전이 맡아요."),
    (1, "", "Skill", "", "Timer", 6, 0, "", "게이지가 차면 스킬이 저절로 나가요. 가전을 누르면 스킬 설명과 게이지를 볼 수 있어요."),
    (1, "", "Spawn", "mold", "Tap", "", 1, "", "곰팡이는 직진형(발밑 붉은 원)이에요. 피하지 않고 막은 가전을 밀고 들어오니 근접 가전으로 길을 막아 세우세요."),
    (1, "", "Leak", "", "Timer", 6, 0, "hud:life", "적이 목표물에 닿으면 청결도가 줄어요. 0이 되면 실패, 하나도 새지 않으면 별 3개예요."),
    (2, "", "Start", "", "Deploy", "gen", 1, "card:gen", "발전기를 먼저 놓으면 전력이 더 빨리 차요. 길에서 먼 가구 칸에 두면 좋은 자리를 아낄 수 있어요."),
    (2, "", "Time", 4, "Tap", "", 1, "hud:power", "같은 가전도 전력만 있으면 여러 대 놓을 수 있어요. 한 번에 최대 8대까지예요."),
    (2, "", "Skill", "gen", "Timer", 5, 0, "", "발전기 스킬 풀가동은 전력을 한 번에 채워 줘요."),
    (3, "", "Heat", 0.5, "Tap", "", 1, "unit:heat", "근접 가전이 적을 막으면 과열이 쌓여요. 가득 차면 5초 동안 멈추고 막던 적을 놓쳐요."),
    (3, "", "Next", "", "Deploy", "fan", 1, "card:fan", "선풍기를 근접 가전 옆에 두면 주변 가전을 식혀 줘요."),
    (4, "", "Time", 12, "Tap", "", 1, "", "근접 가전 아래 주황 점은 저지 정원이에요. 정원이 차면 적이 뒤에 줄을 서고, 줄 선 적도 과열을 쌓아요."),
    (4, "", "Time", 30, "Timer", 7, 0, "", "가전을 누르면 철수할 수 있어요. 비용 절반을 돌려받고, 12초 뒤 다시 놓을 수 있어요."),
    (4, "", "Time", 45, "Timer", 5, 0, "btn:speed", "익숙해졌다면 오른쪽 위 1× 버튼으로 2배속을 써 보세요."),
    (10, "boss", "Boss", "", "Tap", "", 1, "", "보스는 단단하고, 목표물에 닿으면 청결도를 2 깎아요. 근접 가전으로 막아 세우고 화력을 모으세요."),
    (11, "map:kitchen", "Start", "", "Tap", "", 1, "", "주방의 가스레인지 칸(불꽃)에 선 근접 가전은 과열이 훨씬 빨리 쌓여요. 기름때는 직진형이니 불꽃 칸을 피해 막아 보세요."),
    (12, "map:bath", "Start", "", "Tap", "", 1, "", "욕실의 물기 칸에서는 적이 빨라지지만, 그 칸에 선 근접 가전은 더 잘 식어요."),
]
tutorial, _seq = [], {}
for (grp, cond, trg, prm, unt, uprm, pause, focus, text) in TUT:
    _seq[grp] = _seq.get(grp, 0) + 1
    tid = grp * 100 + _seq[grp]
    tutorial.append({"id": tid, "group": grp, "seq": _seq[grp], "cond": cond, "trigger": TT_[trg], "param": prm, "until": TU_[unt],
                     "untilParam": uprm, "pause": pause, "focus": focus, "textKey": s(f"tut.{tid}", text)})

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
    ("boss_dust", "enemy", "contactDps", 30, ""), ("boss_dust", "enemy", "xp", 40, ""), ("boss_dust", "enemy", "speed", 0.9, ""),
    # 공용 강화(가전 카드가 바닥나면 섞여 나옴)
    ("move", "passive", "value", 0.1, "이동 속도 증가(단계당)"), ("move", "passive", "max", 3, "최대 단계"),
    ("hp", "passive", "value", 25, "최대 내구도 증가(단계당, 증가분만큼 회복)"), ("hp", "passive", "max", 3, "최대 단계"),
    ("heal", "passive", "value", 35, "내구도 회복(뽑을 게 없을 때)"),
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
for k, v in [("move", "이동 속도 +10%"), ("hp", "최대 내구도 +25"), ("heal", "내구도 35 회복")]:
    s(f"surv.passive.{k}", v)
# 스폰: t0~t1 동안 초당 rate0→rate1 마리, HP 배율 hp0→hp1 (enemy.hp 기준). 진행도 f의 curve 제곱으로 올라간다(1.6 = 초반 완만, 후반 가파름). burst > 0 이면 t0에 그 수만큼 리더를 둘러싸며 한 번에 등장(한쪽은 빠져나갈 틈을 둔다)
surv_wave = []
for i, (t0, t1, en, r0, r1, h0, h1, burst) in enumerate([
    (0, 300, "dust", 1.2, 6.0, 0.25, 1.1, 0),
    (25, 300, "fly", 0.3, 2.2, 0.3, 1.2, 0),
    (70, 300, "grease", 0.15, 1.0, 0.22, 0.9, 0),
    (130, 300, "mold", 0.12, 0.8, 0.22, 0.8, 0),
    (100, 100, "dust", 0, 0, 0.5, 0.5, 26),
    (190, 190, "fly", 0, 0, 0.8, 0.8, 30),
    (240, 240, "boss_dust", 0, 0, 2.5, 2.5, 1),
    (240, 240, "dust", 0, 0, 1.0, 1.0, 34),
]):
    surv_wave.append({"id": i + 1, "t0": t0, "t1": t1, "enemy": en, "rate0": r0, "rate1": r1, "hp0": h0, "hp1": h1, "curve": 1.6, "burst": burst})

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
    "deckMax": 6, "lvMax": 30, "starMax": 5, "lvBonus": 0.05, "starBonus": 0.12,
    "idleCapHours": 12, "maxUnits": 8, "startPower": 10, "regenPerSec": 1, "redeployCooldown": 12,
    "overheatSec": 5, "lifeMax": 10, "startRewardGroup": 9000, "attendanceRewardGroup": 9001,
    "dupeOverflowRewardGroup": 9002, "repRarityAutoSwap": 1,
    "survDuration": 300,         # 대청소 모드 한 판 길이(초). 끝까지 버티면 성공
    "survLeaderHp": 100,         # 리더 내구도
    "survMoveSpeed": 3.0,        # 리더 이동 속도(칸/초)
    "survPickup": 1.6,           # 먼지 조각 흡입 범위(칸)
    "survXpBase": 5, "survXpStep": 5,   # 다음 레벨 경험치 = base + step × (레벨-1)
    "survMaxComp": 4, "survCompMaxLv": 3, "survWeaponMaxLv": 5,
    "survArena": 22,             # 방 크기(칸, 정사각형)
    "survDailyRewards": 3,       # 하루 보상 받는 판 수
    "survPartPer30s": 1,         # 30초 버틸 때마다 부품(계열마다). 끝까지 버티면 10씩
    "survCoinPerKill": 1, "survClearCrystal": 50,   # 한 판 처치 수는 대략 800~1,400
    "starLifeMin": [1, 6, 10],   # 남은 청결도가 이 값 이상이면 별 1/2/3개
    "queueHeatRatio": 0.5,       # 저지 정원이 차서 줄 선 적이 가전에 주는 과열 비율
    "powerMax": 99,              # 전력 최대치(1 = 100W)
    "retreatRefund": 0.5,        # 철수 시 배치 비용 환급 비율
    "naturalCoolPerSec": 25,     # 근접 가전 자연 냉각(초당)
    "fireTileHeatPerSec": 40,    # 가스레인지(F) 칸 근접 가전 과열(초당)
    "wetTileCoolPerSec": 60,     # 물기(W) 칸 근접 가전 추가 냉각(초당)
    "wetTileEnemySpeedMul": 1.35,  # 물기(W) 칸 지상 적 이동 배율
}

TABLES = {
    "enums": ENUMS, "config": config, "string_ko": S, "item": items, "unit": units, "unit_levelup": unit_levelup,
    "enemy": enemies, "map": maps, "stage": stages, "wave": waves, "reward_group": reward_group,
    "condition": conditions, "content_unlock": content_unlock, "star_reward": star_reward, "tutorial": tutorial, "skill_param": skill_param,
    "surv_param": surv_param, "surv_combo": surv_combo, "surv_set": surv_set, "surv_wave": surv_wave, "gacha": gacha, "gacha_pool": gacha_pool,
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
