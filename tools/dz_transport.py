# -*- coding: utf-8 -*-
"""패치를 **옮기는** 층. 무엇이 바뀌었는지는 여기서 모른다.

지금은 파일로 주고받는다. 나중에 네트워크로 바꿀 때 고쳐야 하는 곳이 여기 하나뿐이
되도록, 아래 네 가지만 할 줄 알면 되는 얇은 규약으로 끊어 두었다.

    push(patch, blobs)   패치 하나를 올린다
    list(since=None)     그 뒤로 올라온 패치 목록(머리말만)
    fetch(patch_id)      패치 본문 + 딸린 그림
    ack(patch_id)        받아서 합쳤다고 표시

FileTransport 는 이 네 가지를 **폴더 하나**로 구현한다. 폴더가 USB 든 공유 드라이브든
메신저로 주고받는 단일 파일이든 상관없다. HttpTransport 는 같은 네 가지를 서버로
구현하면 되고, 패치를 만들고 합치는 코드(dz_patch.py)는 한 줄도 바뀌지 않는다.

패치 하나는 zip 이다:
    patch.json          머리말 + 바뀐 행
    art/Sprites/*.png   딸린 그림
    art/Illust/*.png
"""
import io
import json
import os
import zipfile

SUFFIX = ".dzpatch"


class Transport:
    """옮기는 방법의 규약. 새 방식은 이 네 가지만 채우면 된다."""

    def push(self, patch, blobs):
        """patch: dict, blobs: {'Sprites/x.png': bytes}. 올린 패치 id 를 돌려준다."""
        raise NotImplementedError

    def list(self, since=None):
        """머리말 목록 — [{'id','parent','author','made'}]. 오래된 것부터."""
        raise NotImplementedError

    def fetch(self, patch_id):
        """(patch dict, blobs dict) 를 돌려준다."""
        raise NotImplementedError

    def ack(self, patch_id):
        """합쳤다고 표시한다. 파일 방식에서는 할 일이 없다."""
        return None


# ---------------------------------------------------------------- zip 입출력

def write_bundle(path, patch, blobs):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("patch.json", json.dumps(patch, ensure_ascii=False))
        for rel, data in (blobs or {}).items():
            z.writestr("art/" + rel, data)


def read_bundle(path):
    with zipfile.ZipFile(path) as z:
        patch = json.loads(z.read("patch.json").decode("utf-8"))
        blobs = {n[len("art/"):]: z.read(n)
                 for n in z.namelist() if n.startswith("art/")}
    return patch, blobs


# ---------------------------------------------------------------- 파일

class FileTransport(Transport):
    """폴더 하나에 <id>.dzpatch 로 쌓는다.

    그 폴더가 공유 드라이브면 사실상 자동 동기화처럼 쓸 수 있고, 메신저로 파일만
    주고받아도 똑같이 동작한다. 받은 것을 어디까지 합쳤는지는 받는 쪽 폴더의
    applied.json 에 적는다 — 보내는 쪽 폴더를 건드리지 않기 위해서다.
    """

    def __init__(self, folder, state_path=None):
        self.folder = folder
        self.state_path = state_path or os.path.join(folder, "applied.json")
        os.makedirs(folder, exist_ok=True)

    # -- 규약

    def push(self, patch, blobs):
        path = os.path.join(self.folder, patch["id"] + SUFFIX)
        write_bundle(path, patch, blobs)
        return patch["id"]

    def list(self, since=None):
        heads = []
        for name in sorted(os.listdir(self.folder)):
            if not name.endswith(SUFFIX):
                continue
            try:
                with zipfile.ZipFile(os.path.join(self.folder, name)) as z:
                    p = json.loads(z.read("patch.json").decode("utf-8"))
            except Exception:
                continue                      # 깨진 파일은 조용히 건너뛰지 않고
                                              # 목록에서 빼되, 아래에서 센다
            heads.append({k: p.get(k) for k in ("id", "parent", "author", "made")})
        heads.sort(key=lambda h: h.get("made") or "")
        if since:
            ids = [h["id"] for h in heads]
            if since in ids:
                heads = heads[ids.index(since) + 1:]
        return heads

    def fetch(self, patch_id):
        return read_bundle(os.path.join(self.folder, patch_id + SUFFIX))

    def ack(self, patch_id):
        done = self.applied()
        if patch_id not in done:
            done.append(patch_id)
            with io.open(self.state_path, "w", encoding="utf-8") as f:
                json.dump(done, f)

    # -- 편의

    def applied(self):
        if not os.path.exists(self.state_path):
            return []
        try:
            with io.open(self.state_path, encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            return []

    def pending(self):
        done = set(self.applied())
        return [h for h in self.list() if h["id"] not in done]

    def broken(self):
        """열리지 않는 패치 파일. 조용히 사라지면 안 되므로 따로 센다."""
        bad = []
        for name in sorted(os.listdir(self.folder)):
            if not name.endswith(SUFFIX):
                continue
            try:
                read_bundle(os.path.join(self.folder, name))
            except Exception as exc:
                bad.append((name, str(exc)))
        return bad


class SingleFileTransport(FileTransport):
    """패치 한 개짜리 — 메신저로 받은 파일 하나를 그대로 읽을 때."""

    def __init__(self, path):
        self.path = path
        self.folder = os.path.dirname(os.path.abspath(path)) or "."
        self.state_path = os.path.join(self.folder, "applied.json")

    def list(self, since=None):
        patch, _ = read_bundle(self.path)
        return [{k: patch.get(k) for k in ("id", "parent", "author", "made")}]

    def fetch(self, patch_id):
        return read_bundle(self.path)


# ---------------------------------------------------------------- 네트워크 (예정)

class HttpTransport(Transport):
    """서버로 주고받는 방식. 아직 만들지 않았다.

    **중요: 여기만 채우면 된다.** 패치를 만들고(dz_patch.make_patch) 합치는
    (dz_patch.plan_apply / apply_plan) 코드와 화면(dz_review.py)은 그대로 쓴다.
    필요한 것은 아래 네 가지에 대응하는 끝점 네 개뿐이다:

        POST   {base}/patches            patch.json + 그림 -> {"id": ...}
        GET    {base}/patches?since=ID   머리말 목록
        GET    {base}/patches/{id}       zip 본문
        POST   {base}/patches/{id}/ack

    인증은 전송 쪽 관심사다. 토큰을 헤더에 넣든 무엇을 하든 dz_patch 는 모른다.
    서버를 붙일 때는 '한 번에 한 명만 고친다' 규칙을 서버가 강제하도록(편집 잠금)
    하는 편이 낫다 — 엑셀은 병합이 안 되고, 그 사실은 전송을 바꿔도 달라지지 않는다.
    """

    def __init__(self, base_url, token=None):
        self.base_url = base_url.rstrip("/")
        self.token = token

    def _unavailable(self):
        raise NotImplementedError(
            "네트워크 전송은 아직 없습니다. 지금은 파일로 주고받으세요.\n"
            "서버를 붙일 때 HttpTransport 의 push/list/fetch/ack 네 개만 채우면 됩니다 "
            "— 패치 계산·병합·화면은 그대로 씁니다.")

    def push(self, patch, blobs):
        self._unavailable()

    def list(self, since=None):
        self._unavailable()

    def fetch(self, patch_id):
        self._unavailable()
