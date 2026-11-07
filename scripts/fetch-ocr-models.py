"""Fetch pinned, local-only Tesseract models from the official model repository."""
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request

REVISION = "87416418657359cb625c412a48b6e1d6d41c29bd"
MODELS = ["eng", "rus", "ukr", "deu", "fra", "spa", "ita", "pol", "por", "hun", "ces", "tur"]
ROOT = Path(__file__).resolve().parents[1] / "src/LiveScreenTextSync.App/OcrModels"


def fetch(model):
    url = f"https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/{REVISION}/{model}.traineddata"
    data = urllib.request.urlopen(url, timeout=60).read()
    if len(data) < 100_000:
        raise RuntimeError(f"Unexpectedly short OCR model: {model}")
    (ROOT / f"{model}.traineddata").write_bytes(data)
    return {"model": model, "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}


if __name__ == "__main__":
    ROOT.mkdir(parents=True, exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        entries = sorted(pool.map(fetch, MODELS), key=lambda entry: entry["model"])
    license_url = f"https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/{REVISION}/LICENSE"
    (ROOT / "LICENSE.txt").write_bytes(urllib.request.urlopen(license_url, timeout=30).read())
    leptonica_url = "https://raw.githubusercontent.com/DanBloomberg/leptonica/1.82.0/leptonica-license.txt"
    (ROOT / "LEPTONICA-LICENSE.txt").write_bytes(urllib.request.urlopen(leptonica_url, timeout=30).read())
    (ROOT / "models.json").write_text(json.dumps({"repository": "tesseract-ocr/tessdata_fast", "revision": REVISION, "models": entries}, indent=2) + "\n", encoding="utf-8")
    print(f"Downloaded {len(entries)} pinned OCR models ({sum(entry['bytes'] for entry in entries):,} bytes).")
