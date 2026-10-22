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
CHAPTERS = 15                                      # 10.16-f: 3 → 15 (구역 5개 × 3챕터)
STAGES_PER_CHAPTER = 5
# 스테이지별 적 HP 배율. 밸런스 시뮬레이터(기준 덱·기준 레벨 Lv≈스테이지 순번)로 목표 남은 청결도에 맞춰 보정한 값
# 순서: 1-1 ~ 3-5. 웨이브 구성·입구 배치가 스테이지마다 달라서 숫자가 단조 증가하지는 않는다
STAGE_HP_MUL = [0.84, 0.79, 0.9, 0.86, 1.36, 0.94, 1.22, 1.02, 1.05, 0.9, 0.95, 1.15, 1.16, 1.27, 1.25]   # 10.14-k: 강화·멀티탭·후반 HP 상승 반영 재보정(1-1 튜토리얼은 유지). 10.16-f: 2-5 보스 0.98 → 0.9
# 스테이지별 시작 전력(1 = 100W, 0 = config.startPower). 1-1은 첫 먼지(3초)가 들어올 때 딱 800W가 되도록 500W
STAGE_START_POWER = {101: 5}
COUNT_GROWTH_IN_CHAPTER = 0.08                     # 챕터 안에서 스테이지마다 적 수 +8%
IDLE_COIN_BASE, IDLE_COIN_GROWTH = 500, 1.15       # 시급 코인 = 500 * 1.15^(n-1)  (1~15스테이지)
IDLE_PART_BASE, IDLE_PART_GROWTH = 3, 1.12         # 시급 부품(계열마다) = 3 * 1.12^(n-1)  (1~15스테이지)
IDLE_LATE_GROWTH = 1.005                           # 10.16-f: 16스테이지부터는 시급이 스테이지마다 +0.5%만 오름(성장은 레벨 비용 쪽에서 느려짐)
IDLE_CRYSTAL_BASE = 5                              # 시급 크리스탈 = 5 + (챕터-1)//2  (1~2챕터 5 ... 15챕터 12)
FIRST_COIN_BASE, FIRST_COIN_LATE = 1000, 60        # 첫 클리어 코인 = 1000 + 400×(n-1) (15까지), 이후 스테이지마다 +60

ENUMS = {
    "Rarity": {"R": 1, "SR": 2, "SSR": 3},
    "Family": {"Living": 1, "Kitchen": 2, "Digital": 3, "Season": 4},
    "Tile": {"Ground": 1, "Furniture": 2},
    "RewardKind": {"Item": 1, "Unit": 2},
    "CondKind": {"None": 0, "StageClear": 1},
    "TargetType": {"Ground": 1, "Air": 2, "All": 3},
    "EnemyAI": {"Detour": 1, "Breaker": 2},
    "TutTrigger": {"Start": 1, "Next": 2, "Time": 3, "Spawn": 4, "Power": 5, "Heat": 6, "Skill": 7, "Boss": 8, "Leak": 9, "Over": 10},
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
  # 10.16-f 신규: 2구역(4-5 첫 클리어)에서 다리미, 3구역(7-5)에서 가스레인지, 4구역(10-5)에서 보일러 조각. 소환에서도 나옴
  ("iron", "SR", "Living", "Ground", 11, 1300, 130, 1.4, 1.1, 1, 14, 0, 0, "다리미", "다리미", "근접 · 강타 · 방어 무시", "스팀 프레스",
   "적이 {pressRange}칸 안에 오면 발동. 앞의 지상 적 주변 {pressRadius}칸을 내리눌러 {pressDmg} 피해를 주고 {stun}초 기절시켜요. 자기 과열은 {selfCool_pct}% 식어요. 평타와 스킬 모두 방어를 무시해요.",
   ["주름 하나 못 지나가요.", "스팀 충전 완료!", "뜨거우니까 조심하세요.", "셔츠 깃은 제 전문이에요."]),
  ("stove", "SR", "Kitchen", "Furniture", 13, 0, 55, 1.5, 2.2, 0, 18, 6, 0, "가스레인지", "가스불", "원거리 · 바닥 화염", "센 불",
   "평타는 떨어진 자리에 {burnSec}초 불길(초당 {burnDps})을 남겨요. 스킬: 적이 몰린 곳에 반경 {zoneRadius}칸 화염 지대를 {dur}초간 깔아 초당 {zoneDps} 피해. 지상 적만 태워요.",
   ["불 조절은 제가 할게요.", "약불, 중불, 센 불!", "환기 꼭 해 주세요.", "냄비 받침 어디 있죠?"]),
  ("boiler", "SSR", "Season", "Furniture", 16, 0, 38, 1.0, 2.4, 0, 22, 8, 0, "보일러", "보일러", "원거리 · 방어 깎기", "고압 스팀",
   "평타는 적의 방어를 {armorBreak_pct}%p 깎고 {scaldSec}초 화상(초당 {burnDps}). 스킬: {dur}초간 {tick}초마다 사거리 안 모든 적에게 {skillDmg} 피해, 방어 {skillBreak_pct}%p 감소, {skillSlow_pct}% 둔화.",
   ["난방 가동합니다.", "온수 준비됐어요!", "압력 정상, 출동!", "겨울엔 저만 믿으세요."]),
]
TT = ENUMS["TargetType"]
TARGETS = {"vac": ("Ground", "Ground"), "robot": ("Ground", "Ground"), "fridge": ("Ground", "All"),
           "ac": ("All", "All"), "micro": ("All", "All"), "washer": ("All", "All"), "pc": ("All", "All"), "fan": ("All", "All"), "gen": ("All", "All"),
           "iron": ("Ground", "Ground"), "stove": ("Ground", "Ground"), "boiler": ("All", "All")}
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

# 가전 프로필(unit_profile): 일러스트 정보창. art 가 비어 있으면 SD로 대신 보여줌.
# 10.16-f: 청소기·선풍기·냉장고 외 9종은 tools/art/gen_art.py 로 만든 벡터 일러스트(임시). 진짜 그림이 오면 같은 파일 이름으로 교체
# art 경로 규칙: {art}_full.webp(전신) / {art}_face1~3.webp(표정) / {art}_turn.webp(정면·측면·후면) / {art}_keys.webp(주요 요소)
PROFILE = [
  # id, art, keyColor, bgColor, tagline, profile, motif
  ("vac", "art/vac", "#E0436B", "#FCE6EC", "깨끗한 세상을 만드는 강력한 흡입력!",
   "등에 멘 모터팩으로 무엇이든 빨아들이는 저지 담당. 장난스럽게 웃지만 먼지 앞에선 한 발도 물러서지 않아요. 긴 호스와 헤드를 무기처럼 휘두릅니다.",
   "스틱 헤드 · 백팩 모터 · 흡입 호스"),
  ("robot", "art/robot", "#6B7785", "#EEF1F4", "구석구석, 멈추지 않는 순찰대!",
   "작고 가볍지만 쉬지 않는 순찰 담당. 졸린 눈은 절전 모드일 뿐, 충전만 끝나면 누구보다 빨라요.",
   "원반 몸체 · 회전 브러시 · 충전 램프"),
  ("fridge", "art/fridge", "#4A86D8", "#E5F1FC", "신선함을 지키는 차가운 수호자!",
   "냉장 패널이 달린 롱 재킷 차림의 수호자. 표정은 담담하지만 모두를 위해 얼음 방벽을 세워요. 유통기한은 칼같이 챙깁니다.",
   "냉장 패널 재킷 · 얼음 큐브 · 눈꽃 결정"),
  ("ac", "art/ac", "#2FA9C9", "#E2F4F8", "한여름에도 흔들림 없는 냉정함!",
   "멀리서 냉기를 뿜어 적을 느리게 만드는 원거리 담당. 정해진 온도는 꼭 지키는 원칙주의자예요.",
   "루버 날개 · 냉기 바람 · 리모컨"),
  ("micro", "art/micro", "#F08A3E", "#FFF0E3", "30초면 충분해, 띵!",
   "데우는 동안 꾹 참았다가 한 번에 터뜨리는 화력 담당. 급해 보여도 타이밍만큼은 정확해요.",
   "회전 접시 · 타이머 다이얼 · 오렌지 조명"),
  ("washer", "art/washer", "#4AA8E0", "#E4F2FB", "얼룩 하나 남기지 않는 회오리!",
   "빙글빙글 탈수 회오리로 적을 한꺼번에 밀어내는 광역 담당. 거품 놀이를 제일 좋아해요.",
   "드럼 도어 · 비눗방울 · 회오리"),
  ("pc", "art/pc", "#2FBF9A", "#E3F7F1", "오버클럭, 준비 완료.",
   "주변 동료의 처리 속도를 끌어올리는 버프 담당. 헤드셋 너머로 전황을 전부 계산하고 있어요.",
   "헤드셋 · 모니터 패널 · 민트 LED"),
  ("fan", "art/fan", "#3B6FD8", "#E5EEFC", "시원한 바람으로 전장을 흐르게!",
   "날개처럼 펼쳐지는 투명 블레이드로 동료들의 열을 식혀주는 응원 담당. 늘 들떠 있고, 말할 때마다 목소리가 바람에 떨려요.",
   "투명 블레이드 날개 · 리본 · 스탠드 선풍기"),
  ("gen", "art/gen", "#E9A92E", "#FFF5DE", "전력이 끊기지 않게, 풀가동!",
   "배치만 해도 전력이 차오르는 보급 담당. 기름 냄새가 좀 나도 든든함은 최고예요.",
   "번개 배터리 · 안전모 · 전원 케이블"),
  ("iron", "art/iron", "#7A55C8", "#EEE8FA", "구김도 먼지도, 한 번에 쫙!",
   "커다란 스팀 다리미를 망치처럼 내리치는 강타 담당. 단정한 차림에 깐깐한 성격이라 주름 하나도 그냥 넘기지 않아요. 딱딱한 물때도 뜨거운 바닥판 앞에선 소용없어요.",
   "스팀 다리미 · 수증기 구름 · 보라색 리본"),
  ("stove", "art/stove", "#E0582E", "#FDEAE2", "불 조절은 맡겨 줘, 센 불이다!",
   "바닥에 불길을 깔아 지나가는 적을 태우는 화염 담당. 요리할 때처럼 불 세기를 칼같이 맞추고, 앞치마에는 늘 그을음이 묻어 있어요.",
   "화구 머리띠 · 불꽃 끝머리 · 앞치마와 프라이팬"),
  ("boiler", "art/boiler", "#C46A3A", "#FBEDE4", "온 집안을 데우는 고압 스팀!",
   "구리 배관을 날개처럼 두른 난방 담당. 느긋하고 다정하지만 압력이 차면 무엇이든 녹여 버려요. 단단한 적의 껍질을 벗겨 내는 데는 따라올 가전이 없어요.",
   "구리 배관 날개 · 압력계 브로치 · 드릴 트윈테일"),
]
unit_profile = [{"unitId": uid, "art": art, "keyColor": kc, "bgColor": bg,
                 "taglineKey": s(f"unit.{uid}.tagline", tag), "profileKey": s(f"unit.{uid}.profile", prof),
                 "motifKey": s(f"unit.{uid}.motif", motif)} for uid, art, kc, bg, tag, prof, motif in PROFILE]
