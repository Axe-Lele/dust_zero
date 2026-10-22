"""더스트 제로 콘텐츠 추가 도구.

캐릭터, 상품, 이미지 세트를 엑셀을 직접 열지 않고 추가한다.
쓰는 곳은 전부 GameData/tables/*.xlsx 이고, 저장하면 build + sync 까지 돌려 Unity 에 반영한다.
즉 이 도구는 지름길이지 다른 경로가 아니다 — 엑셀을 직접 고쳐도 똑같이 동작한다.

    python tools/dz_studio.py

필요: openpyxl (이미 파이프라인이 쓰고 있다), tkinter (파이썬 기본 포함).
"""
import contextlib
import io as _io
import os
import re
import shutil
import sys
import tkinter as tk
from tkinter import filedialog, messagebox, ttk


def find_repo_root():
    """프로젝트 루트를 찾는다.

    exe 로 묶으면 __file__ 은 임시 압축 해제 폴더를 가리키므로 쓸 수 없다. 실행 파일이
    놓인 자리에서 위로 올라가며 표식을 찾는다 — 그래야 exe 를 루트에 두든 tools/ 에
    두든 같은 엑셀을 고친다. 변환기들도 각자의 __file__ 로 경로를 잡으므로, 이 함수는
    번들 사본이 아니라 '진짜' GameData/tools 를 찾아야 한다.
    """
    marker = os.path.join("GameData", "tools", "xlsx_io.py")
    starts = []
    if getattr(sys, "frozen", False):
        starts.append(os.path.dirname(os.path.abspath(sys.executable)))
        starts.append(os.getcwd())
    else:
        starts.append(os.path.dirname(os.path.abspath(__file__)))
    for start in starts:
        d = start
        for _ in range(6):
            if os.path.exists(os.path.join(d, marker)):
                return d
            parent = os.path.dirname(d)
            if parent == d:
                break
            d = parent
    return None


ROOT = find_repo_root()
if ROOT is None:
    # 조용히 빈 창을 띄우느니 왜 못 찾았는지 말하고 끝낸다.
    _r = tk.Tk()
    _r.withdraw()
    messagebox.showerror(
        "프로젝트를 찾지 못했습니다",
        "GameData/tools/xlsx_io.py 를 찾지 못했습니다.\n\n"
        "이 실행 파일을 프로젝트 폴더(clean-girls) 안이나 그 tools/ 폴더에 두고 실행하세요.\n"
        f"지금 위치: {os.path.dirname(os.path.abspath(sys.executable))}")
    raise SystemExit(1)

GAMEDATA = os.path.join(ROOT, "GameData")
TABLES = os.path.join(GAMEDATA, "tables")
TOOLS = os.path.join(GAMEDATA, "tools")
# Unity 프로젝트가 있으면 거기에 바로 넣고, 없으면(기획자에게 넘긴 묶음) GameData 안의
# 대기 폴더에 모은다. 그림은 패치에 같이 실려 돌아오고, 합칠 때 Unity 쪽으로 옮겨진다.
_UNITY_ART = os.path.join(ROOT, "DustZeroUnity", "Assets", "DustZero", "Art")
STANDALONE = not os.path.isdir(os.path.join(ROOT, "DustZeroUnity"))
ART = os.path.join(GAMEDATA, "art-staging") if STANDALONE else _UNITY_ART
ILLUST_DIR = os.path.join(ART, "Illust")
SPRITE_DIR = os.path.join(ART, "Sprites")

# 번들 사본이 아니라 디스크의 실제 변환기를 쓴다. 그래야 build.py / sync_to_unity.py 가
# 자기 __file__ 로 잡는 경로들이 전부 진짜 프로젝트를 가리킨다.
sys.path.insert(0, TOOLS)
import xlsx_io  # noqa: E402

# 11_Character.xlsx 의 unit_behaviour 가 고를 수 있는 값. TableValidator 의 목록과 같아야 한다.
DEFENSE_KITS = ["", "vac", "melee", "fridge", "ac", "micro", "washer", "pc", "fan", "gen"]
SURV_LEADERS = ["", "vac", "robot", "ac"]
SURV_COMPS = ["", "vac", "robot", "fridge", "ac", "micro", "washer", "fan"]
ILLUST_SLOTS = ["full", "turn", "keys", "face1", "face2", "face3"]

RARITY = [("1 R", 1), ("2 SR", 2), ("3 SSR", 3)]
FAMILY = [("1 생활", 1), ("2 주방", 2), ("3 디지털", 3), ("4 계절", 4)]
TILE = [("1 바닥(근접)", 1), ("2 가구 위", 2)]


# ---------------------------------------------------------------- table access

def load_tables():
    """현재 엑셀 전체를 읽어 온다. 쓸 때도 같은 dict 를 돌려주므로 시트가 유실되지 않는다."""
    return xlsx_io.read_workbooks(TABLES)


def workbook_of(sheet):
    for fname, sheets in xlsx_io.WORKBOOKS:
        if sheet in sheets:
            return fname
    raise KeyError(f"{sheet} 시트가 어느 워크북에도 없습니다.")


def save_sheets(tables, sheets):
    """지정한 시트가 속한 워크북만 다시 쓴다. 나머지 엑셀은 건드리지 않는다."""
    books = {workbook_of(s) for s in sheets}
    xlsx_io.write_workbooks(tables, TABLES, only=books)
    return sorted(books)


def run_sync(log):
    """엑셀 -> JSON -> StreamingAssets. 검증이 걸리면 그 메시지를 그대로 보여 준다.

    별도 프로세스로 띄우지 않고 직접 호출한다. exe 로 묶이면 sys.executable 이 이 도구
    자신이라, 예전처럼 python 을 부르면 창이 하나 더 열릴 뿐 변환은 일어나지 않는다.
    """
    buf = _io.StringIO()
    try:
        import sync_to_unity
        with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(buf):
            code = sync_to_unity.main()
    except SystemExit as exc:            # main() 이 raise SystemExit 로 끝나는 경우
        code = exc.code if isinstance(exc.code, int) else 1
    except Exception as exc:             # 엑셀 잠김, 테이블 오류 등은 숨기지 않는다
        log(buf.getvalue())
        log(f"동기화 중 오류: {exc}")
        return False
    log(buf.getvalue())
    return (code or 0) == 0


# ---------------------------------------------------------------- shared widgets

class Form(ttk.Frame):
    """라벨 + 입력칸을 한 줄씩 쌓는 작은 도우미."""

    def __init__(self, master):
        super().__init__(master)
        self.vars = {}
        self._row = 0

    def field(self, key, label, default="", width=28, hint=""):
        ttk.Label(self, text=label).grid(row=self._row, column=0, sticky="w", pady=3, padx=(0, 8))
        v = tk.StringVar(value=str(default))
        ttk.Entry(self, textvariable=v, width=width).grid(row=self._row, column=1, sticky="w")
        if hint:
            ttk.Label(self, text=hint, foreground="#5D6871").grid(row=self._row, column=2,
                                                                  sticky="w", padx=(8, 0))
        self.vars[key] = v
        self._row += 1
        return v

    def choice(self, key, label, options, default=None, hint=""):
        ttk.Label(self, text=label).grid(row=self._row, column=0, sticky="w", pady=3, padx=(0, 8))
        v = tk.StringVar(value=str(default if default is not None else options[0]))
        ttk.Combobox(self, textvariable=v, values=[str(o) for o in options],
                     state="readonly", width=26).grid(row=self._row, column=1, sticky="w")
        if hint:
            ttk.Label(self, text=hint, foreground="#5D6871").grid(row=self._row, column=2,
                                                                  sticky="w", padx=(8, 0))
        self.vars[key] = v
        self._row += 1
        return v

    def get(self, key):
        return self.vars[key].get().strip()

    def num(self, key, cast=float):
        raw = self.get(key)
        if raw == "":
            return 0
        return cast(float(raw))


def leading_int(text):
    """'2 SR' 같은 콤보 값에서 앞의 정수를 뽑는다."""
    m = re.match(r"\s*(-?\d+)", text)
    return int(m.group(1)) if m else 0



class Picker(ttk.Frame):
    """왼쪽 목록 + 검색칸. 고르면 on_pick(key) 을 부른다.

    도구가 추가만 되던 때는 목록이 필요 없었다. 고치려면 먼저 지금 뭐가 있는지 보여야
    한다 — 엑셀을 열어 행 번호를 찾아 오는 건 이 도구를 쓰는 이유를 없앤다.
    """

    def __init__(self, master, on_pick, width=26):
        super().__init__(master)
        self.on_pick = on_pick
        self._rows = []

        ttk.Label(self, text="검색").pack(anchor="w")
        self.q = tk.StringVar()
        e = ttk.Entry(self, textvariable=self.q, width=width)
        e.pack(anchor="w", fill="x")
        self.q.trace_add("write", lambda *_: self._refill())

        self.box = tk.Listbox(self, width=width, height=26, exportselection=False)
        self.box.pack(anchor="w", fill="both", expand=True, pady=(6, 0))
        self.box.bind("<<ListboxSelect>>", self._picked)

        self.count = ttk.Label(self, text="", foreground="#5D6871")
        self.count.pack(anchor="w", pady=(4, 0))

    def set_rows(self, rows):
        """rows: (key, 표시문자열) 목록."""
        self._rows = list(rows)
        self._refill()

    def _refill(self):
        needle = self.q.get().strip().lower()
        self._shown = [r for r in self._rows
                       if not needle or needle in r[1].lower() or needle in str(r[0]).lower()]
        self.box.delete(0, "end")
        for _, label in self._shown:
            self.box.insert("end", label)
        self.count.config(text="%d개 / 전체 %d개" % (len(self._shown), len(self._rows)))

    def select(self, key):
        for i, (k, _) in enumerate(getattr(self, "_shown", [])):
            if k == key:
                self.box.selection_clear(0, "end")
                self.box.selection_set(i)
                self.box.see(i)
                return

    def clear(self):
        self.box.selection_clear(0, "end")

    def _picked(self, _evt):
        sel = self.box.curselection()
        if not sel:
            return
        self.on_pick(self._shown[sel[0]][0])


# ---------------------------------------------------------------- 캐릭터

