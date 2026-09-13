#!/usr/bin/env python3
"""Validate looks/raiden_face_shadow.png as a single-channel directional map.

Asserts the active asset is usable with the mirrored-UV face contract and
reports a measured horizontal symmetry axis for FaceMirrorAxis.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT = ROOT / "looks/raiden_face_shadow.png"


def main() -> int:
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument("image", nargs="?", type=Path, default=DEFAULT)
	args = parser.parse_args()

	path: Path = args.image
	if not path.is_file():
		print(f"missing image: {path}", file=sys.stderr)
		return 1

	im = Image.open(path)
	w, h = im.size
	channels = im.split()
	r = channels[0]
	rdata = list(r.getdata())

	# Mask: keep pixels that are not fully empty (covers RGBA replicated maps).
	if len(channels) >= 4:
		alpha = list(channels[3].getdata())
		mask = [a > 8 for a in alpha]
	else:
		mask = [v > 8 for v in rdata]

	masked = [v for v, m in zip(rdata, mask) if m]
	if len(masked) < 100:
		print("FAIL: too few masked pixels", file=sys.stderr)
		return 2

	# Channel agreement: R should carry the signal; G/B may match (replicated).
	if len(channels) >= 2:
		gdata = list(channels[1].getdata())
		diff_rg = sum(abs(a - b) for a, b, m in zip(rdata, gdata, mask) if m)
		mean_rg = diff_rg / len(masked)
		print(f"mean |R-G| on mask: {mean_rg:.4f}")

	# Horizontal monotonicity of column means across the masked region.
	col_sums = [0.0] * w
	col_counts = [0] * w
	for y in range(h):
		row_off = y * w
		for x in range(w):
			idx = row_off + x
			if not mask[idx]:
				continue
			col_sums[x] += rdata[idx]
			col_counts[x] += 1

	xs = [x for x in range(w) if col_counts[x] > 0]
	means = [col_sums[x] / col_counts[x] for x in xs]
	if len(means) < 8:
		print("FAIL: insufficient horizontal samples", file=sys.stderr)
		return 2

	# Count ascending/descending steps between consecutive active columns.
	asc = sum(1 for a, b in zip(means, means[1:]) if b > a + 0.5)
	desc = sum(1 for a, b in zip(means, means[1:]) if b < a - 0.5)
	print(f"size: {w}x{h} mode={im.mode}")
	print(f"masked pixels: {len(masked)} ({100.0 * len(masked) / (w * h):.1f}%)")
	print(f"R range on mask: {min(masked)}–{max(masked)}")
	print(f"column mean steps: ascending={asc} descending={desc}")

	# Symmetry axis: column whose mean is closest to the mid-range of edge means.
	left_mean = sum(means[: max(1, len(means) // 10)]) / max(1, len(means) // 10)
	right_mean = sum(means[-max(1, len(means) // 10) :]) / max(1, len(means) // 10)
	target = 0.5 * (left_mean + right_mean)
	best_i = min(range(len(means)), key=lambda i: abs(means[i] - target))
	axis_u = xs[best_i] / max(w - 1, 1)
	print(f"edge means L={left_mean:.1f} R={right_mean:.1f}")
	print(f"suggested FaceMirrorAxis ≈ {axis_u:.3f}")

	directional = asc >= 3 * desc or desc >= 3 * asc
	if not directional:
		print("FAIL: column means are not clearly directional", file=sys.stderr)
		return 2

	print("PASS: single-channel directional face map")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
