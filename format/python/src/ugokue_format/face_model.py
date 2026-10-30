"""Unity 側のテストに使う、手作りの顔の固定テストモデル（format/samples/test-face）を作る。"""

from __future__ import annotations

import math
from collections.abc import Callable
from dataclasses import dataclass, field
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
    Point,
    ProceduralMotion,
    VertexMatch,
    View,
    ViewOverride,
)
from .package import FORMAT_NAME, FORMAT_VERSION, ModelPackage, save
from .raster import Color, Layer, Vec, band, ellipse, polygon, quadratic

CANVAS = 512
HEAD_CENTER: Vec = (256, 236)
HEAD_RADIUS = 120
DEPTH_SCALE = 0.5
ANGLE_KEYS = [-30, 0, 30]
EYE_CLOSED_Y = 250
MOUTH_CLOSED_Y = 304
MOUTH_CLOSED_SCALE = 0.1

SKIN: Color = (252, 226, 208, 255)
SKIN_SHADE: Color = (236, 200, 182, 255)
HAIR: Color = (96, 60, 48, 255)
HAIR_DARK: Color = (74, 46, 38, 255)
LINE: Color = (52, 36, 40, 255)

VIEWS: list[tuple[str, int, int]] = [
    ("front", 0, 0),
    ("left", -30, 0),
    ("right", 30, 0),
    ("down", 0, -30),
    ("up", 0, 30),
    ("down-left", -30, -30),
    ("up-left", -30, 30),
    ("down-right", 30, -30),
    ("up-right", 30, 30),
]


def mirror(points: list[Vec]) -> list[Vec]:
    """キャンバスの中心線で左右反転した点列を返す。"""
    return [(CANVAS - x, y) for x, y in reversed(points)]


@dataclass
class PartSpec:
    """1パーツの定義（描き方、メッシュの格子、奥行き）。"""

    id: str
    role: str
    parent: str | None
    anchor: Vec
    draw_order: int
    rect: tuple[int, int, int, int] | None
    draw: Callable[[Layer], None] | None = None
    grid: tuple[int, int] = (3, 3)
    depth: Callable[[float, float], float] | None = None
    clip_by: list[str] = field(default_factory=list)


def sphere_depth(bias: float) -> Callable[[float, float], float]:
    """頭を楕円体とみなした手前方向の奥行きに、パーツごとの差を足す関数を作る。"""

    def depth(x: float, y: float) -> float:
        dx, dy = x - HEAD_CENTER[0], y - HEAD_CENTER[1]
        return DEPTH_SCALE * math.sqrt(max(0.0, HEAD_RADIUS**2 - dx * dx - dy * dy)) + bias

    return depth


def flat_depth(z: float) -> Callable[[float, float], float]:
    """位置によらず一定の奥行きを返す関数を作る。"""
    return lambda x, y: z


def rotate(x: float, y: float, z: float, yaw: int, pitch: int) -> Vec:
    """頭の中心まわりに yaw（右向きが正）、pitch（上向きが正）だけ回した画面上の位置を返す。"""
    sy, cy = _sin_cos(yaw)
    sp, cp = _sin_cos(pitch)
    rx, ry = x - HEAD_CENTER[0], y - HEAD_CENTER[1]
    x1 = rx * cy + z * sy
    z1 = -rx * sy + z * cy
    y1 = ry * cp - z1 * sp
    return (HEAD_CENTER[0] + x1, HEAD_CENTER[1] + y1)


def _sin_cos(degrees: int) -> tuple[float, float]:
    """格子点の角度（-30、0、30）の sin と cos を、環境によらず同じ値で返す。"""
    table = {0: (0.0, 1.0), 30: (0.5, math.sqrt(3) / 2), -30: (-0.5, math.sqrt(3) / 2)}
    return table[degrees]


def _r(value: float) -> float:
    """座標を小数第2位で丸め、-0.0 を 0 にする。"""
    rounded = round(value, 2)
    return 0 if rounded == 0 else rounded


def _draw_hair_back(layer: Layer) -> None:
    layer.fill(ellipse(256, 216, 118, 108), HAIR_DARK)
    layer.fill(polygon([(138, 216), (374, 216), (366, 398), (146, 398)]), HAIR_DARK)


