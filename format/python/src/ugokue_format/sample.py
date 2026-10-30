"""検証と往復テストに使う最小のサンプルモデルを作る。"""

from __future__ import annotations

from pathlib import Path

from .model import (
    Canvas,
    Correspondence,
    Deformation,
    Keyform,
    KeyformGroup,
    Landmark,
    Mesh,
    Model,
    Parameter,
    Part,
    Pendulum,
    PendulumInput,
    Physics,
    Point,
    ProceduralMotion,
    Tracking,
    TrackingParameter,
    VertexMatch,
    View,
    ViewOverride,
)
from .package import FORMAT_NAME, FORMAT_VERSION, ModelPackage, save
from .raster import encode_png

QUAD_UVS: list[Point] = [[0, 0], [1, 0], [1, 1], [0, 1]]
QUAD_TRIANGLES = [[0, 1, 2], [0, 2, 3]]


def solid_png(width: int, height: int, rgba: tuple[int, int, int, int]) -> bytes:
    """単色の RGBA の PNG を作る。"""
    return encode_png(width, height, bytes(rgba) * (width * height))


def _quad(left: float, top: float, right: float, bottom: float) -> list[Point]:
    """左上から時計回りの長方形の4頂点を返す。"""
    return [[left, top], [right, top], [right, bottom], [left, bottom]]


def _mesh(mesh_id: str, part: str, vertices: list[Point]) -> Mesh:
    """長方形のメッシュを作る。"""
    return Mesh(
        mesh_id, part, f"textures/{mesh_id}.png", vertices, [list(p) for p in QUAD_UVS], [list(t) for t in QUAD_TRIANGLES]
    )


def _offsets(top: Point, bottom: Point) -> list[Point]:
    """長方形の上辺2頂点と下辺2頂点の移動量を並べる。"""
    return [list(top), list(top), list(bottom), list(bottom)]


def _face_keyforms() -> list[Keyform]:
    """顔の AngleX × AngleY の 3×3 のキーフォームを行優先で作る。"""
    keyforms = []
    for x in (-30, 0, 30):
        for y in (-30, 0, 30):
            dx, dy = x * 0.25 + 0.0, -y * 0.125 + 0.0
            keyforms.append(Keyform([x, y], _offsets([dx, dy], [dx * 0.5 + 0.0, dy])))
    return keyforms


TEXTURE_COLORS: dict[str, tuple[int, int, int, int]] = {
    "body": (70, 90, 160, 255),
    "face": (250, 220, 200, 255),
    "eye_white_l": (255, 255, 255, 255),
    "iris_l": (60, 110, 200, 255),
    "mouth": (200, 70, 80, 255),
    "mouth_side": (190, 60, 70, 255),
    "hair_front": (90, 50, 30, 230),
}


