"""頭の3D形状（行ごとの楕円の断面）を正面の絵に当てはめ、頭のパーツを1つの回転と投影の式で回して、顔の向きのキーフォームを作る。"""

from __future__ import annotations

import io
import math
from dataclasses import asdict, dataclass, fields
from typing import Any

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter1d

from ugokue_format.model import Keyform, KeyformGroup, Mesh, Model
from ugokue_format.package import ModelPackage

from .correspond import rotate_with_depth
from .deform import opaque_triangles, triangle_quality
from .estimate import ANGLE_PARAMETERS, SMOOTHNESS, _alpha_on_canvas, head_parts, solve_offsets, solve_without_flips

# 頭の前面の奥行き（行の半幅に対する割合）。1 で断面が円、小さいほど平たい。
HEAD_DEPTH = 0.8
# あご先の奥行き（頭の半径に対する割合）。口より下では、断面の幅を口の行のままにし、奥行きを口の行の値からこの値へ近づける。
CHIN_DEPTH = 0.75
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
# 板のある断面の外形線の点を探す角度の刻みの数（0〜π/2）。
SILHOUETTE_STEPS = 181
# 首の上端が頭の回転についていく割合。下端では 0。
NECK_FOLLOW = 1.0
# 行の幅をならす幅（顔の層の高さに対する割合）。
ROW_SMOOTHING = 0.02
# 顔の板の半幅（目尻の中心からの距離に対する倍率）。目から口までの断面は、前を平らな板にし、板の外を楕円の角で丸める。0 なら楕円のまま。
FACE_PLATE = 0.8
# あご先の板の半幅（口の高さの断面の半幅に対する割合）。
CHIN_PLATE = 0.3
# 断面の板の半幅の、断面の半幅に対する割合の上限（角を残す）。
MAX_PLATE = 0.9
# 輪郭の目標の線（2D）。3D で回した輪郭を、縦に頭の半径 × この値の幅でならして、角と直線をなくす（あご先の V 字は U 字に近づく）。0 ならならさない。
CONTOUR_SMOOTHING = 0.1
# 頬のふくらみ。奥側の目標の線を、頭の半径 × この値 × sin|AngleX| だけ外へ出す。頬の高さでいちばん強く、目の高さとあご先で 0。
CHEEK_BULGE = 0.3
# 頬のふくらみがいちばん強い高さ（目の高さから口の高さまでの割合）。
CHEEK_HEIGHT = 0.4
# あごの丸み。口の高さからあご先までの目標の線を、あごの角からあご先（幅 chin_plate）へ回り込む楕円の弧に、この割合だけ近づける（0 なら直線のまま）。
JAW_ROUND = 1.0
# 手前側の頭の横の張り（頭の横の奥行き ÷ 断面の半幅）。手前側では、正面の絵に描かれていない頭の横が回ってきて外形線になるので、
# 手前側の目標の線を、奥行きがこの値の楕円の外形線まで外へ出す。0 なら出さない。
NEAR_FULLNESS = 0.8
# 輪郭を目標の線へ寄せる量を、輪郭から内側へ弱める距離と、輪郭の外の頂点（耳、髪）へ弱める距離（頭の半径に対する割合）。
CONTOUR_REACH_INSIDE = 0.3
CONTOUR_REACH_OUTSIDE = 0.8
# 手前側で、輪郭までを並べ直す範囲の内側の端（中心からの距離 ÷ 半幅）。頭の横が回ってきて広がるぶんを、この範囲全体に散らす。
NEAR_START = 0.3
# 表面に付くパーツ（顔、髪）は、正面以外で、輪郭の近くを守りながら移動量をならす（輪郭の合わせ直しの伸びを、メッシュ全体に散らす）。
RELAX = True
# 移動量をならす・裏返りを解き直すとき、パーツの輪郭（描かれている三角形と透明な三角形の境）の頂点で元の動きを守る強さ（ほかは 1）。
FLIP_HOLD = 30.0
# 裏返った三角形を解き直すとき、動きを変えてよい範囲（頭の半径に対する割合）。その外は、離れるほど強く元の動きを守る（最大 FLIP_HOLD_FAR 倍）。
FLIP_REACH = 0.6
FLIP_HOLD_FAR = 1000.0
# 手の修正のグループ（Unity エディタのキーフォームエディタが作る）の ID の末尾。
EDIT_SUFFIX = "_angle_edit"
ANGLES_X = [-45, -30, -15, 0, 15, 30, 45]
ANGLES_Y = [-20, 0, 20]
# 28点の特徴点の番号（anime-face-detector の順）。
MOUTH_LANDMARKS = [f"afd_{i:02d}" for i in range(24, 28)]
EYE_LANDMARKS = range(11, 23)