assert [r["unitId"] for r in unit_profile] == [u["id"] for u in units]

# 가전 조각: 가전마다 하나(item id = 300 + 도감 순서). 중복 소환·상점·대청소·패스로 모아 돌파하거나, 없는 가전은 영입
SHARD_ID = {}
for u in units:
    sid = 300 + u["order"]; SHARD_ID[u["id"]] = sid
    items.append({"id": sid, "nameKey": s(f"item.{sid}.name", f"{S[u['nameKey']]} 조각"), "family": 0, "unitId": u["id"]})
# 장비 재화: 강화석(장비 레벨 업), 전용 정수(장비 소환 1회당 1 + 전용장비 각성 초과분 → 원하는 전용장비 교환)
items.append({"id": 401, "nameKey": s("item.401.name", "장비 강화석"), "family": 0})
items.append({"id": 402, "nameKey": s("item.402.name", "전용 정수"), "family": 0})
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
    "iron": [("pressRange", 1.6, "스팀 프레스 발동 거리(칸). 이 안에 지상 적이 있으면 발동"), ("pressRadius", 1.0, "내리누르는 범위(칸, 가장 앞선 적 기준)"),
             ("pressDmg", 260, "스팀 프레스 피해(성장 배율 적용)"), ("stun", 1.5, "기절(초). 보스는 1/3"), ("selfCool", 0.3, "발동 즉시 자기 과열 감소 비율"),
             ("armorPierce", 1.0, "적 방어 무시 비율(1 = 전부 무시)")],
    "stove": [("burnSec", 2.0, "평타 불길 지속(초)"), ("burnDps", 28, "평타 불길 초당 피해"), ("burnRadius", 0.55, "평타 불길 반경(칸)"),
              ("zoneRadius", 1.3, "센 불 화염 지대 반경(칸)"), ("zoneDps", 70, "센 불 초당 피해")],
    "boiler": [("armorBreak", 0.2, "평타 1회당 방어 감소(절대값, 0.2 = 20%p)"), ("scaldSec", 4.0, "방어 감소·화상 지속(초)"), ("burnDps", 18, "화상 초당 피해"),
               ("tick", 0.5, "고압 스팀 간격(초)"), ("skillDmg", 30, "고압 스팀 1회 피해"), ("skillBreak", 0.5, "고압 스팀 방어 감소"), ("skillSlow", 0.25, "고압 스팀 둔화율")],
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
    "iron":   [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 1, 0, 0.9)],
    "stove":  [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 0, 0.4, 0.9)],
    "boiler": [(1.0, 1.35, 0, 0, 1.0), (1.5, 1.75, 0, 0.4, 0.9)],
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
    # 10.16-f: 75스테이지 확장에 맞춰 Lv.20 이후 비용을 올림(무과금 기준 4구역까지 약 2주, 5구역 끝까지 약 4주 · tools/econ_model.py)
    {"id": 3, "lvMin": 20, "lvMax": 29, "coinBase": 6000, "coinStep": 600, "partBase": 55, "partStep": 6},
    {"id": 4, "lvMin": 30, "lvMax": 44, "coinBase": 18000, "coinStep": 1400, "partBase": 140, "partStep": 10},
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
    # 10.16-f 신규 (2~5구역)
    ("mite", "진드기", 70, 1.55, 14, .7, .16, 1, 0, "mite", 0),          # 빠르고 약한 떼
    ("hair", "머리카락 뭉치", 380, .7, 45, 1.0, .27, 1, 0, "hair", 0),   # 쓰러지면 먼지뭉치 2마리로 갈라짐
    ("scale", "물때", 900, .4, 70, 1.3, .30, 1, 0, "scale", 0),          # 방어 40%
    ("moth", "나방", 420, .9, 0, 1.0, .24, 1, 1, "moth", 0),              # 단단한 비행
    ("rust", "녹", 650, .55, 140, 1.2, .29, 1, 0, "rust", 0),             # 과열을 크게 쌓는 직진형, 방어 20%
    ("boss_mite", "진드기 여왕", 3000, .4, 160, 1.4, .42, 2, 0, "mite", 1),
    ("boss_hair", "털뭉치 대왕", 3600, .35, 200, 1.5, .44, 2, 0, "hair", 1),
    ("boss_scale", "물때 거인", 4200, .28, 220, 1.6, .46, 2, 0, "scale", 1),
    ("boss_moth", "나방 여왕", 3000, .45, 0, 1.0, .42, 2, 1, "moth", 1),
    ("boss_rust", "녹슨 대왕", 4000, .33, 260, 1.4, .44, 2, 0, "rust", 1),
    ("boss_emperor", "먼지 황제", 6500, .26, 260, 1.5, .5, 3, 0, "emperor", 1),
]
# 이동 성향: Detour = 돌아갈 길이 있으면 우회하고 막혔을 때만 공격, Breaker = 길을 막은 가전이 있으면 그대로 부수며 직진
ENEMY_AI = {"dust": "Detour", "grease": "Breaker", "mold": "Breaker", "fly": "Detour", "spore": "Breaker",
            "boss_dust": "Breaker", "boss_grease": "Breaker", "boss_mold": "Breaker",
            "mite": "Detour", "hair": "Breaker", "scale": "Breaker", "moth": "Detour", "rust": "Breaker",
            "boss_mite": "Breaker", "boss_hair": "Breaker", "boss_scale": "Breaker", "boss_moth": "Detour", "boss_rust": "Breaker", "boss_emperor": "Breaker"}
