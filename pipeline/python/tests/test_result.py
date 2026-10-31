"""分解結果のフォルダの読み書きと、See-through の出力からの変換のテスト。"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from conftest import HEIGHT, LAYERS, WIDTH, make_layers, solid
from PIL import Image

from ugokue_pipeline.result import from_see_through, load_result


def test_write_then_load_keeps_layers(result_dir: Path) -> None:
    result = load_result(result_dir)

    assert (result.width, result.height) == (WIDTH, HEIGHT)
    assert [x.tag for x in result.layers] == [t for t, *_ in LAYERS]
    original = {x.tag: x for x in make_layers()}
    for layer in result.layers:
        assert layer.box == original[layer.tag].box
        assert layer.depth_median == original[layer.tag].depth_median
        assert np.array_equal(layer.image, original[layer.tag].image)
    assert result.source is not None and result.source.shape == (HEIGHT, WIDTH, 3)
    assert result.run == {"backend": "test", "seed": 42}


def test_layer_files_use_safe_names(result_dir: Path) -> None:
    index = json.loads((result_dir / "layers.json").read_text(encoding="utf-8"))

    files = {x["tag"]: x["file"] for x in index["layers"]}
    assert files["front hair"] == "layers/front_hair.png"
    assert files["irides-r"] == "layers/irides-r.png"


def test_reads_see_through_output(tmp_path: Path) -> None:
    optimized = tmp_path / "optimized"
    optimized.mkdir()
    parts = {}
    for tag, box, depth in [("hairf", (0, 0, 10, 4), 0.3), ("eyebg", (2, 2, 6, 4), 0.6), ("face", (0, 0, 10, 8), 0.7)]:
        Image.fromarray(solid(box, (10, 20, 30))).save(optimized / f"{tag}.png")
        Image.fromarray(np.full((box[3] - box[1], box[2] - box[0]), 128, dtype=np.uint8)).save(optimized / f"{tag}_depth.png")
        parts[tag] = {"xyxy": list(box), "depth_median": depth, "tag": tag}
    (optimized / "info.json").write_text(json.dumps({"parts": parts, "frame_size": [8, 10]}), encoding="utf-8")
    source = tmp_path / "source.png"
    Image.fromarray(np.zeros((8, 10, 3), dtype=np.uint8)).save(source)

    out = tmp_path / "result"
    from_see_through(optimized, source, out, {"backend": "modal"})
    result = load_result(out)

    assert (result.width, result.height) == (10, 8)
    assert [x.tag for x in result.layers] == ["front hair", "eyewhite", "face"]
    assert result.layers[1].box == (2, 2, 6, 4)
    assert (out / "layers" / "front_hair_depth.png").exists()


def test_maps_square_frame_back_to_source(tmp_path: Path) -> None:
    # 幅 6・高さ 8 のイラストは、左右に 1 ずつ余白を足した 8×8 にしてから 16×16 に拡大される。
    optimized = tmp_path / "optimized"
    optimized.mkdir()
    parts = {}
    for tag, box in [("face", (4, 0, 12, 16)), ("hairb", (0, 0, 4, 16)), ("objects", (0, 0, 2, 16))]:
        Image.fromarray(solid(box, (10, 20, 30))).save(optimized / f"{tag}.png")
        Image.fromarray(np.full((box[3] - box[1], box[2] - box[0]), 128, dtype=np.uint8)).save(optimized / f"{tag}_depth.png")
        parts[tag] = {"xyxy": list(box), "depth_median": 0.5, "tag": tag}
    (optimized / "info.json").write_text(json.dumps({"parts": parts, "frame_size": [16, 16]}), encoding="utf-8")
    source = tmp_path / "source.png"
    Image.fromarray(np.zeros((8, 6, 3), dtype=np.uint8)).save(source)

    out = tmp_path / "result"
    from_see_through(optimized, source, out, {"backend": "modal"})
    result = load_result(out)

    assert (result.width, result.height) == (6, 8)
    layers = {x.tag: x for x in result.layers}
    assert set(layers) == {"face", "back hair"}
    assert layers["face"].box == (1, 0, 5, 8)
    assert layers["face"].image.shape == (8, 4, 4)
    assert layers["face"].depth is not None and layers["face"].depth.shape == (8, 4)
    assert layers["back hair"].box == (0, 0, 1, 8)
    assert layers["back hair"].image.shape == (8, 1, 4)
