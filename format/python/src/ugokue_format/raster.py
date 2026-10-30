"""テスト素材を描くための、依存ライブラリのない単純なラスタライザと PNG の書き出し。"""

from __future__ import annotations

import math
import struct
import zlib
from collections.abc import Callable
from dataclasses import dataclass

Color = tuple[int, int, int, int]
Vec = tuple[float, float]
Spans = Callable[[float], list[tuple[float, float]]]

SUPERSAMPLE = 4


def encode_png(width: int, height: int, rgba: bytes) -> bytes:
    """行ごとにフィルタなしで並べた RGBA のバイト列を PNG にする。"""

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    stride = width * 4
    raw = b"".join(b"\x00" + rgba[y * stride : (y + 1) * stride] for y in range(height))
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    )


@dataclass(frozen=True)
class Shape:
    """y ごとに内側の x の区間を返す図形と、その y の範囲。"""

    spans: Spans
    top: float
    bottom: float


def ellipse(cx: float, cy: float, rx: float, ry: float) -> Shape:
    """楕円を作る。"""

    def spans(y: float) -> list[tuple[float, float]]:
        t = 1 - ((y - cy) / ry) ** 2
        if t <= 0:
            return []
        half = rx * math.sqrt(t)
        return [(cx - half, cx + half)]

    return Shape(spans, cy - ry, cy + ry)


def polygon(points: list[Vec]) -> Shape:
    """多角形（偶奇規則）を作る。"""
    edges = [(points[i - 1], points[i]) for i in range(len(points))]

    def spans(y: float) -> list[tuple[float, float]]:
        xs = sorted(
            (x1 - x0) * (y - y0) / (y1 - y0) + x0 for (x0, y0), (x1, y1) in edges if (y0 > y) != (y1 > y)
        )
        return [(xs[i], xs[i + 1]) for i in range(0, len(xs) - 1, 2)]

    ys = [p[1] for p in points]
    return Shape(spans, min(ys), max(ys))


def quadratic(p0: Vec, p1: Vec, p2: Vec, steps: int = 16) -> list[Vec]:
    """2次ベジェ曲線を、始点と終点を含む折れ線の点列にする。"""
    points = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        points.append((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0], u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]))
    return points


def band(points: list[Vec], above: float, below: float) -> list[Vec]:
    """折れ線の上下に幅を持たせた多角形の輪郭を返す。幅は y 方向にだけ付ける。"""
    return [(x, y - above) for x, y in points] + [(x, y + below) for x, y in reversed(points)]


class Layer:
    """キャンバス上の長方形の範囲を覆う RGBA の画像。"""

    def __init__(self, left: int, top: int, width: int, height: int) -> None:
        self.left = left
        self.top = top
        self.width = width
        self.height = height
        self.pixels = bytearray(width * height * 4)

    @property
    def right(self) -> int:
        """右端のキャンバス座標。"""
        return self.left + self.width

    @property
    def bottom(self) -> int:
        """下端のキャンバス座標。"""
        return self.top + self.height

    def fill(self, shape: Shape, color: Color) -> None:
        """キャンバス座標で表した図形を、アンチエイリアス付きで上に重ねて塗る。"""
        n = SUPERSAMPLE
        first = max(0, math.floor(shape.top) - self.top)
        last = min(self.height, math.ceil(shape.bottom) - self.top + 1)
        for j in range(first, last):
            coverage = [0] * self.width
            for sy in range(n):
                y = self.top + j + (sy + 0.5) / n
                for xa, xb in shape.spans(y):
                    lo = max(0, math.ceil((xa - self.left) * n - 0.5))
                    hi = min(self.width * n, math.ceil((xb - self.left) * n - 0.5))
                    for s in range(lo, hi):
                        coverage[s // n] += 1
            for i, covered in enumerate(coverage):
                if covered:
                    self._blend(i, j, color, covered * color[3] // (n * n))

    def _blend(self, i: int, j: int, color: Color, alpha: int) -> None:
        """1画素に色を重ねる（ストレートアルファの source-over）。"""
        k = (j * self.width + i) * 4
        da = self.pixels[k + 3]
        under = da * (255 - alpha) // 255
        out_a = alpha + under
        if out_a == 0:
            return
        for c in range(3):
            self.pixels[k + c] = (color[c] * alpha + self.pixels[k + c] * under) // out_a
        self.pixels[k + 3] = out_a

    def png(self) -> bytes:
        """PNG のバイト列にする。"""
        return encode_png(self.width, self.height, bytes(self.pixels))
