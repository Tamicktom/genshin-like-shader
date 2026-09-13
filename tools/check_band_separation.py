#!/usr/bin/env python3
"""Report lit/shadow band separation from a final-color capture.

Clusters near-grey character pixels into shadow vs lit families by luminance
and reports how far ambient has lifted the shadow mean toward the lit mean.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT = ROOT / "screenshots/ambient/ambient_max.png"


def main() -> int:
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument("image", nargs="?", type=Path, default=DEFAULT)
	args = parser.parse_args()

	path: Path = args.image
	if not path.is_file():
		print(f"missing image: {path}", file=sys.stderr)
		return 1

	im = Image.open(path).convert("RGB")
	pixels = list(im.getdata())

	# Keep mid-chroma character pixels; skip saturated sky and near-black voids.
	samples: list[float] = []
	for r, g, b in pixels:
		mx = max(r, g, b)
		mn = min(r, g, b)
		if mx < 12 or mx > 250:
			continue
		chroma = mx - mn
		if chroma > 90:
			continue
		lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0
		samples.append(lum)

	if len(samples) < 2000:
		print("too few character luminance samples", file=sys.stderr)
		return 1

	shadow = [v for v in samples if v < 0.42]
	lit = [v for v in samples if v > 0.55]
	if len(shadow) < 200 or len(lit) < 200:
		print(
			f"FAIL: weak clusters shadow={len(shadow)} lit={len(lit)}",
			file=sys.stderr,
		)
		return 2

	shadow_mean = sum(shadow) / len(shadow)
	lit_mean = sum(lit) / len(lit)
	sep = lit_mean - shadow_mean

	print(f"image: {path}")
	print(f"samples: {len(samples)} / {len(pixels)}")
	print(f"shadow n/mean: {len(shadow)} / {shadow_mean:.3f}")
	print(f"lit n/mean: {len(lit)} / {lit_mean:.3f}")
	print(f"separation: {sep:.3f}")

	if sep < 0.12:
		print("FAIL: lit/shadow separation < 0.12", file=sys.stderr)
		return 2

	print("PASS: readable lit/shadow separation")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