# 특수 능력: armor = 받는 피해 감소(0.4 = 40%, 다리미는 무시 · 보일러는 깎음) / split = 쓰러질 때 그 적 splitN마리로 갈라짐
#           summon = summonEvery초마다 그 적 summonN마리를 자기 자리에 부름
ENEMY_EX = {
    "scale": dict(armor=.4), "rust": dict(armor=.2), "hair": dict(split="dust", splitN=2),
    "boss_mite": dict(summon="mite", summonEvery=6, summonN=3), "boss_hair": dict(split="hair", splitN=3, armor=.1),
    "boss_scale": dict(armor=.45), "boss_moth": dict(summon="moth", summonEvery=9, summonN=2), "boss_rust": dict(armor=.3),
    "boss_emperor": dict(armor=.3, summon="dust", summonEvery=7, summonN=4),
}
enemies = []
for e in E:
    ex = ENEMY_EX.get(e[0], {})
    enemies.append({"id": e[0], "nameKey": s(f"enemy.{e[0]}.name", e[1]), "hp": e[2], "spd": e[3], "dmg": e[4], "itv": e[5],
                    "radius": e[6], "weight": e[7], "fly": e[8], "look": e[9], "boss": e[10],
                    "ai": ENUMS["EnemyAI"][ENEMY_AI[e[0]]], "armor": ex.get("armor", 0),
                    "split": ex.get("split", ""), "splitN": ex.get("splitN", 0),
                    "summon": ex.get("summon", ""), "summonEvery": ex.get("summonEvery", 0), "summonN": ex.get("summonN", 0)})

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
    # ---- 10.16-f 2구역: 2층 ----   R = 러그(지상 적 느려짐) / O = 기름 웅덩이(지상 적 빨라짐 · 근접 과열)
    ("bedroom", "bedroom", "침실", ["창문", "방문", "옷장"], "침대", "러그 칸: 지상 적 이동 −30%",
     "이불 속에 숨어 있던 진드기 떼가 쏟아져요. 진드기는 작고 빨라서 근접 가전 사이를 빠져나가요. 러그 칸에서는 적이 느려져요.",
     [[[0, 1], [1, 1], [1, 5], [2, 5], [2, 8], [3, 8], [3, 9]],
      [[3, 0], [3, 3], [4, 3], [4, 6], [3, 6], [3, 9]],
      [[6, 1], [5, 1], [5, 8], [3, 8], [3, 9]]], {(1, 3): "R", (4, 5): "R", (5, 6): "R", (2, 7): "R"}, [(0, 9), (6, 9)]),
    ("kids", "kids", "아이방", ["장난감 통", "창문", "미끄럼틀"], "장난감 상자", "러그 칸: 지상 적 이동 −30%",
     "장난감 사이로 길이 구불구불해요. 아래쪽 미끄럼틀 입구는 목표물과 가까우니 일찍 대비하세요.",
     [[[0, 0], [0, 4], [2, 4], [2, 7], [3, 7], [3, 9]],
      [[6, 0], [4, 0], [4, 2], [5, 2], [5, 5], [4, 5], [4, 8], [3, 8], [3, 9]],
      [[0, 9], [0, 6], [1, 6], [1, 8], [3, 8], [3, 9]]], {(0, 2): "R", (5, 4): "R", (2, 6): "R"}, [(6, 9), (6, 6)]),
    ("laundry", "laundry", "세탁실", ["세탁기 뒤", "환기창", "배수구"], "빨래 바구니", "물기 칸: 적 이동 +35%, 그 칸 근접 가전은 냉각 +60",
     "머리카락 뭉치는 쓰러지면 먼지뭉치 둘로 갈라져요. 막아 세운 자리 뒤에 원거리 가전을 받쳐 두세요.",
     [[[2, 0], [2, 2], [1, 2], [1, 6], [2, 6], [2, 8], [3, 8], [3, 9]],
      [[6, 2], [4, 2], [4, 5], [5, 5], [5, 8], [4, 8], [3, 8], [3, 9]],
      [[0, 6], [0, 8], [3, 8], [3, 9]]], {(1, 4): "W", (4, 4): "W", (5, 7): "W", (2, 7): "W"}, [(6, 9), (0, 9)]),
    # ---- 3구역: 지하 ----
    ("boiler", "boiler", "보일러실", ["배관", "굴뚝", "점검구"], "온수 탱크", "뜨거운 배관 칸: 근접 가전 초당 과열 +40",
     "딱딱한 물때가 배관을 타고 내려와요. 물때는 방어가 높아 피해가 덜 들어가요. 다리미는 방어를 무시하고, 보일러는 방어를 깎아요.",
     [[[0, 0], [1, 0], [1, 4], [3, 4], [3, 8], [3, 9]],
      [[6, 0], [6, 2], [5, 2], [5, 6], [4, 6], [4, 8], [3, 8], [3, 9]],
      [[0, 5], [0, 8], [3, 8], [3, 9]]], {(1, 2): "F", (5, 4): "F", (3, 6): "F"}, [(6, 9), (0, 9)]),
    ("storage", "storage", "창고", ["상자 더미", "선반 틈", "쥐구멍"], "추억 상자", "",
     "어두운 창고에서 나방이 날아올라요. 나방은 단단한 비행 적이라 근접 가전으로는 막을 수 없어요. 공중을 칠 수 있는 원거리 가전을 넉넉히.",
     [[[3, 0], [3, 2], [1, 2], [1, 5], [2, 5], [2, 8], [3, 8], [3, 9]],
      [[6, 1], [5, 1], [5, 4], [4, 4], [4, 7], [3, 7], [3, 9]],
      [[0, 5], [0, 8], [3, 8], [3, 9]]], {}, [(0, 9), (6, 9), (6, 8)]),
    ("garage", "garage", "차고", ["셔터", "공구함", "배수로"], "자동차", "기름 웅덩이: 지상 적 이동 +30%, 그 칸 근접 가전 초당 과열 +20",
     "녹은 느리지만 한 번 붙으면 과열을 크게 쌓아요. 기름 웅덩이를 피해 근접 가전을 세우세요.",
     [[[0, 1], [2, 1], [2, 3], [1, 3], [1, 6], [2, 6], [2, 8], [3, 8], [3, 9]],
      [[4, 0], [4, 4], [3, 4], [3, 6], [4, 6], [4, 8], [3, 8], [3, 9]],
      [[6, 2], [6, 7], [5, 7], [5, 8], [4, 8], [3, 8], [3, 9]]], {(1, 5): "O", (3, 5): "O", (6, 5): "O", (2, 7): "O"}, [(0, 9), (6, 9)]),
    # ---- 4구역: 꼭대기 ----
    ("attic", "attic", "다락방", ["천창", "들보", "환기구"], "앨범 상자", "러그 칸: 지상 적 이동 −30%",
     "오래된 다락에는 진드기·나방·머리카락이 한데 섞여 있어요. 적 종류가 많으니 근접과 원거리를 고르게.",
     [[[1, 0], [1, 3], [0, 3], [0, 6], [2, 6], [2, 8], [3, 8], [3, 9]],
      [[4, 0], [4, 2], [3, 2], [3, 5], [4, 5], [4, 7], [3, 7], [3, 9]],
      [[6, 3], [6, 8], [4, 8], [3, 8], [3, 9]]], {(3, 3): "R", (6, 6): "R", (1, 6): "R"}, [(0, 9), (6, 9)]),
    ("veranda", "veranda", "베란다", ["방충망", "빗물받이", "화분 뒤"], "화분", "물기 칸: 적 이동 +35%, 그 칸 근접 가전은 냉각 +60",
     "비 온 뒤 베란다는 곰팡이 천지예요. 물기 칸이 많아 적이 빠르지만, 거기 선 근접 가전은 잘 식어요.",
     [[[0, 0], [0, 3], [2, 3], [2, 5], [1, 5], [1, 8], [3, 8], [3, 9]],
      [[3, 0], [3, 1], [4, 1], [4, 4], [3, 4], [3, 7], [3, 9]],
      [[6, 0], [6, 6], [5, 6], [5, 8], [3, 8], [3, 9]]], {(1, 3): "W", (4, 3): "W", (6, 4): "W", (1, 7): "W", (5, 7): "W"}, [(0, 9), (6, 9)]),
    ("rooftop", "rooftop", "옥상 정원", ["물탱크", "옥상 문", "배수구"], "텃밭", "러그(잔디) 칸: 지상 적 이동 −30%",
     "탁 트인 옥상은 날벌레의 길이에요. 초파리와 나방이 쉬지 않고 날아들어요. 첫 입구 길이 가장 길어요.",
     [[[0, 4], [1, 4], [1, 1], [3, 1], [3, 3], [2, 3], [2, 7], [3, 7], [3, 9]],
      [[6, 0], [5, 0], [5, 5], [4, 5], [4, 8], [3, 8], [3, 9]],
      [[0, 9], [0, 7], [1, 7], [1, 8], [3, 8], [3, 9]]], {(2, 1): "R", (5, 3): "R", (2, 5): "R"}, [(6, 9)]),
    # ---- 5구역: 먼지 왕국 ----
    ("duct", "duct", "환기 덕트", ["왼쪽 덕트", "오른쪽 덕트", "팬 구멍"], "공기 필터", "",
     "먼지 왕국으로 가는 덕트예요. 지그재그 길은 길지만, 가운데 팬 구멍에서 곧장 내려오는 적을 조심하세요.",
     [[[0, 0], [2, 0], [2, 2], [0, 2], [0, 5], [2, 5], [2, 8], [3, 8], [3, 9]],
      [[6, 0], [4, 0], [4, 3], [6, 3], [6, 6], [4, 6], [4, 8], [3, 8], [3, 9]],
      [[3, 1], [3, 9]]], {}, [(0, 9), (6, 9)]),
    ("sewer", "sewer", "하수 배관", ["하수관", "맨홀", "역류구"], "배수 밸브", "물기 칸은 적이 빨라지고, 기름 웅덩이는 근접 가전이 달아올라요",
     "기름때·곰팡이·물때가 뒤엉킨 하수 배관이에요. 단단한 적이 많으니 방어를 깎을 준비를.",
     [[[0, 1], [1, 1], [1, 4], [0, 4], [0, 7], [2, 7], [2, 8], [3, 8], [3, 9]],
      [[3, 0], [3, 2], [2, 2], [2, 5], [3, 5], [3, 9]],
      [[6, 1], [5, 1], [5, 3], [6, 3], [6, 6], [4, 6], [4, 8], [3, 8], [3, 9]]], {(1, 2): "W", (2, 4): "W", (6, 5): "W", (0, 6): "O", (4, 7): "O"}, [(0, 9), (6, 9)]),
    ("throne", "throne", "먼지 왕좌", ["먼지 문", "왕좌 뒤", "먼지 폭포"], "청정 코어", "러그·기름·불·물기 칸이 모두 있어요",
     "모든 먼지의 우두머리, 먼지 황제가 기다려요. 지금까지 만난 적이 전부 나와요.",
     [[[0, 0], [0, 2], [2, 2], [2, 4], [1, 4], [1, 7], [2, 7], [2, 8], [3, 8], [3, 9]],
      [[3, 0], [3, 1], [4, 1], [4, 3], [3, 3], [3, 6], [4, 6], [4, 7], [3, 7], [3, 9]],
      [[6, 0], [6, 4], [5, 4], [5, 8], [4, 8], [3, 8], [3, 9]]], {(1, 5): "R", (5, 6): "O", (3, 5): "F", (6, 2): "W"}, [(0, 9), (6, 9)]),
]
maps = []
for mid, floor, name, spawns, goal, gim, desc, paths, ov, holes in MAP_DEF:
    maps.append({"id": mid, "floor": floor, "nameKey": s(f"map.{mid}.name", name),
                 "spawnKeys": [s(f"map.{mid}.spawn{i+1}", sp) for i, sp in enumerate(spawns)],
                 "goalKey": s(f"map.{mid}.goal", goal), "gimKey": s(f"map.{mid}.gim", gim) if gim else "",
                 "descKey": s(f"map.{mid}.desc", desc), "tiles": build_tiles(paths, ov, holes), "paths": paths,
                 "noDeployFromEntrance": 2, "noDeployBeforeGoal": 1})
MAP_PATHS = {m[0]: m[7] for m in MAP_DEF}

