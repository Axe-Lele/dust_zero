# -*- coding: utf-8 -*-
"""바뀐 내용을 보여 주는 창. 무엇이 바뀌었는지 **계산**은 dz_patch 가 한다.

합치기 전에 반드시 이 화면을 거친다. 숫자는 전/후와 증감을 같이 띄우고(75 → 82 ▲+7),
충돌은 빨갛게 표시한 뒤 기본으로 꺼 둔다. 항목마다 체크를 끄면 그것만 빼고 합친다.

화면과 데이터를 갈라 둔 이유: 나중에 네트워크로 바꾸면 같은 Plan 을 웹 화면에 그릴
수 있어야 한다. 여기서는 Plan 을 '그리기만' 하고 아무것도 계산하지 않는다.
"""
import tkinter as tk
from tkinter import ttk

UP = "#1F8A50"
DOWN = "#C0392B"
CONFLICT = "#C0392B"
MUTED = "#5D6871"


class ReviewWindow(tk.Toplevel):
    """Plan 을 띄우고, 적용할 항목만 골라 돌려준다.

    show(...) 가 True 를 돌려주면 '적용' 을 누른 것이고, plan 의 각 Change.selected 가
    사용자가 고른 대로 맞춰져 있다.
    """

    def __init__(self, master, plan, title="바뀐 내용 확인"):
        super().__init__(master)
        self.title(title)
        self.geometry("1020x720")
        self.plan = plan
        self.ok = False
        self._nodes = {}          # tree item id -> Change

        n = plan.counts()
        head = ttk.Frame(self, padding=(12, 10, 12, 6))
        head.pack(fill="x")
        ttk.Label(head, font=("", 12, "bold"),
                  text="추가 %d · 변경 %d · 삭제 %d" % (n["add"], n["edit"], n["remove"])
                  ).pack(side="left")
        if n["conflict"]:
            ttk.Label(head, foreground=CONFLICT, font=("", 11, "bold"),
                      text="   충돌 %d건 (기본으로 꺼져 있습니다)" % n["conflict"]
                      ).pack(side="left")
        p = plan.patch
        ttk.Label(head, foreground=MUTED,
                  text="%s · %s" % (p.get("author") or "보낸이 미상", p.get("made") or "")
                  ).pack(side="right")

        bar = ttk.Frame(self, padding=(12, 0))
        bar.pack(fill="x")
        ttk.Button(bar, text="전부 켜기", command=lambda: self._set_all(True)).pack(side="left")
        ttk.Button(bar, text="전부 끄기", command=lambda: self._set_all(False)).pack(side="left", padx=6)
        ttk.Label(bar, foreground=MUTED,
                  text="줄을 눌러 켜고 끕니다").pack(side="left", padx=10)

        # 버튼을 트리보다 먼저 아래에 붙인다. 나중에 붙이면 트리가 늘어나면서
        # 창 밖으로 밀려나 '적용' 이 보이지 않는다.
        foot = ttk.Frame(self, padding=(12, 6, 12, 12))
        foot.pack(side="bottom", fill="x")
        self.count_label = ttk.Label(foot, foreground=MUTED)
        self.count_label.pack(side="left")
        ttk.Button(foot, text="적용", command=self._accept).pack(side="right")
        ttk.Button(foot, text="취소", command=self.destroy).pack(side="right", padx=6)

        wrap = ttk.Frame(self, padding=12)
        wrap.pack(fill="both", expand=True)
        self.tree = ttk.Treeview(wrap, columns=("what", "state"), show="tree headings",
                                 selectmode="none")
        self.tree.heading("#0", text="대상")
        self.tree.heading("what", text="바뀐 내용")
        self.tree.heading("state", text="상태")
        self.tree.column("#0", width=260, stretch=False)
        self.tree.column("what", width=380, stretch=False)
        self.tree.column("state", width=170, stretch=False, anchor="w")
        sb = ttk.Scrollbar(wrap, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=sb.set)
        self.tree.pack(side="left", fill="both", expand=True)
        sb.pack(side="left", fill="y")

        self.tree.tag_configure("up", foreground=UP)
        self.tree.tag_configure("down", foreground=DOWN)
        self.tree.tag_configure("conflict", foreground=CONFLICT)
        self.tree.tag_configure("off", foreground=MUTED)
        self.tree.bind("<Button-1>", self._toggle)

        self._fill()

        self._update_count()

        self.transient(master)
        self.grab_set()

    # ------------------------------------------------------------ 그리기

    def _fill(self):
        by_sheet = {}
        for c in self.plan.changes:
            by_sheet.setdefault(c.sheet, []).append(c)

        for sheet in sorted(by_sheet):
            items = by_sheet[sheet]
            parent = self.tree.insert("", "end", text="%s  (%d)" % (sheet, len(items)),
                                      open=True, values=("", ""))
            for c in items:
                if c.status == "conflict":
                    c.selected = False        # 충돌은 사람이 직접 켜야 한다
                node = self.tree.insert(parent, "end", text=self._mark(c) + c.label,
                                        values=(c.summary(), self._state(c)),
                                        tags=self._tags(c),
                                        open=(c.status == "conflict"))
                self._nodes[node] = c
                # 칸이 여럿이면 한 줄씩 펼쳐 보여 준다. 숫자는 증감까지.
                if len(c.fields) > 1:
                    for f in c.fields:
                        self.tree.insert(node, "end", text="", values=(f.text(), ""),
                                         tags=(f.direction,))

        if self.plan.art_changed or self.plan.art_removed:
            art = self.tree.insert("", "end", open=True, values=("", ""),
                                   text="그림  (%d)" % (len(self.plan.art_changed)
                                                        + len(self.plan.art_removed)))
            for rel in self.plan.art_changed:
                self.tree.insert(art, "end", text="  " + rel, values=("교체", "적용"))
            for rel in self.plan.art_removed:
                self.tree.insert(art, "end", text="  " + rel, values=("표에서 빠짐", "확인"))

    @staticmethod
    def _mark(c):
        return "☑ " if c.selected else "☐ "

    @staticmethod
    def _state(c):
        if c.status == "conflict":
            return "충돌 · " + (c.note or "")
        return {"add": "추가", "remove": "삭제"}.get(c.kind, "변경")

    @staticmethod
    def _tags(c):
        if c.status == "conflict":
            return ("conflict",)
        if not c.selected:
            return ("off",)
        if len(c.fields) == 1:
            return (c.fields[0].direction,)
        return ()

    # ------------------------------------------------------------ 조작

    def _toggle(self, event):
        node = self.tree.identify_row(event.y)
        c = self._nodes.get(node)
        if c is None:
            return
        c.selected = not c.selected
        self.tree.item(node, text=self._mark(c) + c.label, tags=self._tags(c))
        self._update_count()

    def _set_all(self, on):
        for node, c in self._nodes.items():
            c.selected = on
            self.tree.item(node, text=self._mark(c) + c.label, tags=self._tags(c))
        self._update_count()

    def _update_count(self):
        n = sum(1 for c in self._nodes.values() if c.selected)
        off = len(self._nodes) - n
        self.count_label.config(
            text="적용할 항목 %d개%s" % (n, ("  ·  제외 %d개" % off) if off else ""))

    def _accept(self):
        self.ok = True
        self.destroy()


def show(master, plan, title="바뀐 내용 확인"):
    """창을 띄우고 닫힐 때까지 기다린다. '적용' 을 눌렀으면 True."""
    w = ReviewWindow(master, plan, title)
    master.wait_window(w)
    return w.ok


def as_text(plan, limit=200):
    """같은 내용을 글로. 창 없이 돌릴 때(검사·로그)도 같은 문장을 쓰려고 둔다."""
    out = []
    n = plan.counts()
    out.append("추가 %d · 변경 %d · 삭제 %d · 충돌 %d"
               % (n["add"], n["edit"], n["remove"], n["conflict"]))
    for c in plan.changes[:limit]:
        flag = "!" if c.status == "conflict" else " "
        out.append("%s %-28s %s" % (flag, c.label, c.summary()))
        if len(c.fields) > 1:
            for f in c.fields:
                out.append("      " + f.text())
    if len(plan.changes) > limit:
        out.append("   ... 외 %d건" % (len(plan.changes) - limit))
    for rel in plan.art_changed:
        out.append("  그림 교체 " + rel)
    return "\n".join(out)
