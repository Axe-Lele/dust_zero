# -*- coding: utf-8 -*-
"""주고받기 한 바퀴를 끝까지 돌려 본다.

여기서 확인하는 것
  1. 기준점을 찍고, 값을 고치고, 패치를 뽑으면 **그 변화만** 담기는가
  2. 패치를 읽어 계획을 세우면 전/후와 증감이 제대로 나오는가 (75 → 82 ▲+7)
  3. 합치면 실제로 반영되는가
  4. 양쪽이 같은 칸을 서로 다르게 고쳤을 때 **덮어쓰지 않고 충돌로 잡는가**
  5. 항목을 끄면 그것만 빠지는가
  6. 그림이 패치에 실려 건너가는가

엑셀을 건드리지 않는다. 테이블은 메모리에서 돌리고, 그림만 임시 폴더를 쓴다.
창도 띄우지 않는다 — 화면은 dz_review.as_text 로 같은 내용을 글로 확인한다.

실행: python tools/share_selftest.py
"""
import copy
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import dz_patch                                          # noqa: E402
import dz_review                                         # noqa: E402
import dz_transport                                      # noqa: E402

RESULTS = []


def check(label, cond, detail=""):
    RESULTS.append((bool(cond), label, detail))
    print(("OK   " if cond else "FAIL ") + label + (("  — " + detail) if detail else ""))


def snap(tables, art=None):
    return dz_patch.snapshot(tables, art or {})


def find(rows, **kw):
    for r in rows:
        if all(str(r.get(k)) == str(v) for k, v in kw.items()):
            return r
    return None