# ---------------- area / chapter (10.16-f) ----------------
# 구역(area) 5개 × 챕터 3개 × 스테이지 5개 = 75스테이지. 스테이지 탭 위쪽 구역 탭에 3챕터씩 묶어 보여준다
AREAS = [
    # id, name, short(탭), sub, desc, color, bg
    (1, "우리 집 1층", "1층", "거실 · 주방 · 욕실", "먼지와의 첫 전쟁. 가장 익숙한 곳부터 지켜요.", "#E8842A", "#FFF1E3"),
    (2, "2층 · 쉬는 곳", "2층", "침실 · 아이방 · 세탁실", "이불과 장난감 사이에 진드기와 털뭉치가 숨어 있어요.", "#8B6BD9", "#F0EBFB"),
    (3, "지하 · 기계실", "지하", "보일러실 · 창고 · 차고", "단단한 물때와 녹, 어둠 속을 나는 나방.", "#3D8BD9", "#E4EFFA"),
    (4, "꼭대기 · 바깥", "꼭대기", "다락방 · 베란다 · 옥상 정원", "바람을 타고 날벌레와 곰팡이가 몰려와요.", "#3FAE7A", "#E3F5EC"),
    (5, "먼지 왕국", "왕국", "환기 덕트 · 하수 배관 · 먼지 왕좌", "집 밑바닥, 모든 먼지가 태어나는 곳. 먼지 황제를 무찔러요.", "#B04A6E", "#F8E6EE"),
]
CHAPTER_DEF = [
    # ch, area, map, name, boss
    (1, 1, "living", "거실 사수", "boss_dust"), (2, 1, "kitchen", "주방 탈환", "boss_grease"), (3, 1, "bath", "욕실 대청소", "boss_mold"),
    (4, 2, "bedroom", "침실 진드기 소탕", "boss_mite"), (5, 2, "kids", "아이방 정리 작전", "boss_dust"), (6, 2, "laundry", "세탁실 털뭉치 전쟁", "boss_hair"),
    (7, 3, "boiler", "보일러실 물때 제거", "boss_scale"), (8, 3, "storage", "창고의 나방", "boss_moth"), (9, 3, "garage", "차고 녹 제거 작전", "boss_rust"),
    (10, 4, "attic", "다락방 대모험", "boss_mite"), (11, 4, "veranda", "베란다 곰팡이 전선", "boss_mold"), (12, 4, "rooftop", "옥상 정원 수호", "boss_moth"),
    (13, 5, "duct", "환기 덕트 돌파", "boss_rust"), (14, 5, "sewer", "하수 배관 잠입", "boss_hair"), (15, 5, "throne", "먼지 왕좌", "boss_emperor"),
]
assert len(CHAPTER_DEF) == CHAPTERS
CHAPTER_MAP = {c[0]: c[2] for c in CHAPTER_DEF}
CHAPTER_BOSS = {c[0]: c[4] for c in CHAPTER_DEF}
CHAPTER_AREA = {c[0]: c[1] for c in CHAPTER_DEF}
area = []
for aid, nm, short, sub, ds, col, bg in AREAS:
    chs = [c[0] for c in CHAPTER_DEF if c[1] == aid]
    area.append({"id": aid, "nameKey": s(f"area.{aid}.name", nm), "shortKey": s(f"area.{aid}.short", short), "subKey": s(f"area.{aid}.sub", sub), "descKey": s(f"area.{aid}.desc", ds),
                 "chFrom": min(chs), "chTo": max(chs), "color": col, "bg": bg})