class CharacterTab(ttk.Frame):
    """가전 한 종을 추가하거나, 이미 있는 가전을 불러와 고친다.

    한 캐릭터는 여섯 테이블에 걸쳐 있다 — unit(스탯), string_ko(이름·대사),
    unit_art(이미지), unit_behaviour(동작), item(조각), gacha_pool(뽑기).
    손으로 넣으면 하나 빠뜨리기 쉬워서 여기서 한꺼번에 읽고 쓴다.
    """

    def __init__(self, master, app):
        super().__init__(master, padding=14)
        self.app = app
        self.current = None          # 수정 중인 id. None 이면 새로 만드는 중.

        self.picker = Picker(self, self.load)
        self.picker.pack(side="left", fill="y", padx=(0, 14))

        right = ttk.Frame(self)
        right.pack(side="left", fill="both", expand=True)

        head = ttk.Frame(right)
        head.pack(anchor="w", fill="x", pady=(0, 8))
        self.title = ttk.Label(head, text="새 캐릭터", font=("", 11, "bold"))
        self.title.pack(side="left")
        ttk.Button(head, text="새로 만들기", command=self.reset).pack(side="right")

        f = Form(right)
        f.pack(anchor="w")
        self.f = f

        f.field("id", "ID (영문 소문자)", "", hint="예: toaster — 모든 테이블의 키가 된다")
        f.field("name", "이름", "", hint="예: 토스터")
        f.field("short", "짧은 이름", "", hint="편성 슬롯에 들어간다. 3글자 권장")
        f.field("role", "역할", "", hint="예: 근접 · 광역")
        f.choice("rarity", "등급", [r[0] for r in RARITY], RARITY[0][0])
        f.choice("family", "계열", [x[0] for x in FAMILY], FAMILY[0][0], hint="부품 종류가 따라온다")
        f.choice("tile", "배치", [x[0] for x in TILE], TILE[0][0])

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)
        g = Form(right)
        g.pack(anchor="w")
        self.g = g
        g.field("cost", "배치 비용", "10", width=10, hint="1 = 100W")
        g.field("heat", "과열 한도", "1000", width=10, hint="바닥 배치면 0보다 커야 한다")
        g.field("atk", "공격력", "60", width=10)
        g.field("itv", "공격 간격(초)", "1.0", width=10)
        g.field("range", "사거리(칸)", "1.1", width=10)
        g.field("block", "저지 수", "2", width=10, hint="바닥 배치면 1 이상")
        g.field("sp", "스킬 충전(초)", "18", width=10)
        g.field("dur", "스킬 지속(초)", "4", width=10)

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)
        h = Form(right)
        h.pack(anchor="w")
        self.h = h
        h.field("skill", "스킬 이름", "")
        h.field("desc", "스킬 설명", "", width=52)
        h.choice("defense", "디펜스 동작", DEFENSE_KITS, "",
                 hint="비우면 평범한 공격. 고르면 그 동작의 skill_param 을 채워야 한다")
        h.choice("survLeader", "대청소 리더", SURV_LEADERS, "")
        h.choice("survComp", "대청소 동료", SURV_COMPS, "")
        h.choice("support", "지원형", ["0 아니오", "1 예"], "0 아니오")
        h.field("roomSpeed", "로비 걷기 속도", "0.18", width=10)

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)
        ttk.Label(right, text="대사 (빈 줄은 제외됩니다)").pack(anchor="w")
        self.lines = []
        lf = ttk.Frame(right)
        lf.pack(anchor="w", pady=4)
        for i in range(4):
            ttk.Label(lf, text=f"{i + 1}").grid(row=i, column=0, padx=(0, 6))
            v = tk.StringVar()
            ttk.Entry(lf, textvariable=v, width=56).grid(row=i, column=1, pady=2)
            self.lines.append(v)

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)
        opt = ttk.Frame(right)
        opt.pack(anchor="w")
        self.in_gacha = tk.BooleanVar(value=True)
        ttk.Checkbutton(opt, text="소환 풀에 넣기", variable=self.in_gacha).grid(row=0, column=0, sticky="w")
        ttk.Label(opt, text="가중치").grid(row=0, column=1, padx=(14, 4))
        self.weight = tk.StringVar(value="100")
        ttk.Entry(opt, textvariable=self.weight, width=8).grid(row=0, column=2)

        self.save_btn = ttk.Button(right, text="캐릭터 추가", command=self.save)
        self.save_btn.pack(anchor="w", pady=(14, 0))

        self.refresh()

    # ------------------------------------------------------------ list / load

    def refresh(self):
        t = load_tables()
        s = t["string_ko"]
        rows = sorted(t["unit"], key=lambda r: r.get("order") or 0)
        self.picker.set_rows([(r["id"], "%s  (%s)" % (s.get(r.get("nameKey"), r["id"]), r["id"]))
                              for r in rows])

    def reset(self):
        """새로 만들기 모드로 되돌린다."""
        self.current = None
        self.picker.clear()
        self.title.config(text="새 캐릭터")
        self.save_btn.config(text="캐릭터 추가")
        for form, blank in ((self.f, {"rarity": RARITY[0][0], "family": FAMILY[0][0],
                                      "tile": TILE[0][0]}),
                            (self.g, {"cost": "10", "heat": "1000", "atk": "60", "itv": "1.0",
                                      "range": "1.1", "block": "2", "sp": "18", "dur": "4"}),
                            (self.h, {"defense": "", "survLeader": "", "survComp": "",
                                      "support": "0 아니오", "roomSpeed": "0.18"})):
            for key, var in form.vars.items():
                var.set(blank.get(key, ""))
        for v in self.lines:
            v.set("")
        self.in_gacha.set(True)
        self.weight.set("100")

    def load(self, uid):
        try:
            self._load(uid)
        except Exception as e:
            messagebox.showerror("불러오기 실패", str(e))

    def _load(self, uid):
        t = load_tables()
        unit = next((r for r in t["unit"] if r["id"] == uid), None)
        if unit is None:
            raise ValueError(f"'{uid}' 를 unit 테이블에서 찾지 못했습니다.")
        s = t["string_ko"]

        def pick(options, value):
            """'2 SR' 같은 콤보 항목 중 앞 숫자가 value 인 것을 찾는다."""
            want = int(value or 0)
            for o in options:
                if leading_int(o) == want:
                    return o
            return options[0]

        self.f.vars["id"].set(uid)
        self.f.vars["name"].set(s.get(unit.get("nameKey"), ""))
        self.f.vars["short"].set(s.get(unit.get("shortKey"), ""))
        self.f.vars["role"].set(s.get(unit.get("roleKey"), ""))
        self.f.vars["rarity"].set(pick([r[0] for r in RARITY], unit.get("rarity")))
        self.f.vars["family"].set(pick([x[0] for x in FAMILY], unit.get("family")))
        self.f.vars["tile"].set(pick([x[0] for x in TILE], unit.get("tile")))

        for key in ("cost", "heat", "atk", "itv", "range", "block", "sp", "dur"):
            raw = unit.get(key)
            self.g.vars[key].set("" if raw is None else _trim(raw))

        self.h.vars["skill"].set(s.get(unit.get("skillKey"), ""))
        self.h.vars["desc"].set(s.get(unit.get("descKey"), ""))

        art = next((r for r in t.get("unit_art", []) if r["id"] == uid), {})
        self.h.vars["roomSpeed"].set(_trim(art.get("roomSpeed", 0.18)))

        beh = next((r for r in t.get("unit_behaviour", []) if r["id"] == uid), {})
        self.h.vars["defense"].set(beh.get("defense") or "")
        self.h.vars["survLeader"].set(beh.get("survLeader") or "")
        self.h.vars["survComp"].set(beh.get("survComp") or "")
        self.h.vars["support"].set("1 예" if int(beh.get("support") or 0) else "0 아니오")

        for i, v in enumerate(self.lines):
            v.set(s.get(f"unit.{uid}.line{i + 1}", ""))

        g = next((r for r in t["gacha_pool"] if r.get("unitId") == uid), None)
        self.in_gacha.set(g is not None)
        self.weight.set(str(g["weight"]) if g else "100")

        self.current = uid
        self.title.config(text="수정 중 — %s (%s)" % (s.get(unit.get("nameKey"), uid), uid))
        self.save_btn.config(text="수정 내용 저장")
        skp = beh.get("skp") or ""
        self.app.log("'%s' 불러옴%s" % (uid, ("  ·  skp=" + skp) if skp else ""))

    # ------------------------------------------------------------ save

    def save(self):
        try:
            self._save()
        except Exception as e:  # 입력 실수는 흔하다. 창을 죽이지 않고 그대로 보여 준다.
            messagebox.showerror("저장 실패", str(e))

    def _save(self):
        uid = self.f.get("id")
        if not re.fullmatch(r"[a-z][a-z0-9_]{1,15}", uid or ""):
            raise ValueError("ID 는 영문 소문자로 시작하는 2~16자여야 합니다.")
        name = self.f.get("name")
        if not name:
            raise ValueError("이름을 입력하세요.")

        t = load_tables()
        unit = next((r for r in t["unit"] if r["id"] == uid), None)
        editing = self.current is not None

        if editing and uid != self.current:
            # id 는 여섯 테이블의 키다. 바꾸면 참조가 전부 끊긴다.
            raise ValueError("ID 는 바꿀 수 없습니다. 새 ID 로 만들려면 '새로 만들기' 를 누르세요.")
        if not editing and unit is not None:
            raise ValueError(f"'{uid}' 는 이미 있는 가전입니다. 왼쪽 목록에서 골라 고치세요.")

        if unit is None:
            unit = {"id": uid,
                    "order": max((r.get("order") or 0) for r in t["unit"]) + 1,
                    "cool": 0, "atkTarget": 1, "skillTarget": 1,
                    "nameKey": f"unit.{uid}.name", "shortKey": f"unit.{uid}.short",
                    "roleKey": f"unit.{uid}.role", "skillKey": f"unit.{uid}.skill",
                    "descKey": f"unit.{uid}.desc"}
            t["unit"].append(unit)

        unit.update({
            "rarity": leading_int(self.f.get("rarity")),
            "family": leading_int(self.f.get("family")),
            "tile": leading_int(self.f.get("tile")),
            "cost": self.g.num("cost"), "heat": self.g.num("heat"),
            "atk": self.g.num("atk"), "itv": self.g.num("itv"),
            "range": self.g.num("range"), "block": self.g.num("block", int),
            "sp": self.g.num("sp"), "dur": self.g.num("dur"),
            "lineCount": sum(1 for v in self.lines if v.get().strip()),
        })

        s = t["string_ko"]
        s[unit["nameKey"]] = name
        s[unit["shortKey"]] = self.f.get("short") or name[:3]
        s[unit["roleKey"]] = self.f.get("role") or "미정"
        s[unit["skillKey"]] = self.h.get("skill") or f"{name} 스킬"
        s[unit["descKey"]] = self.h.get("desc") or "설명 미정"
        # 줄 수가 줄었으면 남은 키를 지운다. 안 지우면 쓰이지 않는 대사가 테이블에 남는다.
        n = 0
        for v in self.lines:
            text = v.get().strip()
            if text:
                n += 1
                s[f"unit.{uid}.line{n}"] = text
        for extra in range(n + 1, 9):
            s.pop(f"unit.{uid}.line{extra}", None)

        art = next((r for r in t.setdefault("unit_art", []) if r["id"] == uid), None)
        if art is None:
            art = {"id": uid}
            t["unit_art"].append(art)
        art.setdefault("chibi", f"unit_{uid}")
        for slot in ("full", "turn", "keys", "face1", "face2", "face3"):
            art.setdefault(slot, f"{uid}_{slot}")
        art["roomSpeed"] = self.h.num("roomSpeed")

        beh = next((r for r in t.setdefault("unit_behaviour", []) if r["id"] == uid), None)
        if beh is None:
            beh = {"id": uid, "skp": ""}
            t["unit_behaviour"].append(beh)
        # 빈 칸은 None 으로 되돌린다. "" 로 쓰면 비어 있던 행이 전부 바뀐 것으로 잡혀,
        # 아무것도 고치지 않았는데 엑셀이 달라진다.
        beh["defense"] = self.h.get("defense") or None
        beh["survLeader"] = self.h.get("survLeader") or None
        beh["survComp"] = self.h.get("survComp") or None
        beh["support"] = leading_int(self.h.get("support"))
        beh.setdefault("skp", None)

        # 조각 아이템. 영입과 돌파가 이것 없이는 돌지 않는다.
        shard = next((r for r in t["item"] if r.get("unitId") == uid), None)
        if shard is None:
            ids = [r["id"] for r in t["item"] if 301 <= (r.get("id") or 0) < 400]
            sid = (max(ids) + 1) if ids else 301
            shard = {"id": sid, "nameKey": f"item.{sid}.name", "family": 0, "unitId": uid}
            t["item"].append(shard)
        s[shard["nameKey"]] = f"{name} 조각"

        pool_row = next((r for r in t["gacha_pool"] if r.get("unitId") == uid), None)
        if self.in_gacha.get():
            weight = int(float(self.weight.get() or 100))
            if pool_row is None:
                pool = min((r["poolId"] for r in t["gacha_pool"]), default=1)
                t["gacha_pool"].append({"poolId": pool, "unitId": uid, "weight": weight})
            else:
                pool_row["weight"] = weight
        elif pool_row is not None:
            t["gacha_pool"].remove(pool_row)

        books = save_sheets(t, ["unit", "string_ko", "unit_art", "unit_behaviour",
                                "item", "gacha_pool"])
        verb = "수정" if editing else "추가"
        self.app.log(f"'{name}'({uid}) {verb} — 수정한 엑셀: {', '.join(books)}")
        kit = self.h.get("defense")
        if kit:
            self.app.log(f"  주의: 디펜스 동작 '{kit}' 을 골랐습니다. "
                         f"02_Unit.xlsx 의 skill_param 시트에 '{uid}' 행을 채우고, "
                         f"11_Character.xlsx 의 unit_behaviour.skp 에 필요한 키를 적으세요.")
        if not editing:
            self.app.log(f"  이미지: Art/Sprites/unit_{uid}.png 와 "
                         f"Art/Illust/{uid}_*.png 를 '이미지 세트' 탭에서 넣으세요.")
        self.current = uid
        self.title.config(text="수정 중 — %s (%s)" % (name, uid))
        self.save_btn.config(text="수정 내용 저장")
        self.refresh()
        self.picker.select(uid)
        self.app.sync()