def _draw_body(layer: Layer) -> None:
    layer.fill(polygon([(204, 372), (308, 372), (390, 404), (414, 512), (98, 512), (122, 404)]), (64, 74, 128, 255))
    layer.fill(polygon([(226, 372), (286, 372), (256, 414)]), SKIN_SHADE)
    layer.fill(polygon([(222, 372), (226, 372), (256, 414), (290, 372), (294, 372), (256, 424)]), (240, 240, 248, 255))


def _draw_neck(layer: Layer) -> None:
    layer.fill(polygon([(232, 300), (280, 300), (280, 392), (232, 392)]), SKIN_SHADE)


def _draw_face(layer: Layer) -> None:
    layer.fill(ellipse(256, 226, 91, 100), SKIN)
    layer.fill(polygon(quadratic((165, 228), (170, 318), (256, 342)) + quadratic((256, 342), (342, 318), (347, 228))[1:]), SKIN)
    layer.fill(ellipse(206, 290, 16, 7), (246, 176, 176, 110))
    layer.fill(ellipse(306, 290, 16, 7), (246, 176, 176, 110))


def _draw_ear(cx: float) -> Callable[[Layer], None]:
    def draw(layer: Layer) -> None:
        layer.fill(ellipse(cx, 241, 13, 24), SKIN_SHADE)

    return draw


def _draw_eye_white(cx: float) -> Callable[[Layer], None]:
    def draw(layer: Layer) -> None:
        layer.fill(ellipse(cx, 246, 24, 15), (255, 255, 255, 255))

    return draw


def _draw_iris(cx: float, highlight_dx: float) -> Callable[[Layer], None]:
    def draw(layer: Layer) -> None:
        layer.fill(ellipse(cx, 248, 12, 15), (72, 96, 176, 255))
        layer.fill(ellipse(cx, 251, 5, 7), (30, 30, 64, 255))
        layer.fill(ellipse(cx + highlight_dx, 242, 3.5, 3.5), (255, 255, 255, 255))

    return draw


def _eyelid_r() -> list[Vec]:
    return band(quadratic((186, 244), (210, 222), (240, 240)), 1.5, 2.5) + [(178, 238)]


def _eyebrow_r() -> list[Vec]:
    return band(quadratic((190, 212), (212, 200), (234, 208)), 1.6, 1.6)


def _hair_side_r() -> list[Vec]:
    return [(178, 150), (198, 172), (188, 280), (174, 368), (150, 386), (154, 300), (160, 200)]


def _fill(points: list[Vec], color: Color) -> Callable[[Layer], None]:
    def draw(layer: Layer) -> None:
        layer.fill(polygon(points), color)

    return draw


def _draw_hair_front(layer: Layer) -> None:
    top = quadratic((150, 230), (158, 96), (256, 104)) + quadratic((256, 104), (354, 96), (362, 230))[1:]
    fringe = [(344, 196), (330, 222), (314, 186), (296, 216), (276, 184), (256, 214), (236, 184)]
    fringe += [(216, 216), (198, 186), (182, 222), (168, 196)]
    layer.fill(polygon(top + fringe), HAIR)


def _draw_nose(layer: Layer) -> None:
    layer.fill(polygon([(256, 268), (252, 281), (260, 281)]), (226, 178, 164, 255))


def _draw_mouth_inner(layer: Layer) -> None:
    layer.fill(ellipse(256, 304, 17, 10), (150, 50, 62, 255))
    layer.fill(ellipse(256, 311, 10, 4), (222, 112, 122, 255))


def _draw_teeth(layer: Layer) -> None:
    layer.fill(polygon([(242, 294), (270, 294), (270, 299), (242, 299)]), (255, 255, 255, 255))


def _draw_mouth(layer: Layer) -> None:
    layer.fill(polygon(band(quadratic((238, 302), (256, 309), (274, 302)), 1, 1.2)), (170, 80, 84, 255))


