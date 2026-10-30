"""検証ツールが仕様の各規則の違反を JSON パス付きで検出することのテスト。"""

from __future__ import annotations

from pathlib import Path

from conftest import SAMPLE_DIR

from ugokue_format.validate import Issue, validate_dir


def paths(issues: list[Issue]) -> set[str]:
    """問題の JSON パスの集合を返す。"""
    return {i.path for i in issues}


def test_sample_is_valid() -> None:
    assert validate_dir(SAMPLE_DIR) == []


def test_missing_required_field(mutate) -> None:
    issues = mutate(lambda d: d["parts"][2].pop("role"))
    assert any(i.path == "$.parts[2]" and "'role'" in i.message for i in issues)


def test_wrong_type(mutate) -> None:
    issues = mutate(lambda d: d["parts"][2].__setitem__("drawOrder", "20"))
    assert "$.parts[2].drawOrder" in paths(issues)


def test_unknown_field(mutate) -> None:
    issues = mutate(lambda d: d["meshes"][0].__setitem__("color", "red"))
    assert any(i.path == "$.meshes[0]" and "'color'" in i.message for i in issues)


def test_confidence_out_of_range(mutate) -> None:
    issues = mutate(lambda d: d["correspondence"]["landmarks"][0].__setitem__("confidence", 1.5))
    assert "$.correspondence.landmarks[0].confidence" in paths(issues)


def test_bad_version(mutate) -> None:
    issues = mutate(lambda d: d.__setitem__("version", "0.2.0"))
    assert any(i.path == "$.version" and "0.1.x" in i.message for i in issues)


def test_rule01_schema_id_format(mutate) -> None:
    issues = mutate(lambda d: d["parts"][2].__setitem__("id", "Face"))
    assert "$.parts[2].id" in paths(issues)


def test_rule02_wrong_format(mutate) -> None:
    issues = mutate(lambda d: d.__setitem__("format", "other-model"))
    assert any(i.path == "$.format" and "ugokue-model" in i.message for i in issues)


def test_rule03_duplicates(mutate) -> None:
    def change(d) -> None:
        d["meshes"][6]["id"] = "face"
        d["correspondence"]["landmarks"][1]["view"] = "front"
        d["correspondence"]["vertexMatches"][1]["vertex"] = 0
        d["views"][1]["overrides"][1]["part"] = "mouth"
        d["tracking"]["parameters"][1]["parameter"] = "AngleX"

    found = paths(mutate(change))
    assert {
        "$.meshes[6]",
        "$.correspondence.landmarks[1]",
        "$.correspondence.vertexMatches[1]",
        "$.views[1].overrides[1]",
        "$.tracking.parameters[1]",
    } <= found


def test_rule04_missing_reference(mutate) -> None:
    def change(d) -> None:
        d["parts"][2]["parent"] = "nothing"
        d["deformation"]["keyformGroups"][0]["parameters"][1] = "AngleW"
        d["physics"]["pendulums"][0]["output"] = "HairBack"

    found = paths(mutate(change))
    assert {
        "$.parts[2].parent",
        "$.deformation.keyformGroups[0].parameters[1]",
        "$.physics.pendulums[0].output",
    } <= found


def test_rule05_parent_cycle(mutate) -> None:
    issues = mutate(lambda d: d["parts"][0].__setitem__("parent", "face"))
    assert any(i.path == "$.parts[0].parent" and "循環" in i.message for i in issues)


def test_rule05_clip_self(mutate) -> None:
    issues = mutate(lambda d: d["parts"][4].__setitem__("clipBy", ["iris_l"]))
    assert "$.parts[4].clipBy[0]" in paths(issues)


def test_rule06_mesh_of_other_part(mutate) -> None:
    def change(d) -> None:
        d["parts"][2]["mesh"] = "mouth"
        d["views"][1]["overrides"][0]["mesh"] = "face"

    found = paths(mutate(change))
    assert {"$.parts[2].mesh", "$.views[1].overrides[0].mesh"} <= found


def test_rule07_parameter_range(mutate) -> None:
    def change(d) -> None:
        d["parameters"][0]["min"] = 30
        d["parameters"][3]["default"] = 1.5

    found = paths(mutate(change))
    assert {"$.parameters[0].max", "$.parameters[3].default"} <= found


def test_rule08_mesh_shape(mutate) -> None:
    def change(d) -> None:
        d["meshes"][0]["uvs"].pop()
        d["meshes"][1]["triangles"][1][2] = 4

    found = paths(mutate(change))
    assert {"$.meshes[0].uvs", "$.meshes[1].triangles[1][2]"} <= found