def _trim(value):
    """1.0 을 '1', 1.25 를 '1.25' 로. 엑셀에서 읽은 float 이 칸마다 .0 으로 보이지 않게."""
    try:
        f = float(value)
    except (TypeError, ValueError):
        return "" if value is None else str(value)
    return str(int(f)) if f == int(f) else str(f)


# ---------------------------------------------------------------- 상품

class ProductTab(ttk.Frame):
    """상점 상품을 추가하거나, 이미 있는 상품을 불러와 고친다.

    보상 묶음(reward_group)도 같이 만들고 같이 고친다. 상품만 고치고 보상 묶음을
    그대로 두면 가격은 바뀌었는데 주는 건 그대로인 상태가 조용히 남는다.
    """

    TABS = ["1 특가", "2 크리스탈", "3 성장"]
    PAY = ["1 현금", "2 크리스탈", "3 광고", "4 코인"]
    LIMIT = ["0 제한 없음", "1 일일", "2 주간", "3 계정당 1회"]

    def __init__(self, master, app):
        super().__init__(master, padding=14)
        self.app = app
        self.current = None          # 수정 중인 상품 id

        self.picker = Picker(self, self.load, width=30)
        self.picker.pack(side="left", fill="y", padx=(0, 14))

        right = ttk.Frame(self)
        right.pack(side="left", fill="both", expand=True)

        head = ttk.Frame(right)
        head.pack(anchor="w", fill="x", pady=(0, 8))
        self.title = ttk.Label(head, text="새 상품", font=("", 11, "bold"))
        self.title.pack(side="left")
        ttk.Button(head, text="새로 만들기", command=self.reset).pack(side="right")

        f = Form(right)
        f.pack(anchor="w")
        self.f = f
        f.field("name", "상품 이름", "", hint="예: 크리스탈 300")
        f.field("desc", "설명", "", width=52)
        f.choice("tab", "상점 탭", self.TABS, self.TABS[0])
        f.choice("payKind", "결제 수단", self.PAY, self.PAY[1])
        f.field("price", "가격", "1200", width=12, hint="광고면 0")
        f.choice("limitType", "구매 제한", self.LIMIT, self.LIMIT[0])
        f.field("limitCount", "제한 횟수", "0", width=12)
        f.field("cooldown", "재구매 대기(초)", "0", width=12, hint="광고 상품에 쓴다")

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)
        ttk.Label(right, text="지급 보상 — 아이템 ID 와 개수를 한 줄에 하나씩").pack(anchor="w")
        ttk.Label(right, text="예)  1, 5000   코인 5000   /   2, 300   크리스탈 300",
                  foreground="#5D6871").pack(anchor="w")
        self.rewards = tk.Text(right, width=46, height=5)
        self.rewards.pack(anchor="w", pady=4)

        self.save_btn = ttk.Button(right, text="상품 추가", command=self.save)
        self.save_btn.pack(anchor="w", pady=(12, 0))

        self.refresh()

    # ------------------------------------------------------------ list / load

    def refresh(self):
        t = load_tables()
        s = t["string_ko"]
        rows = sorted(t["shop_product"], key=lambda r: (r.get("tab") or 0, r.get("order") or 0))
        out = []
        for r in rows:
            tab = next((x for x in self.TABS if leading_int(x) == (r.get("tab") or 0)), "?")
            out.append((r["id"], "[%s] %s" % (tab.split(" ", 1)[-1],
                                              s.get(r.get("nameKey"), str(r["id"])))))
        self.picker.set_rows(out)

    def reset(self):
        self.current = None
        self.picker.clear()
        self.title.config(text="새 상품")
        self.save_btn.config(text="상품 추가")
        defaults = {"name": "", "desc": "", "tab": self.TABS[0], "payKind": self.PAY[1],
                    "price": "1200", "limitType": self.LIMIT[0], "limitCount": "0",
                    "cooldown": "0"}
        for key, var in self.f.vars.items():
            var.set(defaults.get(key, ""))
        self.rewards.delete("1.0", "end")

    def load(self, pid):
        try:
            self._load(pid)
        except Exception as e:
            messagebox.showerror("불러오기 실패", str(e))

    def _load(self, pid):
        t = load_tables()
        row = next((r for r in t["shop_product"] if r["id"] == pid), None)
        if row is None:
            raise ValueError("상품 %s 를 찾지 못했습니다." % pid)
        s = t["string_ko"]

        def pick(options, value):
            want = int(value or 0)
            return next((o for o in options if leading_int(o) == want), options[0])

        self.f.vars["name"].set(s.get(row.get("nameKey"), ""))
        self.f.vars["desc"].set(s.get(row.get("descKey"), ""))
        # 몇몇 상품 행에는 descKey 칸이 비어 있다. 저장할 때 만들어 준다.
        self.f.vars["tab"].set(pick(self.TABS, row.get("tab")))
        self.f.vars["payKind"].set(pick(self.PAY, row.get("payKind")))
        self.f.vars["price"].set(str(int(row.get("price") or 0)))
        self.f.vars["limitType"].set(pick(self.LIMIT, row.get("limitType")))
        self.f.vars["limitCount"].set(str(int(row.get("limitCount") or 0)))
        self.f.vars["cooldown"].set(str(int(row.get("cooldown") or 0)))

        gid = row.get("rewardGroup") or 0
        kind_item = ((t.get("enums") or {}).get("RewardKind") or {}).get("Item", 1)
        lines = []
        for r in sorted((r for r in t["reward_group"] if r.get("groupId") == gid),
                        key=lambda r: r.get("seq") or 0):
            if int(r.get("kind") or kind_item) != kind_item:
                # 가전을 주는 줄. 이 칸은 아이템만 다루므로 손대지 않고 보여 주기만 한다.
                lines.append("# 가전 %s (이 화면에서는 고칠 수 없습니다)" % r.get("refId"))
                continue
            lines.append("%s, %s" % (int(r.get("refId") or 0), int(r.get("max") or 0)))
        self.rewards.delete("1.0", "end")
        self.rewards.insert("1.0", "\n".join(lines))

        self.current = pid
        self.title.config(text="수정 중 — %s (id %s)" % (s.get(row.get("nameKey"), pid), pid))
        self.save_btn.config(text="수정 내용 저장")
        self.app.log("상품 %s 불러옴 — 보상 묶음 %s" % (pid, gid))

    # ------------------------------------------------------------ save

    def save(self):
        try:
            self._save()
        except Exception as e:
            messagebox.showerror("저장 실패", str(e))

    def _save(self):
        name = self.f.get("name")
        if not name:
            raise ValueError("상품 이름을 입력하세요.")

        pairs = []
        for ln in self.rewards.get("1.0", "end").splitlines():
            ln = ln.strip()
            if not ln or ln.startswith("#"):
                continue
            parts = [p.strip() for p in ln.replace("\t", ",").split(",") if p.strip()]
            if len(parts) != 2:
                raise ValueError("보상 줄을 읽을 수 없습니다: " + ln + "  (형식: 아이템ID, 개수)")
            pairs.append((int(parts[0]), int(parts[1])))
        t = load_tables()
        kind_item = ((t.get("enums") or {}).get("RewardKind") or {}).get("Item", 1)
        gid_now = next((r.get("rewardGroup") for r in t["shop_product"]
                        if r["id"] == self.current), None) if self.current is not None else None
        has_units = any(r.get("groupId") == gid_now and int(r.get("kind") or kind_item) != kind_item
                        for r in t["reward_group"])
        if not pairs and self.current is None:
            raise ValueError("보상을 한 줄 이상 입력하세요. 아무것도 안 주는 상품은 만들 수 없습니다.")
        if not pairs and not has_units:
            # 이 화면이 표현하지 못하는 보상(가전 지급 등)만 가진 기존 상품이다. 가격과
            # 제한만 고치고 보상 묶음은 건드리지 않는다.
            self.app.log("  보상 줄이 비어 있어 보상 묶음은 그대로 둡니다.")

        known = {r["id"] for r in t["item"]}
        for item_id, _ in pairs:
            if item_id not in known:
                raise ValueError("아이템 %d 이 item 테이블에 없습니다." % item_id)

        tab = leading_int(self.f.get("tab"))
        row = next((r for r in t["shop_product"] if r["id"] == self.current), None) \
            if self.current is not None else None
        editing = row is not None

        if row is None:
            pid = max((r["id"] for r in t["shop_product"]), default=99) + 1
            row = {"id": pid, "firstBonusGroup": 0, "unlockCond": 0,
                   "rewardGroup": max((r["groupId"] for r in t["reward_group"]),
                                      default=30000) + 1,
                   "order": sum(1 for r in t["shop_product"] if r.get("tab") == tab) + 1,
                   "nameKey": "shop.%d.name" % pid, "descKey": "shop.%d.desc" % pid}
            t["shop_product"].append(row)
        elif row.get("tab") != tab:
            # 탭을 옮기면 그 탭의 맨 뒤로 간다. 다른 탭의 순서는 건드리지 않는다.
            row["order"] = sum(1 for r in t["shop_product"] if r.get("tab") == tab) + 1

        gid = row["rewardGroup"]
        # 보상은 통째로 다시 쓴다. 줄을 지웠을 때 옛 보상이 남아 있으면 상품이 말없이
        # 더 주게 된다 — 상점에서 가장 알아채기 어려운 종류의 버그다.
        # 이 묶음의 줄만 **제자리에서** 갈아 끼운다. 지우고 끝에 붙이면 값은 같아도
        # reward_group 의 행 순서가 통째로 밀려, 고치지 않은 상품까지 전부 바뀐 것처럼
        # 보인다 — 엑셀 diff 가 쓸모없어진다.
        rows = t["reward_group"]
        skip_rewards = not pairs and not has_units
        at = next((i for i, r in enumerate(rows) if r.get("groupId") == gid), len(rows))
        kept = [r for r in rows
                if r.get("groupId") == gid and int(r.get("kind") or kind_item) != kind_item]
        seq = max((r.get("seq") or 0) for r in kept) if kept else 0
        fresh = []
        for item_id, count in pairs:
            seq += 1
            # 실제 열은 refId/min/max/kind/prob 다. 예전 코드가 쓰던 itemId/count 는 이
            # 테이블에 없는 이름이라, 이 도구로 만든 상품은 아무것도 주지 않았다.
            fresh.append({"groupId": gid, "seq": seq, "kind": kind_item,
                          "refId": item_id, "min": count, "max": count, "prob": 10000})
        if not skip_rewards:
            t["reward_group"] = (rows[:at]
                                 + kept + fresh
                                 + [r for r in rows[at:] if r.get("groupId") != gid])

        row.update({
            "tab": tab,
            "payKind": leading_int(self.f.get("payKind")),
            "price": self.f.num("price", int),
            "limitType": leading_int(self.f.get("limitType")),
            "limitCount": self.f.num("limitCount", int),
            "cooldown": self.f.num("cooldown", int),
        })
        if not row.get("nameKey"):
            row["nameKey"] = "shop.%d.name" % row["id"]
        t["string_ko"][row["nameKey"]] = name

        # 설명이 없는 상품이 실제로 있다. 비어 있는데 키를 만들어 주면, 고치지도 않은
        # 상품의 행이 바뀐다. 사용자가 실제로 적었을 때만 키를 만든다.
        desc = self.f.get("desc")
        if desc:
            if not row.get("descKey"):
                row["descKey"] = "shop.%d.desc" % row["id"]
            t["string_ko"][row["descKey"]] = desc
        elif row.get("descKey"):
            t["string_ko"][row["descKey"]] = ""

        books = save_sheets(t, ["shop_product", "reward_group", "string_ko"])
        self.app.log("상품 %s %s — id %d, 보상 묶음 %d — 수정한 엑셀: %s"
                     % (name, "수정" if editing else "추가", row["id"], gid, ", ".join(books)))
        if leading_int(self.f.get("payKind")) == 1:
            self.app.log("  주의: 현금 결제는 테스트 공급자로만 구현돼 있습니다. "
                         "실제 스토어 결제 SDK 는 아직 붙어 있지 않습니다.")
        self.current = row["id"]
        self.title.config(text="수정 중 — %s (id %s)" % (name, row["id"]))
        self.save_btn.config(text="수정 내용 저장")
        self.refresh()
        self.picker.select(row["id"])
        self.app.sync()


