# -*- coding: utf-8 -*-
"""불러오기 → 저장이 데이터를 잃지 않는지 확인한다.

추가만 하던 도구에 수정을 넣으면 가장 위험한 건 조용한 손실이다. 불러올 때 읽지 않은
열은 저장할 때 날아가고, 화면에는 아무 신호도 뜨지 않는다. 그래서 아무것도 바꾸지 않고
한 바퀴 돌린 뒤 테이블을 통째로 비교한다. 한 글자라도 달라지면 실패다.

엑셀을 실제로 쓰지 않고 **메모리 안에서** 돈다. 진짜 파일로 돌리면 한 바퀴에 workbook
수십 번 읽고 쓰느라 십수 분이 걸려, 쓰지 않게 되고, 쓰지 않는 검사는 검사가 아니다.
openpyxl 왕복 자체는 xlsx_io 쪽 문제라 여기서 볼 것이 아니다.

실행: python tools/studio_roundtrip.py
"""
import copy
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import dz_studio                                        # noqa: E402


def diff(before, after, label):
    """두 테이블 묶음의 차이를 사람이 읽을 수 있게 돌려준다."""
    problems = []
    for sheet in sorted(set(before) | set(after)):
        a, b = before.get(sheet), after.get(sheet)
        if type(a) is not type(b):
            problems.append(f"{sheet}: 형태가 바뀜 {type(a)} -> {type(b)}")
            continue
        if isinstance(a, dict):
            for k in sorted(set(a) | set(b)):
                if a.get(k) != b.get(k):
                    problems.append(f"{sheet}[{k}]: {a.get(k)!r} -> {b.get(k)!r}")
        else:
            if len(a) != len(b):
                problems.append(f"{sheet}: 행 수 {len(a)} -> {len(b)}")
                continue
            for i, (ra, rb) in enumerate(zip(a, b)):
                for k in sorted(set(ra) | set(rb)):
                    va, vb = ra.get(k), rb.get(k)
                    if va == vb:
                        continue
                    try:                       # 1 과 1.0 은 같은 값으로 본다
                        if float(va) == float(vb):
                            continue
                    except (TypeError, ValueError):
                        pass
                    problems.append(f"{sheet}[{i}].{k}: {va!r} -> {vb!r}")
    return [f"{label}: {p}" for p in problems]


class FakeApp:
    """로그만 받고 동기화는 하지 않는 가짜 창."""

    def __init__(self):
        self.lines = []

    def log(self, text):
        self.lines.append(str(text).rstrip())

    def sync(self):
        pass


def main():
    try:
        import tkinter as tk
        from tkinter import ttk
    except Exception as exc:
        print("Tk 를 쓸 수 없습니다:", exc)
        return 2

    disk = dz_studio.load_tables()
    live = copy.deepcopy(disk)

    # 디스크 대신 이 사본을 읽고 쓰게 바꿔 둔다. 탭 코드는 전혀 모르는 채로 돈다.
    dz_studio.load_tables = lambda: copy.deepcopy(live)

    def fake_save(tables, sheets):
        for sheet in sheets:
            live[sheet] = copy.deepcopy(tables[sheet])
        return ["(메모리)"]

    dz_studio.save_sheets = fake_save

    root = tk.Tk()
    root.withdraw()
    app = FakeApp()
    failures = []

    # 탭은 실패를 messagebox 로 띄운다. 창이 없는 검사에서는 그게 영원히 기다리는
    # 모달이 되어 테스트가 멈춘다 — 실제로 멈췄다. 띄우는 대신 모아 둔다.
    shown = []
    dz_studio.messagebox.showerror = lambda title, msg: shown.append(f"{title}: {msg}")
    dz_studio.messagebox.showinfo = lambda *a, **k: None
    dz_studio.messagebox.showwarning = lambda *a, **k: None

    before = copy.deepcopy(live)
    tab = dz_studio.CharacterTab(ttk.Frame(root), app)
    units = [r["id"] for r in before["unit"]]
    for uid in units:
        tab.load(uid)
        if tab.current != uid:
            failures.append(f"캐릭터 {uid}: 불러오기가 끝나지 않았습니다.")
            continue
        tab.save()
    failures += diff(before, live, "캐릭터 왕복")

    before2 = copy.deepcopy(live)
    ptab = dz_studio.ProductTab(ttk.Frame(root), app)
    products = [r["id"] for r in before2["shop_product"]]
    for pid in products:
        ptab.load(pid)
        if ptab.current != pid:
            failures.append(f"상품 {pid}: 불러오기가 끝나지 않았습니다.")
            continue
        ptab.save()
    failures += diff(before2, live, "상품 왕복")

    before3 = copy.deepcopy(live)
    stab = dz_studio.StageTab(ttk.Frame(root), app)
    stages = [r["id"] for r in before3["stage"]]
    for sid in stages:
        stab.load(sid)
        if stab.current != sid:
            failures.append(f"스테이지 {sid}: 불러오기가 끝나지 않았습니다.")
            continue
        stab.save()
    failures += diff(before3, live, "스테이지 왕복")

    before4 = copy.deepcopy(live)
    mtab = dz_studio.MapTab(ttk.Frame(root), app)
    maps = [r["id"] for r in before4["map"]]
    for mid in maps:
        mtab.load(mid)
        if mtab.current != mid:
            failures.append(f"맵 {mid}: 불러오기가 끝나지 않았습니다.")
            continue
        mtab.save()
    failures += diff(before4, live, "맵 왕복")

    root.destroy()
    failures += ["대화상자: " + m for m in shown]

    if failures:
        print("실패 %d건:" % len(failures))
        for f in failures[:60]:
            print("  ", f)
        if len(failures) > 60:
            print("   ... 외 %d건" % (len(failures) - 60))
        return 1

    print("PASS — 가전 %d종, 상품 %d개, 스테이지 %d판, 맵 %d종을 불러왔다 저장해도 "
          "테이블이 그대로입니다."
          % (len(units), len(products), len(stages), len(maps)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
