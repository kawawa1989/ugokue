"""Modal の GPU で、テストイラストの生成、See-through によるパーツ分解、Qwen-Image-Edit による向きと表情の差分の生成を実行する。

実行例（リポジトリのルートで）：
    uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::generate_illustrations
    uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::decompose_images
    uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::generate_views
    uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::review_keyforms
"""

from __future__ import annotations

import hashlib
import io
import json
import time
import zipfile
from datetime import datetime, timezone
from pathlib import Path

import modal

SEE_THROUGH_REPOSITORY = "https://github.com/shitagaki-lab/see-through.git"
SEE_THROUGH_COMMIT = "df019de5129d6c4b406587a14c3501669441a783"
SEE_THROUGH_DIR = "/opt/see-through"
LAYERDIFF_REPO = "layerdifforg/seethroughv0.0.2_layerdiff3d"
DEPTH_REPO = "24yearsold/seethroughv0.0.1_marigold"
GPU = "L4"
EDIT_GPU = "A100-80GB"
# 頭の範囲のマスクを広げる量（イラストの短い辺に対する割合）と、ぼかす量（画素）。
HEAD_MARGIN = 0.02
HEAD_FEATHER = 4.0

image = (
    modal.Image.from_registry("nvidia/cuda:12.8.1-cudnn-devel-ubuntu24.04", add_python="3.12")
    .apt_install("git", "libgl1", "libglib2.0-0")
    .run_commands(
        f"git clone {SEE_THROUGH_REPOSITORY} {SEE_THROUGH_DIR}",
        f"cd {SEE_THROUGH_DIR} && git checkout {SEE_THROUGH_COMMIT} && ln -s common/assets assets",
    )
    .pip_install(
        "torch==2.8.0", "torchvision==0.23.0", "torchaudio==2.8.0", index_url="https://download.pytorch.org/whl/cu128"
    )
    .run_commands(f"cd {SEE_THROUGH_DIR} && pip install -r requirements.txt")
    .env({"HF_HOME": "/cache/huggingface"})
    .add_local_python_source("ugokue_pipeline")
)

edit_image = (
    modal.Image.debian_slim(python_version="3.12")
    .pip_install("torch==2.8.0", "torchvision==0.23.0", index_url="https://download.pytorch.org/whl/cu128")
    .pip_install("diffusers==0.42.1", "transformers==5.22.1", "accelerate==1.15.0", "numpy", "pillow")
    .env({"HF_HOME": "/cache/huggingface"})
    .add_local_python_source("ugokue_pipeline")
)

# anime-face-detector は mmcv-full 1.x 系に依存する。Python 3.11 で入る mmcv-full 1.7.2 を使い、mmpose の版の上限の確認だけを緩める。
landmark_image = (
    modal.Image.debian_slim(python_version="3.11")
    .apt_install("libgl1", "libglib2.0-0")
    .pip_install("torch==2.0.1", "torchvision==0.15.2", extra_index_url="https://download.pytorch.org/whl/cpu")
    .pip_install("mmcv-full==1.7.2", find_links="https://download.openmmlab.com/mmcv/dist/cpu/torch2.0/index.html")
    .pip_install("numpy<1.24", "scipy<1.12", "setuptools<70", "wheel")
    .pip_install("chumpy==0.70", extra_options="--no-build-isolation")
    .pip_install("mmdet==2.28.2", "mmpose==0.29.0", "anime-face-detector==0.0.9", "numpy<1.24", "opencv-python-headless<4.9")
    .run_commands(
        "python -c \"import pathlib, importlib.util; p = pathlib.Path(importlib.util.find_spec('mmpose').origin); "
        "p.write_text(p.read_text().replace('1.7.0', '1.7.2'))\"",
        "rm -rf /usr/local/lib/python3.11/site-packages/mmpose/__pycache__",
    )
)

cache = modal.Volume.from_name("ugokue-model-cache", create_if_missing=True)
app = modal.App("ugokue-pipeline", image=image)


def _model_sha(repo: str) -> str | None:
    from huggingface_hub import HfApi

    try:
        return HfApi().model_info(repo).sha
    except Exception:
        return None


def _zip_directory(directory: Path) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(directory.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(directory).as_posix())
    return buffer.getvalue()


