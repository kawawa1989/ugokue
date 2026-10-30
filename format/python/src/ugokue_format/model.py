"""中間形式 v0 の model.json を表すデータクラス群。"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

Point = list[float]


def _point(value: Any) -> Point:
    """`[x, y]` 形式の値を新しいリストとして取り出す。"""
    return [value[0], value[1]]


def _points(values: Any) -> list[Point]:
    """`[x, y]` の配列を新しいリストのリストとして取り出す。"""
    return [_point(v) for v in values]


@dataclass
class Canvas:
    """キャンバスの大きさ（ピクセル）。"""

    width: int
    height: int

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Canvas:
        """辞書から作る。"""
        return cls(d["width"], d["height"])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"width": self.width, "height": self.height}


@dataclass
class Parameter:
    """モデルを動かす入力値の定義。"""

    id: str
    min: float
    max: float
    default: float

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Parameter:
        """辞書から作る。"""
        return cls(d["id"], d["min"], d["max"], d["default"])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"id": self.id, "min": self.min, "max": self.max, "default": self.default}


@dataclass
class Part:
    """角度をまたいで同じ ID で追跡する部品。"""

    id: str
    role: str
    parent: str | None
    anchor: Point
    draw_order: int
    mesh: str | None
    clip_by: list[str] = field(default_factory=list)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Part:
        """辞書から作る。"""
        return cls(d["id"], d["role"], d["parent"], _point(d["anchor"]), d["drawOrder"], d["mesh"], list(d["clipBy"]))

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "id": self.id,
            "role": self.role,
            "parent": self.parent,
            "anchor": list(self.anchor),
            "drawOrder": self.draw_order,
            "mesh": self.mesh,
            "clipBy": list(self.clip_by),
        }


@dataclass
class Mesh:
    """テクスチャを貼る三角形メッシュ。"""

    id: str
    part: str
    texture: str
    vertices: list[Point]
    uvs: list[Point]
    triangles: list[list[int]]

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Mesh:
        """辞書から作る。"""
        return cls(
            d["id"], d["part"], d["texture"], _points(d["vertices"]), _points(d["uvs"]), [list(t) for t in d["triangles"]]
        )

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "id": self.id,
            "part": self.part,
            "texture": self.texture,
            "vertices": [list(p) for p in self.vertices],
            "uvs": [list(p) for p in self.uvs],
            "triangles": [list(t) for t in self.triangles],
        }


@dataclass
class ViewOverride:
    """ビューの中で基準から変えるパーツの設定。"""

    part: str
    mesh: str | None = None
    visible: bool | None = None
    draw_order: int | None = None

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> ViewOverride:
        """辞書から作る。"""
        return cls(d["part"], d.get("mesh"), d.get("visible"), d.get("drawOrder"))

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にし、None の任意項目は省く。"""
        d: dict[str, Any] = {"part": self.part}
        if self.mesh is not None:
            d["mesh"] = self.mesh
        if self.visible is not None:
            d["visible"] = self.visible
        if self.draw_order is not None:
            d["drawOrder"] = self.draw_order
        return d


@dataclass
class View:
    """AI が生成した角度ごとの絵と、その角度での切り替え情報。"""

    id: str
    yaw: float
    pitch: float
    overrides: list[ViewOverride] = field(default_factory=list)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> View:
        """辞書から作る。"""
        return cls(d["id"], d["yaw"], d["pitch"], [ViewOverride.from_dict(o) for o in d["overrides"]])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"id": self.id, "yaw": self.yaw, "pitch": self.pitch, "overrides": [o.to_dict() for o in self.overrides]}


@dataclass
class Landmark:
    """ある特徴点が、あるビューの絵のどこにあるか。"""

    id: str
    part: str
    view: str
    position: Point
    confidence: float
    source: str

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Landmark:
        """辞書から作る。"""
        return cls(d["id"], d["part"], d["view"], _point(d["position"]), d["confidence"], d["source"])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "id": self.id,
            "part": self.part,
            "view": self.view,
            "position": list(self.position),
            "confidence": self.confidence,
            "source": self.source,
        }


@dataclass
class VertexMatch:
    """あるメッシュの頂点が、あるビューの絵のどこに対応するか。"""

    mesh: str
    vertex: int
    view: str
    position: Point
    confidence: float
    source: str

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> VertexMatch:
        """辞書から作る。"""
        return cls(d["mesh"], d["vertex"], d["view"], _point(d["position"]), d["confidence"], d["source"])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "mesh": self.mesh,
            "vertex": self.vertex,
            "view": self.view,
            "position": list(self.position),
            "confidence": self.confidence,
            "source": self.source,
        }


@dataclass
class Correspondence:
    """角度をまたいだ特徴点と頂点の対応づけ。"""

    landmarks: list[Landmark] = field(default_factory=list)
    vertex_matches: list[VertexMatch] = field(default_factory=list)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Correspondence:
        """辞書から作る。"""
        return cls(
            [Landmark.from_dict(x) for x in d["landmarks"]], [VertexMatch.from_dict(x) for x in d["vertexMatches"]]
        )

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "landmarks": [x.to_dict() for x in self.landmarks],
            "vertexMatches": [x.to_dict() for x in self.vertex_matches],
        }


@dataclass
class Keyform:
    """格子点1つでの各頂点の移動量。"""

    at: list[float]
    offsets: list[Point]

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Keyform:
        """辞書から作る。"""
        return cls(list(d["at"]), _points(d["offsets"]))

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"at": list(self.at), "offsets": [list(p) for p in self.offsets]}