def part_specs() -> list[PartSpec]:
    """テストモデルのパーツの定義を返す。"""
    face = sphere_depth(0)
    return [
        PartSpec("body", "body", None, (256, 470), 10, (96, 360, 416, 512), _draw_body),
        PartSpec("neck", "neck", "body", (256, 380), 15, (226, 296, 286, 396), _draw_neck, (2, 2)),
        PartSpec("head", "head", "neck", (256, 330), 20, None),
        PartSpec("hair_back", "hair_back", "head", (256, 200), 5, (134, 104, 378, 402), _draw_hair_back, (5, 5), flat_depth(-35)),
        PartSpec("face", "face", "head", (256, 236), 20, (160, 120, 352, 346), _draw_face, (5, 5), face),
        PartSpec("ear_r", "ear", "head", (164, 241), 18, (148, 214, 180, 268), _draw_ear(164), depth=flat_depth(-30)),
        PartSpec("ear_l", "ear", "head", (348, 241), 18, (332, 214, 364, 268), _draw_ear(348), depth=flat_depth(-30)),
        PartSpec("nose", "nose", "face", (256, 276), 25, (246, 264, 266, 286), _draw_nose, depth=sphere_depth(18)),
        PartSpec("eye_white_r", "eye_white", "face", (212, 246), 30, (186, 228, 238, 264), _draw_eye_white(212), depth=face),
        PartSpec("eye_white_l", "eye_white", "face", (300, 246), 30, (274, 228, 326, 264), _draw_eye_white(300), depth=face),
        PartSpec("iris_r", "iris", "face", (213, 248), 31, (199, 231, 227, 265), _draw_iris(213, -5), depth=face, clip_by=["eye_white_r"]),
        PartSpec("iris_l", "iris", "face", (299, 248), 31, (285, 231, 313, 265), _draw_iris(299, -5), depth=face, clip_by=["eye_white_l"]),
        PartSpec("eyelid_r", "eyelid", "face", (212, 234), 33, (176, 218, 244, 248), _fill(_eyelid_r(), LINE), depth=sphere_depth(2)),
        PartSpec("eyelid_l", "eyelid", "face", (300, 234), 33, (268, 218, 336, 248), _fill(mirror(_eyelid_r()), LINE), depth=sphere_depth(2)),
        PartSpec("eyebrow_r", "eyebrow", "face", (212, 206), 34, (186, 196, 238, 216), _fill(_eyebrow_r(), (92, 56, 46, 255)), depth=sphere_depth(4)),
        PartSpec("eyebrow_l", "eyebrow", "face", (300, 206), 34, (274, 196, 326, 216), _fill(mirror(_eyebrow_r()), (92, 56, 46, 255)), depth=sphere_depth(4)),
        PartSpec("mouth_inner", "mouth_inner", "face", (256, 304), 30, (236, 292, 276, 316), _draw_mouth_inner, depth=face),
        PartSpec("teeth", "teeth", "face", (256, 296), 31, (240, 292, 272, 302), _draw_teeth, depth=face, clip_by=["mouth_inner"]),
        PartSpec("mouth", "mouth", "face", (256, 304), 32, (232, 296, 280, 312), _draw_mouth, depth=sphere_depth(1)),
        PartSpec("hair_side_r", "hair_side", "head", (178, 150), 40, (144, 144, 204, 392), _fill(_hair_side_r(), HAIR), (3, 5), sphere_depth(-6)),
        PartSpec("hair_side_l", "hair_side", "head", (334, 150), 40, (308, 144, 368, 392), _fill(mirror(_hair_side_r()), HAIR), (3, 5), sphere_depth(-6)),
        PartSpec("hair_front", "hair_front", "head", (256, 150), 45, (144, 96, 368, 236), _draw_hair_front, (5, 3), sphere_depth(14)),
    ]


def _grid(rect: tuple[int, int, int, int], grid: tuple[int, int]) -> tuple[list[Point], list[Point], list[list[int]]]:
    """長方形を cols × rows の格子に分けた頂点、UV、三角形を返す。"""
    left, top, right, bottom = rect
    cols, rows = grid
    vertices, uvs, triangles = [], [], []
    for j in range(rows):
        for i in range(cols):
            u, v = i / (cols - 1), j / (rows - 1)
            vertices.append([_r(left + (right - left) * u), _r(top + (bottom - top) * v)])
            uvs.append([_r(u), _r(v)])
    for j in range(rows - 1):
        for i in range(cols - 1):
            k = j * cols + i
            triangles += [[k, k + 1, k + cols + 1], [k, k + cols + 1, k + cols]]
    return vertices, uvs, triangles


def _closed_mouth(vertices: list[Point]) -> list[Point]:
    """口の中と歯の頂点を、閉じた口の位置（縦につぶした形）に移す。"""
    return [[x, _r(MOUTH_CLOSED_Y + (y - MOUTH_CLOSED_Y) * MOUTH_CLOSED_SCALE)] for x, y in vertices]


