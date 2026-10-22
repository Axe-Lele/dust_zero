# -*- coding: utf-8 -*-
"""바뀐 내용을 계산하고 합치는 층. 화면도 전송 방법도 여기서는 모른다.

왜 갈라 놓았나
  지금은 패치를 파일로 주고받지만 나중에는 네트워크로 바꿀 것이다. 그때 바꿔야 하는
  것은 **전송(dz_transport.py)** 뿐이어야 한다. 무엇이 바뀌었는지 계산하고, 충돌을
  가려내고, 사람이 읽을 수 있는 차이를 만드는 일은 전송 방법과 아무 상관이 없다.

  마찬가지로 차이를 **그리는** 일(dz_review.py)도 여기 넣지 않는다. 여기서는 차이를
  '데이터' 로만 만들고, Tk 창이든 나중의 웹 화면이든 같은 데이터를 받아 그린다.

핵심 개념
  Snapshot  어느 시점의 테이블 + 그림 해시. 패치는 두 스냅샷의 차이다.
  Patch     기준점(parent) 대비 바뀐 행과 그림. 내용 해시가 곧 id 다.
  Change    한 줄의 변화. 사람이 읽을 라벨과 전/후 값, 숫자면 증감까지 들고 있다.
  Plan      받은 패치를 지금 테이블에 맞춰 본 결과. 항목마다 적용/충돌 상태가 붙는다.

엑셀은 병합이 안 된다. 그래서 파일을 통째로 주고받지 않고 **행 단위**로 비교하며,
합칠 때는 세 쪽(기준점 · 받은 것 · 지금)을 보고 양쪽이 같은 칸을 서로 다르게 고쳤으면
덮어쓰지 않는다.
"""
import datetime
import hashlib
import json

# 패치가 실어 나르는 시트. 기획자가 고칠 수 있는 범위와 같다.
SHARED_SHEETS = [
    "unit", "unit_art", "unit_behaviour", "string_ko", "item", "gacha_pool",
    "shop_product", "reward_group",
    "stage", "wave", "map", "map_tile", "map_entrance", "enemy",
]

# 행을 알아보는 키. 앞에서부터 있는 것을 쓴다.
PRIMARY_KEYS = ("id", "groupId", "poolId", "unitId", "stageId", "waveId", "mapId")
# 한 키로 행이 안 갈리는 시트가 있다(reward_group 은 groupId 하나에 여러 줄).
SECONDARY_KEYS = ("seq", "unitId", "order", "idx", "no")

# 화면에 그대로 띄워도 뜻이 통하는 칸 이름.
FIELD_LABEL = {
    "atk": "공격력", "cost": "배치 비용", "heat": "과열 한도", "itv": "공격 간격",
    "range": "사거리", "block": "저지 수", "sp": "스킬 충전", "dur": "스킬 지속",
    "rarity": "등급", "family": "계열", "tile": "배치", "order": "순서",
    "price": "가격", "payKind": "결제 수단", "limitType": "구매 제한",
    "limitCount": "제한 횟수", "cooldown": "재구매 대기", "rewardGroup": "보상 묶음",
    "min": "최소", "max": "최대", "prob": "확률", "refId": "대상", "kind": "종류",
    "weight": "가중치", "hp": "체력", "spd": "속도", "dmg": "피해", "armor": "방어",
    "roomSpeed": "로비 걷기 속도", "lineCount": "대사 수",
    "defense": "디펜스 동작", "survLeader": "대청소 리더", "survComp": "대청소 동료",
    # 스테이지 · 웨이브
    "hpMul": "적 체력 배수", "dmgMul": "적 피해 배수", "refLv": "권장 레벨",
    "refStar": "권장 돌파", "refGear": "권장 장비", "startPower": "시작 전력",
    "idleCoin": "방치 코인", "idlePart": "방치 부품", "idleCrystal": "방치 크리스탈",
    "chapter": "챕터", "area": "구역", "no": "번호", "mapId": "맵", "boss": "보스 판",
    "waveGroup": "웨이브 묶음", "unlockCond": "해금 조건",
    "firstRewardGroup": "첫 클리어 보상",
    "t": "등장 시각", "enemy": "적", "count": "마리 수", "gap": "등장 간격",
    "entrance": "입구", "seq": "순서",
}


