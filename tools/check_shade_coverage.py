#!/usr/bin/env python3
"""Report shade-band coverage from a debug/shade.png capture.

Expects a grayscale shade debug view (debug_view == 3) under a neutral grade.
Only near-equal RGB samples are counted so sky/ground gradients do not dominate.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT = ROOT / "screenshots/debug/shade.png"


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

	# Shade debug is grayscale; sky/ground keep chromatic gradients.
	grays = []
	for r, g, b in pixels:
		if abs(r - g) <= 5 and abs(g - b) <= 5 and abs(r - b) <= 5:
			grays.append(r / 255.0)

	if len(grays) < 1000:
		print("too few grayscale shade samples", file=sys.stderr)
		return 1

	deep = sum(1 for v in grays if v <= 0.08)
	lit = sum(1 for v in grays if v >= 0.95)
	mid = sum(1 for v in grays if 0.1 < v < 0.9)
	n = len(grays)

	print(f"image: {path}")
	print(f"grayscale shade pixels: {n} / {len(pixels)} ({100.0 * n / len(pixels):.1f}%)")
	print(f"shade <= 0.08: {deep} ({100.0 * deep / n:.1f}%)")
	print(f"shade >= 0.95: {lit} ({100.0 * lit / n:.1f}%)")
	print(f"transition 0.1–0.9: {mid} ({100.0 * mid / n:.1f}%)")
	print(f"min/max/mean: {min(grays):.3f} / {max(grays):.3f} / {sum(grays) / n:.3f}")

	if deep < 1000:
		print("FAIL: fewer than 1000 deep-shadow grayscale pixels", file=sys.stderr)
		return 2

	print("PASS: non-trivial deep-shadow coverage")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
