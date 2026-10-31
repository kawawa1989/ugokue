"""頭の3D形状（行ごとの楕円の断面）を正面の絵に当てはめ、頭のパーツを1つの回転と投影の式で回して、顔の向きのキーフォームを作る。"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np
from scipy.ndimage import gaussian_filter1d

from ugokue_format.model import Keyform, KeyformGroup, Mesh, Model
from ugokue_format.package import ModelPackage

from .correspond import rotate_with_depth
from .deform import triangle_quality
from .estimate import ANGLE_PARAMETERS, _alpha_on_canvas, head_parts, solve_without_flips

# 頭の前面の奥行き（行の半幅に対する割合）。1 で断面が円、小さいほど平たい。
HEAD_DEPTH = 0.8
# あご先の奥行き（頭の半径に対する割合）。口より下では、断面の幅を口の行のままにし、奥行きを口の行の値からこの値へ近づける。
CHIN_DEPTH = 0.55
# 鼻の、顔の表面からの突き出し（頭の半径に対する割合）。鼻は小さな線なので、パーツ全体を前へ出す。
NOSE_PROTRUSION = 0.15
# 前髪・頭の飾りの殻の厚み、まぶた・眉・口の表面からの浮き（頭の半径に対する割合）。
SHELL_OFFSET = 0.06
FEATURE_LIFT = {"eyelid": 0.01, "eyebrow": 0.02, "mouth": 0.005}
# 後ろ髪の奥行き（頭の半径に対する割合。頭の中心より奥）。
BACK_HAIR_DEPTH = 0.3
# 目・眉・口の形の縮み方を、頭の表面どおりの縮みに対してこの割合にする（1 で表面どおり）。
FEATURE_FORESHORTENING = 0.6
# 左右に回す軸の、頭の中心からの奥行き（頭の半径に対する割合）。首の付け根のように奥の軸で回すと、頭全体が向いた側へ動く。
PIVOT_DEPTH = 0.5
# 上下の回転は、AngleY の角度のこの割合だけ回す。頭の前面が深いので、角度どおりに回すと顔が大きく上下にずれて見える。
PITCH_SCALE = 0.6
# 眼鏡の、顔の表面からの浮き（頭の半径に対する割合）。
EYEWEAR_LIFT = 0.08
# 輪郭を合わせ直す外形線の点を、外形線の点の角度のこの割合だけ内側にする。
CONTOUR_INSET = 0.15
# 輪郭の外の頂点が、輪郭の合わせ直しの動きについていく範囲（頭の半径に対する割合）。
CONTOUR_REACH = 0.15
# 首の上端が頭の回転についていく割合。下端では 0。
NECK_FOLLOW = 1.0
# 行の幅をならす幅（顔の層の高さに対する割合）。
ROW_SMOOTHING = 0.02
ANGLES_X = [-45, -30, -15, 0, 15, 30, 45]
ANGLES_Y = [-20, 0, 20]
# 28点の特徴点の番号（anime-face-detector の順）。
MOUTH_LANDMARKS = [f"afd_{i:02d}" for i in range(24, 28)]


@dataclass
class HeadShape:
    """頭の3D形状。画面の行 y ごとに、中心 c、半幅 w、前面の奥行き h（頭の中心の面から手前が正）の楕円の断面を持つ。"""

    top: float
    centers: np.ndarray
    half_widths: np.ndarray
    depths: np.ndarray
    center: np.ndarray
    radius: float

    @classmethod
    def sphere(cls, center: tuple[float, float], radius: float, depth_scale: float) -> HeadShape:
        """中心 center、半径 radius の球を、奥行き方向に depth_scale 倍した楕円体。"""
        ys = np.arange(math.floor(center[1] - radius), math.ceil(center[1] + radius) + 1, dtype=np.float64)
        widths = np.sqrt(np.clip(radius**2 - (ys - center[1]) ** 2, 0, None))
        return cls(float(ys[0]), np.full(len(ys), float(center[0])), widths, depth_scale * widths, np.array(center, dtype=np.float64), radius)

    def section(self, y: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        """行 y の断面の中心、半幅、奥行き。形の範囲外の行は、いちばん近い端の行の断面にする（あご先の下で奥行きが急に 0 にならないように）。"""
        rows = np.arange(len(self.centers)) + self.top
        c = np.interp(y, rows, self.centers)
        w = np.interp(y, rows, self.half_widths)
        h = np.interp(y, rows, self.depths)
        return c, w, h


def fit_head(package: ModelPackage, landmarks: dict[str, tuple[float, float]], head_depth: float = HEAD_DEPTH, chin_depth: float = CHIN_DEPTH) -> HeadShape:
    """顔の層（髪の下に補完された頭皮を含む）の行ごとの幅をテンプレートの断面にし、口より下はあごの板にする。口の高さは特徴点から取る。"""
    model = package.model
    face = next(p for p in model.parts if p.id == "face" and p.mesh is not None)
    mesh = next(m for m in model.meshes if m.id == face.mesh)
    mask = _alpha_on_canvas(mesh, package.textures[mesh.texture], (model.canvas.height, model.canvas.width))
    rows = np.nonzero(mask.any(axis=1))[0]
    top, bottom = int(rows.min()), int(rows.max())
    lefts = np.array([np.argmax(mask[y]) if mask[y].any() else np.nan for y in range(top, bottom + 1)], dtype=np.float64)
    rights = np.array([mask.shape[1] - np.argmax(mask[y][::-1]) if mask[y].any() else np.nan for y in range(top, bottom + 1)], dtype=np.float64)
    index = np.arange(len(lefts))
    known = ~np.isnan(lefts)
    lefts, rights = np.interp(index, index[known], lefts[known]), np.interp(index, index[known], rights[known])
    sigma = max(1.0, ROW_SMOOTHING * len(lefts))
    centers = gaussian_filter1d((lefts + rights) / 2, sigma, mode="nearest")
    widths = gaussian_filter1d((rights - lefts) / 2, sigma, mode="nearest")
    radius = float(widths.max())
    depths = head_depth * widths
    mouth = [landmarks[i] for i in MOUTH_LANDMARKS if i in landmarks]
    if mouth:
        start = int(np.clip(round(np.mean([p[1] for p in mouth])) - top, 0, len(widths) - 1))
        t = np.clip((index - start) / max(1, len(widths) - 1 - start), 0, 1)
        # あごの輪郭（V字）は横の輪郭ではなく、あごの下側へ回り込む線なので、断面の幅は口の行のままにし、奥行きだけをあご先の値へ近づける。
        # 行ごとの幅で断面を作ると、輪郭の上で奥行きが急に 0 になり、上を向いたときにあごの輪郭がぎざぎざになる。
        later = index > start
        widths = np.where(later, widths[start], widths)
        depths = np.where(later, depths[start] + (chin_depth * radius - depths[start]) * t, depths)
    ys, xs = np.nonzero(mask)
    center = np.array([(xs.min() + xs.max() + 1) / 2, (top + bottom + 1) / 2])
    shape = HeadShape(float(top), centers, widths, depths, center, radius)
    return shape


@dataclass(frozen=True)
class Attachment:
    """パーツの頂点を頭の3D形状にどう付けるか。
    surface：断面を grow だけ太らせた楕円の表面に lift だけ浮かせる（輪郭の外は奥行き lift の板）。flat：奥行き depth の板。
    planar：パーツの形を2次式でなめらかにし、縮み方を弱める（目・眉・鼻・口）。"""

    kind: str = "surface"
    lift: float = 0.0
    grow: float = 0.0
    depth: float = 0.0
    planar: bool = False


def attachments_for(model: Model, radius: float) -> dict[str, Attachment]:
    """パーツの役割から付き方を決める（顔・目・眉・口は表面、鼻は突き出し、眼鏡は表面から浮かせる、前髪と頭の飾りは殻、耳は板、後ろ髪は奥の板）。"""
    result = {}
    for part in model.parts:
        if part.mesh is None:
            continue
        base = part.id.rsplit("_", 1)[0] if part.id.endswith(("_l", "_r")) else part.id
        if part.role == "hair_back":
            result[part.id] = Attachment("flat", depth=-BACK_HAIR_DEPTH * radius)
        elif part.role == "ear":
            result[part.id] = Attachment("flat")
        elif part.role == "nose":
            result[part.id] = Attachment(lift=NOSE_PROTRUSION * radius, planar=True)
        elif part.role in ("eye_white", "iris", "eyelid", "eyebrow", "mouth"):
            result[part.id] = Attachment(lift=FEATURE_LIFT.get(base, 0.0) * radius, planar=True)
        elif base == "eyewear":
            result[part.id] = Attachment(lift=EYEWEAR_LIFT * radius, planar=True)
        elif part.role == "face":
            result[part.id] = Attachment()
        else:
            result[part.id] = Attachment(grow=SHELL_OFFSET * radius)
    return result


def turn(shape: HeadShape, points: np.ndarray, attachment: Attachment, yaw: float, pitch: float, pivot: float = 0.0, silhouette: bool = True) -> np.ndarray:
    """パーツの頂点を頭と一緒に yaw → pitch の順に回したときの画面上の位置。
    silhouette が真なら、向いた側の輪郭の頂点を、回した形の外形線（断面がいちばん外に出る点）に合わせ直す。断面の点の角度を、輪郭から外形線の点までの範囲に引き伸ばす。"""
    if attachment.kind == "flat":
        z = np.full(len(points), attachment.depth)
        moved = rotate_with_depth(points, shape.center, z, yaw, pitch)
    else:
        c, w, h = shape.section(points[:, 1])
        w, h = np.where(w > 0, w + attachment.grow, 0.0), np.where(w > 0, h + attachment.grow, 0.0)
        inside = (w > 0) & (np.abs(points[:, 0] - c) <= w)
        s = np.clip((points[:, 0] - c) / np.where(w > 0, w, 1.0), -1, 1)
        phi = np.arccos(s)
        edge_angle = _silhouette_angle(w, h, yaw) if silhouette else np.zeros(len(points))
        phi = _stretch(phi, edge_angle, yaw)
        x3 = np.where(inside, c + w * np.cos(phi), points[:, 0])
        z3 = np.where(inside, h * np.sin(phi), 0.0) + attachment.lift
        if attachment.planar:
            x3, z3 = _smooth(points, x3, z3, inside)
        flat = np.stack([x3, points[:, 1]], axis=1)
        moved = rotate_with_depth(flat, shape.center, z3, yaw, pitch)
        if silhouette:
            moved = moved + _follow_contour(shape, points, attachment, inside, c, w, h, edge_angle, yaw, pitch)
    moved[:, 0] += pivot * math.sin(math.radians(yaw))
    return moved


def _silhouette_angle(w: np.ndarray, h: np.ndarray, yaw: float) -> np.ndarray:
    """左右に yaw 回したとき、向いた側（奥へ回り込む側）で断面がいちばん外に出る点の角度（輪郭からの角度、0〜π/2）を、CONTOUR_INSET 倍だけ内側にしたもの。
    外形線ちょうどでは表面を真横から見るので、輪郭の近くの三角形がつぶれる。"""
    t = abs(math.radians(yaw))
    return np.minimum((1 + CONTOUR_INSET) * np.arctan2(h * math.sin(t), np.where(w > 0, w, 1.0) * math.cos(t)), math.pi / 2)


def _stretch(phi: np.ndarray, edge_angle: np.ndarray, yaw: float) -> np.ndarray:
    """断面の点の角度（右の輪郭で 0、正面で π/2、左の輪郭で π）を、向いた側だけ、輪郭が外形線の点に来るように詰める。"""
    if yaw > 0:
        return np.where(phi < math.pi / 2, edge_angle + phi * (1 - 2 * edge_angle / math.pi), phi)
    if yaw < 0:
        mirrored = math.pi - phi
        return np.where(phi > math.pi / 2, math.pi - (edge_angle + mirrored * (1 - 2 * edge_angle / math.pi)), phi)
    return phi


def _follow_contour(
    shape: HeadShape, points: np.ndarray, attachment: Attachment, inside: np.ndarray, c: np.ndarray, w: np.ndarray, h: np.ndarray, edge_angle: np.ndarray, yaw: float, pitch: float
) -> np.ndarray:
    """輪郭の外の頂点（耳や、顔の横に垂れる髪）を、輪郭の合わせ直しの動きに、輪郭から離れるほど弱くついていかせる。"""
    outside = (~inside) & (w > 0)
    if yaw == 0 or not outside.any():
        return np.zeros_like(points)
    side = np.sign(points[:, 0] - c)
    toward = side == np.sign(yaw)
    edge = np.stack([c + side * w, points[:, 1]], axis=1)
    lift = np.full(len(points), attachment.lift)
    plain = rotate_with_depth(edge, shape.center, lift, yaw, pitch)
    angle = np.where(side > 0, edge_angle, math.pi - edge_angle)
    snapped_edge = np.stack([c + w * np.cos(angle), points[:, 1]], axis=1)
    snapped = rotate_with_depth(snapped_edge, shape.center, h * np.sin(angle) + lift, yaw, pitch)
    distance = np.abs(points[:, 0] - edge[:, 0])
    weight = np.exp(-((distance / (CONTOUR_REACH * shape.radius)) ** 2)) * (outside & toward)
    return (snapped - plain) * weight[:, None]


def _smooth(points: np.ndarray, x3: np.ndarray, z3: np.ndarray, inside: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """パーツの3D座標を、輪郭の内側の頂点に当てはめた2次式で置き換える。まつ毛の目尻のように輪郭の外に出た頂点で奥行きが急に変わると、回したときに三角形が裏返るため。"""
    if inside.sum() < 6:
        return x3, z3
    x, y = points[:, 0] - points[:, 0].mean(), points[:, 1] - points[:, 1].mean()
    design = np.column_stack([np.ones(len(points)), x, y, x * x, x * y, y * y])
    fitted = []
    for values in (x3, z3):
        coefficients, *_ = np.linalg.lstsq(design[inside], values[inside], rcond=None)
        fitted.append(design @ coefficients)
    return fitted[0], fitted[1]


def _soften(rest: np.ndarray, moved: np.ndarray, foreshortening: float) -> np.ndarray:
    """パーツの重心の動きはそのままにし、形の変化（縮み）を foreshortening 倍に弱める。"""
    shift = moved.mean(axis=0) - rest.mean(axis=0)
    return rest + shift + foreshortening * (moved - rest - shift)


def neck_follow(shape: HeadShape, mesh: Mesh, yaw: float, pitch: float, pivot: float) -> np.ndarray:
    """首の頂点の移動量。あごの高さより上は頭と一緒に回り、下端に向かって追従を弱める。
    上下はあご先と同じ奥行きで回し（上を向いても、あごの下から首の上端が見えない）、左右は頭の中心の面で回す（あご先と同じだけ横に動くと、首が大きくゆがむ）。"""
    rest = np.array(mesh.vertices, dtype=np.float64)
    chin = shape.top + len(shape.centers) - 1
    bottom = float(rest[:, 1].max())
    weight = NECK_FOLLOW * np.clip((bottom - rest[:, 1]) / max(1.0, bottom - chin), 0, 1)
    moved = np.stack([
        rotate_with_depth(rest, shape.center, np.zeros(len(rest)), yaw, pitch)[:, 0],
        rotate_with_depth(rest, shape.center, np.full(len(rest), shape.depths[-1]), yaw, pitch)[:, 1],
    ], axis=1)
    moved[:, 0] += pivot * math.sin(math.radians(yaw))
    return (moved - rest) * weight[:, None]


def build_keyforms(
    package: ModelPackage,
    shape: HeadShape,
    attachments: dict[str, Attachment] | None = None,
    xs: list[int] = ANGLES_X,
    ys: list[int] = ANGLES_Y,
    pivot_depth: float = PIVOT_DEPTH,
    silhouette: bool = True,
    foreshortening: float = FEATURE_FORESHORTENING,
    neck: bool = True,
    pitch_scale: float = PITCH_SCALE,
) -> list[KeyformGroup]:
    """頭のパーツ（と首）のメッシュごとに、AngleX × AngleY の格子の顔の向きのキーフォームグループを作る。三角形が裏返る角度では、移動量をならして解き直す。上下は pitch_scale 倍の角度で回す。"""
    model = package.model
    attachments = attachments if attachments is not None else attachments_for(model, shape.radius)
    parts = head_parts(model)
    pivot = pivot_depth * shape.radius
    groups = []
    for mesh in model.meshes:
        is_neck = neck and mesh.part == "neck"
        if mesh.part not in parts and not is_neck:
            continue
        rest = np.array(mesh.vertices, dtype=np.float64)
        keyforms = []
        for x in xs:
            for y in ys:
                if is_neck:
                    offsets = neck_follow(shape, mesh, x, y * pitch_scale, pivot)
                else:
                    attachment = attachments[mesh.part]
                    moved = turn(shape, rest, attachment, x, y * pitch_scale, pivot, silhouette)
                    if attachment.planar:
                        moved = _soften(rest, moved, foreshortening)
                    offsets = moved - rest
                    if triangle_quality(rest, moved, mesh.triangles)["flipped"]:
                        offsets = solve_without_flips(rest, mesh.triangles, rest, np.zeros(len(rest)), offsets, 1.0, keep_prior_shape=True)
                keyforms.append(Keyform([x, y], [[round(float(dx), 3), round(float(dy), 3)] for dx, dy in offsets]))
        groups.append(KeyformGroup(f"{mesh.id}_angle", mesh.id, list(ANGLE_PARAMETERS), [list(xs), list(ys)], "linear", keyforms))
    return groups


def apply_keyforms(package: ModelPackage, groups: list[KeyformGroup]) -> ModelPackage:
    """顔の向きのキーフォームグループを置き換え、AngleX・AngleY の範囲を格子に合わせて広げる。"""
    model = package.model
    replaced = {g.mesh for g in groups}
    kept = [g for g in model.deformation.keyform_groups if not (g.mesh in replaced and g.parameters == ANGLE_PARAMETERS)]
    model.deformation.keyform_groups = kept + groups
    for parameter in model.parameters:
        for index, name in enumerate(ANGLE_PARAMETERS):
            if parameter.id == name and groups:
                keys = groups[0].keys[index]
                parameter.min, parameter.max = min(parameter.min, keys[0]), max(parameter.max, keys[-1])
    return package