@dataclass(frozen=True)
class HeadParams:
    """イラストごとに変えられる、頭の3D形状と回し方の値。既定値は上の定数。"""

    head_depth: float = HEAD_DEPTH
    chin_depth: float = CHIN_DEPTH
    nose_protrusion: float = NOSE_PROTRUSION
    shell_offset: float = SHELL_OFFSET
    back_hair_depth: float = BACK_HAIR_DEPTH
    eyewear_lift: float = EYEWEAR_LIFT
    feature_foreshortening: float = FEATURE_FORESHORTENING
    pivot_depth: float = PIVOT_DEPTH
    pitch_scale: float = PITCH_SCALE
    neck_follow: float = NECK_FOLLOW
    face_plate: float = FACE_PLATE
    chin_plate: float = CHIN_PLATE
    contour_smoothing: float = CONTOUR_SMOOTHING
    jaw_round: float = JAW_ROUND
    cheek_bulge: float = CHEEK_BULGE
    near_fullness: float = NEAR_FULLNESS

    @classmethod
    def from_json(cls, data: dict[str, Any]) -> HeadParams:
        """JSON の値（キーは camelCase）で既定値を上書きする。未知のキーや数でない値は拒否する。"""
        names = {_camel(f.name): f.name for f in fields(cls)}
        unknown = sorted(set(data) - set(names))
        if unknown:
            raise ValueError(f"unknown head parameters: {', '.join(unknown)}")
        values = {}
        for key, value in data.items():
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                raise ValueError(f"head parameter {key} must be a finite number")
            values[names[key]] = float(value)
        return cls(**values)

    def to_json(self) -> dict[str, float]:
        """camelCase のキーの辞書にする。"""
        return {_camel(k): v for k, v in asdict(self).items()}


def _camel(name: str) -> str:
    head, *rest = name.split("_")
    return head + "".join(word.capitalize() for word in rest)


@dataclass
class HeadShape:
    """頭の3D形状。画面の行 y ごとに、中心 c、半幅 w、前面の奥行き h（頭の中心の面から手前が正）の断面を持つ。
    断面は、前が半幅の k 倍の平らな板で、板の外を楕円の角で丸めた形（k が 0 なら楕円）。plates がなければ k はすべて 0。
    edges は、行ごとの顔の層の左右の端（なめらかにしたもの。口より下も実際のあごの線）。cheeks は、行ごとの頬のふくらみの強さ（0〜1）。"""

    top: float
    centers: np.ndarray
    half_widths: np.ndarray
    depths: np.ndarray
    center: np.ndarray
    radius: float
    plates: np.ndarray | None = None
    edges: np.ndarray | None = None
    cheeks: np.ndarray | None = None
    mouth_y: float | None = None

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

    def row_values(self, values: np.ndarray | None, y: np.ndarray) -> np.ndarray:
        """行ごとの値 values を行 y で補間する。values がなければ 0。"""
        if values is None:
            return np.zeros(np.shape(y))
        return np.interp(y, np.arange(len(self.centers)) + self.top, values)