# ---------------------------------------------------------------- 행 키

def row_key(row):
    """한 행을 알아볼 수 있는 문자열. 못 만들면 None."""
    for k in PRIMARY_KEYS:
        v = row.get(k)
        if v in (None, ""):
            continue
        extra = [str(row[x]) for x in SECONDARY_KEYS if x in row and x != k
                 and row[x] not in (None, "")]
        return "%s=%s%s" % (k, v, ("|" + "|".join(extra)) if extra else "")
    return None


def index_rows(rows):
    """키 -> 행. 키를 못 만든 행은 따로 돌려준다."""
    out, loose = {}, []
    for r in rows or []:
        k = row_key(r)
        if k is None or k in out:
            loose.append(r)
        else:
            out[k] = r
    return out, loose


# ---------------------------------------------------------------- 값 비교

def same(a, b):
    """1 과 1.0, None 과 '' 은 같은 값으로 본다. 엑셀 왕복에서 늘 생긴다."""
    if a == b:
        return True
    if a in (None, "") and b in (None, ""):
        return True
    try:
        return float(a) == float(b)
    except (TypeError, ValueError):
        return False


def as_number(v):
    try:
        f = float(v)
    except (TypeError, ValueError):
        return None
    return f


def show(v):
    if v in (None, ""):
        return "(없음)"
    n = as_number(v)
    if n is not None:
        return str(int(n)) if n == int(n) else ("%g" % n)
    text = str(v)
    return text if len(text) <= 48 else text[:47] + "…"


class FieldChange:
    """칸 하나의 변화. 숫자면 증감까지 들고 있다 — 화면이 계산하지 않게."""

    __slots__ = ("field", "label", "before", "after", "delta", "direction")

    def __init__(self, field, before, after):
        self.field = field
        self.label = FIELD_LABEL.get(field, field)
        self.before = before
        self.after = after
        a, b = as_number(before), as_number(after)
        if a is not None and b is not None:
            self.delta = b - a
            self.direction = "up" if b > a else ("down" if b < a else "same")
        else:
            self.delta = None
            self.direction = "text"

    def text(self):
        """'공격력  75 → 82  (▲ +7)' 한 줄."""
        line = "%s  %s → %s" % (self.label, show(self.before), show(self.after))
        if self.delta:
            arrow = "▲" if self.delta > 0 else "▼"
            d = self.delta
            amount = str(int(d)) if d == int(d) else ("%g" % d)
            line += "  (%s %s%s)" % (arrow, "+" if d > 0 else "", amount)
        return line

    def to_json(self):
        return {"field": self.field, "label": self.label, "before": self.before,
                "after": self.after, "delta": self.delta, "direction": self.direction}


class Change:
    """한 행(또는 한 문자열 키)의 변화."""

    def __init__(self, sheet, key, kind, fields=None, label=None, row=None):
        self.sheet = sheet
        self.key = key
        self.kind = kind                 # add | edit | remove
        self.fields = fields or []       # FieldChange 목록 (edit 일 때)
        self.label = label or key        # 사람이 읽는 이름
        self.row = row                   # 적용할 행 전체 (add/edit)
        self.status = "ok"               # ok | conflict
        self.note = ""
        self.selected = True             # 화면에서 끌 수 있다

    def summary(self):
        if self.kind == "add":
            return "추가"
        if self.kind == "remove":
            return "삭제"
        if len(self.fields) == 1:
            return self.fields[0].text()
        return "%d개 칸 변경" % len(self.fields)

    def to_json(self):
        return {"sheet": self.sheet, "key": self.key, "kind": self.kind,
                "label": self.label, "status": self.status, "note": self.note,
                "fields": [f.to_json() for f in self.fields]}


# ---------------------------------------------------------------- 라벨

