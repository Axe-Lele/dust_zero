"""13_Character.xlsx 를 현재 코드에 박혀 있던 값으로 1회 생성한다.

캐릭터를 추가할 때 C# 을 건드려야 하는 두 가지가 남아 있었다.
  1. 이미지  — 어떤 가전이 어떤 치비/일러스트를 쓰는지가 SpriteLibrary 에 손으로 들어 있었다.
  2. 스킬 동작 — UnitBehaviours / SurvivorSim 의 switch 가 가전 id 를 직접 비교했다.

동작 자체는 코드가 맞다(수치가 아니라 로직이다). 하지만 "이 가전이 어느 동작을 쓰는가" 는
데이터다. 그 연결과 이미지 세트를 이 테이블로 뺀다.

한 번만 실행한다. 이후 편집은 엑셀이나 tools/dz_studio.py 로 한다.
    python GameData/tools/seed_character_table.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import xlsx_io  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, ".."))
TABLES = os.path.join(ROOT, "tables")
OUT = os.path.join(TABLES, "13_Character.xlsx")

# 12종의 치비는 export_sprites.py 가 Art/Sprites/unit_<id>.png 로 뽑아 둔 것이고,
# 일러스트 6종은 Art/Illust/<id>_<슬롯>.png 로 변환돼 있다. 이름 규칙이 이미 일정하므로
# 시드는 규칙대로 채우되, 테이블에서는 파일명을 직접 바꿀 수 있게 열로 남긴다.
UNITS = ["vac", "robot", "fridge", "ac", "micro", "washer",
         "pc", "fan", "gen", "iron", "stove", "boiler"]

# RoomView.SpeedOf 에 박혀 있던 값.
ROOM_SPEED = {"robot": 0.26, "fridge": 0.12, "washer": 0.12}
DEFAULT_ROOM_SPEED = 0.18

# UnitBehaviours.Act / Ready / OnSkill 의 switch 와 TableValidator 의 필수 키 표.
# 빈 칸은 "전용 동작 없음" = 평범한 사거리 공격.
DEFENSE = {
    "vac": "vac", "robot": "melee", "fridge": "fridge", "ac": "ac", "micro": "micro",
    "washer": "washer", "pc": "pc", "fan": "fan", "gen": "gen",
    "iron": "", "stove": "", "boiler": "",
}
SKP_KEYS = {
    "vac": "pullRadius,pullDps,pullSpeed,holdDist",
    "robot": "triggerHeat,skillAtkBonus",
    "fridge": "freezeRadius,freezeDps,selfCool",
    "ac": "tick,skillDmg,skillSlow,skillSlowSec,atkSlow,atkSlowSec",
    "micro": "rangeBonus,blastRadius,blastDmg,noTargetRefund",
    "washer": "tick,skillDmg,knockback,bossKnockback,splashRadius",
    "pc": "buffRange,buffAtk,skillRange,skillAtk,skillIntervalMul",
    "fan": "coolRange,skillRange,skillCoolMul,skillBurst,triggerHeat",
    "gen": "regenPerSec,skillRegenMul,burst",
}
# SurvivorSim 의 리더 switch 와 동료 switch, 그리고 SupportCompanions 집합.
SURV_LEADER = {"vac": "vac", "robot": "robot", "ac": "ac"}
SURV_COMP = {"vac": "vac", "robot": "robot", "fridge": "fridge", "ac": "ac",
             "micro": "micro", "washer": "washer", "fan": "fan"}
SUPPORT = {"pc", "fan", "gen"}


def art_rows():
    rows = []
    for uid in UNITS:
        rows.append({
            "id": uid,
            "chibi": f"unit_{uid}",
            "full": f"{uid}_full",
            "turn": f"{uid}_turn",
            "keys": f"{uid}_keys",
            "face1": f"{uid}_face1",
            "face2": f"{uid}_face2",
            "face3": f"{uid}_face3",
            "roomSpeed": ROOM_SPEED.get(uid, DEFAULT_ROOM_SPEED),
        })
    return rows


def behaviour_rows():
    rows = []
    for uid in UNITS:
        rows.append({
            "id": uid,
            "defense": DEFENSE.get(uid, ""),
            "survLeader": SURV_LEADER.get(uid, ""),
            "survComp": SURV_COMP.get(uid, ""),
            "support": 1 if uid in SUPPORT else 0,
            "skp": SKP_KEYS.get(uid, ""),
        })
    return rows


def main():
    if os.path.exists(OUT):
        print(f"이미 있습니다: {OUT}\n덮어쓰지 않습니다. 편집은 엑셀이나 dz_studio 로 하세요.")
        return 1
    tables = {"unit_art": art_rows(), "unit_behaviour": behaviour_rows()}
    xlsx_io.write_workbooks(tables, TABLES, only={"13_Character.xlsx"})
    print(f"생성: {OUT}")
    print("다음: python GameData/tools/sync_to_unity.py")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