def section_point(phi: np.ndarray, k: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """板の割合 k の断面の、角度 phi（右の輪郭で 0、正面で π/2、左の輪郭で π）の点（半幅と奥行きを 1 にしたときの x と z）。
    角度の 0〜(1−k)π/2 は右の角（楕円の 1/4）、そこから π/2 までは板。k が 0 なら (cos φ, sin φ)。"""
    phi, k = np.broadcast_arrays(phi, k)
    side = np.where(phi <= math.pi / 2, 1.0, -1.0)
    a = np.where(side > 0, phi, math.pi - phi)
    corner = (1 - k) * math.pi / 2
    theta = np.clip(a / np.where(corner > 0, corner, 1.0), 0, 1) * math.pi / 2
    on_corner = a < corner
    x = np.where(on_corner, k + (1 - k) * np.cos(theta), k * (math.pi / 2 - a) / np.where(corner < math.pi / 2, math.pi / 2 - corner, 1.0))
    z = np.where(on_corner, np.sin(theta), 1.0)
    return side * x, z


def section_angle(s: np.ndarray, k: np.ndarray) -> np.ndarray:
    """横の位置 s（中心からの距離 ÷ 半幅、−1〜1）にある断面の点の角度。section_point の逆。"""
    s, k = np.broadcast_arrays(s, k)
    a_s = np.abs(s)
    corner = (1 - k) * math.pi / 2
    on_corner = a_s >= k
    theta = np.arccos(np.clip((a_s - k) / np.where(k < 1, 1 - k, 1.0), 0, 1))
    a = np.where(on_corner, theta / (math.pi / 2) * corner, math.pi / 2 - a_s / np.where(k > 0, k, 1.0) * (math.pi / 2 - corner))
    return np.where(s >= 0, a, math.pi - a)


def fit_head(package: ModelPackage, landmarks: dict[str, tuple[float, float]], params: HeadParams = HeadParams()) -> HeadShape:
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
    edges = np.stack([gaussian_filter1d(lefts, sigma, mode="nearest"), gaussian_filter1d(rights, sigma, mode="nearest")], axis=1)
    centers = gaussian_filter1d((lefts + rights) / 2, sigma, mode="nearest")
    widths = gaussian_filter1d((rights - lefts) / 2, sigma, mode="nearest")
    radius = float(widths.max())
    depths = params.head_depth * widths
    eyes = [landmarks[f"afd_{i:02d}"] for i in EYE_LANDMARKS if f"afd_{i:02d}" in landmarks]
    if eyes:
        # 顔の前面（目・鼻・口）は1つの面に乗るので、目より下の奥行きは目の行の値のままにする（正面の輪郭の V 字に合わせて浅くしない）。
        eye_row = int(np.clip(round(np.mean([p[1] for p in eyes])) - top, 0, len(widths) - 1))
        depths = np.where(index > eye_row, depths[eye_row], depths)
    mouth = [landmarks[i] for i in MOUTH_LANDMARKS if i in landmarks]
    if mouth:
        start = int(np.clip(round(np.mean([p[1] for p in mouth])) - top, 0, len(widths) - 1))
        t = np.clip((index - start) / max(1, len(widths) - 1 - start), 0, 1)
        # あごの輪郭（V字）は横の輪郭ではなく、あごの下側へ回り込む線なので、断面の幅は口の行のままにし、奥行きだけをあご先の値へ近づける。
        # 行ごとの幅で断面を作ると、輪郭の上で奥行きが急に 0 になり、上を向いたときにあごの輪郭がぎざぎざになる。
        later = index > start
        widths = np.where(later, widths[start], widths)
        depths = np.where(later, depths[start] + (params.chin_depth * radius - depths[start]) * t, depths)
    plates, cheeks = _face_plates(landmarks, centers, widths, top, params)
    ys, xs = np.nonzero(mask)
    center = np.array([(xs.min() + xs.max() + 1) / 2, (top + bottom + 1) / 2])
    shape = HeadShape(float(top), centers, widths, depths, center, radius, plates, edges, cheeks, float(np.mean([p[1] for p in mouth])) if mouth else None)
    return shape


def _face_plates(
    landmarks: dict[str, tuple[float, float]], centers: np.ndarray, widths: np.ndarray, top: int, params: HeadParams
) -> tuple[np.ndarray | None, np.ndarray | None]:
    """行ごとの断面の板の割合と、頬のふくらみの強さ。板の割合は、目の行で 目尻の中心からの距離 × face_plate ÷ 断面の半幅（顔の前面）で、口まではその割合のまま。
    目より上は目と口の間の長さで、口より下はいちばん下の行までで、なめらかに（cos で）細くし、あご先では口の行の半幅 × chin_plate にする。
    頬のふくらみは、頬の高さで 1、目の高さといちばん下の行で 0 になるよう cos でつなぐ。"""
    eyes = np.array([landmarks[f"afd_{i:02d}"] for i in EYE_LANDMARKS if f"afd_{i:02d}" in landmarks])
    mouth = [landmarks[i] for i in MOUTH_LANDMARKS if i in landmarks]
    if len(eyes) == 0 or not mouth:
        return None, None
    rows = np.arange(len(widths)) + top
    eye_y, mouth_y = float(eyes[:, 1].mean()), float(np.mean([p[1] for p in mouth]))
    eye_c = float(np.interp(eye_y, rows, centers))
    eye_w = float(np.interp(eye_y, rows, widths))
    ratio = min(MAX_PLATE, params.face_plate * float(np.abs(eyes[:, 0] - eye_c).max()) / max(1.0, eye_w))
    span = max(1.0, mouth_y - eye_y)
    mouth_w = float(np.interp(mouth_y, rows, widths))
    chin = params.chin_plate * mouth_w
    above = np.clip((eye_y - rows) / span, 0, 1)
    below = np.clip((rows - mouth_y) / max(1.0, rows[-1] - mouth_y), 0, 1)
    # 目より下は、板の割合を目の行の値のままにする（板の幅を一定にすると、顔が細くなる口の高さで板の外の角が急になり、輪郭が内へ引き込まれる）。
    plate = np.where(rows < eye_y, eye_w * ratio * (1 + np.cos(math.pi * above)) / 2, widths * ratio)
    plate = np.where(rows > mouth_y, chin + (mouth_w * ratio - chin) * (1 + np.cos(math.pi * below)) / 2, plate)
    cheek = eye_y + CHEEK_HEIGHT * span
    toward = np.where(rows < cheek, (cheek - rows) / max(1.0, cheek - eye_y), (rows - cheek) / max(1.0, rows[-1] - cheek))
    cheeks = (1 + np.cos(math.pi * np.clip(toward, 0, 1))) / 2
    plates = np.clip(plate / np.where(widths > 0, widths, 1.0), 0, MAX_PLATE) if params.face_plate else None
    return plates, cheeks


@dataclass(frozen=True)
class Attachment:
    """パーツの頂点を頭の3D形状にどう付けるか。
    surface：断面を grow だけ太らせた楕円の表面に lift だけ浮かせる（輪郭の外は奥行き lift の板）。flat：奥行き depth の板。
    planar：パーツの形を2次式でなめらかにし、縮み方を弱める（目・眉・鼻・口）。ear：手前側では、輪郭の目標の線からの離れ方を正面のまま保つ（耳）。"""

    kind: str = "surface"
    lift: float = 0.0
    grow: float = 0.0
    depth: float = 0.0
    planar: bool = False
    ear: bool = False


def attachments_for(model: Model, radius: float, params: HeadParams = HeadParams()) -> dict[str, Attachment]:
    """パーツの役割から付き方を決める（顔・目・眉・口は表面、鼻は突き出し、眼鏡は表面から浮かせる、前髪と頭の飾りは殻、耳は板、後ろ髪は奥の板）。"""
    result = {}
    for part in model.parts:
        if part.mesh is None:
            continue
        base = part.id.rsplit("_", 1)[0] if part.id.endswith(("_l", "_r")) else part.id
        if part.role == "hair_back":
            result[part.id] = Attachment("flat", depth=-params.back_hair_depth * radius)
        elif part.role == "ear":
            result[part.id] = Attachment("flat", ear=True)
        elif part.role == "nose":
            result[part.id] = Attachment(lift=params.nose_protrusion * radius, planar=True)
        elif part.role in ("eye_white", "iris", "eyelid", "eyebrow", "mouth"):
            result[part.id] = Attachment(lift=FEATURE_LIFT.get(base, 0.0) * radius, planar=True)
        elif base == "eyewear":
            result[part.id] = Attachment(lift=params.eyewear_lift * radius, planar=True)
        elif part.role == "face":
            result[part.id] = Attachment()
        else:
            result[part.id] = Attachment(grow=params.shell_offset * radius)
    return result


def turn(
    shape: HeadShape, points: np.ndarray, attachment: Attachment, yaw: float, pitch: float, pivot: float = 0.0, silhouette: bool = True, outline: HeadParams | None = None
) -> np.ndarray:
    """パーツの頂点を頭と一緒に yaw → pitch の順に回したときの画面上の位置。
    silhouette が真なら、向いた側の輪郭の頂点を、回した形の外形線（断面がいちばん外に出る点）に合わせ直す。断面の点の角度を、輪郭から外形線の点までの範囲に引き伸ばす。
    outline があれば、目・眉・鼻・口以外の頂点を、その値で作った輪郭の目標の線へ寄せる（_outline_shift）。"""
    if attachment.kind == "flat":
        z = np.full(len(points), attachment.depth)
        moved = rotate_with_depth(points, shape.center, z, yaw, pitch)
    else:
        c, w, h = shape.section(points[:, 1])
        k = shape.row_values(shape.plates, points[:, 1])
        w, h = np.where(w > 0, w + attachment.grow, 0.0), np.where(w > 0, h + attachment.grow, 0.0)
        inside = (w > 0) & (np.abs(points[:, 0] - c) <= w)
        s = np.clip((points[:, 0] - c) / np.where(w > 0, w, 1.0), -1, 1)
        phi = section_angle(s, k)
        edge_angle = _silhouette_angle(w, h, k, yaw) if silhouette else np.zeros(len(points))
        phi = _stretch(phi, edge_angle, yaw)
        px, pz = section_point(phi, k)
        x3 = np.where(inside, c + w * px, points[:, 0])
        z3 = np.where(inside, h * pz, 0.0) + attachment.lift
        if attachment.planar:
            x3, z3 = _smooth(points, x3, z3, inside)
        flat = np.stack([x3, points[:, 1]], axis=1)
        moved = rotate_with_depth(flat, shape.center, z3, yaw, pitch)
        if silhouette and not attachment.planar:
            moved[:, 0] = _even_near_corner(shape, points, moved[:, 0], c, w, h, k, s, inside, attachment.lift, yaw, pitch)
        if silhouette:
            moved = moved + _follow_contour(shape, points, attachment, inside, c, w, h, k, edge_angle, yaw, pitch)
    if silhouette and outline is not None and not attachment.planar:
        moved[:, 0] += _outline_shift(shape, points, yaw, pitch, outline)
        if attachment.ear:
            moved[:, 0] = _attach_ear(shape, points, moved[:, 0], yaw, pitch, outline)
    moved[:, 0] += pivot * math.sin(math.radians(yaw))
    return moved


def _silhouette_angle(w: np.ndarray, h: np.ndarray, k: np.ndarray, yaw: float) -> np.ndarray:
    """左右に yaw 回したとき、向いた側（奥へ回り込む側）で断面がいちばん外に出る点の角度（輪郭からの角度、0〜π/2）を、CONTOUR_INSET 倍だけ内側にしたもの。
    外形線ちょうどでは表面を真横から見るので、輪郭の近くの三角形がつぶれる。楕円は式で、板のある断面は角度を細かく調べて求める。"""
    t = abs(math.radians(yaw))
    w = np.where(w > 0, w, 1.0)
    angle = np.arctan2(h * math.sin(t), w * math.cos(t))
    if np.any(k > 0):
        grid = np.linspace(0, math.pi / 2, SILHOUETTE_STEPS)
        px, pz = section_point(grid[None, :], k[:, None])
        reach = w[:, None] * math.cos(t) * px + h[:, None] * math.sin(t) * pz
        angle = np.where(k > 0, grid[np.argmax(reach, axis=1)], angle)
    return np.minimum((1 + CONTOUR_INSET) * angle, math.pi / 2)


def _stretch(phi: np.ndarray, edge_angle: np.ndarray, yaw: float) -> np.ndarray:
    """断面の点の角度（右の輪郭で 0、正面で π/2、左の輪郭で π）を、向いた側だけ、輪郭が外形線の点に来るように詰める。"""
    if yaw > 0:
        return np.where(phi < math.pi / 2, edge_angle + phi * (1 - 2 * edge_angle / math.pi), phi)
    if yaw < 0:
        mirrored = math.pi - phi
        return np.where(phi > math.pi / 2, math.pi - (edge_angle + mirrored * (1 - 2 * edge_angle / math.pi)), phi)
    return phi


def _follow_contour(
    shape: HeadShape, points: np.ndarray, attachment: Attachment, inside: np.ndarray, c: np.ndarray, w: np.ndarray, h: np.ndarray, k: np.ndarray, edge_angle: np.ndarray, yaw: float, pitch: float
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
    px, pz = section_point(angle, k)
    snapped_edge = np.stack([c + w * px, points[:, 1]], axis=1)
    snapped = rotate_with_depth(snapped_edge, shape.center, h * pz + lift, yaw, pitch)
    distance = np.abs(points[:, 0] - edge[:, 0])
    weight = np.exp(-((distance / (CONTOUR_REACH * shape.radius)) ** 2)) * (outside & toward)
    return (snapped - plain) * weight[:, None]


def _even_near_corner(
    shape: HeadShape, points: np.ndarray, x: np.ndarray, c: np.ndarray, w: np.ndarray, h: np.ndarray, k: np.ndarray, s: np.ndarray, inside: np.ndarray,
    lift: float, yaw: float, pitch: float,
) -> np.ndarray:
    """手前側（頭の横が回ってくる側）の、中心から NEAR_START × 半幅より外の頂点の画面の x を、その点と輪郭の点を回した位置の間に、なめらかな曲線で並べ直す。
    板の外の角は急なので、そのまま回すと、輪郭のすぐ内側が大きく内へ引き込まれて耳から離れ、輪郭の近くの三角形が大きく伸びて、輪郭の線が太ってにじむ。
    曲線の比は、内側の端で板の上の比（cos yaw）、輪郭で 1（輪郭の線の太さを保つ）にし、広がるぶんを範囲の中ほどに散らす。輪郭の外の頂点は比 1 で延ばす。"""
    side = -np.sign(yaw)
    original, k = k, np.full_like(k, NEAR_START)
    near = (w > 0) & (side * s > k) & (yaw != 0)
    if not near.any():
        return x
    rows = points[near, 1]
    plate_edge = np.stack([c[near] + side * k[near] * w[near], rows], axis=1)
    outline = np.stack([c[near] + side * w[near], rows], axis=1)
    _, depth = section_point(section_angle(k[near], original[near]), original[near])
    start = rotate_with_depth(plate_edge, shape.center, h[near] * depth + lift, yaw, pitch)[:, 0]
    end = rotate_with_depth(outline, shape.center, np.full(near.sum(), lift), yaw, pitch)[:, 0]
    span = np.where(k[near] < 1, 1 - k[near], 1.0) * w[near]
    u = side * (points[near, 0] - plate_edge[:, 0]) / span
    # 比がなめらかに変わる3次の曲線（比が折れると、粗いメッシュの細長い三角形で縁が階段状になる）。
    first, last = side * math.cos(math.radians(yaw)) * span, side * span
    curve = start + first * (u ** 3 - 2 * u ** 2 + u) + (end - start) * (-2 * u ** 3 + 3 * u ** 2) + last * (u ** 3 - u ** 2)
    placed = np.where(u <= 1, curve, end + last * (u - 1))
    if shape.mouth_y is not None:
        # 口より下（あごの板）は、輪郭の目標の線（丸いあご）に任せ、あご先へ向かって並べ直しを弱める。断面の幅は口の行のままで、描かれたあごより広いため。
        bottom = shape.top + len(shape.centers) - 1
        fade = np.clip((bottom - rows) / max(1.0, bottom - shape.mouth_y), 0, 1)
        placed = x[near] + fade * (placed - x[near])
    x = x.copy()
    x[near] = placed
    return x


def _outline_targets(shape: HeadShape, yaw: float, pitch: float, params: HeadParams) -> tuple[np.ndarray, list[tuple[float, np.ndarray, np.ndarray]]]:
    """行ごとの、左右の輪郭の基の線（顔の層の端を 3D で回した位置）と目標の線。基の線を縦にならし、奥側には頬のふくらみを、手前側には頭の横の張りを足して目標の線にする。
    行と、左右それぞれの（側、基の線、目標の線）を返す。"""
    t = abs(math.radians(yaw))
    rows = np.arange(len(shape.centers)) + shape.top
    half = (shape.edges[:, 1] - shape.edges[:, 0]) / 2
    cheek = shape.row_values(shape.cheeks, rows)
    sides = []
    for column, side in ((0, -1.0), (1, 1.0)):
        base = turn(shape, np.stack([shape.edges[:, column], rows], axis=1), Attachment(), yaw, pitch)[:, 0]
        target = gaussian_filter1d(base, max(1e-6, params.contour_smoothing * shape.radius), mode="nearest") if params.contour_smoothing else base.copy()
        if params.jaw_round and shape.mouth_y is not None:
            target = _round_jaw(shape, rows, target, side, params, t)
        if side == np.sign(yaw):
            target += side * params.cheek_bulge * shape.radius * math.sin(t) * cheek
        else:
            target += side * half * (math.hypot(math.cos(t), params.near_fullness * math.sin(t)) - math.cos(t))
        sides.append((side, base, target))
    return rows, sides


def _outline_shift(shape: HeadShape, points: np.ndarray, yaw: float, pitch: float, params: HeadParams) -> np.ndarray:
    """頂点を、輪郭の目標の線へ寄せる横の量。目標 − 基 を、その側の輪郭の近くの頂点に、輪郭からの距離で弱めて足す（輪郭の外の耳や髪にも）。"""
    if yaw == 0 or shape.edges is None:
        return np.zeros(len(points))
    rows, sides = _outline_targets(shape, yaw, pitch, params)
    y = points[:, 1]
    left, right = np.interp(y, rows, shape.edges[:, 0]), np.interp(y, rows, shape.edges[:, 1])
    in_rows = (y >= rows[0]) & (y <= rows[-1])
    shift = np.zeros(len(points))
    for side, base, target in sides:
        delta = np.interp(y, rows, target - base)
        edge = right if side > 0 else left
        offset = side * (points[:, 0] - edge)
        weight = np.where(
            offset <= 0,
            np.exp(-((offset / (CONTOUR_REACH_INSIDE * shape.radius)) ** 2)),
            np.exp(-((offset / (CONTOUR_REACH_OUTSIDE * shape.radius)) ** 2)),
        )
        on_side = np.sign(points[:, 0] - (left + right) / 2) == side
        shift += delta * weight * on_side * in_rows
    return shift


def _attach_ear(shape: HeadShape, points: np.ndarray, x: np.ndarray, yaw: float, pitch: float, params: HeadParams) -> np.ndarray:
    """手前側（頭の横が回ってくる側）の耳の頂点の画面の x を、輪郭の目標の線から、正面での輪郭からの離れ方だけ離した位置にする。
    耳は頭の横に付いているので、回して頭の横が見えてきても、輪郭から離れて浮かない。"""
    if yaw == 0 or shape.edges is None:
        return x
    rows, sides = _outline_targets(shape, yaw, pitch, params)
    side, _, target = next(entry for entry in sides if entry[0] != np.sign(yaw))
    y = np.clip(points[:, 1], rows[0], rows[-1])
    edge = np.interp(y, rows, shape.edges[:, 0 if side < 0 else 1])
    mid = np.interp(y, rows, (shape.edges[:, 0] + shape.edges[:, 1]) / 2)
    near = np.sign(points[:, 0] - mid) == side
    return np.where(near, np.interp(y, rows, target) + (points[:, 0] - edge), x)


def _round_jaw(shape: HeadShape, rows: np.ndarray, target: np.ndarray, side: float, params: HeadParams, t: float) -> np.ndarray:
    """口の高さからいちばん下の行までの目標の線を、あごの角（口の行の目標の点）から、あご先の中心から chin_plate × 口の行の半幅だけ離れた底へ回り込む
    楕円の弧に、jaw_round の割合だけ近づける。"""
    below = rows >= shape.mouth_y
    if below.sum() < 3:
        return target
    start = int(np.argmax(below))
    corner = target[start]
    half = (shape.edges[start, 1] - shape.edges[start, 0]) / 2
    bottom = target[-1] + side * params.chin_plate * half * math.cos(t)
    u = (rows[below] - rows[start]) / max(1.0, rows[-1] - rows[start])
    arc = bottom + (corner - bottom) * np.sqrt(np.clip(1 - u ** 2, 0, 1))
    line = target[below]
    result = target.copy()
    result[below] = line + params.jaw_round * (arc - line)
    return result


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


def neck_follow(shape: HeadShape, mesh: Mesh, yaw: float, pitch: float, pivot: float, follow: float = NECK_FOLLOW) -> np.ndarray:
    """首の頂点の移動量。あごの高さより上は頭と一緒に回り、下端に向かって追従を弱める。
    上下はあご先と同じ奥行きで回し（上を向いても、あごの下から首の上端が見えない）、左右は頭の中心の面で回す（あご先と同じだけ横に動くと、首が大きくゆがむ）。"""
    rest = np.array(mesh.vertices, dtype=np.float64)
    chin = shape.top + len(shape.centers) - 1
    bottom = float(rest[:, 1].max())
    weight = follow * np.clip((bottom - rest[:, 1]) / max(1.0, bottom - chin), 0, 1)
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
    params: HeadParams = HeadParams(),
    silhouette: bool = True,
    neck: bool = True,
) -> list[KeyformGroup]:
    """頭のパーツ（と首）のメッシュごとに、AngleX × AngleY の格子の顔の向きのキーフォームグループを作る。三角形が裏返る角度では、移動量をならして解き直す。
    上下は params.pitch_scale 倍の角度で回す。"""
    model = package.model
    attachments = attachments if attachments is not None else attachments_for(model, shape.radius, params)
    parts = head_parts(model)
    pivot = params.pivot_depth * shape.radius
    pitch_scale = params.pitch_scale
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
                    offsets = neck_follow(shape, mesh, x, y * pitch_scale, pivot, params.neck_follow)
                else:
                    attachment = attachments[mesh.part]
                    moved = turn(shape, rest, attachment, x, y * pitch_scale, pivot, silhouette, params)
                    if attachment.planar:
                        moved = _soften(rest, moved, params.feature_foreshortening)
                    offsets = moved - rest
                    relax = RELAX and silhouette and attachment.kind == "surface" and not attachment.planar and (x, y) != (0, 0)
                    hold = _outline_hold(mesh, package.textures[mesh.texture]) if relax else 1.0
                    if relax:
                        # 1回だけならす（裏返りの解き直しのように滑らかさを強めると、輪郭を守る強さを上回り、輪郭が動く）。
                        offsets = solve_offsets(rest, mesh.triangles, rest, np.zeros(len(rest)), offsets, SMOOTHNESS, hold, keep_prior_shape=False)
                    if triangle_quality(rest, rest + offsets, mesh.triangles)["flipped"]:
                        hold = np.maximum(hold, _flip_hold(rest, rest + offsets, mesh.triangles, shape.radius))
                        offsets = solve_without_flips(rest, mesh.triangles, rest, np.zeros(len(rest)), offsets, hold, keep_prior_shape=True)
                keyforms.append(Keyform([x, y], [[round(float(dx), 3), round(float(dy), 3)] for dx, dy in offsets]))
        groups.append(KeyformGroup(f"{mesh.id}_angle", mesh.id, list(ANGLE_PARAMETERS), [list(xs), list(ys)], "linear", keyforms))
    return groups


def _flip_hold(rest: np.ndarray, moved: np.ndarray, triangles: list[list[int]], radius: float) -> np.ndarray:
    """裏返りを解き直すときの、頂点ごとの元の動きを守る強さ。裏返った三角形の近くは 1 で、離れるほど強くし、離れた所（反対側の輪郭など）の動きを変えない。"""
    tri = np.asarray(triangles, dtype=np.int64).reshape(-1, 3)
    a, b, c = (rest[tri[:, i]] for i in range(3))
    p, q, r = (moved[tri[:, i]] for i in range(3))
    cross = lambda u, v, w: (v[:, 0] - u[:, 0]) * (w[:, 1] - u[:, 1]) - (v[:, 1] - u[:, 1]) * (w[:, 0] - u[:, 0])  # noqa: E731
    flipped = np.unique(tri[np.sign(cross(a, b, c)) != np.sign(cross(p, q, r))])
    distance = np.sqrt(((rest[:, None, :] - rest[None, flipped, :]) ** 2).sum(axis=-1)).min(axis=1)
    return np.minimum(np.exp((distance / (FLIP_REACH * radius)) ** 2), FLIP_HOLD_FAR)


def _outline_hold(mesh: Mesh, texture: bytes) -> np.ndarray:
    """移動量をならすときの、頂点ごとの元の動きを守る強さ。描かれている部分の内側の頂点（まわりの三角形がすべて描かれ、メッシュの外周でない）を 1 にしてならし
    （ならさないと、粗いメッシュの三角形が大きく伸びる）、ほか（パーツの輪郭と、その外の透明な頂点）は FLIP_HOLD にして、輪郭の目標の線に合わせた形を保つ。"""
    triangles = np.asarray(mesh.triangles, dtype=np.int64).reshape(-1, 3)
    drawn = opaque_triangles(mesh, np.array(Image.open(io.BytesIO(texture)).convert("RGBA")))
    edges = np.sort(np.concatenate([triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]]]), axis=1)
    unique, counts = np.unique(edges, axis=0, return_counts=True)
    border = np.unique(unique[counts == 1].ravel())
    hold = np.full(len(mesh.vertices), FLIP_HOLD)
    inner = np.setdiff1d(triangles[drawn].ravel(), np.concatenate([triangles[~drawn].ravel(), border]))
    hold[inner] = 1.0
    return hold