def build_sample() -> ModelPackage:
    """format/samples/minimal の内容を作る。"""
    parameters = [
        Parameter("AngleX", -30, 30, 0),
        Parameter("AngleY", -30, 30, 0),
        Parameter("AngleZ", -30, 30, 0),
        Parameter("EyeOpenL", 0, 1, 1),
        Parameter("MouthOpenY", 0, 1, 0),
        Parameter("Breath", 0, 1, 0),
        Parameter("HairFront", -1, 1, 0),
    ]
    parts = [
        Part("body", "body", None, [128, 200], 0, "body"),
        Part("head", "head", "body", [128, 150], 10, None),
        Part("face", "face", "head", [128, 100], 20, "face"),
        Part("eye_white_l", "eye_white", "face", [151, 90], 30, "eye_white_l"),
        Part("iris_l", "iris", "face", [151, 90], 31, "iris_l", ["eye_white_l"]),
        Part("mouth", "mouth", "face", [128, 128.25], 30, "mouth"),
        Part("hair_front", "hair_front", "head", [128, 30], 40, "hair_front"),
    ]
    meshes = [
        _mesh("body", "body", _quad(80, 150, 176, 256)),
        _mesh("face", "face", _quad(78, 40, 178, 150)),
        _mesh("eye_white_l", "eye_white_l", _quad(140, 84, 162, 96)),
        _mesh("iris_l", "iris_l", _quad(146, 85, 156, 95)),
        _mesh("mouth", "mouth", _quad(118, 124, 138, 132.5)),
        _mesh("mouth_side", "mouth", _quad(122, 124, 138, 132.5)),
        _mesh("hair_front", "hair_front", _quad(70, 30, 186, 72.5)),
    ]
    views = [
        View("front", 0, 0, []),
        View(
            "right",
            30,
            0,
            [ViewOverride("mouth", mesh="mouth_side"), ViewOverride("hair_front", draw_order=25)],
        ),
        View("left", -30, 0, [ViewOverride("iris_l", visible=False)]),
    ]
    correspondence = Correspondence(
        [
            Landmark("eye_l_center", "eye_white_l", "front", [151, 90], 0.95, "auto"),
            Landmark("eye_l_center", "eye_white_l", "right", [158.5, 90], 0.8, "auto"),
            Landmark("mouth_center", "mouth", "front", [128, 128.25], 1, "manual"),
            Landmark("mouth_center", "mouth", "right", [136, 128.25], 0.75, "manual"),
        ],
        [
            VertexMatch("face", 0, "right", [86.5, 40], 0.9, "auto"),
            VertexMatch("face", 1, "right", [180, 40.5], 0.85, "auto"),
            VertexMatch("mouth", 2, "left", [130, 132.5], 1, "manual"),
        ],
    )
    zero = _offsets([0, 0], [0, 0])
    deformation = Deformation(
        [
            KeyformGroup(
                "face_angle", "face", ["AngleX", "AngleY"], [[-30, 0, 30], [-30, 0, 30]], "linear", _face_keyforms()
            ),
            KeyformGroup(
                "eye_open_l",
                "eye_white_l",
                ["EyeOpenL"],
                [[0, 1]],
                "linear",
                [Keyform([0], _offsets([0, 6], [0, -6])), Keyform([1], zero)],
            ),
            KeyformGroup(
                "mouth_open",
                "mouth",
                ["MouthOpenY"],
                [[0, 1]],
                "linear",
                [Keyform([0], zero), Keyform([1], _offsets([0, 0], [0, 8.5]))],
            ),
            KeyformGroup(
                "hair_sway",
                "hair_front",
                ["HairFront"],
                [[-1, 0, 1]],
                "linear",
                [
                    Keyform([-1], _offsets([0, 0], [-6, 0])),
                    Keyform([0], zero),
                    Keyform([1], _offsets([0, 0], [6, 0])),
                ],
            ),
        ],
        [
            ProceduralMotion("rotate", "AngleZ", "head", degrees_per_unit=1),
            ProceduralMotion("translate", "Breath", "body", offset_per_unit=[0, -3]),
        ],
    )
    physics = Physics(
        [
            Pendulum(
                "hair_front_sway",
                [PendulumInput("AngleX", 1), PendulumInput("AngleZ", 0.5)],
                "HairFront",
                0.3,
                0.15,
                0.05,
            )
        ]
    )
    tracking = Tracking([TrackingParameter("AngleX", 0.5), TrackingParameter("AngleY", 0.25)])
    model = Model(
        FORMAT_NAME,
        FORMAT_VERSION,
        Canvas(256, 256),
        parameters,
        parts,
        meshes,
        views,
        correspondence,
        deformation,
        physics,
        tracking,
    )
    textures = {m.texture: solid_png(4, 4, TEXTURE_COLORS[m.id]) for m in meshes}
    return ModelPackage(model, textures)


def make_sample(directory: str | Path) -> None:
    """サンプルモデルをフォルダに書き出す。"""
    save(build_sample(), directory)