# ---------------------------------------------------------------- 이미지 세트

class ArtTab(ttk.Frame):
    """고른 가전의 치비 1장과 일러스트 6장을 보여 주고 바꾼다.

    예전에는 쓰기 전용이었다 — 파일을 고르고 적용은 되는데, **지금 뭐가 들어 있는지는
    볼 수 없었다.** 그래서 표에 이름만 있고 파일은 없는 칸(게임에서 그냥 안 보이는 칸)을
    알아챌 방법이 없었다. 이제 칸마다 현재 이름·파일 유무·미리보기를 띄우고, 바꾸거나
    비울 수 있다.
    """

    SLOT_LABEL = {"chibi": "치비 (전투/로비)", "full": "전신", "turn": "삼면도",
                  "keys": "설정화", "face1": "표정 1", "face2": "표정 2", "face3": "표정 3"}

    def __init__(self, master, app):
        super().__init__(master, padding=14)
        self.app = app
        self.uid = None
        self.pending = {}            # slot -> 고른 원본 경로 (아직 복사 전)
        self.cleared = set()         # 비우기를 누른 칸
        self.thumbs = {}             # PhotoImage 참조. 놓으면 Tk 가 그림을 지운다.

        self.picker = Picker(self, self.load)
        self.picker.pack(side="left", fill="y", padx=(0, 14))

        right = ttk.Frame(self)
        right.pack(side="left", fill="both", expand=True)

        head = ttk.Frame(right)
        head.pack(anchor="w", fill="x", pady=(0, 8))
        self.title = ttk.Label(head, text="가전을 고르세요", font=("", 11, "bold"))
        self.title.pack(side="left")
        ttk.Button(head, text="되돌리기", command=lambda: self.load(self.uid)
                   if self.uid else None).pack(side="right")

        grid = ttk.Frame(right)
        grid.pack(anchor="w", fill="x")
        self.rows = {}
        for i, slot in enumerate(["chibi"] + ILLUST_SLOTS):
            ttk.Label(grid, text=self.SLOT_LABEL[slot], width=16).grid(
                row=i, column=0, sticky="w", pady=4)

            thumb = tk.Label(grid, width=8, height=4, relief="solid", borderwidth=1,
                             background="#F7F9FA")
            thumb.grid(row=i, column=1, padx=(0, 10))

            name = ttk.Label(grid, text="", width=26)
            name.grid(row=i, column=2, sticky="w")

            state = ttk.Label(grid, text="", width=18)
            state.grid(row=i, column=3, sticky="w")

            ttk.Button(grid, text="바꾸기", width=7,
                       command=lambda sl=slot: self.pick(sl)).grid(row=i, column=4, padx=3)
            ttk.Button(grid, text="비우기", width=7,
                       command=lambda sl=slot: self.clear_slot(sl)).grid(row=i, column=5)
            self.rows[slot] = (thumb, name, state)

        ttk.Label(right, text="바꾸기를 누르면 규약 이름으로 복사되고 표의 이름도 맞춰집니다. "
                             "비우기는 표의 칸만 지웁니다 — 파일은 지우지 않습니다.",
                  foreground="#5D6871", wraplength=560, justify="left").pack(
            anchor="w", pady=(10, 0))

        self.apply_btn = ttk.Button(right, text="이미지 적용", command=self.apply)
        self.apply_btn.pack(anchor="w", pady=(12, 0))

        self.refresh()

    # ------------------------------------------------------------ list / load

    def refresh(self):
        try:
            t = load_tables()
            names = t["string_ko"]
            rows = sorted(t["unit"], key=lambda r: r.get("order") or 0)
            self.picker.set_rows([
                (r["id"], "%s  (%s)" % (names.get("unit.%s.name" % r["id"], "?"), r["id"]))
                for r in rows])
        except Exception as e:
            self.app.log("가전 목록을 읽지 못했습니다: %s" % e)

    def path_of(self, slot, stem):
        """표에 적힌 이름이 디스크의 어느 파일을 가리키는지."""
        if not stem:
            return None
        folder = SPRITE_DIR if slot == "chibi" else ILLUST_DIR
        return os.path.join(folder, str(stem) + ".png")

    def load(self, uid):
        try:
            self._load(uid)
        except Exception as e:
            messagebox.showerror("불러오기 실패", str(e))

    def _load(self, uid):
        t = load_tables()
        row = next((r for r in t.get("unit_art", []) if r["id"] == uid), {})
        names = t["string_ko"]
        self.uid = uid
        self.pending.clear()
        self.cleared.clear()
        self.title.config(text="%s (%s)" % (names.get("unit.%s.name" % uid, uid), uid))

        missing = []
        for slot in ["chibi"] + ILLUST_SLOTS:
            stem = row.get(slot) or ""
            self._paint(slot, stem, pending=None)
            path = self.path_of(slot, stem)
            if stem and (path is None or not os.path.exists(path)):
                missing.append(slot)

        if missing:
            # 표에 이름은 있는데 파일이 없다. 게임에서는 그냥 빈칸으로 그려진다.
            self.app.log("%s: 파일이 없는 칸 — %s" % (uid, ", ".join(missing)))
        self.picker.select(uid)

    def _paint(self, slot, stem, pending):
        """한 칸의 미리보기·이름·상태를 다시 그린다."""
        thumb, name_lab, state_lab = self.rows[slot]

        if pending:
            src, label, state, colour = pending, os.path.basename(pending), "바꿀 예정", "#E8A23A"
        elif slot in self.cleared:
            src, label, state, colour = None, "(비움)", "저장하면 지워짐", "#E0524A"
        elif stem:
            path = self.path_of(slot, stem)
            src = path if path and os.path.exists(path) else None
            label = str(stem)
            state, colour = (("파일 있음", "#3FAE7A") if src else ("파일 없음", "#E0524A"))
        else:
            src, label, state, colour = None, "(없음)", "", "#5D6871"

        name_lab.config(text=label)
        state_lab.config(text=state, foreground=colour)

        img = self._thumb(src)
        self.thumbs[slot] = img          # 참조를 들고 있어야 사라지지 않는다
        thumb.config(image=img if img else "", text="" if img else "—")

    def _thumb(self, path, box=64):
        """PNG 미리보기. PIL 없이 Tk 기본 기능만 쓴다 — exe 에 넣을 것이 늘지 않는다."""
        if not path or not os.path.exists(path):
            return None
        try:
            img = tk.PhotoImage(file=path)
        except Exception:
            return None                  # Tk 가 못 읽는 PNG 는 그냥 비워 둔다
        step = max(1, max(img.width(), img.height()) // box)
        return img.subsample(step, step) if step > 1 else img

    # ------------------------------------------------------------ edit

    def pick(self, slot):
        if not self.uid:
            messagebox.showerror("바꾸기", "먼저 왼쪽에서 가전을 고르세요.")
            return
        p = filedialog.askopenfilename(title=self.SLOT_LABEL[slot],
                                       filetypes=[("PNG", "*.png"), ("모든 파일", "*.*")])
        if not p:
            return
        self.pending[slot] = p
        self.cleared.discard(slot)
        self._paint(slot, None, pending=p)

    def clear_slot(self, slot):
        if not self.uid:
            return
        self.pending.pop(slot, None)
        self.cleared.add(slot)
        self._paint(slot, None, pending=None)

    def apply(self):
        try:
            self._apply()
        except Exception as e:
            messagebox.showerror("적용 실패", str(e))

    def _apply(self):
        if not self.uid:
            raise ValueError("가전을 고르세요.")
        if not self.pending and not self.cleared:
            raise ValueError("바꾸거나 비울 칸을 고르세요.")
        uid = self.uid

        t = load_tables()
        row = next((r for r in t.setdefault("unit_art", []) if r["id"] == uid), None)
        if row is None:
            row = {"id": uid, "chibi": "unit_" + uid, "roomSpeed": 0.18}
            t["unit_art"].append(row)

        copied = []
        for slot, src in self.pending.items():
            if slot == "chibi":
                stem, dest_dir = "unit_" + uid, SPRITE_DIR
            else:
                stem, dest_dir = uid + "_" + slot, ILLUST_DIR
            os.makedirs(dest_dir, exist_ok=True)
            dest = os.path.join(dest_dir, stem + ".png")
            shutil.copyfile(src, dest)
            row[slot] = stem
            copied.append(os.path.relpath(dest, ROOT))

        for slot in self.cleared:
            # 표의 칸만 비운다. 파일을 지우면 다른 가전이 같은 그림을 쓰고 있을 때 같이 깨진다.
            row[slot] = None

        books = save_sheets(t, ["unit_art"])
        for c in copied:
            self.app.log("  복사: " + c)
        if self.cleared:
            self.app.log("  비움: " + ", ".join(sorted(self.cleared)) + " (파일은 남아 있습니다)")
        self.app.log("%s 이미지 %d장 적용 — 수정한 엑셀: %s"
                     % (uid, len(copied), ", ".join(books)))
        self.app.log("  Unity 에서 DustZero > Rebuild Project Assets 를 실행하면 화면에 반영됩니다.")
        self.pending.clear()
        self.cleared.clear()
        self._load(uid)
        self.app.sync()


# ---------------------------------------------------------------- 스테이지 · 웨이브

class StageTab(ttk.Frame):
    """스테이지 한 판과 그 판의 웨이브를 고친다.

    웨이브는 칸마다 위젯을 두지 않고 **글로** 고친다. 한 판이 7~25줄이고 기획 작업은
    보통 '전체를 훑으며 숫자를 민다' 이기 때문에, 줄을 복사·붙여넣고 한꺼번에 바꾸는
    편이 칸을 하나씩 클릭하는 것보다 빠르다. 대신 저장할 때 한 줄씩 검사해서 어디가
    왜 틀렸는지 줄 번호로 알려 준다 — 글로 고치는 방식의 유일한 위험이 그것이다.

    맵(타일·경로)은 여기서 고치지 않는다. 격자와 경로를 글로 편집하게 하면 사고가
    나고, 그만한 화면은 따로 만들어야 한다. 지금은 있는 맵 중에서 고르기만 한다.
    """

    HEADER = "# 시각(초), 적, 마리, 간격, 입구, 체력배수"

    def __init__(self, master, app):
        super().__init__(master, padding=14)
        self.app = app
        self.current = None

        self.picker = Picker(self, self.load, width=24)
        self.picker.pack(side="left", fill="y", padx=(0, 14))

        right = ttk.Frame(self)
        right.pack(side="left", fill="both", expand=True)

        head = ttk.Frame(right)
        head.pack(anchor="w", fill="x", pady=(0, 8))
        self.title = ttk.Label(head, text="새 스테이지", font=("", 11, "bold"))
        self.title.pack(side="left")
        ttk.Button(head, text="새로 만들기", command=self.reset).pack(side="right")

        top = ttk.Frame(right)
        top.pack(anchor="w", fill="x")

        f = Form(top)
        f.grid(row=0, column=0, sticky="nw")
        self.f = f
        f.field("no", "번호", "", width=12, hint="예: 2-4 — 화면에 그대로 나온다")
        f.field("chapter", "챕터", "1", width=8)
        f.field("area", "구역", "1", width=8)
        f.choice("mapId", "맵", ["living"], "living")
        f.field("startPower", "시작 전력", "5", width=8, hint="1 = 100W")
        f.choice("boss", "보스 판", ["0 아니오", "1 예"], "0 아니오")

        g = Form(top)
        g.grid(row=0, column=1, sticky="nw", padx=(28, 0))
        self.g = g
        g.field("hpMul", "적 체력 배수", "1", width=8)
        g.field("dmgMul", "적 피해 배수", "1", width=8)
        g.field("refLv", "권장 레벨", "1", width=8)
        g.field("refStar", "권장 돌파", "0", width=8)
        g.field("idleCoin", "방치 코인", "500", width=8)
        g.field("idlePart", "방치 부품", "3", width=8)
        g.field("idleCrystal", "방치 크리스탈", "5", width=8)

        self.map_note = ttk.Label(right, foreground="#5D6871", wraplength=620,
                                  justify="left")
        self.map_note.pack(anchor="w", pady=(10, 0))

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)
        wh = ttk.Frame(right)
        wh.pack(anchor="w", fill="x")
        ttk.Label(wh, text="웨이브", font=("", 10, "bold")).pack(side="left")
        self.wave_note = ttk.Label(wh, foreground="#5D6871")
        self.wave_note.pack(side="left", padx=(12, 0))

        ttk.Label(right, text=self.HEADER + "   (입구는 1부터, 체력배수는 비우면 1)",
                  foreground="#5D6871").pack(anchor="w")
        box = ttk.Frame(right)
        box.pack(anchor="w", fill="both", expand=True, pady=4)
        self.waves = tk.Text(box, width=58, height=16, wrap="none")
        wsb = ttk.Scrollbar(box, orient="vertical", command=self.waves.yview)
        self.waves.configure(yscrollcommand=wsb.set)
        self.waves.pack(side="left", fill="both", expand=True)
        wsb.pack(side="left", fill="y")
        self.waves.bind("<KeyRelease>", lambda _e: self._preview())

        self.save_btn = ttk.Button(right, text="스테이지 추가", command=self.save)
        self.save_btn.pack(anchor="w", pady=(10, 0))

        self.refresh()

    # ------------------------------------------------------------ 목록

    def refresh(self):
        t = load_tables()
        s = t["string_ko"]
        maps = [r["id"] for r in t.get("map", [])]
        if maps:
            self.f.vars["mapId"].set(self.f.get("mapId") or maps[0])
            for w in self.f.winfo_children():
                if isinstance(w, ttk.Combobox) and w.cget("textvariable") == str(self.f.vars["mapId"]):
                    w["values"] = maps
        rows = sorted(t.get("stage", []), key=lambda r: r.get("order") or 0)
        self._map_name = {r["id"]: s.get(r.get("nameKey"), r["id"])
                          for r in t.get("map", [])}
        self.picker.set_rows([(r["id"], "%s  %s" % (r.get("no"),
                                                    self._map_name.get(r.get("mapId"), "")))
                              for r in rows])

    def reset(self):
        self.current = None
        self.picker.clear()
        self.title.config(text="새 스테이지")
        self.save_btn.config(text="스테이지 추가")
        blanks = {"no": "", "chapter": "1", "area": "1", "startPower": "5",
                  "boss": "0 아니오", "hpMul": "1", "dmgMul": "1", "refLv": "1",
                  "refStar": "0", "idleCoin": "500", "idlePart": "3", "idleCrystal": "5"}
        for form in (self.f, self.g):
            for k, v in form.vars.items():
                if k in blanks:
                    v.set(blanks[k])
        self.waves.delete("1.0", "end")
        self.waves.insert("1.0", "3, dust, 3, 1, 1, 1\n")
        self._preview()
        self.map_note.config(text="")

    def load(self, sid):
        try:
            self._load(sid)
        except Exception as e:
            messagebox.showerror("불러오기 실패", str(e))

    def _load(self, sid):
        t = load_tables()
        row = next((r for r in t["stage"] if r["id"] == sid), None)
        if row is None:
            raise ValueError("스테이지 %s 를 찾지 못했습니다." % sid)

        self.f.vars["no"].set(str(row.get("no") or ""))
        self.f.vars["chapter"].set(str(int(row.get("chapter") or 0)))
        self.f.vars["area"].set(str(int(row.get("area") or 0)))
        self.f.vars["mapId"].set(str(row.get("mapId") or ""))
        self.f.vars["startPower"].set(_trim(row.get("startPower")))
        self.f.vars["boss"].set("1 예" if int(row.get("boss") or 0) else "0 아니오")
        for k in ("hpMul", "dmgMul", "refLv", "refStar",
                  "idleCoin", "idlePart", "idleCrystal"):
            self.g.vars[k].set(_trim(row.get(k)))

        gid = row.get("waveGroup")
        lines = []
        for w in sorted((w for w in t["wave"] if w.get("groupId") == gid),
                        key=lambda w: w.get("seq") or 0):
            lines.append("%s, %s, %s, %s, %s, %s" % (
                _trim(w.get("t")), w.get("enemy"), _trim(w.get("count")),
                _trim(w.get("gap")), _trim(w.get("entrance")), _trim(w.get("hpMul"))))
        self.waves.delete("1.0", "end")
        self.waves.insert("1.0", "\n".join(lines))

        self.current = sid
        s = t["string_ko"]
        mp = next((m for m in t.get("map", []) if m["id"] == row.get("mapId")), None)
        if mp:
            spawns = [s.get(k, k) for k in (mp.get("spawnKeys") or [])]
            self.map_note.config(
                text="맵 '%s' — 입구 %d곳: %s   (맵의 타일과 경로는 엑셀에서 고칩니다)"
                     % (self._map_name.get(mp["id"], mp["id"]), len(spawns),
                        ", ".join("%d %s" % (i + 1, n) for i, n in enumerate(spawns))))
        self.title.config(text="수정 중 — %s %s (id %s)"
                               % (row.get("no"),
                                  self._map_name.get(row.get("mapId"), ""), sid))
        self.save_btn.config(text="수정 내용 저장")
        self._preview()
        self.picker.select(sid)

    # ------------------------------------------------------------ 웨이브 해석

    def _parse(self, enemies=None, spawn_count=None):
        """글로 적힌 웨이브를 행으로. 틀린 줄은 줄 번호와 함께 올린다."""
        rows, problems = [], []
        for n, raw in enumerate(self.waves.get("1.0", "end").splitlines(), 1):
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            parts = [p.strip() for p in line.replace("\t", ",").split(",")]
            parts = [p for p in parts if p != ""]
            if len(parts) < 5:
                problems.append("%d번째 줄: 칸이 모자랍니다 (%s)" % (n, self.HEADER))
                continue
            try:
                t0 = float(parts[0])
                enemy = parts[1]
                count = int(float(parts[2]))
                gap = float(parts[3])
                entrance = int(float(parts[4]))
                hp = float(parts[5]) if len(parts) > 5 else 1.0
            except ValueError:
                problems.append("%d번째 줄: 숫자를 읽을 수 없습니다 — %s" % (n, line))
                continue
            if enemies is not None and enemy not in enemies:
                problems.append("%d번째 줄: '%s' 는 없는 적입니다" % (n, enemy))
            if count <= 0:
                problems.append("%d번째 줄: 마리 수는 1 이상이어야 합니다" % n)
            if spawn_count and not (1 <= entrance <= spawn_count):
                problems.append("%d번째 줄: 입구는 1~%d 사이여야 합니다 (지금 %d)"
                                % (n, spawn_count, entrance))
            rows.append({"seq": len(rows) + 1, "t": t0, "enemy": enemy,
                         "count": count, "gap": gap, "entrance": entrance,
                         "hpMul": hp})
        return rows, problems

    def _preview(self):
        rows, problems = self._parse()
        if not rows:
            self.wave_note.config(text="", foreground="#5D6871")
            return
        total = sum(r["count"] for r in rows)
        last = max(r["t"] + r["gap"] * max(0, r["count"] - 1) for r in rows)
        doors = {}
        for r in rows:
            doors[r["entrance"]] = doors.get(r["entrance"], 0) + r["count"]
        door_text = " · ".join("%d번 %d마리" % (k, v) for k, v in sorted(doors.items()))
        text = "%d줄 · 총 %d마리 · 마지막 등장 %d초 · %s" % (
            len(rows), total, round(last), door_text)
        if problems:
            text += "   ⚠ %d줄 확인 필요" % len(problems)
        self.wave_note.config(text=text,
                              foreground="#C0392B" if problems else "#5D6871")

    # ------------------------------------------------------------ 저장

    def save(self):
        try:
            self._save()
        except Exception as e:
            messagebox.showerror("저장 실패", str(e))

    def _save(self):
        no = self.f.get("no")
        if not re.fullmatch(r"\d+-\d+", no or ""):
            raise ValueError("번호는 '2-4' 처럼 적어 주세요.")

        t = load_tables()
        enemies = {r["id"] for r in t.get("enemy", [])}
        map_id = self.f.get("mapId")
        mp = next((m for m in t.get("map", []) if m["id"] == map_id), None)
        if mp is None:
            raise ValueError("'%s' 는 없는 맵입니다." % map_id)
        spawn_count = len(mp.get("spawnKeys") or []) or None

        rows, problems = self._parse(enemies, spawn_count)
        if problems:
            raise ValueError("웨이브를 고쳐 주세요:\n\n" + "\n".join(problems[:12]))
        if not rows:
            raise ValueError("웨이브를 한 줄 이상 적어 주세요. 적이 안 나오는 판은 만들 수 없습니다.")

        stage = next((r for r in t["stage"] if r["id"] == self.current), None) \
            if self.current is not None else None
        editing = stage is not None

        if stage is None:
            if any(str(r.get("no")) == no for r in t["stage"]):
                raise ValueError("'%s' 번호의 스테이지가 이미 있습니다." % no)
            sid = max((r["id"] for r in t["stage"]), default=100) + 1
            stage = {"id": sid, "waveGroup": sid, "unlockCond": 0,
                     "firstRewardGroup": 0, "refGear": 0,
                     "order": max((r.get("order") or 0) for r in t["stage"]) + 1}
            t["stage"].append(stage)

        stage.update({
            "no": no,
            "chapter": self.f.num("chapter", int),
            "area": self.f.num("area", int),
            "mapId": map_id,
            "startPower": self.f.num("startPower"),
            "boss": leading_int(self.f.get("boss")),
            "hpMul": self.g.num("hpMul"), "dmgMul": self.g.num("dmgMul"),
            "refLv": self.g.num("refLv", int), "refStar": self.g.num("refStar", int),
            "idleCoin": self.g.num("idleCoin", int),
            "idlePart": self.g.num("idlePart", int),
            "idleCrystal": self.g.num("idleCrystal", int),
        })

        # 이 판의 웨이브만 **제자리에서** 갈아 끼운다. 지우고 끝에 붙이면 1356행짜리
        # wave 시트의 순서가 통째로 밀려, 고치지도 않은 판까지 바뀐 것처럼 보인다.
        gid = stage["waveGroup"]
        for r in rows:
            r["groupId"] = gid
        old = t["wave"]
        at = next((i for i, w in enumerate(old) if w.get("groupId") == gid), len(old))
        t["wave"] = (old[:at] + rows
                     + [w for w in old[at:] if w.get("groupId") != gid])

        books = save_sheets(t, ["stage", "wave"])
        total = sum(r["count"] for r in rows)
        self.app.log("스테이지 %s %s — 웨이브 %d줄 · 총 %d마리 — 수정한 엑셀: %s"
                     % (no, "수정" if editing else "추가", len(rows), total,
                        ", ".join(books)))
        if not editing:
            self.app.log("  주의: 첫 클리어 보상(firstRewardGroup)과 해금 조건(unlockCond)은 "
                         "아직 0 입니다. 필요하면 04_Stage.xlsx 에서 채우세요.")
        self.current = stage["id"]
        self.title.config(text="수정 중 — %s (id %s)" % (no, stage["id"]))
        self.save_btn.config(text="수정 내용 저장")
        self.refresh()
        self.picker.select(stage["id"])
        self.app.sync()