def main():
    import dz_studio
    disk = dz_studio.load_tables()

    # ---------------------------------------------------------- 1. 변화만 담기는가
    base_tables = copy.deepcopy(disk)
    base = snap(base_tables)

    theirs = copy.deepcopy(base_tables)
    vac = find(theirs["unit"], id="vac")
    before_atk = float(vac["atk"])
    vac["atk"] = before_atk + 7
    vac["cost"] = float(vac["cost"]) - 2
    theirs["string_ko"]["unit.vac.name"] = "청소기 II"
    theirs["unit"].append({"id": "zz_test", "order": 999, "rarity": 1, "family": 1,
                           "tile": 1, "cost": 10, "heat": 100, "atk": 50, "itv": 1,
                           "range": 1, "block": 1, "sp": 10, "dur": 3,
                           "nameKey": "unit.zz_test.name"})

    patch = dz_patch.make_patch(base, snap(theirs), author="기획")
    sheets = set(patch["tables"])
    check("바뀐 시트만 담긴다", sheets == {"unit", "string_ko"},
          "담긴 시트: %s" % ", ".join(sorted(sheets)))
    check("패치에 id 가 붙는다", len(patch.get("id") or "") == 16, patch.get("id"))

    # ---------------------------------------------------------- 2. 전/후와 증감
    plan = dz_patch.plan_apply(patch, copy.deepcopy(base_tables))
    edits = [c for c in plan.changes if c.kind == "edit" and c.sheet == "unit"]
    adds = [c for c in plan.changes if c.kind == "add" and c.sheet == "unit"]
    check("추가 1건이 잡힌다", len(adds) == 1, "%d건" % len(adds))
    check("수정 1건이 잡힌다", len(edits) == 1, "%d건" % len(edits))

    atk = next((f for c in edits for f in c.fields if f.field == "atk"), None)
    cost = next((f for c in edits for f in c.fields if f.field == "cost"), None)
    check("오른 값은 up 으로 표시된다",
          atk is not None and atk.direction == "up" and atk.delta == 7,
          atk.text() if atk else "없음")
    check("내린 값은 down 으로 표시된다",
          cost is not None and cost.direction == "down" and cost.delta == -2,
          cost.text() if cost else "없음")
    check("라벨이 id 가 아니라 이름이다",
          edits and "청소기" in edits[0].label, edits[0].label if edits else "")
    check("글로도 같은 내용이 나온다", "▲" in dz_review.as_text(plan))

    # ---------------------------------------------------------- 3. 합치면 반영되는가
    mine = copy.deepcopy(base_tables)
    plan = dz_patch.plan_apply(patch, mine)
    touched = dz_patch.apply_plan(plan, mine)
    after = find(mine["unit"], id="vac")
    check("수정이 반영된다", float(after["atk"]) == before_atk + 7,
          "atk=%s" % after["atk"])
    check("추가가 반영된다", find(mine["unit"], id="zz_test") is not None)
    check("문자열도 반영된다", mine["string_ko"]["unit.vac.name"] == "청소기 II")
    check("건드린 시트만 보고된다", touched == {"unit", "string_ko"},
          ", ".join(sorted(touched)))

    # ---------------------------------------------------------- 4. 충돌을 잡는가
    conflicted = copy.deepcopy(base_tables)
    find(conflicted["unit"], id="vac")["atk"] = before_atk + 100   # 이쪽도 고쳤다
    plan = dz_patch.plan_apply(patch, conflicted)
    clash = [c for c in plan.changes if c.status == "conflict"]
    check("양쪽이 고친 칸은 충돌로 잡는다", len(clash) == 1,
          "%d건: %s" % (len(clash), clash[0].note if clash else ""))

    before_conflict = float(find(conflicted["unit"], id="vac")["atk"])
    for c in plan.changes:
        if c.status == "conflict":
            c.selected = False
    dz_patch.apply_plan(plan, conflicted)
    check("충돌을 끄면 이쪽 값이 지켜진다",
          float(find(conflicted["unit"], id="vac")["atk"]) == before_conflict,
          "atk=%s" % find(conflicted["unit"], id="vac")["atk"])

    # ---------------------------------------------------------- 5. 고른 것만 적용
    picky = copy.deepcopy(base_tables)
    plan = dz_patch.plan_apply(patch, picky)
    for c in plan.changes:
        c.selected = (c.kind == "add")
    dz_patch.apply_plan(plan, picky)
    check("끈 항목은 적용되지 않는다",
          float(find(picky["unit"], id="vac")["atk"]) == before_atk
          and find(picky["unit"], id="zz_test") is not None,
          "atk=%s" % find(picky["unit"], id="vac")["atk"])

    # ---------------------------------------------------------- 6. 그림이 건너가는가
    tmp = tempfile.mkdtemp(prefix="dz-share-")
    try:
        png = (b"\x89PNG\r\n\x1a\n" + b"\x00" * 64)
        art_base = {"Sprites/unit_vac.png": "old"}
        art_new = {"Sprites/unit_vac.png": "new"}
        p2 = dz_patch.make_patch({"tables": {}, "art": art_base},
                                 {"tables": {}, "art": art_new}, author="기획")
        check("바뀐 그림이 패치 목록에 오른다", p2["art"] == ["Sprites/unit_vac.png"],
              ", ".join(p2["art"]))

        t = dz_transport.FileTransport(tmp)
        t.push(p2, {"Sprites/unit_vac.png": png})
        heads = t.list()
        check("전송층이 패치를 되돌려준다", len(heads) == 1 and heads[0]["id"] == p2["id"])
        got, blobs = t.fetch(p2["id"])
        check("그림 내용이 그대로 건너간다",
              blobs.get("Sprites/unit_vac.png") == png,
              "%d바이트" % len(blobs.get("Sprites/unit_vac.png", b"")))
        check("합친 뒤에는 대기 목록에서 빠진다",
              (t.ack(p2["id"]) or True) and not t.pending())

        # 네트워크 전송은 아직 없다. 조용히 아무것도 안 하는 대신 분명히 막혀야 한다.
        try:
            dz_transport.HttpTransport("http://example").list()
            check("네트워크 전송은 아직 막혀 있다", False, "예외가 나지 않았습니다")
        except NotImplementedError as exc:
            check("네트워크 전송은 아직 막혀 있다", "파일로" in str(exc))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    # ---------------------------------------------------------- 7. 확인 창
    # 그림으로 보고 끝내지 않는다. 버튼이 창 밖으로 밀려 '적용' 을 누를 수 없던 적이
    # 있었고, 스크린샷만으로는 그걸 알아채기 어려웠다.
    import tkinter as tk
    plan = dz_patch.plan_apply(patch, copy.deepcopy(base_tables))
    root = tk.Tk()
    root.geometry("1x1+0+0")
    win = dz_review.ReviewWindow(root, plan, "검사")
    win.geometry("1020x600+40+40")
    for _ in range(6):
        win.update(); win.update_idletasks()

    kids = win.winfo_children()
    buttons = [w for f in kids for w in f.winfo_children()
               if w.winfo_class() == "TButton"]
    texts = {w.cget("text") for w in buttons}
    check("확인 창에 적용/취소 버튼이 있다", {"적용", "취소"} <= texts,
          ", ".join(sorted(texts)))

    apply_btn = next(w for w in buttons if w.cget("text") == "적용")
    bottom = apply_btn.winfo_rooty() + apply_btn.winfo_height()
    win_bottom = win.winfo_rooty() + win.winfo_height()
    check("적용 버튼이 창 안에 들어온다", bottom <= win_bottom,
          "버튼 아래 %d / 창 아래 %d" % (bottom, win_bottom))

    cols = sum(win.tree.column(c, "width") for c in ("#0", "what", "state"))
    check("칸 세 개가 창 너비 안에 들어온다", cols <= 1020 - 40,
          "칸 합계 %dpx" % cols)

    conflicted = copy.deepcopy(base_tables)
    find(conflicted["unit"], id="vac")["atk"] = before_atk + 100
    cplan = dz_patch.plan_apply(patch, conflicted)
    win2 = dz_review.ReviewWindow(root, cplan, "검사2")
    win2.update(); win2.update_idletasks()
    off = [c for c in cplan.changes if c.status == "conflict" and c.selected]
    check("충돌 항목은 기본으로 꺼져 있다", not off, "켜져 있는 충돌 %d개" % len(off))
    win2.destroy(); win.destroy(); root.destroy()

    # ---------------------------------------------------------- 8. 바뀐 게 없으면
    check("바뀐 것이 없으면 빈 패치다",
          dz_patch.is_empty(dz_patch.make_patch(base, snap(base_tables))))

    bad = [r for r in RESULTS if not r[0]]
    print()
    if bad:
        print("실패 %d건 / 전체 %d건" % (len(bad), len(RESULTS)))
        return 1
    print("PASS — %d건 전부 통과" % len(RESULTS))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