@app.function(gpu=GPU, volumes={"/cache": cache}, timeout=1800)
def generate(spec: dict) -> dict[str, bytes]:
    """Animagine XL 4.0 で、指定のプロンプトとシードのイラストを生成し、名前と PNG のバイト列を返す。"""
    import torch
    from diffusers import EulerAncestralDiscreteScheduler, StableDiffusionXLPipeline

    pipe = StableDiffusionXLPipeline.from_pretrained(spec["model"], torch_dtype=torch.float16, use_safetensors=True)
    pipe.scheduler = EulerAncestralDiscreteScheduler.from_config(pipe.scheduler.config)
    pipe.to("cuda")
    cache.commit()
    images = {}
    for item in spec["illustrations"]:
        generator = torch.Generator(device="cuda").manual_seed(item["seed"])
        result = pipe(
            prompt=f"{item['prompt']}, {spec['suffix']}",
            negative_prompt=spec["negative"],
            width=spec["width"],
            height=spec["height"],
            num_inference_steps=spec["steps"],
            guidance_scale=spec["guidance"],
            generator=generator,
        ).images[0]
        buffer = io.BytesIO()
        result.save(buffer, format="PNG")
        images[item["name"]] = buffer.getvalue()
    images["_model_sha"] = (_model_sha(spec["model"]) or "").encode()
    return images