# ---------------------------------------------------------------- 맵 격자

class MapTab(ttk.Frame):
    """맵의 타일과 입구별 경로를 격자 위에서 고친다.

    왜 격자여야 하나
      타일은 10x7 짜리 글자판이고 경로는 그 위를 지나는 꺾은선이다. 둘은 **서로
      맞아야** 한다 — 경로가 벽을 지나면 적이 벽을 통과하고, 목표에 닿지 않으면
      그 입구로 들어온 적이 영원히 돌아다닌다. 글로 고치면 그걸 눈으로 확인할
      방법이 없다. 그래서 같은 그림 위에서 둘 다 고치고, 저장할 때 서로 맞는지
      검사한다.

    경로 좌표는 [열, 행] 이다(행,열이 아니다). 원본 데이터가 그렇게 되어 있다.
    """

    # 게임이 쓰는 색과 같은 값. 화면에서 보는 것이 곧 전장에서 보일 것이어야 한다.
    PALETTE = [
        ("P", "바닥", "#DCC39A", "근접 가전을 놓는다. 적도 여기로 다닌다"),
        ("H", "가구", "#F3F5F6", "원거리 가전을 놓는다. 적은 못 지나간다"),
        ("X", "벽", "#C3CBD1", "아무것도 못 한다"),
        ("E", "목표", "#E0524A", "적이 닿으면 실점. 맵에 딱 하나"),
        ("F", "불", "#2F3438", "위에 놓인 가전이 과열된다"),
        ("W", "물", "#8FC0E0", "가전이 식는다. 적은 빨라진다"),
        ("R", "러그", "#D99AAE", "적이 느려진다"),
        ("O", "기름", "#2A2A30", "가전이 과열되고 적이 빨라진다"),
    ]
    WALKABLE = set("PFWRO")          # isGround — 적이 지나갈 수 있는 칸
    CELL = 42

    def __init__(self, master, app):
        super().__init__(master, padding=14)
        self.app = app
        self.current = None
        self.tiles = []              # 행 문자열 목록
        self.paths = []              # [[[col,row], ...], ...]
        self.brush = tk.StringVar(value="P")
        self.mode = tk.StringVar(value="tile")   # tile | path
        self.path_index = tk.IntVar(value=0)

        self.picker = Picker(self, self.load, width=20)
        self.picker.pack(side="left", fill="y", padx=(0, 14))

        mid = ttk.Frame(self)
        mid.pack(side="left", fill="y", padx=(0, 14))
        self.title = ttk.Label(mid, text="맵을 고르세요", font=("", 11, "bold"))
        self.title.pack(anchor="w", pady=(0, 6))
        self.canvas = tk.Canvas(mid, width=7 * self.CELL + 1, height=10 * self.CELL + 1,
                                background="#F7F9FA", highlightthickness=0)
        self.canvas.pack(anchor="w")
        self.canvas.bind("<Button-1>", self._click)
        self.canvas.bind("<B1-Motion>", self._drag)
        self.canvas.bind("<Button-3>", self._right_click)
        self.hint = ttk.Label(mid, foreground="#5D6871", wraplength=7 * self.CELL,
                              justify="left")
        self.hint.pack(anchor="w", pady=(6, 0))

        right = ttk.Frame(self)
        right.pack(side="left", fill="both", expand=True)

        ttk.Label(right, text="무엇을 고칠까", font=("", 10, "bold")).pack(anchor="w")
        mf = ttk.Frame(right)
        mf.pack(anchor="w", pady=(4, 10))
        ttk.Radiobutton(mf, text="타일", value="tile", variable=self.mode,
                        command=self._draw).pack(side="left")
        ttk.Radiobutton(mf, text="경로", value="path", variable=self.mode,
                        command=self._draw).pack(side="left", padx=(10, 0))

        self.mode_slot = ttk.Frame(right)
        self.mode_slot.pack(anchor="w", fill="x")

        self.tile_box = ttk.Frame(self.mode_slot)
        self.tile_box.pack(anchor="w", fill="x")
        ttk.Label(self.tile_box, text="칠할 것", font=("", 10, "bold")).pack(anchor="w")
        for ch, name, colour, note in self.PALETTE:
            row = ttk.Frame(self.tile_box)
            row.pack(anchor="w", fill="x", pady=1)
            ttk.Radiobutton(row, text="", value=ch, variable=self.brush,
                            width=2).pack(side="left")
            sw = tk.Label(row, width=3, background=colour, relief="solid", borderwidth=1)
            sw.pack(side="left", padx=(0, 6))
            ttk.Label(row, text="%s  %s" % (name, note),
                      foreground="#5D6871").pack(side="left")

        self.path_box = ttk.Frame(self.mode_slot)
        ttk.Label(self.path_box, text="경로", font=("", 10, "bold")).pack(anchor="w")
        self.path_buttons = ttk.Frame(self.path_box)
        self.path_buttons.pack(anchor="w", pady=4)
        ttk.Label(self.path_box, justify="left", foreground="#5D6871", wraplength=380,
                  text="칸을 누르면 그 입구의 경로에 꺾는점이 하나 붙습니다. "
                       "오른쪽 버튼으로 마지막 점을 지웁니다.\n"
                       "경로는 가로·세로로만 꺾이고, 지나는 칸은 전부 적이 다닐 수 "
                       "있어야 하며, 마지막은 목표(E)여야 합니다.").pack(anchor="w")
        self.path_list = tk.Text(self.path_box, width=44, height=8, wrap="word")
        self.path_list.pack(anchor="w", pady=(6, 0))
        self.path_list.configure(state="disabled")

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)
        self.report = tk.Text(right, width=44, height=9, wrap="word")
        self.report.pack(anchor="w", fill="both", expand=True)
        self.report.configure(state="disabled")

        btns = ttk.Frame(right)
        btns.pack(anchor="w", pady=(10, 0))
        self.save_btn = ttk.Button(btns, text="맵 저장", command=self.save)
        self.save_btn.pack(side="left")
        ttk.Button(btns, text="되돌리기",
                   command=lambda: self.load(self.current) if self.current else None
                   ).pack(side="left", padx=6)

        self._sync_mode()
        self.mode.trace_add("write", lambda *_: self._sync_mode())
        self.refresh()

    # ------------------------------------------------------------ 목록

    def refresh(self):
        t = load_tables()
        s = t["string_ko"]
        self.picker.set_rows([(m["id"], "%s  (%s)" % (s.get(m.get("nameKey"), m["id"]),
                                                      m["id"]))
                              for m in t.get("map", [])])

    def load(self, mid):
        try:
            self._load(mid)
        except Exception as e:
            messagebox.showerror("불러오기 실패", str(e))

    def _load(self, mid):
        t = load_tables()
        m = next((r for r in t.get("map", []) if r["id"] == mid), None)
        if m is None:
            raise ValueError("'%s' 맵을 찾지 못했습니다." % mid)
        s = t["string_ko"]
        self.current = mid
        self.tiles = [str(r) for r in (m.get("tiles") or [])]
        self.paths = [[list(pt) for pt in path] for path in (m.get("paths") or [])]
        self.spawn_names = [s.get(k, k) for k in (m.get("spawnKeys") or [])]
        self.title.config(text="%s (%s)" % (s.get(m.get("nameKey"), mid), mid))
        self._build_path_buttons()
        self._draw()
        self._check()
        self.picker.select(mid)

    def _build_path_buttons(self):
        for w in self.path_buttons.winfo_children():
            w.destroy()
        for i, name in enumerate(getattr(self, "spawn_names", [])):
            ttk.Radiobutton(self.path_buttons, text="%d %s" % (i + 1, name), value=i,
                            variable=self.path_index,
                            command=self._draw).pack(side="left", padx=(0, 10))
        if self.spawn_names:
            self.path_index.set(0)

    def _sync_mode(self):
        if self.mode.get() == "tile":
            self.path_box.pack_forget()
            self.tile_box.pack(anchor="w", fill="x")
            self.hint.config(text="칸을 눌러 칠합니다. 끌어서 여러 칸을 한 번에 칠할 수 있습니다.")
        else:
            self.tile_box.pack_forget()
            self.path_box.pack(anchor="w", fill="x")
            self.hint.config(text="칸을 누르면 꺾는점이 붙고, 오른쪽 버튼으로 지웁니다.")

    # ------------------------------------------------------------ 그리기

    def _colour(self, ch):
        for c, _, colour, _ in self.PALETTE:
            if c == ch:
                return colour
        return "#DCC39A"

    def _draw(self):
        c = self.canvas
        c.delete("all")
        if not self.tiles:
            return
        cell = self.CELL
        for r, row in enumerate(self.tiles):
            for col, ch in enumerate(row):
                x, y = col * cell, r * cell
                c.create_rectangle(x, y, x + cell, y + cell,
                                   fill=self._colour(ch), outline="#C7D0D6")
                c.create_text(x + cell - 7, y + 8, text=ch, anchor="ne",
                              fill="#5D6871", font=("", 7))

        # 경로. 지금 고르는 것만 진하게, 나머지는 흐리게 — 서로 가리지 않도록.
        active = self.path_index.get() if self.mode.get() == "path" else -1
        for i, path in enumerate(self.paths):
            if len(path) < 2 and not path:
                continue
            pts = []
            for col, row in path:
                pts += [col * cell + cell / 2, row * cell + cell / 2]
            live = (i == active)
            colour = "#E8842A" if live else "#8D99A2"
            if len(pts) >= 4:
                c.create_line(*pts, fill=colour, width=4 if live else 2,
                              arrow="last", capstyle="round", joinstyle="round")
            for n, (col, row) in enumerate(path):
                x, y = col * cell + cell / 2, row * cell + cell / 2
                rr = 7 if live else 4
                c.create_oval(x - rr, y - rr, x + rr, y + rr,
                              fill=colour, outline="#FFFFFF", width=1)
                if live:
                    c.create_text(x, y, text=str(n + 1), fill="#FFFFFF", font=("", 7, "bold"))
        self._write_paths()

    def _write_paths(self):
        self.path_list.configure(state="normal")
        self.path_list.delete("1.0", "end")
        for i, path in enumerate(self.paths):
            name = self.spawn_names[i] if i < len(self.spawn_names) else "?"
            pts = " → ".join("(%d,%d)" % (c, r) for c, r in path)
            self.path_list.insert("end", "%d %s: %s\n" % (i + 1, name, pts or "(없음)"))
        self.path_list.configure(state="disabled")

    # ------------------------------------------------------------ 조작

    def _cell_at(self, event):
        col, row = event.x // self.CELL, event.y // self.CELL
        if 0 <= row < len(self.tiles) and 0 <= col < len(self.tiles[0]):
            return int(col), int(row)
        return None

    def _click(self, event):
        at = self._cell_at(event)
        if at is None or not self.tiles:
            return
        col, row = at
        if self.mode.get() == "tile":
            self._paint(col, row)
        else:
            i = self.path_index.get()
            if i < len(self.paths):
                self.paths[i].append([col, row])
        self._draw()
        self._check()

    def _drag(self, event):
        if self.mode.get() != "tile":
            return                       # 경로는 끌어서 찍지 않는다. 실수로 점이 쏟아진다.
        at = self._cell_at(event)
        if at:
            self._paint(*at)
            self._draw()

    def _right_click(self, event):
        if self.mode.get() != "path":
            return
        i = self.path_index.get()
        if i < len(self.paths) and self.paths[i]:
            self.paths[i].pop()
            self._draw()
            self._check()

    def _paint(self, col, row):
        ch = self.brush.get()
        line = self.tiles[row]
        if line[col] == ch:
            return
        self.tiles[row] = line[:col] + ch + line[col + 1:]

    # ------------------------------------------------------------ 검사

    def _problems(self):
        """맵이 말이 되는지. 여기가 이 화면의 존재 이유다."""
        bad = []
        if not self.tiles:
            return ["맵을 먼저 고르세요."]
        rows, cols = len(self.tiles), len(self.tiles[0])
        if any(len(r) != cols for r in self.tiles):
            bad.append("행마다 길이가 다릅니다.")

        goals = [(c, r) for r, line in enumerate(self.tiles)
                 for c, ch in enumerate(line) if ch == "E"]
        if len(goals) != 1:
            bad.append("목표(E)는 딱 하나여야 합니다. 지금 %d개." % len(goals))

        if len(self.paths) != len(getattr(self, "spawn_names", [])):
            bad.append("경로가 %d개인데 입구 이름은 %d개입니다."
                       % (len(self.paths), len(self.spawn_names)))

        for i, path in enumerate(self.paths):
            name = "%d번 경로" % (i + 1)
            if len(path) < 2:
                bad.append("%s: 꺾는점이 2개 이상이어야 합니다." % name)
                continue
            for (c0, r0), (c1, r1) in zip(path, path[1:]):
                if c0 != c1 and r0 != r1:
                    bad.append("%s: (%d,%d)→(%d,%d) 가 비스듬합니다. "
                               "가로나 세로로만 움직여야 합니다." % (name, c0, r0, c1, r1))
            # 지나는 칸이 전부 걸을 수 있는지
            for c, r in self._walk(path):
                if not (0 <= r < rows and 0 <= c < cols):
                    bad.append("%s: (%d,%d) 가 맵 밖입니다." % (name, c, r))
                    break
                ch = self.tiles[r][c]
                if ch not in self.WALKABLE and ch != "E":
                    bad.append("%s: (%d,%d) 는 '%s' 라 지나갈 수 없습니다." % (name, c, r, ch))
                    break
            if goals and tuple(path[-1]) != goals[0]:
                bad.append("%s: 마지막 점이 목표(E)가 아닙니다. 목표는 (%d,%d)."
                           % (name, goals[0][0], goals[0][1]))
        return bad

    def _notes(self):
        """막지는 않지만 한 번 보는 게 좋은 것.

        '입구는 가장자리에 있어야 한다' 를 처음에 막는 규칙으로 넣었다가 기존 맵 두
        개가 걸렸다 — 욕실의 배수구([1,3])와 덕트의 팬 구멍([3,1])이다. 둘 다 바닥
        한가운데에서 솟는 입구이고 그게 맞다. 게임에 없는 규칙을 내가 만들어 멀쩡한
        맵을 막을 뻔했다. 그래서 알림으로만 남긴다.
        """
        out = []
        if not self.tiles:
            return out
        rows, cols = len(self.tiles), len(self.tiles[0])
        for i, path in enumerate(self.paths):
            if not path:
                continue
            c0, r0 = path[0]
            if not (c0 in (0, cols - 1) or r0 in (0, rows - 1)):
                out.append("%d번 경로의 첫 점 (%d,%d) 이 가장자리가 아닙니다. "
                           "배수구처럼 안에서 솟는 입구면 그대로 두세요."
                           % (i + 1, c0, r0))
        return out

    @staticmethod
    def _walk(path):
        """꺾은선이 실제로 밟는 칸을 전부 펼친다."""
        out = []
        for (c0, r0), (c1, r1) in zip(path, path[1:]):
            step_c = (c1 > c0) - (c1 < c0)
            step_r = (r1 > r0) - (r1 < r0)
            c, r = c0, r0
            out.append((c, r))
            while (c, r) != (c1, r1):
                c += step_c
                r += step_r
                out.append((c, r))
        return out

    def _check(self):
        bad = self._problems()
        notes = self._notes()
        self.report.configure(state="normal")
        self.report.delete("1.0", "end")
        if bad:
            self.report.insert("end", "고쳐야 저장됩니다 — %d가지\n\n" % len(bad))
            for b in bad:
                self.report.insert("end", "  · " + b + "\n")
        else:
            counts = {}
            for line in self.tiles:
                for ch in line:
                    counts[ch] = counts.get(ch, 0) + 1
            names = {c: n for c, n, _, _ in self.PALETTE}
            text = " · ".join("%s %d" % (names.get(c, c), n)
                              for c, n in sorted(counts.items()))
            self.report.insert("end", "문제 없음\n\n" + text + "\n")
            for i, path in enumerate(self.paths):
                self.report.insert("end", "\n%d번 경로: %d칸"
                                   % (i + 1, len(self._walk(path))))
        if notes:
            self.report.insert("end", "\n\n알림 (저장은 됩니다)\n")
            for n in notes:
                self.report.insert("end", "  · " + n + "\n")
        self.report.configure(state="disabled")
        return bad

    # ------------------------------------------------------------ 저장

    def save(self):
        try:
            self._save()
        except Exception as e:
            messagebox.showerror("저장 실패", str(e))

    def _save(self):
        if not self.current:
            raise ValueError("맵을 고르세요.")
        bad = self._check()
        if bad:
            raise ValueError("맵이 아직 말이 되지 않습니다:\n\n" + "\n".join(bad[:10]))

        t = load_tables()
        m = next((r for r in t["map"] if r["id"] == self.current), None)
        if m is None:
            raise ValueError("'%s' 맵이 사라졌습니다." % self.current)
        m["tiles"] = list(self.tiles)
        m["paths"] = [[list(pt) for pt in path] for path in self.paths]

        books = save_sheets(t, ["map"])
        self.app.log("맵 %s 저장 — 수정한 엑셀: %s" % (self.current, ", ".join(books)))
        self.app.log("  이 맵을 쓰는 스테이지가 전부 바뀝니다. "
                     "Unity 에서 Rebuild Project Assets 후 실제로 돌아 보세요.")
        self.app.sync()