def _angle_offsets(spec: PartSpec, vertices: list[Point], yaw: int, pitch: int) -> list[Point]:
    """顔の向き (yaw, pitch) のときの、各頂点の基準からの移動量を返す。"""
    assert spec.depth is not None
    offsets = []
    for x, y in vertices:
        px, py = rotate(x, y, spec.depth(x, y), yaw, pitch)
        offsets.append([_r(px - x), _r(py - y)])
    return offsets


def _angle_group(spec: PartSpec, vertices: list[Point]) -> KeyformGroup:
    """AngleX × AngleY の 3×3 のキーフォームグループを作る。"""
    keyforms = [Keyform([x, y], _angle_offsets(spec, vertices, x, y)) for x in ANGLE_KEYS for y in ANGLE_KEYS]
    return KeyformGroup(f"{spec.id}_angle", spec.id, ["AngleX", "AngleY"], [list(ANGLE_KEYS), list(ANGLE_KEYS)], "linear", keyforms)


def _one_axis_group(group_id: str, mesh: str, parameter: str, closed: list[Point], opened: list[Point]) -> KeyformGroup:
    """0 と 1 の2点を持つ1軸のキーフォームグループを作る。"""
    return KeyformGroup(group_id, mesh, [parameter], [[0, 1]], "linear", [Keyform([0], closed), Keyform([1], opened)])


def _zeros(count: int) -> list[Point]:
    return [[0, 0] for _ in range(count)]


def _blink_groups(side: str, vertices: dict[str, list[Point]]) -> list[KeyformGroup]:
    """片目のまばたき（EyeOpen が 0 で閉じる）のキーフォームグループを作る。"""
    parameter = "EyeOpenR" if side == "r" else "EyeOpenL"
    groups = []
    for mesh in (f"eye_white_{side}", f"iris_{side}"):
        closed = [[0, _r((EYE_CLOSED_Y - y) * 0.92)] for _, y in vertices[mesh]]
        groups.append(_one_axis_group(f"{mesh}_blink", mesh, parameter, closed, _zeros(len(closed))))
    lid = f"eyelid_{side}"
    columns = 3
    closed = [[0, 16 if k % columns == 1 else 6] for k in range(len(vertices[lid]))]
    groups.append(_one_axis_group(f"{lid}_blink", lid, parameter, closed, _zeros(len(closed))))
    return groups


def _mouth_groups(vertices: dict[str, list[Point]], textured: dict[str, list[Point]]) -> list[KeyformGroup]:
    """口の開き（MouthOpenY が 1 で開く）のキーフォームグループを作る。"""
    groups = []
    for mesh in ("mouth_inner", "teeth"):
        opened = [[0, _r(ty - y)] for (_, y), (_, ty) in zip(vertices[mesh], textured[mesh], strict=True)]
        groups.append(_one_axis_group(f"{mesh}_open", mesh, "MouthOpenY", _zeros(len(opened)), opened))
    lip = vertices["mouth"]
    groups.append(_one_axis_group("mouth_open", "mouth", "MouthOpenY", _zeros(len(lip)), [[0, -8] for _ in lip]))
    face = vertices["face"]
    bottom = max(y for _, y in face)
    jaw = [[0, 8 if y == bottom else (3 if y > HEAD_CENTER[1] + 40 else 0)] for _, y in face]
    groups.append(_one_axis_group("jaw_open", "face", "MouthOpenY", _zeros(len(face)), jaw))
    return groups


LANDMARKS: list[tuple[str, str, Vec]] = [
    ("brow_r", "eyebrow_r", (212, 206)),
    ("brow_l", "eyebrow_l", (300, 206)),
    ("eye_r_outer", "eye_white_r", (188, 246)),
    ("eye_r_center", "eye_white_r", (212, 246)),
    ("eye_r_inner", "eye_white_r", (236, 246)),
    ("eye_l_inner", "eye_white_l", (276, 246)),
    ("eye_l_center", "eye_white_l", (300, 246)),
    ("eye_l_outer", "eye_white_l", (324, 246)),
    ("nose_tip", "nose", (256, 279)),
    ("mouth_r", "mouth", (238, 302)),
    ("mouth_center", "mouth", (256, 306)),
    ("mouth_l", "mouth", (274, 302)),
    ("face_r", "face", (165, 230)),
    ("face_l", "face", (347, 230)),
    ("chin", "face", (256, 342)),
    ("ear_r", "ear_r", (164, 241)),
    ("ear_l", "ear_l", (348, 241)),
]


