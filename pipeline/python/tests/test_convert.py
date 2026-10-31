"""分解結果から中間形式のモデルへの変換のテスト。"""

from __future__ import annotations

from pathlib import Path

from conftest import HEIGHT, WIDTH

from ugokue_format.validate import validate_dir
from ugokue_pipeline.convert import convert, part_rule
from ugokue_pipeline.result import load_result, write_result


def _parts(model_dir: Path) -> dict:
    from ugokue_format.package import load

    return {p.id: p for p in load(model_dir).model.parts}


def test_converted_model_passes_validation(result_dir: Path, tmp_path: Path) -> None:
    out = tmp_path / "model"
    convert(result_dir, out)

    assert validate_dir(out) == []


def test_maps_tags_to_parts_with_roles_and_parents(result_dir: Path, tmp_path: Path) -> None:
    out = tmp_path / "model"
    convert(result_dir, out)
    parts = _parts(out)

    assert parts["hair_front"].role == "hair_front"
    assert parts["hair_back"].parent == "head"
    assert parts["eye_white_r"].parent == "face"
    assert parts["iris_l"].role == "iris"
    assert parts["ear_r"].role == "ear"
    assert parts["head"].mesh is None and parts["head"].parent == "body"
    assert parts["body"].mesh is None and parts["body"].parent is None
    assert "head_layer" not in parts and all(p.role != "head" or p.mesh is None for p in parts.values())


def test_irises_are_clipped_by_their_eye_whites(result_dir: Path, tmp_path: Path) -> None:
    out = tmp_path / "model"
    convert(result_dir, out)
    parts = _parts(out)

    assert parts["iris_r"].clip_by == ["eye_white_r"]
    assert parts["iris_l"].clip_by == ["eye_white_l"]
    assert parts["face"].clip_by == []


def test_draw_order_follows_depth_from_far_to_near(result_dir: Path, tmp_path: Path) -> None:
    out = tmp_path / "model"
    convert(result_dir, out)
    parts = _parts(out)

    order = sorted((p for p in parts.values() if p.mesh is not None), key=lambda p: p.draw_order)
    assert [p.id for p in order] == [
        "hair_back", "ear_r", "face", "eye_white_l", "eye_white_r", "mouth", "iris_l", "iris_r", "hair_front"
    ]


def test_front_hair_is_never_drawn_behind_the_face(result_dir: Path, tmp_path: Path) -> None:
    # 横や後ろに垂れた髪を含む前髪の層は、層全体の深度の中央値が顔より奥になることがある。
    result = load_result(result_dir)
    for layer in result.layers:
        if layer.tag == "front hair":
            layer.depth_median = 0.8
    changed = tmp_path / "changed"
    write_result(changed, result.width, result.height, result.layers, result.source, result.run)
    out = tmp_path / "model"
    convert(changed, out)
    parts = _parts(out)

    face_parts = [p for p in parts.values() if p.mesh is not None and (p.id == "face" or p.parent == "face")]
    assert all(parts["hair_front"].draw_order > p.draw_order for p in face_parts)
    assert parts["hair_back"].draw_order < parts["face"].draw_order


def test_each_part_has_a_quad_mesh_over_its_box(result_dir: Path, tmp_path: Path) -> None:
    from ugokue_format.package import load

    out = tmp_path / "model"
    convert(result_dir, out)
    package = load(out)
    meshes = {m.id: m for m in package.model.meshes}

    face = meshes["face"]
    assert face.part == "face"
    assert face.vertices == [[14, 8], [50, 8], [50, 44], [14, 44]]
    assert face.uvs == [[0, 0], [1, 0], [1, 1], [0, 1]]
    assert face.triangles == [[0, 1, 2], [0, 2, 3]]
    assert package.model.canvas.width == WIDTH and package.model.canvas.height == HEIGHT
    assert face.texture in package.textures


def test_unknown_tags_become_accessories() -> None:
    assert part_rule("objects") == ("objects", "accessory", "body")
    assert part_rule("magic wand-l") == ("magic_wand_l", "accessory", "body")
    assert part_rule("head") is None


def test_result_without_layers_still_has_base_view(tmp_path: Path) -> None:
    from ugokue_pipeline.result import write_result

    empty = tmp_path / "empty"
    write_result(empty, 8, 8, [], None, {})
    out = tmp_path / "model"
    convert(empty, out)

    assert validate_dir(out) == []
    assert load_result(empty).layers == []