# ---------------------------------------------------------------- 창

class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("더스트 제로 — 콘텐츠 관리")
        self.geometry("1180x900")

        nb = ttk.Notebook(self)
        nb.pack(fill="both", expand=True, padx=10, pady=(10, 4))
        nb.add(CharacterTab(nb, self), text="캐릭터")
        nb.add(ProductTab(nb, self), text="상품")
        nb.add(ArtTab(nb, self), text="이미지 세트")
        nb.add(StageTab(nb, self), text="스테이지")
        nb.add(MapTab(nb, self), text="맵")

        bar = ttk.Frame(self)
        bar.pack(fill="x", padx=10)
        ttk.Button(bar, text="지금 빌드 + 동기화", command=self.sync).pack(side="left")
        ttk.Button(bar, text="기록 지우기",
                   command=lambda: self.out.delete("1.0", "end")).pack(side="left", padx=6)
        ttk.Label(bar, text="엑셀: GameData/tables  →  Unity: StreamingAssets/data",
                  foreground="#5D6871").pack(side="right")

        self.out = tk.Text(self, height=12, wrap="word")
        self.out.pack(fill="both", padx=10, pady=(6, 10))
        self.log("왼쪽 목록에서 고르면 불러와 고칠 수 있습니다. "
                 "엑셀을 직접 고쳐도 됩니다 — 이 도구는 같은 파일을 씁니다.")

    def log(self, text):
        self.out.insert("end", text.rstrip() + "\n")
        self.out.see("end")
        self.update_idletasks()

    def sync(self):
        """엑셀 -> JSON -> StreamingAssets. 검증 실패는 숨기지 않고 그대로 보여 준다."""
        self.log("--- 빌드 + 동기화 ---")
        ok = run_sync(self.log)
        self.log("동기화 완료." if ok else
                 "동기화 실패 — 위 메시지의 테이블 오류를 고친 뒤 다시 실행하세요.")


