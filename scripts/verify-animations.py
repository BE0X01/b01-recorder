"""Run after --verify. Requires Pillow: python -m pip install pillow."""
import json
import sys
from pathlib import Path
from PIL import Image

folder = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/b01-recorder-v0.2-win-x64/verification")
results = []
for item in json.loads((folder / "results.json").read_text(encoding="utf-8")):
    if item["format"] not in ("GIF", "WEBP"):
        continue
    file = folder / Path(item["file"]).name
    with Image.open(file) as image:
        assert image.n_frames > 1, f"Expected moving test panel in {file.name}"
        duration = 0
        for frame in range(image.n_frames):
            image.seek(frame)
            image.load()
            duration += image.info.get("duration", 0)
        results.append({"file": file.name, "frames": image.n_frames, "size": image.size, "duration_ms": duration})
assert len(results) == 4, "Expected low/high quality GIF and WebP outputs"
(folder / "animation-check.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(json.dumps(results, indent=2))