@dataclass
class KeyformGroup:
    """1つのメッシュについて、パラメータの格子点ごとのキーフォームを持つ。"""

    id: str
    mesh: str
    parameters: list[str]
    keys: list[list[float]]
    interpolation: str
    keyforms: list[Keyform]

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> KeyformGroup:
        """辞書から作る。"""
        return cls(
            d["id"],
            d["mesh"],
            list(d["parameters"]),
            [list(k) for k in d["keys"]],
            d["interpolation"],
            [Keyform.from_dict(k) for k in d["keyforms"]],
        )

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "id": self.id,
            "mesh": self.mesh,
            "parameters": list(self.parameters),
            "keys": [list(k) for k in self.keys],
            "interpolation": self.interpolation,
            "keyforms": [k.to_dict() for k in self.keyforms],
        }


@dataclass
class ProceduralMotion:
    """パラメータに比例してパーツを回転・移動する動き。"""

    type: str
    parameter: str
    part: str
    degrees_per_unit: float | None = None
    offset_per_unit: Point | None = None

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> ProceduralMotion:
        """辞書から作る。"""
        offset = d.get("offsetPerUnit")
        return cls(
            d["type"], d["parameter"], d["part"], d.get("degreesPerUnit"), None if offset is None else _point(offset)
        )

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にし、None の任意項目は省く。"""
        d: dict[str, Any] = {"type": self.type, "parameter": self.parameter, "part": self.part}
        if self.degrees_per_unit is not None:
            d["degreesPerUnit"] = self.degrees_per_unit
        if self.offset_per_unit is not None:
            d["offsetPerUnit"] = list(self.offset_per_unit)
        return d


@dataclass
class Deformation:
    """キーフォームによる変形と手続き的な動き。"""

    keyform_groups: list[KeyformGroup] = field(default_factory=list)
    procedural: list[ProceduralMotion] = field(default_factory=list)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Deformation:
        """辞書から作る。"""
        return cls(
            [KeyformGroup.from_dict(x) for x in d["keyformGroups"]],
            [ProceduralMotion.from_dict(x) for x in d["procedural"]],
        )

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "keyformGroups": [x.to_dict() for x in self.keyform_groups],
            "procedural": [x.to_dict() for x in self.procedural],
        }


@dataclass
class PendulumInput:
    """振り子を揺らす入力パラメータとその重み。"""

    parameter: str
    weight: float

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> PendulumInput:
        """辞書から作る。"""
        return cls(d["parameter"], d["weight"])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"parameter": self.parameter, "weight": self.weight}


@dataclass
class Pendulum:
    """入力パラメータの変化で揺れ、振れを出力パラメータに書き込む振り子。"""

    id: str
    inputs: list[PendulumInput]
    output: str
    stiffness: float
    damping: float
    scale: float

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Pendulum:
        """辞書から作る。"""
        return cls(
            d["id"],
            [PendulumInput.from_dict(x) for x in d["inputs"]],
            d["output"],
            d["stiffness"],
            d["damping"],
            d["scale"],
        )

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {
            "id": self.id,
            "inputs": [x.to_dict() for x in self.inputs],
            "output": self.output,
            "stiffness": self.stiffness,
            "damping": self.damping,
            "scale": self.scale,
        }


@dataclass
class Physics:
    """振り子の集合。"""

    pendulums: list[Pendulum] = field(default_factory=list)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Physics:
        """辞書から作る。"""
        return cls([Pendulum.from_dict(x) for x in d["pendulums"]])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"pendulums": [x.to_dict() for x in self.pendulums]}


@dataclass
class TrackingParameter:
    """1つのパラメータのトラッキング値の平滑化。"""

    parameter: str
    smoothing: float

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> TrackingParameter:
        """辞書から作る。"""
        return cls(d["parameter"], d["smoothing"])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"parameter": self.parameter, "smoothing": self.smoothing}


@dataclass
class Tracking:
    """トラッキング値を受け取ったときのモデルごとの調整。"""

    parameters: list[TrackingParameter] = field(default_factory=list)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Tracking:
        """辞書から作る。"""
        return cls([TrackingParameter.from_dict(x) for x in d["parameters"]])

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にする。"""
        return {"parameters": [x.to_dict() for x in self.parameters]}


@dataclass
class Model:
    """model.json の最上位。"""

    format: str
    version: str
    canvas: Canvas
    parameters: list[Parameter]
    parts: list[Part]
    meshes: list[Mesh]
    views: list[View]
    correspondence: Correspondence
    deformation: Deformation
    physics: Physics | None = None
    tracking: Tracking | None = None

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Model:
        """辞書から作る。"""
        return cls(
            d["format"],
            d["version"],
            Canvas.from_dict(d["canvas"]),
            [Parameter.from_dict(x) for x in d["parameters"]],
            [Part.from_dict(x) for x in d["parts"]],
            [Mesh.from_dict(x) for x in d["meshes"]],
            [View.from_dict(x) for x in d["views"]],
            Correspondence.from_dict(d["correspondence"]),
            Deformation.from_dict(d["deformation"]),
            Physics.from_dict(d["physics"]) if d.get("physics") is not None else None,
            Tracking.from_dict(d["tracking"]) if d.get("tracking") is not None else None,
        )

    def to_dict(self) -> dict[str, Any]:
        """仕様の項目順の辞書にし、None の任意項目は省く。"""
        d: dict[str, Any] = {
            "format": self.format,
            "version": self.version,
            "canvas": self.canvas.to_dict(),
            "parameters": [x.to_dict() for x in self.parameters],
            "parts": [x.to_dict() for x in self.parts],
            "meshes": [x.to_dict() for x in self.meshes],
            "views": [x.to_dict() for x in self.views],
            "correspondence": self.correspondence.to_dict(),
            "deformation": self.deformation.to_dict(),
        }
        if self.physics is not None:
            d["physics"] = self.physics.to_dict()
        if self.tracking is not None:
            d["tracking"] = self.tracking.to_dict()
        return d