def _probe_unit():
    """GUI 없이, 캐릭터 탭이 읽는 경로를 그대로 따라가 본다."""
    t = load_tables()
    if not t["unit"]:
        raise RuntimeError("unit 테이블이 비어 있습니다.")
    u = t["unit"][0]
    s = t["string_ko"]
    name = s.get(u.get("nameKey"))
    if not name:
        raise RuntimeError("%s 의 이름 문자열(%s)이 없습니다." % (u["id"], u.get("nameKey")))
    art = next((r for r in t.get("unit_art", []) if r["id"] == u["id"]), None)
    beh = next((r for r in t.get("unit_behaviour", []) if r["id"] == u["id"]), None)
    if art is None or beh is None:
        raise RuntimeError("%s 의 unit_art/unit_behaviour 행이 없습니다." % u["id"])
    return "%s(%s) atk=%s defense=%r" % (name, u["id"], u.get("atk"), beh.get("defense"))


def _probe_product():
    t = load_tables()
    if not t["shop_product"]:
        raise RuntimeError("shop_product 테이블이 비어 있습니다.")
    p = t["shop_product"][0]
    rewards = [r for r in t["reward_group"] if r.get("groupId") == p.get("rewardGroup")]
    if not rewards:
        raise RuntimeError("상품 %s 의 보상 묶음 %s 가 비어 있습니다."
                           % (p["id"], p.get("rewardGroup")))
    return "%s 가격=%s 보상 %d줄" % (t["string_ko"].get(p.get("nameKey"), p["id"]),
                                     p.get("price"), len(rewards))


