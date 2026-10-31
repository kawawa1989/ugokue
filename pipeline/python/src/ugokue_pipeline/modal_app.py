"""Modal の GPU で、テストイラストの生成と See-through によるパーツ分解を実行する。

実行例（リポジトリのルートで）：
    uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::generate_illustrations
    uv run --project pipeline/python --extra modal modal run pipeline/python/src/ugokue_pipeline/modal_app.py::decompose_images
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