def make_labeller(tables):
    """id 대신 '청소기' 처럼 읽히게. 화면과 로그가 같은 이름을 쓰도록 여기서 만든다."""
    strings = tables.get("string_ko") or {}
    unit_name = {}
    for r in tables.get("unit") or []:
        unit_name[str(r.get("id"))] = strings.get(r.get("nameKey"), str(r.get("id")))
    shop_name = {}
    for r in tables.get("shop_product") or []:
        shop_name[str(r.get("id"))] = strings.get(r.get("nameKey"), str(r.get("id")))
    # stage 에는 nameKey 가 없다. 번호와 맵 이름을 붙여 '2-4 주방' 으로 읽히게 한다.
    map_name = {r["id"]: strings.get(r.get("nameKey"), r["id"])
                for r in tables.get("map") or []}
    stage_name = {}
    for r in tables.get("stage") or []:
        stage_name[str(r.get("id"))] = "%s %s" % (
            r.get("no") or r.get("id"), map_name.get(r.get("mapId"), ""))

    def label(sheet, key, row=None):
        ident = key.split("=", 1)[-1].split("|", 1)[0] if "=" in key else key
        if sheet in ("unit", "unit_art", "unit_behaviour"):
            return "%s (%s)" % (unit_name.get(ident, ident), ident)
        if sheet == "shop_product":
            return "%s (id %s)" % (shop_name.get(ident, ident), ident)
        if sheet == "stage":
            return stage_name.get(ident, ident).strip() or ident
        if sheet == "wave":
            # groupId 는 곧 스테이지 번호다. 'wave 101=|3' 보다 '1-1 거실 3번째 줄' 이 낫다.
            seq = key.split("|", 1)[-1] if "|" in key else "?"
            return "%s 웨이브 %s번째" % (stage_name.get(ident, ident).strip() or ident, seq)
        if sheet == "gacha_pool" and row is not None:
            u = str(row.get("unitId"))
            return "소환 풀 · %s" % unit_name.get(u, u)
        if sheet == "string_ko":
            return key
        return "%s %s" % (sheet, ident)

    return label


# ---------------------------------------------------------------- 스냅샷 / 패치

def snapshot(tables, art):
    """비교의 기준이 되는 가벼운 사본."""
    return {"tables": {s: tables[s] for s in SHARED_SHEETS if s in tables},
            "art": dict(art)}


def patch_id(payload):
    raw = json.dumps(payload, ensure_ascii=False, sort_keys=True).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()[:16]


def make_patch(base, now, author="", parent=None):
    """기준점 대비 바뀐 것만 담은 패치. 돌려주는 것은 그냥 dict 라 그대로 직렬화된다."""
    old_t, new_t = base.get("tables", {}), now.get("tables", {})
    changes = {}

    for sheet in SHARED_SHEETS:
        b = new_t.get(sheet)
        if b is None:
            continue
        a = old_t.get(sheet)
        if isinstance(b, dict):
            setd = {k: v for k, v in b.items() if not same((a or {}).get(k), v)}
            gone = [k for k in (a or {}) if k not in b]
            if setd or gone:
                changes[sheet] = {"kind": "map", "set": setd, "remove": gone}
        else:
            ai, _ = index_rows(a)
            bi, loose = index_rows(b)
            if loose:
                # 키를 못 만든 행이 있는 시트는 통째로 싣는다. 반만 보내면 합칠 때
                # 어느 행이 어디로 가는지 알 수 없다.
                changes[sheet] = {"kind": "whole", "rows": b}
                continue
            upsert = [r for k, r in bi.items()
                      if k not in ai or any(not same(ai[k].get(f), r.get(f))
                                            for f in set(ai[k]) | set(r))]
            removed = [k for k in ai if k not in bi]
            if upsert or removed:
                changes[sheet] = {"kind": "rows", "upsert": upsert, "remove": removed}

    art_old, art_new = base.get("art", {}), now.get("art", {})
    art_changed = sorted(k for k, v in art_new.items() if art_old.get(k) != v)
    art_gone = sorted(k for k in art_old if k not in art_new)

    payload = {
        "tables": changes,
        "baseTables": {s: old_t.get(s) for s in changes},   # 충돌 검사용 기준값
        "art": art_changed,
        "artRemoved": art_gone,
    }
    payload["id"] = patch_id(payload)
    payload["parent"] = parent
    payload["author"] = author
    payload["made"] = datetime.datetime.now().isoformat(timespec="seconds")
    return payload


def is_empty(patch):
    return not patch.get("tables") and not patch.get("art") and not patch.get("artRemoved")