def apply_keyforms(package: ModelPackage, groups: list[KeyformGroup]) -> ModelPackage:
    """顔の向きのキーフォームグループを同じ ID のものと置き換え、AngleX・AngleY の範囲を格子に合わせて広げる。手の修正のグループ（_angle_edit）は残す。"""
    model = package.model
    replaced = {g.id for g in groups}
    kept = [g for g in model.deformation.keyform_groups if g.id not in replaced]
    model.deformation.keyform_groups = kept + groups
    for parameter in model.parameters:
        for index, name in enumerate(ANGLE_PARAMETERS):
            if parameter.id == name and groups:
                keys = groups[0].keys[index]
                parameter.min, parameter.max = min(parameter.min, keys[0]), max(parameter.max, keys[-1])
    return package


def carry_edits(previous: Model, package: ModelPackage) -> list[str]:
    """前に作ったモデルの手の修正のグループ（Unity エディタで作る _angle_edit）を、作り直したモデルに引き継ぐ。
    メッシュの頂点か格子が変わったグループは引き継がず、その ID を返す。"""
    model = package.model
    meshes = {m.id: m for m in model.meshes}
    previous_meshes = {m.id: m for m in previous.meshes}
    grids = {g.mesh: g.keys for g in model.deformation.keyform_groups if g.parameters == ANGLE_PARAMETERS and not g.id.endswith(EDIT_SUFFIX)}
    grid = next(iter(grids.values()), None)
    dropped = []
    for group in previous.deformation.keyform_groups:
        if not group.id.endswith(EDIT_SUFFIX):
            continue
        mesh, before = meshes.get(group.mesh), previous_meshes.get(group.mesh)
        if mesh is None or before is None or mesh.vertices != before.vertices or group.keys != grids.get(group.mesh, grid):
            dropped.append(group.id)
            continue
        model.deformation.keyform_groups = [g for g in model.deformation.keyform_groups if g.id != group.id] + [group]
    return dropped