HAIR_SIDE_BEHIND_FACE = 15


def _far_side(yaw: int) -> str | None:
    """その向きで奥になる側の接尾辞（"_l" か "_r"）を返す。正面なら None を返す。"""
    if yaw > 0:
        return "_l"
    if yaw < 0:
        return "_r"
    return None


def _view_overrides(yaw: int) -> list[ViewOverride]:
    """奥になる側の耳を隠し、奥の横髪を顔の後ろに回す。"""
    side = _far_side(yaw)
    if side is None:
        return []
    return [ViewOverride(f"ear{side}", visible=False), ViewOverride(f"hair_side{side}", draw_order=HAIR_SIDE_BEHIND_FACE)]


def _hidden_ear(yaw: int) -> str | None:
    """その向きで顔の陰に隠れる耳のパーツ ID を返す。"""
    side = _far_side(yaw)
    return None if side is None else f"ear{side}"


def build_test_face() -> ModelPackage:
    """format/samples/test-face の内容を作る。"""
    specs = part_specs()
    by_id = {s.id: s for s in specs}
    parameters = [
        Parameter("AngleX", -30, 30, 0),
        Parameter("AngleY", -30, 30, 0),
        Parameter("AngleZ", -30, 30, 0),
        Parameter("EyeOpenL", 0, 1, 1),
        Parameter("EyeOpenR", 0, 1, 1),
        Parameter("MouthOpenY", 0, 1, 0),
        Parameter("Breath", 0, 1, 0),
    ]
    parts, meshes, textures = [], [], {}
    vertices: dict[str, list[Point]] = {}
    textured: dict[str, list[Point]] = {}
    for spec in specs:
        mesh_id = spec.id if spec.rect is not None else None
        parts.append(Part(spec.id, spec.role, spec.parent, list(spec.anchor), spec.draw_order, mesh_id, list(spec.clip_by)))
        if spec.rect is None or spec.draw is None:
            continue
        left, top, right, bottom = spec.rect
        layer = Layer(left, top, right - left, bottom - top)
        spec.draw(layer)
        verts, uvs, triangles = _grid(spec.rect, spec.grid)
        textured[spec.id] = verts
        if spec.id in ("mouth_inner", "teeth"):
            verts = _closed_mouth(verts)
        vertices[spec.id] = verts
        texture = f"textures/{spec.id}.png"
        meshes.append(Mesh(spec.id, spec.id, texture, verts, uvs, triangles))
        textures[texture] = layer.png()

    groups = [_angle_group(s, vertices[s.id]) for s in specs if s.depth is not None]
    groups += _blink_groups("r", vertices) + _blink_groups("l", vertices)
    groups += _mouth_groups(vertices, textured)
    procedural = [
        ProceduralMotion("rotate", "AngleZ", "head", degrees_per_unit=1),
        ProceduralMotion("translate", "Breath", "body", offset_per_unit=[0, -3]),
    ]

    views, landmarks, matches = [], [], []
    for view_id, yaw, pitch in VIEWS:
        hidden = _hidden_ear(yaw)
        views.append(View(view_id, yaw, pitch, _view_overrides(yaw)))
        for landmark_id, part_id, (x, y) in LANDMARKS:
            if part_id == hidden:
                continue
            spec = by_id[part_id]
            assert spec.depth is not None
            px, py = rotate(x, y, spec.depth(x, y), yaw, pitch)
            landmarks.append(Landmark(landmark_id, part_id, view_id, [_r(px), _r(py)], 1, "manual"))
        if view_id == "front":
            continue
        offsets = _angle_offsets(by_id["face"], vertices["face"], yaw, pitch)
        for v, ((x, y), (dx, dy)) in enumerate(zip(vertices["face"], offsets, strict=True)):
            matches.append(VertexMatch("face", v, view_id, [_r(x + dx), _r(y + dy)], 1, "manual"))

    model = Model(
        FORMAT_NAME,
        FORMAT_VERSION,
        Canvas(CANVAS, CANVAS),
        parameters,
        parts,
        meshes,
        views,
        Correspondence(landmarks, matches),
        Deformation(groups, procedural),
    )
    return ModelPackage(model, textures)


def make_test_face(directory: str | Path) -> None:
    """固定テストモデルをフォルダに書き出す。"""
    save(build_test_face(), directory)