# 챕터별 기본 웨이브 (t초, 적, 마릿수, 간격초, 입구 1~3). 입구 번호는 '열리는 순서'이고 실제 입구는 스테이지마다 바뀐다.
# 웨이브는 5~6초마다 시작해 쉬는 시간이 거의 없다. 처음 18초는 입구 1곳, 30초부터 3곳 모두.
# 같은 웨이브 안 간격은 0.8초 이상: 더 촘촘하면 단일 공격 가전의 공격 속도로는 다 맞히지 못해 체력과 무관하게 샌다(진드기만 예외: 약하고 빠른 떼).
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
# 4챕터부터는 '구성표'로 웨이브를 만든다: 박자(5~6초)마다 main 적 한 무리 + 홀수 박자엔 1초 뒤 extra 한 무리(다른 입구)
#   마릿수 = 기본 수 + 박자 순번에 따라 조금씩 증가. 같은 구성표면 매번 같은 웨이브가 나온다(무작위 없음)
WAVE_BASE_COUNT = {"dust": 4, "mite": 5, "fly": 3, "grease": 1, "mold": 1, "hair": 2, "scale": 1, "rust": 1, "moth": 2}
WAVE_GAP = {"dust": .9, "mite": .7, "fly": .9, "grease": 2.2, "mold": 2.2, "hair": 1.6, "scale": 2.6, "rust": 2.4, "moth": 1.5}
WAVE_HEAVY = {"grease", "mold", "hair", "scale", "rust", "moth"}
WAVE_PLAN = {
    # ch: (main 순환, extra 순환 [(적, 수)], 길이초)
    4: (["dust", "mite", "dust", "mite", "fly"], [("mite", 4), ("fly", 3), ("mold", 1), ("mite", 4)], 62),
    5: (["dust", "mite", "grease", "hair", "fly"], [("mite", 6), ("mold", 2), ("fly", 4), ("hair", 2)], 66),
    6: (["dust", "hair", "mite", "hair", "fly"], [("hair", 2), ("mite", 5), ("mold", 1), ("fly", 3)], 64),
    7: (["dust", "scale", "mite", "grease", "fly"], [("scale", 1), ("mite", 5), ("grease", 2), ("fly", 3)], 66),
    8: (["dust", "moth", "mite", "hair", "dust"], [("moth", 1), ("fly", 3), ("mite", 5), ("scale", 1)], 66),
    9: (["dust", "rust", "grease", "mite", "moth"], [("rust", 1), ("mite", 5), ("grease", 2), ("moth", 2)], 68),
    10: (["mite", "moth", "hair", "dust", "mite"], [("moth", 2), ("hair", 2), ("mite", 6), ("scale", 1)], 68),
    11: (["dust", "mold", "fly", "mite", "mold"], [("mold", 2), ("fly", 4), ("moth", 2), ("rust", 1)], 70),
    12: (["dust", "fly", "moth", "mite", "dust"], [("fly", 3), ("moth", 1), ("hair", 2), ("mold", 1)], 70),
    13: (["dust", "rust", "moth", "mite", "scale"], [("fly", 4), ("rust", 1), ("mite", 6), ("moth", 2)], 72),
    14: (["grease", "hair", "mold", "scale", "mite", "rust"], [("hair", 3), ("mold", 2), ("rust", 2), ("fly", 5)], 74),
    15: (["dust", "scale", "moth", "rust", "hair", "mite"], [("mold", 2), ("moth", 2), ("rust", 1), ("mite", 6), ("fly", 4)], 76),
}
ENT_ORDER = [1, 1, 1, 2, 1, 2, 3, 1, 2, 3, 2, 3, 1, 3, 2, 1, 3, 2]
WAVE_EARLY_SAFE = 16   # 이 시각 전에는 비행·단단한 적을 내지 않는다(배치가 덜 된 초반에 전력과 무관하게 새는 것을 막음)
def plan_wave(main, extra, dur):
    rows, t, k = [], 2, 0
    while t <= dur:
        ent = ENT_ORDER[k % len(ENT_ORDER)] if t >= 18 else 1
        en = main[k % len(main)]
        if t < WAVE_EARLY_SAFE and (en in WAVE_HEAVY or en in ("fly", "moth")): en = main[0]
        c = WAVE_BASE_COUNT[en] + (k // 6 if en in WAVE_HEAVY else k // 4)
        rows.append((t, en, c, WAVE_GAP[en] if c > 1 else 0, ent))
        if k % 2 == 1 and t >= WAVE_EARLY_SAFE:
            xe, xc = extra[(k // 2) % len(extra)]
            xent = ent % 3 + 1 if t >= 18 else 1
            xc = xc + (k // 8 if xe in WAVE_HEAVY else k // 5)
            rows.append((t + 1, xe, xc, WAVE_GAP[xe] if xc > 1 else 0, xent))
        t += 6 if k % 3 != 2 else 5
        k += 1
    return rows
for ch, (main, extra, dur) in WAVE_PLAN.items():
    BASE_WAVE[ch] = plan_wave(main, extra, dur)
# 1-1은 튜토리얼: 30초대로 짧고 가볍게, 입구 2곳만
STAGE_WAVE_OVERRIDE = {
    101: [(3, "dust", 3, 1.0, 1), (9, "dust", 4, .9, 1), (15, "dust", 3, .9, 1), (20, "fly", 2, .9, 2),
          (26, "dust", 4, .8, 1), (27, "fly", 2, .8, 2), (32, "mold", 1, 0, 1)],
}
LATE_RAMP_START, LATE_RAMP_HP = 0.4, 2.0   # 마지막 웨이브 시각의 40%부터 HP가 올라 마지막 웨이브는 ×3.0 (wave.hpMul)
ENEMY_BOSS = {e[0]: 1 for e in E if e[10]}
BOSS_DELAY, BOSS_ESCORT = 5, (4, 1.0)   # 마지막 웨이브 시작 후 보스 등장까지(초), 호위(챕터 첫 적, 마릿수, 간격)

# ---------------- 기준 계정 (10.16-f) ----------------
# 스테이지마다 '이 스테이지에 처음 도전하는 무과금 플레이어'의 평균 계정을 정해 두고, 그 계정으로 별 2개(청결도 6~8)가 나오게
# 적 HP를 맞춘다(tools/sim 의 전투 시뮬레이터). 기준 계정은 경제 모델(tools/econ_model.py)로 수입과 맞춘 값.
#   refLv: 편성 평균 레벨, refStar: 돌파(레벨 상한에 필요한 만큼), refGear: 공용장비 능력(공격력·과열 한도 +%)
REF_LV_PTS = [(1, 1), (15, 15), (30, 25), (45, 31), (60, 36), (75, 40)]
def ref_lv(n):
    for (a, la), (b, lb) in zip(REF_LV_PTS, REF_LV_PTS[1:]):
        if a <= n <= b: return la + (lb - la) * (n - a) / (b - a)
def ref_star(n): return max(0, math.ceil((ref_lv(n) - 20) / 5 - 1e-9))
def ref_gear(n): return 0.0 if n <= 15 else round(min(0.40, 0.40 * (n - 15) / 60), 3)
def ref_pow(n): return 1 + 0.05 * (ref_lv(n) - 1) + 0.12 * ref_star(n)
# 적 피해 배율: 1구역은 1(기존 밸런스 유지), 그 뒤는 기준 계정의 과열 한도 성장에 맞춰 따라 올림(지수 0.85 = 조금 덜 오름)
def stage_dmg_mul(n): return 1.0 if n <= 15 else round((ref_pow(n) * (1 + ref_gear(n)) / ref_pow(15)) ** 0.85, 2)
# 적 HP 배율 = 기준 계정 전투력 × 보정(HP_K). HP_K 는 전투 시뮬레이터로 맞춘 값(1구역은 10.14-k 값을 그대로 환산)
# 4챕터부터: tools/sim/run.mjs 로 스테이지마다 찾은 값(목표 청결도 일반 7 · 보스 6)을 챕터 중앙값으로 고르고, 각 스테이지 값의 0.78~1.1배 안으로 묶음(10.16-f)
# 10.16-l: 12챕터 손봄. 12-2 ×0.9(0.743→0.669) · 12-3 ×0.85(0.871→0.740) · 12-4 ×0.9(0.801→0.721). 기준 계정 청결도 7·5·4·5·6 → 7·7·7·7·6
#   HP 5% 차이로 청결도가 2~3칸 바뀌는 경계를 피해, 앞뒤 배수에서도 청결도 6~8이 나오는 값으로 고름(scan: ×1.0 · 0.95 · 0.9 · 0.85 · 0.8)
HP_K_LATE = [0.644, 0.557, 0.644, 0.584, 0.644, 1.314, 1.187, 1.314, 1.007, 1.314, 1.174, 1.108, 1.082, 0.888, 1.108, 1.165, 1.165, 1.253, 1.165, 0.993, 0.792, 0.637, 0.792, 0.846, 0.748, 1.238, 1.238, 1.238, 1.068, 1.238, 0.73, 0.832, 0.887, 0.832, 0.662, 0.84, 0.587, 0.773, 0.84, 0.72, 0.871, 0.669, 0.740, 0.721, 0.774, 0.81, 0.963, 0.963, 0.963, 0.648, 1.43, 1.705, 1.705, 1.569, 1.105, 1.067, 1.067, 0.949, 1.067, 1.067]   # tools/sim/adjust.mjs 최종 보정(청결도 5 미만 스테이지는 8%씩 완화) 반영
HP_K = [round(v / ref_pow(i + 1), 3) for i, v in enumerate(STAGE_HP_MUL[:15])] + HP_K_LATE

# ---------------- reward_group / condition / stage / wave ----------------
reward_group, conditions, stages, waves = [], [], [], []
def rg(group_id, rows):
    for i, (kind, ref, mn, mx, prob) in enumerate(rows):
        reward_group.append({"groupId": group_id, "seq": i + 1, "kind": kind, "refId": ref, "min": mn, "max": mx, "prob": prob})
    return group_id
ITEM, UNIT = ENUMS["RewardKind"]["Item"], ENUMS["RewardKind"]["Unit"]
# 구역 첫 챕터 보스(4-5 · 7-5 · 10-5) 첫 클리어: 새 가전(이미 있으면 조각)
AREA_UNIT_REWARD = {405: [(UNIT, "iron", 1, 1, 10000)], 705: [(UNIT, "stove", 1, 1, 10000)], 1005: [(ITEM, SHARD_ID["boiler"], 25, 25, 10000)]}

def idle_coin(n): return int(round(IDLE_COIN_BASE * IDLE_COIN_GROWTH ** min(n - 1, 14) * IDLE_LATE_GROWTH ** max(0, n - 15), -1))
def idle_part(n): return int(round(IDLE_PART_BASE * IDLE_PART_GROWTH ** min(n - 1, 14) * IDLE_LATE_GROWTH ** max(0, n - 15)))
def first_coin(n): return FIRST_COIN_BASE + 400 * min(n - 1, 14) + FIRST_COIN_LATE * max(0, n - 15)

n = 0
prev_stage = 0
for ch in range(1, CHAPTERS + 1):
    s(f"chapter.{ch}.name", CHAPTER_DEF[ch - 1][3])
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
            esc = "dust" if ch <= 3 else WAVE_PLAN[ch][0][0]
            rows.append((last + BOSS_DELAY, CHAPTER_BOSS[ch], 1, 0, rot(2)))
            rows.append((last + BOSS_DELAY + 2, esc, BOSS_ESCORT[0], BOSS_ESCORT[1], rot(1)))
            rows.append((last + BOSS_DELAY + 2, esc, BOSS_ESCORT[0], BOSS_ESCORT[1], rot(3)))
        # 후반 압박: 웨이브 시각이 뒤로 갈수록 적 HP를 올린다(배치가 다 찬 뒤 남는 전력을 강화에 쓰게 만드는 장치)
        t_last = max(t for (t, en, c, g, e) in rows if not ENEMY_BOSS.get(en))
        for seq, (t, en, c, g, e) in enumerate(rows):
            x = max(0.0, (t - LATE_RAMP_START * t_last) / ((1 - LATE_RAMP_START) * t_last)) if sid not in STAGE_WAVE_OVERRIDE else 0
            hm = 1.0 if ENEMY_BOSS.get(en) else round(1 + LATE_RAMP_HP * min(1.0, x) ** 1.5, 2)
            waves.append({"groupId": sid, "seq": seq + 1, "t": t, "enemy": en, "count": c, "gap": g, "entrance": e, "hpMul": hm})
        # 첫 클리어 보상
        fc = first_coin(n)
        first_rows = [(ITEM, 1, fc, fc, 10000),
                      (ITEM, 2, 150 if boss else 50, 150 if boss else 50, 10000)]   # 첫 클리어 크리스탈(10.15-b: 100/300 → 50/150)
        first_rows += [(ITEM, 401, 30 if boss else 10, 30 if boss else 10, 10000)]   # 장비 강화석
        if boss:
            bp = int(15 * (1 + 0.3 * (ch - 1)))
            first_rows += [(ITEM, p, bp, bp, 10000) for p in (101, 102, 103, 104)]
        first_rows += AREA_UNIT_REWARD.get(sid, [])
        first_group = rg(10000 + sid, first_rows)
        stages.append({
            "id": sid, "order": n, "chapter": ch, "area": CHAPTER_AREA[ch], "no": f"{ch}-{k}", "mapId": CHAPTER_MAP[ch], "waveGroup": sid,
            "boss": 1 if boss else 0, "hpMul": round(ref_pow(n) * HP_K[n - 1], 2), "dmgMul": stage_dmg_mul(n),
            "refLv": round(ref_lv(n)), "refStar": ref_star(n), "refGear": ref_gear(n),
            "unlockCond": cond_id, "firstRewardGroup": first_group,
            "idleCoin": idle_coin(n), "idlePart": idle_part(n),
            "idleCrystal": IDLE_CRYSTAL_BASE + (ch - 1) // 2,
            "startPower": STAGE_START_POWER.get(sid, 0),
        })
        prev_stage = sid
conditions.append({"id": 1076, "kind": ENUMS["CondKind"]["StageClear"], "p1": prev_stage, "p2": 0})   # 15-5 클리어(패키지용)

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
        # 10.16-f: 크리스탈은 챕터마다 +10%(15챕터 2.4배), 코인·부품은 +50%(레벨 비용 증가에 맞춤)
        mul = lambda ref: (1 + 0.1 * (ch - 1)) if ref == 2 else (1 + 0.5 * (ch - 1))
        gid = rg(20000 + ch * 10 + i + 1, [(k, ref, int(round(c * mul(ref), -1)), int(round(c * mul(ref), -1)), 10000) for (k, ref, c) in rows])
        star_reward.append({"id": ch * 10 + i + 1, "chapter": ch, "needStars": need, "rewardGroup": gid})

# ---------------- power_skill (전력 스킬, 10.16-h) ----------------
# 배치·강화를 다 하고 남는 전력을 쓰는 곳. 챕터 보스를 깨면 열리고, 뒤 챕터에서 단계가 오른다(전투 화면 아래 버튼).
#   storm(과전류 방출): 누른 자리 반경 radius칸에 dmg × 스테이지 적 HP 배율 피해 + stun초 기절(보스 1/3). 비행 적도 맞음, 방어 적용
#   boost(전력 증폭): dur초 동안 모든 가전 공격력 ×atkMul, 공격 간격 ×itvMul, 스킬 충전 속도 ×spMul
#   cost: 전력(1 = 100W), cd: 다시 쓰기까지(초), unlockCond: condition id(스테이지 클리어)
COND_CLEAR = lambda sid: 1000 + next(st["order"] for st in stages if st["id"] == sid) + 1   # 그 스테이지 클리어 = 다음 스테이지 열림 조건
POWER_SKILL = [
    # id, lv, 열리는 스테이지(클리어), cost, cd, radius, dmg, stun, dur, atkMul, itvMul, spMul
    ("storm", 1, 305, 16, 24, 1.5, 260, 1.5, 0, 1, 1, 1),
    ("storm", 2, 805, 16, 22, 1.8, 340, 1.8, 0, 1, 1, 1),
    ("storm", 3, 1305, 16, 20, 2.1, 440, 2.0, 0, 1, 1, 1),
    ("boost", 1, 605, 20, 32, 0, 0, 0, 8, 1.25, 0.8, 1.5),
    ("boost", 2, 1105, 20, 30, 0, 0, 0, 10, 1.35, 0.75, 1.75),
    ("boost", 3, 1405, 20, 28, 0, 0, 0, 12, 1.45, 0.7, 2.0),
]
power_skill = []
for pid, lv, sid, cost, cd, rad, dmg, stun, dur, am, im, sm in POWER_SKILL:
    power_skill.append({"id": pid, "lv": lv, "unlockCond": COND_CLEAR(sid), "unlockStage": sid, "cost": cost, "cd": cd, "radius": rad, "dmg": dmg, "stun": stun,
                        "dur": dur, "atkMul": am, "itvMul": im, "spMul": sm})
s("ps.storm.name", "과전류 방출"); s("ps.boost.name", "전력 증폭")
s("ps.storm.desc", "누른 자리 반경 {radius}칸에 과전류를 쏟아 피해를 주고 {stun}초 기절시켜요(보스는 짧게). 비행 적도 맞아요.")
s("ps.boost.desc", "{dur}초 동안 모든 가전의 공격력 +{atk_pct}%, 공격 속도 +{spd_pct}%, 스킬 충전 {sp}배.")

# ---------------- tutorial ----------------
# 규칙을 한 가지씩 나눠 가르친다. 스테이지가 아니라 '몇 번째 전투인지'에 묶는다:
#   cond 가 빈 그룹은 순서대로 전투 1판에 1그룹씩 열린다(이미 진행한 저장 데이터도 다음 전투부터 1그룹).
#   그 판에 조건이 안 와서 못 본 스텝은 다음 판들로 넘어가 조건이 오면 뜬다.
#   cond 가 있는 그룹(boss / map:kitchen / map:bath)은 그 조건의 전투에서 처음 한 번.
# 스텝마다 한 번만 뜨고(저장), 편성 탭에서 다시 보기 가능.
# trigger: Start 전투 시작 / Next 같은 그룹 앞 스텝 끝난 직후 / Time 경과 초 / Spawn 그 적 첫 등장 / Power 전력(1=100W) 이상
#          Heat 근접 가전 과열 비율 이상 / Skill 스킬 첫 발동(가전 ID, 비우면 아무거나) / Boss 보스 등장 / Leak 첫 누수 / Over 가전이 실제로 과열됨
# until:   Tap 확인 버튼 / Select 그 카드 선택 / Deploy 그 가전 배치 / Timer 초 뒤 자동으로 닫힘(10.16-m부터 쓰지 않음)
# 10.16-m: 모든 안내는 pause 1. 설명 안내는 '확인'을 누를 때까지, 배치 안내는 놓을 때까지 전투가 멈춘다(Timer → Tap)
# cond:    비움 / boss / map:맵ID / ps:전력스킬ID / over(어느 판이든 처음 과열될 때)
#          대상은 가전 ID 또는 역할: melee(덱의 첫 근접 가전) / ranged(덱의 첫 공중 공격 가능한 원거리 가전)
#          덱에 대상이 없으면 그 스텝은 미뤄진다. 문구의 {unit} 은 대상 가전 이름으로 바뀐다.
# pause:   1이면 안내가 떠 있는 동안 전투 시간이 멈춘다(배치·선택은 가능)
# focus:   card:가전ID 또는 card:melee/ranged / hud:life / hud:power / btn:speed / unit:heat (가장 뜨거운 근접 가전) / unit:over (과열된 가전)
# afterPrev: 1이면 같은 그룹 앞 스텝을 끝낸 뒤에만 조건을 본다(앞 스텝 대상이 덱에 없으면 건너뜀)
# tile:    배치 스텝에서 그 칸에만 놓을 수 있게 고정(빛나는 칸). tileStage 스테이지에서만 적용, 다른 판에선 자유 배치
#          고정 배치 스텝 동안에는 해당 카드만 고를 수 있다
TT_, TU_ = ENUMS["TutTrigger"], ENUMS["TutUntil"]
TUT = [
    # group, cond, trigger, param, until, untilParam, pause, focus, text, tile("x,y" 배치 칸 고정), tileStage(그 스테이지에서만 칸 고정)
    # 1-1은 전력에 맞춰 딱딱 진행: 먼지 등장 = 800W(청소기) → 600W(발전기) → 1,200W(에어컨, 곧 초파리)
    (1, "", "Spawn", "dust", "Deploy", "melee", 1, "card:melee", "먼지가 들어와요! {unit} 카드를 누르고 빛나는 칸에 놓아 길목을 막아 보세요.", "1,3", 101),
    (1, "", "Next", "", "Tap", "", 1, "", "먼지뭉치는 우회형이에요. 막힌 길은 피해 돌아가니, 놓친 먼지는 원거리 가전이 잡아야 해요.", "", 0),
    (1, "", "Power", 6, "Deploy", "gen", 1, "card:gen", "전력 600W가 모였어요. 발전기를 놓으면 전력이 더 빨리 차요. 빛나는 가구 칸에 놓아 보세요.", "0,1", 101, 1),
    (1, "", "Power", 12, "Deploy", "ranged", 1, "card:ranged", "전력 1,200W! 곧 초파리가 날아와요. 근접 가전은 공중을 못 때리니 {unit} 카드로 빛나는 칸에 놓아 잡아 보세요.", "3,4", 101, 1),
    (1, "", "Spawn", "fly", "Tap", "", 1, "", "초파리는 날아다녀요. 공중은 원거리 가전만 칠 수 있어요.", "", 0),
    (1, "", "Skill", "", "Tap", "", 1, "", "게이지가 차면 스킬이 저절로 나가요. 가전을 누르면 스킬 설명과 게이지를 볼 수 있어요.", "", 0),
    (1, "", "Spawn", "mold", "Deploy", "fan", 1, "card:fan", "곰팡이는 직진형(발밑 붉은 원)이라 피하지 않고 막아선 근접 가전을 밀어붙여 과열시켜요. 선풍기를 그 옆 빛나는 칸에 놓아 식혀 주세요.", "0,3", 101),
    (1, "", "Next", "", "Tap", "", 1, "", "기본은 다 익혔어요! 이제 남은 전력으로 원하는 가전을 자유롭게 더 놓아 보세요.", "", 0),
    (1, "", "Leak", "", "Tap", "", 1, "hud:life", "적이 목표물에 닿으면 청결도가 줄어요. 0이 되면 실패, 하나도 새지 않으면 별 3개예요.", "", 0),
    (2, "", "Time", 4, "Tap", "", 1, "hud:power", "같은 가전도 여러 대 놓을 수 있어요. 다만 한 대 놓으면 같은 가전 카드는 잠깐 쉬어요(카드에 남은 초 표시). 한 번에 최대 8대, 아래 멀티탭을 사면 10대까지 늘어나요.", "", 0),
    (2, "", "Skill", "gen", "Tap", "", 1, "", "발전기 스킬 풀가동은 전력을 한 번에 채워 줘요.", "", 0),
    (2, "", "Power", 25, "Tap", "", 1, "", "전력이 남아요! 놓인 가전을 누르면 남는 전력으로 그 판에서만 강화할 수 있어요(3단계까지). 3단계는 저지 +1, 사거리 증가 같은 보너스가 붙어요.", "", 0),
    (3, "", "Heat", 0.5, "Tap", "", 1, "unit:heat", "근접 가전이 적을 막으면 과열이 쌓여요. 가득 차면 5초 동안 멈추고 막던 적을 놓쳐요.", "", 0),
    (3, "", "Next", "", "Tap", "", 1, "card:fan", "선풍기를 근접 가전 옆에 두면 주변 가전을 식혀 줘요. 과열이 쌓이는 자리 옆에 놓아 보세요.", "", 0),
    (4, "", "Time", 12, "Tap", "", 1, "", "근접 가전 아래 주황 점은 저지 정원이에요. 정원이 차면 적이 뒤에 줄을 서고, 줄 선 적도 과열을 쌓아요.", "", 0),
    (4, "", "Time", 30, "Tap", "", 1, "", "가전을 누르면 철수할 수 있어요. 비용 절반을 돌려받고, 12초 뒤 다시 놓을 수 있어요.", "", 0),
    (4, "", "Time", 45, "Tap", "", 1, "btn:speed", "익숙해졌다면 오른쪽 위 1× 버튼으로 2배속을 써 보세요.", "", 0),
    (10, "boss", "Boss", "", "Tap", "", 1, "", "보스는 단단하고, 목표물에 닿으면 청결도를 2 깎아요. 근접 가전으로 막아 세우고 화력을 모으세요.", "", 0),
    (11, "map:kitchen", "Start", "", "Tap", "", 1, "", "주방의 가스레인지 칸(불꽃)에 선 근접 가전은 과열이 훨씬 빨리 쌓여요. 기름때는 직진형이니 불꽃 칸을 피해 막아 보세요.", "", 0),
    (12, "map:bath", "Start", "", "Tap", "", 1, "", "욕실의 물기 칸에서는 적이 빨라지지만, 그 칸에 선 근접 가전은 더 잘 식어요.", "", 0),
    # 10.16-f: 2~5구역 새 규칙
    (13, "map:bedroom", "Start", "", "Tap", "", 1, "", "침실에는 러그 칸(무늬 바닥)이 있어요. 러그 위 지상 적은 30% 느려져요. 근접 가전을 러그 근처에 세우면 오래 붙잡을 수 있어요.", "", 0),
    (13, "map:bedroom", "Spawn", "mite", "Tap", "", 1, "", "진드기는 작고 빨라요. 우회형이라 근접 가전 사이로 빠져나가니, 원거리 가전으로 길목을 덮어 주세요.", "", 0),
    (14, "map:laundry", "Spawn", "hair", "Tap", "", 1, "", "머리카락 뭉치는 쓰러지면 먼지뭉치 둘로 갈라져요. 갈라진 먼지까지 잡을 수 있게 뒤쪽을 받쳐 두세요.", "", 0),
    (15, "map:boiler", "Spawn", "scale", "Tap", "", 1, "", "물때는 방어가 40%라 피해가 덜 들어가요(방패 표시). 다리미는 방어를 무시하고, 보일러는 방어를 깎아요.", "", 0),
    (16, "map:storage", "Spawn", "moth", "Tap", "", 1, "", "나방은 단단한 비행 적이에요. 근접 가전은 공중을 못 치니, 공중을 치는 원거리 가전을 늘려 주세요.", "", 0),
    (17, "map:garage", "Start", "", "Tap", "", 1, "", "차고의 기름 웅덩이(검은 얼룩) 위에서는 지상 적이 빨라지고, 그 칸에 선 근접 가전은 과열이 쌓여요. 웅덩이를 피해 막아 보세요.", "", 0),
    (18, "boss", "Spawn", "boss_mite", "Tap", "", 1, "", "진드기 여왕은 몇 초마다 진드기를 불러내요. 여왕을 빨리 쓰러뜨릴수록 편해요.", "", 0),
    (18, "boss", "Spawn", "boss_moth", "Tap", "", 1, "", "나방 여왕은 날아다니는 보스예요. 원거리 가전만 칠 수 있어요.", "", 0),
    # 10.16-h 전력 스킬
    (19, "ps:storm", "Power", 16, "Tap", "", 1, "btn:storm", "새 전력 스킬 '과전류 방출'! 남는 전력 1,600W로 쓸 수 있어요. 버튼을 누른 뒤 적이 몰린 곳을 누르면 번개가 떨어져 피해와 기절을 줘요.", "", 0),
    (20, "ps:boost", "Power", 20, "Tap", "", 1, "btn:boost", "새 전력 스킬 '전력 증폭'! 2,000W로 잠깐 동안 모든 가전이 더 세고 빨라져요. 보스가 나오거나 적이 몰릴 때 눌러 보세요.", "", 0),
    # 10.16-m 처음 과열됐을 때(어느 판이든 한 번). {unit} = 과열된 가전
    (21, "over", "Over", "", "Tap", "", 1, "unit:over", "{unit} 과열! 적을 오래 막아 너무 뜨거워지면 5초 동안 멈추고, 그동안 막던 적은 그냥 지나가요. 옆에 선풍기를 두면 식고, 근접 가전을 여러 대 나눠 세우면 한 대에 열이 몰리지 않아요.", "", 0),
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
    ("levelup", [("result.lobby", "첫 청소 성공! 로비로 돌아가 가전을 키워 볼까요?"),
                 ("tab.roster", "가전 탭에서 가전을 키울 수 있어요."),
                 ("roster.unit:vac", "오늘 활약한 청소기를 눌러 보세요."),
                 ("detail.levelup", "레벨 업! 코인과 같은 계열 부품을 쓰면 공격력과 과열 한도가 올라가요."),
                 ("detail.close", "더 튼튼해졌어요. 닫고 다음 스테이지로 가 볼까요?"),
                 ("tab.home", "홈으로 돌아가요."),
                 ("home.cta", "다음 스테이지에 도전해 볼까요? 막히면 가전을 더 키우거나 자동 청소 보상을 받아 오세요.")]),
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
    ("robot", "leader", "radius", 1.6, "회전 반경(칸). 브러시는 이 반경의 55~100% 사이를 오가며 안쪽도 쓴다"), ("robot", "leader", "dmg", 50, "브러시 1회 피해(10.16-f: 40 → 50, 리더 3종 중 가장 약했음)"),
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
    # 10.16-f 신규 동료
    ("iron", "comp", "cd", 3.2, "스팀 프레스 간격(초)"), ("iron", "comp", "range", 2.6, "내리찍을 적을 찾는 거리"), ("iron", "comp", "radius", 1.3, "내리찍는 범위"),
    ("iron", "comp", "dmg", 85, "피해"), ("iron", "comp", "stun", 0.7, "기절(초, 보스 1/3)"), ("iron", "comp", "perLv", 0.4, "레벨당 강화"),
    ("stove", "comp", "cd", 4.5, "화염 지대 간격(초)"), ("stove", "comp", "range", 5, "사거리"), ("stove", "comp", "radius", 1.3, "화염 지대 반경"),
    ("stove", "comp", "dps", 34, "초당 피해"), ("stove", "comp", "dur", 3, "지속(초)"), ("stove", "comp", "perLv", 0.4, "레벨당 강화"),
    ("boiler", "comp", "cd", 3.0, "증기 고리 간격(초)"), ("boiler", "comp", "radius", 2.3, "반경(리더 중심)"), ("boiler", "comp", "dmg", 26, "피해"),
    ("boiler", "comp", "burn", 12, "화상 초당 피해(3초)"), ("boiler", "comp", "perLv", 0.4, "레벨당 강화"),
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
    ("mite", "enemy", "contactDps", 3, "진드기: 약하지만 빠른 떼(10.16-f)"), ("mite", "enemy", "xp", 1, ""), ("mite", "enemy", "speed", 2.15, ""),
    ("moth", "enemy", "contactDps", 6, "나방: 단단한 비행(10.16-f)"), ("moth", "enemy", "xp", 3, ""), ("moth", "enemy", "speed", 1.25, ""),
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
    ("gen", "자가 발전", "리더에 붙어 먼지 조각 흡입 범위 +{pickup_pct}%, 경험치 +{xp_pct}%(레벨당)"),
    ("iron", "스팀 프레스", "{cd}초마다 가까운 적 무리를 내리찍어 {dmg} 피해, {stun}초 기절"),
    ("stove", "화염 지대", "{cd}초마다 적이 몰린 곳에 {dur}초 불길(초당 {dps})"),
    ("boiler", "증기 고리", "리더에 붙어 {cd}초마다 주변 {radius}칸에 {dmg} 피해와 화상(초당 {burn})")]:
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
    (50, 360, "mite", 0.2, 1.6, 0.35, 1.2, 0),      # 10.16-f: 빠른 진드기 떼(무빙을 흔든다)
    (160, 360, "moth", 0.06, 0.5, 0.3, 0.9, 0),     # 10.16-f: 단단한 비행 적
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
s("banner.1.title", "겨울맞이 신상 입고")
s("banner.1.desc", "SSR 보일러가 새로 입고됐어요. 다리미·가스레인지도 함께! 10회 소환은 SR 이상 1개가 확정입니다.")
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
    "survStonePerMin": 3,            # 대청소 보상 판: 버틴 1분마다 장비 강화석
    "gearMaxLv": [5, 10, 15, 10],    # 장비 최대 강화 레벨(1·2·3등급 공용 / 4등급 전용)
    "gearStonePerLv": [2, 4, 7, 12], # 강화 L→L+1 강화석 = 값 × (L+1)
    "gearCoinPerLv": [300, 600, 1000, 2000],   # 강화 L→L+1 코인 = 값 × (L+1)
    "gearStatPerLv": 0.1,            # 레벨마다 기본 능력 +10%(기본 능력 대비)
    "gearDismantle": [3, 10, 30],    # 공용장비 분해 시 강화석(등급별) + 그 장비에 쓴 강화석의 gearRefund
    "gearRefund": 0.5,
    "gearFuseNeed": 3,               # 같은 등급 공용장비 이만큼 → 한 등급 위 공용장비 1개(종류 무작위). 1·2등급만
    "exclMaxStar": 3, "exclStarBonus": 0.25,   # 전용장비 각성 최대, ★마다 스킬 강화 효과 +25%
    "exclDupeEssence": 20,           # 각성이 꽉 찬 전용장비가 또 나오면 전용 정수
    "gearEssencePerPull": 1, "exclExchange": 80,   # 장비 소환 1회당 정수, 원하는 전용장비 1개 교환 비용
    "lvBonus": 0.05, "starBonus": 0.12,
    "idleCapHours": 12, "maxUnits": 8, "tutGuestMax": 3, "startPower": 10, "regenPerSec": 1, "redeployCooldown": 12,
    "deployCooldown": 8,         # 10.16-j: 같은 가전을 한 대 놓은 뒤 같은 가전을 다시 놓기까지 대기(초). 철수 대기(redeployCooldown)와 별개, 더 긴 쪽이 적용
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
    "rugTileEnemySpeedMul": 0.7,   # 10.16-f 러그(R) 칸 지상 적 이동 배율
    "oilTileEnemySpeedMul": 1.3,   # 10.16-f 기름 웅덩이(O) 칸 지상 적 이동 배율
    "oilTileHeatPerSec": 20,       # 10.16-f 기름 웅덩이(O) 칸 근접 가전 과열(초당)
    "armorMax": 0.8,               # 10.16-f 적 방어 상한(보일러 방어 감소로 음수까지 내려가면 받는 피해 증가, 하한 −0.5)
    "stunBossMul": 0.33,           # 10.16-f 보스 기절 시간 배율
}

# ---------------- shop (상점 · BM) ----------------
# payKind: Iap = 현금 결제(프로토타입은 테스트 결제창만 뜨고 실제 결제 없음), Crystal = 크리스탈 차감, Ad = 보상형 광고 시청 후 지급
# price  : Iap = 원(KRW), Crystal = 크리스탈 개수, Ad = 0
# limit  : None 무제한 / Daily 하루 n회(자정 초기화) / Weekly 주 n회(월요일 초기화) / Account 계정당 n회
# first  : 첫 구매 보너스 보상(크리스탈 상품의 '첫 구매 2배'), cooldown: 광고 상품 재시청 대기(초)
# special: idle:H = 현재 자동 청소 시급으로 H시간치 즉시 지급 / pass:month = 구독(즉시 지급 + 매일 받기) / pass:noads = 광고 제거
# unlockCond: condition id (0 = 항상)
# ---------------- gear (장비) ----------------
# 가전마다 칸 2개: [전용] 그 가전의 4등급 전용장비만 / [공용] 1~3등급 공용장비 하나
# 공용(1~3등급): 종류 3가지(공격 · 내구 · 충전), 수치는 '그냥 쓸 만한' 수준. 같은 장비가 여러 개 생길 수 있다(개체마다 강화 레벨)
# 전용(4등급): 가전마다 하나. 기본 능력 + 그 가전 스킬을 강화. 중복은 각성(★, 최대 exclMaxStar) → 그 이상은 전용 정수
# 능력: atk = 공격력·스킬 피해 +%, heat = 과열 한도 +%, sp = 스킬 충전 시간 −%. 레벨마다 기본 능력 × (1 + gearStatPerLv × 레벨)
# 스킬 강화(sk*): key = skill_param 키(또는 dur = 스킬 지속), op = add(더하기) / mul(곱하기), val = ★0 기준. ★마다 효과 +exclStarBonus
GEAR_COMMON = [
    # id, grade, type, name, atk, heat, sp
    (1101, 1, "atk", "소형 터보 모터", 0.06, 0, 0), (1102, 2, "atk", "고속 터보 모터", 0.12, 0, 0), (1103, 3, "atk", "BLDC 터보 모터", 0.20, 0, 0),
    (1201, 1, "heat", "알루미늄 방열판", 0, 0.08, 0), (1202, 2, "heat", "구리 방열판", 0, 0.16, 0), (1203, 3, "heat", "히트파이프 쿨러", 0, 0.26, 0),
    (1301, 1, "sp", "보조 배터리", 0, 0, 0.05), (1302, 2, "sp", "고속 충전기", 0, 0, 0.10), (1303, 3, "sp", "슈퍼 커패시터", 0, 0, 0.16),
]
GEAR_EXCL = {
    # unit: (name, [(key, op, val), ...], 설명)
    "vac":    ("터보 사이클론 노즐", [("pullRadius", "add", 0.5), ("pullDps", "mul", 1.4)], "강력 흡입 반경 +{0}칸, 흡입 피해 +{1}%"),
    "robot":  ("고속 충전 도크", [("skillAtkBonus", "add", 0.5), ("dur", "add", 2)], "급속 충전 공격력 +{0}%p, 지속 +{1}초"),
    "fridge": ("급속 냉매 코일", [("freezeRadius", "add", 0.5), ("freezeDps", "mul", 1.5)], "급속 냉동 반경 +{0}칸, 빙결 피해 +{1}%"),
    "ac":     ("인버터 코어", [("skillDmg", "mul", 1.5), ("skillSlow", "add", 0.15)], "급속 냉방 피해 +{0}%, 둔화 +{1}%p"),
    "micro":  ("터보 마그네트론", [("blastDmg", "mul", 1.5), ("blastRadius", "add", 0.3)], "띵! 폭발 피해 +{0}%, 반경 +{1}칸"),
    "washer": ("다이렉트 드라이브", [("skillDmg", "mul", 1.4), ("knockback", "add", 0.15)], "탈수 회오리 피해 +{0}%, 밀쳐내기 +{1}칸"),
    "pc":     ("수랭 쿨링 키트", [("skillAtk", "add", 0.2), ("skillRange", "add", 0.4)], "오버클럭 공격력 +{0}%p, 범위 +{1}칸"),
    "fan":    ("터보 블레이드", [("skillCoolMul", "add", 1.0), ("skillBurst", "add", 0.15)], "터보 바람 냉각 배율 +{0}, 즉시 냉각 +{1}%p"),
    "gen":    ("고효율 터빈", [("burst", "add", 4), ("regenPerSec", "mul", 1.3)], "풀가동 즉시 충전 +{0}W, 발전량 +{1}%"),
    "iron":   ("세라믹 열판", [("pressDmg", "mul", 1.5), ("stun", "add", 0.5)], "스팀 프레스 피해 +{0}%, 기절 +{1}초"),
    "stove":  ("이중 화구", [("zoneDps", "mul", 1.5), ("zoneRadius", "add", 0.3)], "센 불 피해 +{0}%, 범위 +{1}칸"),
    "boiler": ("콘덴싱 열교환기", [("skillBreak", "add", 0.2), ("skillDmg", "mul", 1.4)], "고압 스팀 방어 감소 +{0}%p, 피해 +{1}%"),
}
# 대청소 모드에서의 전용장비 효과(10.16-f). (slot, key, op, val) — slot: leader 리더 무기 / comp 동료. ★마다 +exclStarBonus
GEAR_EXCL_SURV = {
    "vac":    [("leader", "radius", "add", 0.3), ("leader", "dps", "mul", 1.3), ("comp", "radius", "add", 0.3), ("comp", "dps", "mul", 1.3)],
    "robot":  [("leader", "dmg", "mul", 1.3), ("leader", "count", "add", 1), ("comp", "dmg", "mul", 1.4), ("comp", "cd", "mul", 0.85)],
    "fridge": [("comp", "freeze", "add", 0.5), ("comp", "dmg", "mul", 1.4)],
    "ac":     [("leader", "dmg", "mul", 1.3), ("leader", "slow", "add", 0.1), ("comp", "dmg", "mul", 1.4), ("comp", "slow", "add", 0.1)],
    "micro":  [("comp", "dmg", "mul", 1.4), ("comp", "radius", "add", 0.3)],
    "washer": [("comp", "dmg", "mul", 1.4), ("comp", "count", "add", 1)],
    "pc":     [("comp", "cdMul", "mul", 1.25), ("comp", "dmgMul", "mul", 1.25)],
    "fan":    [("comp", "knock", "mul", 1.3), ("comp", "dmg", "mul", 1.5)],
    "gen":    [("comp", "xp", "mul", 1.3), ("comp", "pickup", "mul", 1.3)],
    "iron":   [("comp", "dmg", "mul", 1.4), ("comp", "stun", "add", 0.3)],
    "stove":  [("comp", "dps", "mul", 1.4), ("comp", "radius", "add", 0.3)],
    "boiler": [("comp", "dmg", "mul", 1.4), ("comp", "burn", "mul", 1.4)],
}
gear = []
for gid, gr, ty, nm, atk, heat, sp in GEAR_COMMON:
    gear.append({"id": gid, "grade": gr, "type": ty, "unitId": "", "atk": atk, "heat": heat, "sp": sp, "nameKey": s(f"gear.{gid}.name", nm), "effKey": "",
                 "sk1Key": "", "sk1Op": "", "sk1Val": 0, "sk2Key": "", "sk2Op": "", "sk2Val": 0})
for i, (uid, (nm, sks, eff)) in enumerate(GEAR_EXCL.items()):
    gid = 4001 + i
    row = {"id": gid, "grade": 4, "type": "excl", "unitId": uid, "atk": 0.15, "heat": 0.15, "sp": 0, "nameKey": s(f"gear.{gid}.name", nm), "effKey": s(f"gear.{gid}.eff", eff)}
    for j in range(2):
        k, op, v = sks[j] if j < len(sks) else ("", "", 0)
        row.update({f"sk{j+1}Key": k, f"sk{j+1}Op": op, f"sk{j+1}Val": v})
    gear.append(row)
gear_surv = [{"gearId": 4001 + i, "unitId": uid, "slot": sl, "key": k, "op": op, "val": v}
             for i, uid in enumerate(GEAR_EXCL) for (sl, k, op, v) in GEAR_EXCL_SURV.get(uid, [])]
assert set(GEAR_EXCL) == {u["id"] for u in units}
for g, nm in [(1, "일반"), (2, "고급"), (3, "희귀"), (4, "전용")]:
    s(f"gear.grade.{g}", nm)
# 장비 소환: 등급 확률(만분율, 합 10000). 4등급은 전용장비(가전 수만큼) 중 하나(균등). 10회는 3등급 이상 1개 확정
gear_gacha = [
    {"id": 1, "nameKey": s("gearGacha.1.name", "장비 1회"), "costItem": 2, "costCount": 100, "pulls": 1, "guaranteeGrade": 0, "w1": 5500, "w2": 3000, "w3": 1200, "w4": 300},
    {"id": 2, "nameKey": s("gearGacha.2.name", "장비 10회"), "costItem": 2, "costCount": 1000, "pulls": 10, "guaranteeGrade": 3, "w1": 5500, "w2": 3000, "w3": 1200, "w4": 300},
]

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
    (205, "Daily", "Ad", 0, [], "Daily", 1, None, "gearbox:1", 0, 0, "무료", "무료 장비 상자", "1등급 70% · 2등급 30%"),
    (231, "Daily", "Crystal", 300, [], "Daily", 1, None, "gearbox:2", 0, 0, "", "고급 장비 상자", "2등급 70% · 3등급 30%"),
    (232, "Daily", "Crystal", 100, [(ITEM, 401, 30, 30, 10000)], "Daily", 2, None, "", 0, 0, "", "장비 강화석 30", ""),
    (211, "Daily", "Crystal", 200, COIN(20000), "Daily", 3, None, "", 0, 0, "", "코인 20,000", ""),
    (212, "Daily", "Crystal", 300, PARTS(30), "Daily", 2, None, "", 0, 0, "", "부품 묶음", "모든 계열 부품 30개씩"),
    (213, "Daily", "Crystal", 150, [], "Daily", 1, None, "idle:4", 0, 1, "", "자동 청소 4시간 즉시", "지금 시급으로 4시간치를 바로 받아요"),
    (301, "Package", "Iap", 4900, CRY(500) + COIN(10000) + PARTS(20), "Account", 1, None, "", 0, 0, "1회 한정", "첫 출동 패키지", "처음 한 번만. 크리스탈 500 · 코인 10,000 · 부품 각 20"),
    (302, "Package", "Iap", 5900, CRY(300), "None", 0, None, "pass:month", 0, 0, "30일", "가전 관리 구독", "바로 크리스탈 300, 30일 동안 매일 상점에서 크리스탈 100 (총 3,300)"),
    (303, "Package", "Iap", 9900, CRY(300), "Account", 1, None, "pass:noads", 0, 0, "영구", "광고 제거 패스", "광고 없이 무료 보상을 바로 받아요. 크리스탈 300 포함"),
    (304, "Package", "Iap", 9900, CRY(300) + COIN(30000) + PARTS(40), "Weekly", 1, None, "", 0, 0, "주 1회", "주간 성장 패키지", "크리스탈 300 · 코인 30,000 · 부품 각 40"),
    (306, "Package", "Iap", 11000, [], "None", 0, None, "pass:season", 0, 0, "시즌", "프리미엄 패스", "이번 시즌 패스의 유료 보상 줄을 열어요. 이미 지난 레벨 보상도 바로 받을 수 있어요"),
    (305, "Package", "Iap", 19900, CRY(1200) + COIN(30000) + PARTS(50), "Account", 1, None, "", 0, 1006, "1-5 클리어", "거실 사수 기념 패키지", "1-5를 깨면 열려요. 크리스탈 1,200 · 코인 30,000 · 부품 각 50"),
    # 10.16-f: 구역을 끝낼 때마다 열리는 1회 한정 패키지(성장 비용이 커지는 시점에 맞춤)
    (307, "Package", "Iap", 29000, CRY(1500) + COIN(120000) + PARTS(150) + [(ITEM, 401, 150, 150, 10000)], "Account", 1, None, "", 0, 1031, "6-5 클리어", "2층 정복 기념 패키지", "6-5를 깨면 열려요. 크리스탈 1,500 · 코인 120,000 · 부품 각 150 · 강화석 150"),
    (308, "Package", "Iap", 39000, CRY(2000) + COIN(250000) + PARTS(300) + [(ITEM, 401, 300, 300, 10000)], "Account", 1, None, "", 0, 1046, "9-5 클리어", "지하 정복 기념 패키지", "9-5를 깨면 열려요. 크리스탈 2,000 · 코인 250,000 · 부품 각 300 · 강화석 300"),
    (309, "Package", "Iap", 49000, CRY(2500) + COIN(400000) + PARTS(450) + [(ITEM, 401, 500, 500, 10000), (ITEM, 402, 40, 40, 10000)], "Account", 1, None, "", 0, 1061, "12-5 클리어", "꼭대기 정복 기념 패키지", "12-5를 깨면 열려요. 크리스탈 2,500 · 코인 400,000 · 부품 각 450 · 강화석 500 · 전용 정수 40"),
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
    "enums": ENUMS, "config": config, "string_ko": S, "item": items, "unit": units, "unit_star": unit_star, "unit_profile": unit_profile, "unit_levelup": unit_levelup,
    "enemy": enemies, "map": maps, "stage": stages, "wave": waves, "reward_group": reward_group,
    "condition": conditions, "content_unlock": content_unlock, "star_reward": star_reward, "tutorial": tutorial, "skill_param": skill_param, "unit_boost": unit_boost,
    "guide": guide, "area": area, "gear_surv": gear_surv, "power_skill": power_skill, "shop_product": shop_product, "attendance": attendance, "pass_level": pass_level, "surv_param": surv_param, "surv_combo": surv_combo, "surv_set": surv_set, "surv_wave": surv_wave, "surv_reward": surv_reward, "gacha": gacha, "gacha_pool": gacha_pool, "gear": gear, "gear_gacha": gear_gacha,
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