def test_rule09_texture_missing(sample_copy: Path) -> None:
    (sample_copy / "textures" / "face.png").unlink()
    assert "$.meshes[1].texture" in paths(validate_dir(sample_copy))


def test_rule09_texture_not_png(sample_copy: Path) -> None:
    (sample_copy / "textures" / "face.png").write_bytes(b"GIF89a")
    assert "$.meshes[1].texture" in paths(validate_dir(sample_copy))


def test_rule09_texture_outside(mutate) -> None:
    issues = mutate(lambda d: d["meshes"][1].__setitem__("texture", "../face.png"))
    assert any(i.path == "$.meshes[1].texture" and "外" in i.message for i in issues)


def test_rule10_base_view_count(mutate) -> None:
    issues = mutate(lambda d: d["views"][1].__setitem__("yaw", 0))
    assert any(i.path == "$.views" and "基準のビュー" in i.message for i in issues)


def test_rule10_base_view_overrides(mutate) -> None:
    issues = mutate(lambda d: d["views"][0]["overrides"].append({"part": "face", "visible": False}))
    assert "$.views[0].overrides" in paths(issues)


def test_rule11_keys_count(mutate) -> None:
    issues = mutate(lambda d: d["deformation"]["keyformGroups"][0]["keys"].pop())
    assert "$.deformation.keyformGroups[0].keys" in paths(issues)


def test_rule11_keys_not_ascending(mutate) -> None:
    issues = mutate(lambda d: d["deformation"]["keyformGroups"][0]["keys"].__setitem__(0, [-30, 30, 0]))
    assert "$.deformation.keyformGroups[0].keys[0]" in paths(issues)


def test_rule11_keys_out_of_range(mutate) -> None:
    issues = mutate(lambda d: d["deformation"]["keyformGroups"][1]["keys"][0].__setitem__(1, 2))
    assert "$.deformation.keyformGroups[1].keys[0][1]" in paths(issues)


def test_rule11_missing_keyform(mutate) -> None:
    issues = mutate(lambda d: d["deformation"]["keyformGroups"][0]["keyforms"].pop(4))
    assert any(i.path == "$.deformation.keyformGroups[0].keyforms" and "[0, 0]" in i.message for i in issues)


def test_rule11_duplicate_keyform(mutate) -> None:
    issues = mutate(lambda d: d["deformation"]["keyformGroups"][0]["keyforms"][3].__setitem__("at", [0, 0]))
    found = paths(issues)
    assert {"$.deformation.keyformGroups[0].keyforms[4].at", "$.deformation.keyformGroups[0].keyforms"} <= found


def test_rule11_at_off_grid(mutate) -> None:
    issues = mutate(lambda d: d["deformation"]["keyformGroups"][0]["keyforms"][3].__setitem__("at", [0, 15]))
    assert "$.deformation.keyformGroups[0].keyforms[3].at" in paths(issues)


def test_rule11_offsets_count(mutate) -> None:
    issues = mutate(lambda d: d["deformation"]["keyformGroups"][0]["keyforms"][3]["offsets"].pop())
    assert "$.deformation.keyformGroups[0].keyforms[3].offsets" in paths(issues)


def test_rule12_vertex_match_index(mutate) -> None:
    issues = mutate(lambda d: d["correspondence"]["vertexMatches"][0].__setitem__("vertex", 4))
    assert "$.correspondence.vertexMatches[0].vertex" in paths(issues)


def test_rule13_procedural_missing_amount(mutate) -> None:
    def change(d) -> None:
        d["deformation"]["procedural"][0].pop("degreesPerUnit")
        d["deformation"]["procedural"][1].pop("offsetPerUnit")

    issues = mutate(change)
    assert any(i.path == "$.deformation.procedural[0]" and "degreesPerUnit が必要" in i.message for i in issues)
    assert any(i.path == "$.deformation.procedural[1]" and "offsetPerUnit が必要" in i.message for i in issues)


def test_rule14_pendulum_output_in_inputs(mutate) -> None:
    issues = mutate(lambda d: d["physics"]["pendulums"][0]["inputs"].append({"parameter": "HairFront", "weight": 1}))
    assert "$.physics.pendulums[0].output" in paths(issues)


def test_malformed_document_reports_without_crash(mutate) -> None:
    def change(d) -> None:
        d["parts"] = {"not": "a list"}
        d["meshes"][0]["vertices"] = "abc"
        d["deformation"]["keyformGroups"][0]["keys"] = [[0, "x"], 5]

    assert mutate(change)