@app.function(gpu=GPU, volumes={"/cache": cache}, timeout=1800)
def decompose(
    png: bytes,
    name: str,
    seed: int = 42,
    resolution: int = 1280,
    vram_limit_gib: float | None = None,
    group_offload: bool = False,
) -> bytes:
    """See-through でイラスト1枚を層に分け、分解結果のフォルダを zip にして返す。vram_limit_gib で使える VRAM を絞り、group_offload で重みを少しずつ GPU に載せる。"""
    import os
    import tempfile

    import torch

    os.chdir(SEE_THROUGH_DIR)
    from utils.inference_utils import apply_layerdiff, apply_marigold, further_extr
    from utils.torch_utils import seed_everything

    from ugokue_pipeline.result import from_see_through

    work = Path(tempfile.mkdtemp())
    source = work / f"{name}.png"
    source.write_bytes(png)
    save_dir = work / "see-through"
    if vram_limit_gib is not None:
        total = torch.cuda.get_device_properties(0).total_memory
        torch.cuda.set_per_process_memory_fraction(min(vram_limit_gib * 2**30 / total, 1.0))
    torch.cuda.reset_peak_memory_stats()

    started = time.perf_counter()
    seed_everything(seed)
    apply_layerdiff(str(source), LAYERDIFF_REPO, save_dir=str(save_dir), seed=seed, resolution=resolution, num_inference_steps=30, group_offload=group_offload)
    layerdiff_done = time.perf_counter()
    apply_marigold(str(source), DEPTH_REPO, save_dir=str(save_dir), seed=seed, resolution=768, num_inference_steps=-1, group_offload=group_offload)
    depth_done = time.perf_counter()
    further_extr(str(save_dir / name), rotate=False, save_to_psd=False, tblr_split=True)
    finished = time.perf_counter()
    cache.commit()

    run = {
        "backend": "modal",
        "gpu": torch.cuda.get_device_name(0),
        "seeThrough": {
            "repository": SEE_THROUGH_REPOSITORY,
            "commit": SEE_THROUGH_COMMIT,
            "layerdiff": {"repo": LAYERDIFF_REPO, "sha": _model_sha(LAYERDIFF_REPO)},
            "depth": {"repo": DEPTH_REPO, "sha": _model_sha(DEPTH_REPO)},
            "resolution": resolution,
            "resolutionDepth": 768,
            "steps": 30,
            "tblrSplit": True,
            "groupOffload": group_offload,
        },
        "seed": seed,
        "sourceSha256": hashlib.sha256(png).hexdigest(),
        "seconds": {
            "layerdiff": round(layerdiff_done - started, 2),
            "depth": round(depth_done - layerdiff_done, 2),
            "postprocess": round(finished - depth_done, 2),
            "total": round(finished - started, 2),
        },
        "vramLimitGiB": vram_limit_gib,
        "peakVramAllocatedGiB": round(torch.cuda.max_memory_allocated() / 2**30, 2),
        "peakVramReservedGiB": round(torch.cuda.max_memory_reserved() / 2**30, 2),
        "createdAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }
    out = work / "result"
    from_see_through(save_dir / name / "optimized", source, out, run)
    return _zip_directory(out)


@app.function(image=edit_image, gpu=EDIT_GPU, volumes={"/cache": cache}, timeout=3600)
def edit_views(png: bytes, spec: dict, names: list[str] | None = None, crop: list[int] | None = None) -> dict:
    """Qwen-Image-Edit で、イラスト1枚から向きと表情の絵を生成する。names を渡すとその絵だけを、crop を渡すとその範囲だけを切り出して編集する。"""
    import torch
    from diffusers import QwenImageEditPlusPipeline
    from PIL import Image

    from ugokue_pipeline.view_spec import parse_spec

    parsed = parse_spec(spec)
    views = [v for v in parsed.views if names is None or v.name in names]
    source = Image.open(io.BytesIO(png)).convert("RGB")
    if crop is not None:
        source = source.crop(tuple(crop))
    torch.cuda.reset_peak_memory_stats()
    started = time.perf_counter()
    pipeline = QwenImageEditPlusPipeline.from_pretrained(parsed.model, torch_dtype=torch.bfloat16).to("cuda")
    pipeline.set_progress_bar_config(disable=True)
    cache.commit()
    loaded = time.perf_counter()

    images: dict[str, bytes] = {}
    seconds: dict[str, float] = {}
    output_size = None
    for view in views:
        began = time.perf_counter()
        with torch.inference_mode():
            result = pipeline(
                image=[source],
                prompt=view.prompt,
                negative_prompt=parsed.negative_prompt,
                true_cfg_scale=parsed.true_cfg_scale,
                guidance_scale=1.0,
                num_inference_steps=parsed.steps,
                generator=torch.Generator(device="cuda").manual_seed(parsed.seed),
            ).images[0]
        output_size = list(result.size)
        if result.size != source.size:
            result = result.resize(source.size, Image.Resampling.LANCZOS)
        buffer = io.BytesIO()
        result.save(buffer, format="PNG")
        images[view.name] = buffer.getvalue()
        seconds[view.name] = round(time.perf_counter() - began, 2)

    run = {
        "backend": "modal",
        "gpu": torch.cuda.get_device_name(0),
        "model": {"repo": parsed.model, "sha": _model_sha(parsed.model)},
        "dtype": "bfloat16",
        "steps": parsed.steps,
        "trueCfgScale": parsed.true_cfg_scale,
        "negativePrompt": parsed.negative_prompt,
        "seed": parsed.seed,
        "prompts": {v.name: v.prompt for v in views},
        "sourceSha256": hashlib.sha256(png).hexdigest(),
        "crop": crop,
        "outputSize": output_size,
        "seconds": {"load": round(loaded - started, 2), "views": seconds},
        "peakVramAllocatedGiB": round(torch.cuda.max_memory_allocated() / 2**30, 2),
        "peakVramReservedGiB": round(torch.cuda.max_memory_reserved() / 2**30, 2),
        "createdAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }
    return {"images": images, "run": run}


@app.function(image=landmark_image, volumes={"/cache": cache}, timeout=1800)
def detect_landmarks(pngs: dict[str, bytes]) -> dict[str, list]:
    """anime-face-detector で、各画像の顔の枠と28点の特徴点を検出する（CPU）。"""
    import os

    import cv2
    import numpy as np

    os.environ.setdefault("TORCH_HOME", "/cache/torch")
    from anime_face_detector import create_detector

    detector = create_detector("yolov3", device="cpu")
    faces = {}
    for name, png in pngs.items():
        image = cv2.imdecode(np.frombuffer(png, np.uint8), cv2.IMREAD_COLOR)
        faces[name] = [{"bbox": f["bbox"].tolist(), "keypoints": f["keypoints"].tolist()} for f in detector(image)]
    return faces


def _unzip(data: bytes, directory: Path) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        archive.extractall(directory)


@app.local_entrypoint()
def generate_illustrations(spec: str = "pipeline/samples/illustrations.json", out: str = "pipeline/samples/illustrations") -> None:
    """テストイラストを生成し、PNG と生成に使ったモデルの版を書き出す。"""
    data = json.loads(Path(spec).read_text(encoding="utf-8"))
    images = generate.remote(data)
    directory = Path(out)
    directory.mkdir(parents=True, exist_ok=True)
    model_sha = images.pop("_model_sha").decode()
    for name, png in images.items():
        (directory / f"{name}.png").write_bytes(png)
        print(f"wrote {directory / name}.png")
    (directory / "generated.json").write_text(
        json.dumps({"model": data["model"], "modelSha": model_sha or None}, indent=2) + "\n", encoding="utf-8"
    )


@app.local_entrypoint()
def decompose_images(images: str = "pipeline/samples/illustrations", out: str = "artifacts/decomposition", seed: int = 42) -> None:
    """フォルダ内のイラストを分解し、変換と、重ねた画像の比較までを行って、結果をまとめる。"""
    from ugokue_pipeline.report import write_report

    sources = sorted(Path(images).glob("*.png"))
    calls = [(path, decompose.spawn(path.read_bytes(), path.stem, seed)) for path in sources]
    for path, call in calls:
        _unzip(call.get(), Path(out) / path.stem / "result")
        print(f"decomposed {path.name}")
    write_report(Path(out), [Path(out) / p.stem for p in sources])


@app.local_entrypoint()
def check_determinism(
    image: str = "pipeline/samples/illustrations/01-long-straight.png",
    out: str = "artifacts/determinism",
    seed: int = 42,
    vram_limit_gib: float = 15.0,
    group_offload: bool = True,
) -> None:
    """同じイラストを同じシードで、VRAM 16GB の GPU 相当に絞って2回分解し、層の画素がどれだけ違うかを表示する。"""
    from ugokue_pipeline.report import compare_results

    path = Path(image)
    options = {"vram_limit_gib": vram_limit_gib, "group_offload": group_offload}
    first = decompose.spawn(path.read_bytes(), path.stem, seed, **options)
    second = decompose.spawn(path.read_bytes(), path.stem, seed, **options)
    _unzip(first.get(), Path(out) / "first")
    _unzip(second.get(), Path(out) / "second")
    summary = compare_results(Path(out) / "first", Path(out) / "second")
    (Path(out) / "determinism.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=2))


def _save_generated(generated: dict, directory: Path) -> None:
    """生成した絵と記録を、判定の前に保存する（判定で失敗しても生成し直さずに済むように）。"""
    directory.mkdir(parents=True, exist_ok=True)
    for name, png in generated["images"].items():
        (directory / f"{name}.png").write_bytes(png)
    (directory / "run.json").write_text(json.dumps(generated["run"], ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def _load_generated(directory: Path) -> dict:
    run = json.loads((directory / "run.json").read_text(encoding="utf-8"))
    images = {name: (directory / f"{name}.png").read_bytes() for name in run["prompts"]}
    return {"images": images, "run": run}


def _finish_views(source_path: Path, result_dir: Path, spec: dict, generated: dict, out_dir: Path) -> dict:
    """生成した絵の頭だけを元のイラストに戻し、特徴点を検出して、結果のフォルダを書き出す。"""
    import numpy as np
    from PIL import Image

    from ugokue_pipeline.head import head_mask, paste_head
    from ugokue_pipeline.result import load_result
    from ugokue_pipeline.views import FRONT, contact_sheet, parse_spec, write_views

    source = np.array(Image.open(source_path).convert("RGB"))
    result = load_result(result_dir)
    mask = head_mask(result, margin=round(HEAD_MARGIN * min(source.shape[:2])), feather=HEAD_FEATHER)
    raw = {}
    x1, y1, x2, y2 = generated["run"]["crop"] or (0, 0, source.shape[1], source.shape[0])
    for view, png in generated["images"].items():
        full = source.copy()
        full[y1:y2, x1:x2] = np.array(Image.open(io.BytesIO(png)).convert("RGB"))
        raw[view] = full
    pasted = {FRONT: source_path.read_bytes()}
    for view, image in raw.items():
        buffer = io.BytesIO()
        Image.fromarray(paste_head(source, image, mask)).save(buffer, format="PNG")
        pasted[view] = buffer.getvalue()
    landmarks = detect_landmarks.remote(pasted)
    views = parse_spec(spec).views
    angles = {v.name: (v.angle_x, v.angle_y) for v in views if v.expression is None}
    run = {**generated["run"], "headMask": {"margin": HEAD_MARGIN, "feather": HEAD_FEATHER, "decomposition": str(result_dir)}}
    summary = write_views(out_dir, source, raw, mask, landmarks, run, angles)
    contact_sheet(out_dir, [v.name for v in views if v.expression is not None])
    print(f"wrote {out_dir}")
    return summary


@app.local_entrypoint()
def generate_views(
    images: str = "pipeline/samples/illustrations",
    decomposition: str = "artifacts/decomposition",
    out: str = "artifacts/views",
    spec: str = "pipeline/samples/views.json",
    only: str = "",
    reuse: bool = False,
) -> None:
    """各イラストから向きと表情の絵を生成し、頭だけを戻して、頭以外の変化と向きの判定をまとめる。分解結果（decompose_images）が先に必要。
    reuse を付けると、前に生成して保存した絵（generated/）を使い、生成し直さない。"""
    from ugokue_pipeline.head import head_crop
    from ugokue_pipeline.result import load_result

    data = json.loads(Path(spec).read_text(encoding="utf-8"))
    sources = sorted(Path(images).glob("*.png"))
    if only:
        sources = [p for p in sources if p.stem in only.split(",")]
    calls = []
    for path in sources:
        crop = list(head_crop(load_result(Path(decomposition) / path.stem / "result")))
        calls.append((path, None if reuse else edit_views.spawn(path.read_bytes(), data, crop=crop)))
    summaries = {}
    for path, call in calls:
        saved = Path(out) / path.stem / "generated"
        if call is not None:
            _save_generated(call.get(), saved)
        generated = _load_generated(saved)
        summaries[path.stem] = _finish_views(path, Path(decomposition) / path.stem / "result", data, generated, Path(out) / path.stem)
    summary_path = Path(out) / "summary.json"
    merged = json.loads(summary_path.read_text(encoding="utf-8")) if summary_path.exists() else {}
    merged.update(summaries)
    summary_path.write_text(json.dumps(merged, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


@app.local_entrypoint()
def review_keyforms(keyforms: str = "artifacts/keyforms", references: str = "artifacts/views-45", only: str = "") -> None:
    """描いたモデルの特徴点を検出して（CPU）、顔の向きの合否をエージェントが判定するための資料を <keyforms>/<name>/review に書く。keyforms が先に必要。"""
    from ugokue_pipeline.review import RENDER_LANDMARKS, REVIEW_DIR, render_images, review_pack

    names = only.split(",") if only else sorted(p.name for p in Path(keyforms).iterdir() if (p / "model" / "model.json").exists())
    for name in names:
        directory = Path(keyforms) / name
        faces = detect_landmarks.remote(render_images(directory))
        (directory / REVIEW_DIR).mkdir(parents=True, exist_ok=True)
        (directory / REVIEW_DIR / RENDER_LANDMARKS).write_text(json.dumps(faces) + "\n", encoding="utf-8")
        reference = Path(references) / name
        review_pack(directory, reference if reference.is_dir() else None)
        print(f"wrote {directory / REVIEW_DIR}")


@app.local_entrypoint()
def check_view_determinism(
    image: str = "pipeline/samples/illustrations/01-long-straight.png",
    view: str = "x+30_y+00",
    out: str = "artifacts/views-determinism",
    spec: str = "pipeline/samples/views.json",
    decomposition: str = "artifacts/decomposition",
) -> None:
    """同じイラストと同じ設定で同じ絵を2回生成し、画素がどれだけ違うかを書き出す。"""
    import numpy as np
    from PIL import Image

    from ugokue_pipeline.head import head_crop
    from ugokue_pipeline.result import load_result

    data = json.loads(Path(spec).read_text(encoding="utf-8"))
    png = Path(image).read_bytes()
    crop = list(head_crop(load_result(Path(decomposition) / Path(image).stem / "result")))
    first, second = edit_views.spawn(png, data, [view], crop), edit_views.spawn(png, data, [view], crop)
    a, b = first.get()["images"][view], second.get()["images"][view]
    directory = Path(out)
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "first.png").write_bytes(a)
    (directory / "second.png").write_bytes(b)
    diff = np.abs(np.array(Image.open(io.BytesIO(a))).astype(np.int16) - np.array(Image.open(io.BytesIO(b))).astype(np.int16))
    summary = {"view": view, "identical": bool(diff.max() == 0), "maxAbsDiff": int(diff.max()), "differentPixels": int((diff.max(axis=-1) > 0).sum())}
    (directory / "determinism.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=2))