def selftest(out_path):
    """GUI 없이 exe 가 제대로 묶였는지 확인하고 결과를 파일로 남긴다.

    --windowed 로 묶은 exe 는 콘솔이 없어 print 가 어디에도 나오지 않는다. 그래서
    파일에 쓴다. 빌드가 '성공'했는데 실제로는 openpyxl 이 빠졌거나 프로젝트를 못 찾는
    경우를 잡기 위한 것이다 — 창이 뜨는 것만 보고 넘어가면 그걸 알 수 없다.
    """
    lines = []
    ok = True

    def check(label, fn):
        nonlocal ok
        try:
            lines.append(f"OK   {label}: {fn()}")
        except Exception as exc:
            ok = False
            lines.append(f"FAIL {label}: {exc!r}")

    lines.append(f"frozen={getattr(sys, 'frozen', False)}")
    lines.append(f"exe={sys.executable}")
    lines.append(f"ROOT={ROOT}")
    check("xlsx_io.WORKBOOKS", lambda: f"{len(xlsx_io.WORKBOOKS)} workbooks")
    check("openpyxl", lambda: __import__("openpyxl").__version__)
    check("tables 읽기", lambda: f"{len(load_tables())} tables")
    check("unit 행", lambda: f"{len(load_tables()['unit'])} units")
    check("sync_to_unity 임포트", lambda: __import__("sync_to_unity").__name__)
    check("build 임포트", lambda: __import__("build").__name__)
    check("tables 폴더", lambda: os.path.isdir(TABLES) and TABLES)
    check("Art 폴더", lambda: os.path.isdir(ILLUST_DIR) and ILLUST_DIR)

    # 불러오기가 실제로 되는지 — 목록만 뜨고 값이 비어 나오면 수정이 삭제가 된다.
    check("캐릭터 불러오기", lambda: _probe_unit())
    check("상품 불러오기", lambda: _probe_product())

    lines.append("RESULT=" + ("PASS" if ok else "FAIL"))
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return 0 if ok else 1


if __name__ == "__main__":
    # --selftest <파일>  : 창을 띄우지 않고 점검 결과만 기록한다.
    if "--selftest" in sys.argv:
        i = sys.argv.index("--selftest")
        dest = sys.argv[i + 1] if len(sys.argv) > i + 1 else os.path.join(ROOT, "studio-selftest.txt")
        raise SystemExit(selftest(dest))
    App().mainloop()
