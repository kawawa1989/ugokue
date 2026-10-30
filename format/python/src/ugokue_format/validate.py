"""model.json の JSON Schema 検証と、仕様の検証規則による意味の検証。"""

from __future__ import annotations

import itertools
import json
from collections.abc import Iterator
from dataclasses import dataclass
from functools import cache
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

from .package import (
    FORMAT_NAME,
    MODEL_FILE,
    PNG_SIGNATURE,
    FormatError,
    is_readable_version,
    read_model_json,
    texture_path_problem,
)

SCHEMA_PATH = Path(__file__).resolve().parents[3] / "schema" / "model.schema.json"

PathPart = str | int


@dataclass(frozen=True)
class Issue:
    """検証で見つかった問題と、その JSON パス。"""

    path: str
    message: str

    def __str__(self) -> str:
        return f"{self.path}: {self.message}"


def json_path(parts: tuple[PathPart, ...] | list[PathPart]) -> str:
    """キーと添字の並びを `$.a[0].b` 形式の JSON パスにする。"""
    return "$" + "".join(f"[{p}]" if isinstance(p, int) else f".{p}" for p in parts)


@cache
def load_schema() -> dict[str, Any]:
    """リポジトリの format/schema/model.schema.json を読む。"""
    return json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))


def _is_num(v: Any) -> bool:
    """JSON の数値（真偽値を除く）かどうかを返す。"""
    return isinstance(v, int | float) and not isinstance(v, bool)


def _list(obj: Any, key: str) -> list[Any]:
    """辞書の配列の項目を取り出し、配列でなければ空にする。"""
    v = obj.get(key) if isinstance(obj, dict) else None
    return v if isinstance(v, list) else []


def _dict(obj: Any, key: str) -> dict[str, Any]:
    """辞書のオブジェクトの項目を取り出し、オブジェクトでなければ空にする。"""
    v = obj.get(key) if isinstance(obj, dict) else None
    return v if isinstance(v, dict) else {}


def _objects(items: list[Any]) -> Iterator[tuple[int, dict[str, Any]]]:
    """配列のうちオブジェクトである要素を添字とともに返す。"""
    return ((i, x) for i, x in enumerate(items) if isinstance(x, dict))


def _hashable(v: Any) -> Any:
    """値を集合のキーに使える形にする。"""
    return json.dumps(v, sort_keys=True) if isinstance(v, list | dict) else v