# ---------------------------------------------------------------- 적용 계획

class Plan:
    """받은 패치를 지금 테이블에 맞춰 본 결과. 적용 전에 화면에 그대로 띄운다."""

    def __init__(self, patch, changes, art_changed, art_removed):
        self.patch = patch
        self.changes = changes
        self.art_changed = art_changed
        self.art_removed = art_removed

    @property
    def conflicts(self):
        return [c for c in self.changes if c.status == "conflict"]

    def counts(self):
        n = {"add": 0, "edit": 0, "remove": 0, "conflict": 0}
        for c in self.changes:
            n[c.kind] += 1
            if c.status == "conflict":
                n["conflict"] += 1
        return n


def plan_apply(patch, tables):
    """패치 + 지금 테이블 -> 항목별 전/후와 충돌 여부. 아무것도 바꾸지 않는다."""
    label = make_labeller(tables)
    base_all = patch.get("baseTables") or {}
    changes = []

    for sheet, change in (patch.get("tables") or {}).items():
        mine = tables.get(sheet)
        base_sheet = base_all.get(sheet)

        if change["kind"] == "map":
            cur = mine or {}
            for k, v in change["set"].items():
                was = (base_sheet or {}).get(k)
                here = cur.get(k)
                kind = "add" if k not in cur else "edit"
                c = Change(sheet, k, kind, [FieldChange("값", here, v)],
                           label=label(sheet, k), row=v)
                if k in cur and not same(here, was) and not same(here, v):
                    c.status, c.note = "conflict", "이쪽도 고쳤습니다"
                changes.append(c)
            for k in change.get("remove", []):
                if k in cur:
                    changes.append(Change(sheet, k, "remove", label=label(sheet, k)))

        elif change["kind"] == "whole":
            c = Change(sheet, sheet, "edit", label="%s 시트 전체" % sheet,
                       row=change["rows"])
            if mine is not None and mine != base_sheet:
                c.status, c.note = "conflict", "이쪽도 고쳐 통째로 덮어쓸 수 없습니다"
            changes.append(c)

        else:  # rows
            idx, _ = index_rows(mine)
            base_idx, _ = index_rows(base_sheet)
            for row in change["upsert"]:
                k = row_key(row)
                here, was = idx.get(k), base_idx.get(k)
                if here is None:
                    changes.append(Change(sheet, k, "add", label=label(sheet, k, row),
                                          row=row))
                    continue
                fields = [FieldChange(f, here.get(f), row.get(f))
                          for f in sorted(set(here) | set(row))
                          if not same(here.get(f), row.get(f))]
                if not fields:
                    continue
                c = Change(sheet, k, "edit", fields, label=label(sheet, k, row), row=row)
                # 세 쪽 비교: 기준점과도 다르고 받은 것과도 다르면 양쪽이 고친 것이다.
                if was is not None and any(
                        not same(here.get(f.field), was.get(f.field)) for f in fields):
                    c.status, c.note = "conflict", "이쪽도 고쳤습니다"
                changes.append(c)
            for k in change.get("remove", []):
                if k in idx:
                    changes.append(Change(sheet, k, "remove",
                                          label=label(sheet, k, idx[k])))

    return Plan(patch, changes, list(patch.get("art") or []),
                list(patch.get("artRemoved") or []))


def apply_plan(plan, tables):
    """선택된 항목만 실제로 반영한다. 건드린 시트 이름을 돌려준다."""
    touched = set()
    for c in plan.changes:
        if not c.selected:
            continue
        sheet = c.sheet
        if sheet not in tables:
            tables[sheet] = {} if c.kind != "add" or isinstance(c.row, dict) else []
        target = tables[sheet]

        if isinstance(target, dict):
            if c.kind == "remove":
                target.pop(c.key, None)
            else:
                target[c.key] = c.row
        elif c.label.endswith("시트 전체"):
            tables[sheet] = c.row
        else:
            idx, _ = index_rows(target)
            here = idx.get(c.key)
            if c.kind == "remove":
                if here is not None:
                    target.remove(here)
            elif here is None:
                target.append(c.row)
            else:
                here.clear()
                here.update(c.row)
        touched.add(sheet)
    return touched