class _Checker:
    """1つの model.json に対して仕様の検証規則を順に調べる。"""

    def __init__(self, doc: dict[str, Any], root: Path | None) -> None:
        self.doc = doc
        self.root = root
        self.issues: list[Issue] = []
        deformation = _dict(doc, "deformation")
        correspondence = _dict(doc, "correspondence")
        self.parameters = _list(doc, "parameters")
        self.parts = _list(doc, "parts")
        self.meshes = _list(doc, "meshes")
        self.views = _list(doc, "views")
        self.landmarks = _list(correspondence, "landmarks")
        self.vertex_matches = _list(correspondence, "vertexMatches")
        self.groups = _list(deformation, "keyformGroups")
        self.procedural = _list(deformation, "procedural")
        self.pendulums = _list(_dict(doc, "physics"), "pendulums")
        self.tracking = _list(_dict(doc, "tracking"), "parameters")
        self.param_by_id = self._index(self.parameters)
        self.part_by_id = self._index(self.parts)
        self.mesh_by_id = self._index(self.meshes)
        self.view_by_id = self._index(self.views)

    @staticmethod
    def _index(items: list[Any]) -> dict[Any, dict[str, Any]]:
        """ID から最初に現れた要素への対応を作る。"""
        index: dict[Any, dict[str, Any]] = {}
        for _, x in _objects(items):
            if isinstance(x.get("id"), str):
                index.setdefault(x["id"], x)
        return index

    def add(self, path: tuple[PathPart, ...], message: str) -> None:
        """問題を1件記録する。"""
        self.issues.append(Issue(json_path(path), message))

    def vertex_count(self, mesh_id: Any) -> int | None:
        """メッシュの頂点数を返し、分からなければ None を返す。"""
        mesh = self.mesh_by_id.get(mesh_id) if isinstance(mesh_id, str) else None
        vertices = mesh.get("vertices") if mesh else None
        return len(vertices) if isinstance(vertices, list) else None

    def run(self) -> list[Issue]:
        """すべての規則を調べ、見つかった問題を返す。"""
        self.check_header()
        self.check_duplicates()
        self.check_references()
        self.check_part_tree()
        self.check_part_meshes()
        self.check_parameter_ranges()
        self.check_mesh_shapes()
        self.check_textures()
        self.check_base_view()
        self.check_keyform_groups()
        self.check_vertex_matches()
        self.check_procedural()
        self.check_pendulums()
        return self.issues

    def check_header(self) -> None:
        """`format` と `version` を調べる。"""
        if self.doc.get("format") != FORMAT_NAME:
            self.add(("format",), f"format は {FORMAT_NAME!r} でなければならない")
        if not is_readable_version(self.doc.get("version")):
            self.add(("version",), f"version {self.doc.get('version')!r} は読めない（読めるのは 0.1.x）")

    def _unique(self, base: tuple[PathPart, ...], items: list[Any], key: Any, label: str) -> None:
        """配列の要素について、key が返す値の重複を調べる。"""
        seen: set[Any] = set()
        for i, x in _objects(items):
            k = key(x)
            if k is None:
                continue
            k = _hashable(k)
            if k in seen:
                self.add((*base, i), f"{label} {k} が重複している")
            seen.add(k)

    def check_duplicates(self) -> None:
        """種類ごとの ID と、一意でなければならない組の重複を調べる。"""
        def by_id(x: dict[str, Any]) -> Any:
            return x.get("id")

        self._unique(("parameters",), self.parameters, by_id, "パラメータ ID")
        self._unique(("parts",), self.parts, by_id, "パーツ ID")
        self._unique(("meshes",), self.meshes, by_id, "メッシュ ID")
        self._unique(("views",), self.views, by_id, "ビュー ID")
        self._unique(("deformation", "keyformGroups"), self.groups, by_id, "キーフォームグループ ID")
        self._unique(("physics", "pendulums"), self.pendulums, by_id, "振り子 ID")
        self._unique(
            ("correspondence", "landmarks"), self.landmarks, lambda x: [x.get("id"), x.get("view")], "(id, view)"
        )
        self._unique(
            ("correspondence", "vertexMatches"),
            self.vertex_matches,
            lambda x: [x.get("mesh"), x.get("vertex"), x.get("view")],
            "(mesh, vertex, view)",
        )
        self._unique(("tracking", "parameters"), self.tracking, lambda x: x.get("parameter"), "parameter")
        for v, view in _objects(self.views):
            self._unique(("views", v, "overrides"), _list(view, "overrides"), lambda x: x.get("part"), "part")

    def _ref(self, path: tuple[PathPart, ...], value: Any, index: dict[Any, Any], label: str) -> None:
        """文字列の参照先が存在するかを調べる。"""
        if isinstance(value, str) and value not in index:
            self.add(path, f"存在しない{label} {value!r} を参照している")

    def check_references(self) -> None:
        """ID による参照の参照先が存在するかを調べる。"""
        P, M, V, R = self.part_by_id, self.mesh_by_id, self.view_by_id, self.param_by_id
        for i, part in _objects(self.parts):
            self._ref(("parts", i, "parent"), part.get("parent"), P, "パーツ")
            self._ref(("parts", i, "mesh"), part.get("mesh"), M, "メッシュ")
            for j, c in enumerate(_list(part, "clipBy")):
                self._ref(("parts", i, "clipBy", j), c, P, "パーツ")
        for i, mesh in _objects(self.meshes):
            self._ref(("meshes", i, "part"), mesh.get("part"), P, "パーツ")
        for v, view in _objects(self.views):
            for o, ov in _objects(_list(view, "overrides")):
                self._ref(("views", v, "overrides", o, "part"), ov.get("part"), P, "パーツ")
                self._ref(("views", v, "overrides", o, "mesh"), ov.get("mesh"), M, "メッシュ")
        for i, lm in _objects(self.landmarks):
            self._ref(("correspondence", "landmarks", i, "part"), lm.get("part"), P, "パーツ")
            self._ref(("correspondence", "landmarks", i, "view"), lm.get("view"), V, "ビュー")
        for i, vm in _objects(self.vertex_matches):
            self._ref(("correspondence", "vertexMatches", i, "mesh"), vm.get("mesh"), M, "メッシュ")
            self._ref(("correspondence", "vertexMatches", i, "view"), vm.get("view"), V, "ビュー")
        for g, group in _objects(self.groups):
            self._ref(("deformation", "keyformGroups", g, "mesh"), group.get("mesh"), M, "メッシュ")
            for j, p in enumerate(_list(group, "parameters")):
                self._ref(("deformation", "keyformGroups", g, "parameters", j), p, R, "パラメータ")
        for i, motion in _objects(self.procedural):
            self._ref(("deformation", "procedural", i, "parameter"), motion.get("parameter"), R, "パラメータ")
            self._ref(("deformation", "procedural", i, "part"), motion.get("part"), P, "パーツ")
        for i, pend in _objects(self.pendulums):
            for j, inp in _objects(_list(pend, "inputs")):
                self._ref(("physics", "pendulums", i, "inputs", j, "parameter"), inp.get("parameter"), R, "パラメータ")
            self._ref(("physics", "pendulums", i, "output"), pend.get("output"), R, "パラメータ")
        for i, tp in _objects(self.tracking):
            self._ref(("tracking", "parameters", i, "parameter"), tp.get("parameter"), R, "パラメータ")

    def check_part_tree(self) -> None:
        """パーツの親子の循環と、clipBy での自己参照を調べる。"""
        for i, part in _objects(self.parts):
            start = part.get("id")
            seen: set[Any] = set()
            current = part.get("parent")
            while isinstance(current, str) and current in self.part_by_id and current not in seen:
                if current == start:
                    self.add(("parts", i, "parent"), f"パーツ {start!r} の親子関係が循環している")
                    break
                seen.add(current)
                current = self.part_by_id[current].get("parent")
            for j, c in enumerate(_list(part, "clipBy")):
                if c == start:
                    self.add(("parts", i, "clipBy", j), "clipBy に自分自身は指定できない")

    def check_part_meshes(self) -> None:
        """Part の mesh とビューの overrides[].mesh が、そのパーツのメッシュかを調べる。"""

        def check(path: tuple[PathPart, ...], part_id: Any, mesh_id: Any) -> None:
            mesh = self.mesh_by_id.get(mesh_id) if isinstance(mesh_id, str) else None
            if mesh is not None and mesh.get("part") != part_id:
                self.add(path, f"メッシュ {mesh_id!r} はパーツ {mesh.get('part')!r} のもので、{part_id!r} のものではない")

        for i, part in _objects(self.parts):
            check(("parts", i, "mesh"), part.get("id"), part.get("mesh"))
        for v, view in _objects(self.views):
            for o, ov in _objects(_list(view, "overrides")):
                check(("views", v, "overrides", o, "mesh"), ov.get("part"), ov.get("mesh"))

    def check_parameter_ranges(self) -> None:
        """パラメータの min < max と min ≤ default ≤ max を調べる。"""
        for i, p in _objects(self.parameters):
            lo, hi, default = p.get("min"), p.get("max"), p.get("default")
            if not (_is_num(lo) and _is_num(hi)):
                continue
            if lo >= hi:
                self.add(("parameters", i, "max"), f"min ({lo}) は max ({hi}) より小さくなければならない")
            elif _is_num(default) and not lo <= default <= hi:
                self.add(("parameters", i, "default"), f"default ({default}) が範囲 [{lo}, {hi}] の外にある")

    def check_mesh_shapes(self) -> None:
        """uvs と vertices の個数の一致と、三角形の頂点番号の範囲を調べる。"""
        for i, mesh in _objects(self.meshes):
            vertices, uvs = mesh.get("vertices"), mesh.get("uvs")
            if not isinstance(vertices, list):
                continue
            if isinstance(uvs, list) and len(uvs) != len(vertices):
                self.add(("meshes", i, "uvs"), f"uvs の個数 {len(uvs)} が vertices の個数 {len(vertices)} と違う")
            for t, tri in enumerate(_list(mesh, "triangles")):
                for k, idx in enumerate(tri if isinstance(tri, list) else []):
                    if isinstance(idx, int) and not isinstance(idx, bool) and not 0 <= idx < len(vertices):
                        self.add(("meshes", i, "triangles", t, k), f"頂点番号 {idx} が頂点数 {len(vertices)} の範囲外")

    def check_textures(self) -> None:
        """テクスチャのパスがフォルダの内側を指し、PNG のファイルがあるかを調べる。"""
        for i, mesh in _objects(self.meshes):
            texture = mesh.get("texture")
            if not isinstance(texture, str):
                continue
            path = ("meshes", i, "texture")
            if problem := texture_path_problem(texture):
                self.add(path, problem)
                continue
            if self.root is None:
                continue
            file = self.root / texture
            root = self.root.resolve()
            if not file.resolve().is_relative_to(root):
                self.add(path, f"テクスチャ {texture!r} がフォルダの外を指している")
            elif not file.is_file():
                self.add(path, f"テクスチャ {texture!r} がない")
            else:
                with file.open("rb") as f:
                    if f.read(len(PNG_SIGNATURE)) != PNG_SIGNATURE:
                        self.add(path, f"テクスチャ {texture!r} が PNG ではない")

    def check_base_view(self) -> None:
        """yaw = 0、pitch = 0 の基準のビューがちょうど1つで、overrides が空かを調べる。"""
        base = [
            (v, view)
            for v, view in _objects(self.views)
            if all(_is_num(view.get(k)) and view.get(k) == 0 for k in ("yaw", "pitch"))
        ]
        if len(base) != 1:
            self.add(("views",), f"基準のビュー（yaw = 0、pitch = 0）が {len(base)} 個ある（ちょうど1個必要）")
        for v, view in base:
            if _list(view, "overrides"):
                self.add(("views", v, "overrides"), "基準のビューの overrides は空でなければならない")

    def check_keyform_groups(self) -> None:
        """キーフォームグループの格子とキーフォームの整合を調べる。"""
        for g, group in _objects(self.groups):
            base: tuple[PathPart, ...] = ("deformation", "keyformGroups", g)
            params = _list(group, "parameters")
            keys = _list(group, "keys")
            grid_ok = len(keys) == len(params)
            if not grid_ok:
                self.add((*base, "keys"), f"keys の軸の数 {len(keys)} が parameters の数 {len(params)} と違う")
            for a, axis in enumerate(keys):
                if not isinstance(axis, list) or not all(_is_num(k) for k in axis):
                    grid_ok = False
                    continue
                if any(x >= y for x, y in itertools.pairwise(axis)):
                    grid_ok = False
                    self.add((*base, "keys", a), "keys の値が狭義の昇順でない")
                param = self.param_by_id.get(params[a]) if a < len(params) and isinstance(params[a], str) else None
                lo, hi = (param.get("min"), param.get("max")) if param else (None, None)
                if _is_num(lo) and _is_num(hi):
                    for k, value in enumerate(axis):
                        if not lo <= value <= hi:
                            self.add((*base, "keys", a, k), f"格子点 {value} がパラメータ {params[a]!r} の範囲 [{lo}, {hi}] の外にある")
            grid = set(itertools.product(*keys)) if grid_ok else None
            seen: set[tuple[Any, ...]] = set()
            vertex_count = self.vertex_count(group.get("mesh"))
            for k, keyform in _objects(_list(group, "keyforms")):
                at = keyform.get("at")
                if grid is not None and isinstance(at, list) and all(_is_num(x) for x in at):
                    point = tuple(at)
                    if len(at) != len(params):
                        self.add((*base, "keyforms", k, "at"), f"at の値の数 {len(at)} が parameters の数 {len(params)} と違う")
                    elif point not in grid:
                        self.add((*base, "keyforms", k, "at"), f"at {list(point)} が格子点ではない")
                    elif point in seen:
                        self.add((*base, "keyforms", k, "at"), f"格子点 {list(point)} のキーフォームが重複している")
                    seen.add(point)
                offsets = keyform.get("offsets")
                if vertex_count is not None and isinstance(offsets, list) and len(offsets) != vertex_count:
                    self.add(
                        (*base, "keyforms", k, "offsets"),
                        f"offsets の個数 {len(offsets)} がメッシュの頂点数 {vertex_count} と違う",
                    )
            if grid is not None:
                for point in sorted(grid - seen):
                    self.add((*base, "keyforms"), f"格子点 {list(point)} のキーフォームがない")

    def check_vertex_matches(self) -> None:
        """vertexMatches[].vertex がメッシュの頂点数未満かを調べる。"""
        for i, vm in _objects(self.vertex_matches):
            count, vertex = self.vertex_count(vm.get("mesh")), vm.get("vertex")
            if count is not None and isinstance(vertex, int) and vertex >= count:
                self.add(("correspondence", "vertexMatches", i, "vertex"), f"頂点番号 {vertex} がメッシュの頂点数 {count} 以上")

    def check_procedural(self) -> None:
        """rotate の degreesPerUnit と translate の offsetPerUnit があるかを調べる。"""
        required = {"rotate": "degreesPerUnit", "translate": "offsetPerUnit"}
        for i, motion in _objects(self.procedural):
            key = required.get(str(motion.get("type")))
            if key is not None and motion.get(key) is None:
                self.add(("deformation", "procedural", i), f"{motion['type']} には {key} が必要")

    def check_pendulums(self) -> None:
        """振り子の output が自分の inputs に含まれないかを調べる。"""
        for i, pend in _objects(self.pendulums):
            inputs = {_hashable(inp.get("parameter")) for _, inp in _objects(_list(pend, "inputs"))}
            if _hashable(pend.get("output")) in inputs:
                self.add(("physics", "pendulums", i, "output"), f"output {pend.get('output')!r} は inputs に含められない")


def schema_issues(doc: Any) -> list[Issue]:
    """JSON Schema に合わない箇所を返す。"""
    validator = Draft202012Validator(load_schema())
    return [Issue(json_path(list(e.absolute_path)), e.message) for e in validator.iter_errors(doc)]


def validate_document(doc: Any, root: Path | None = None) -> list[Issue]:
    """model.json の内容を検証する。root を渡すとテクスチャのファイルも調べる。"""
    issues = schema_issues(doc)
    if isinstance(doc, dict):
        issues += _Checker(doc, root).run()
    return issues


def validate_dir(directory: str | Path) -> list[Issue]:
    """モデルのフォルダを検証する。"""
    directory = Path(directory)
    if not (directory / MODEL_FILE).is_file():
        return [Issue("$", f"{directory / MODEL_FILE} がない")]
    try:
        doc = read_model_json(directory)
    except (FormatError, ValueError) as e:
        return [Issue("$", f"{MODEL_FILE} を読めない: {e}")]
    return validate_document(doc, directory)
