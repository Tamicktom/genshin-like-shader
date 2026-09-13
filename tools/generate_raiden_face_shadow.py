#!/usr/bin/env python3
"""Generate a single-channel face-shadow SDF debug fixture for Raiden.

This is a heuristic fixture used to exercise the mirrored-UV face path.
It is NOT the committed production asset at looks/raiden_face_shadow.png —
that file is a hand/derived grayscale directional gradient.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
FACE_ALBEDO = ROOT / "assets/raiden-shogun/textures/gltf_embedded_0.png"
OUT_PATH = ROOT / "looks/raiden_face_shadow_fixture.png"


def find_eyes(px, w: int, h: int) -> list[tuple[int, int]]:
	"""Dark reddish eye clusters in the upper face island."""
	candidates: list[tuple[int, int]] = []
	for y in range(int(h * 0.25), int(h * 0.55)):
		for x in range(int(w * 0.15), int(w * 0.85)):
			r, g, b = px[x, y]
			if r < 200 and g < 140 and b < 140 and r > g and (r + g + b) < 420:
				candidates.append((x, y))
	return candidates


def cluster_centers(points: list[tuple[int, int]], max_clusters: int = 2) -> list[tuple[float, float]]:
	if not points:
		return []
	xs = sorted(p[0] for p in points)
	mid = xs[len(xs) // 2]
	left = [p for p in points if p[0] < mid]
	right = [p for p in points if p[0] >= mid]
	centers: list[tuple[float, float]] = []
	for group in (left, right):
		if not group:
			continue
		cx = sum(p[0] for p in group) / len(group)
		cy = sum(p[1] for p in group) / len(group)
		centers.append((cx, cy))
	centers.sort(key=lambda c: c[0])
	return centers[:max_clusters]


def main() -> None:
	src = Image.open(FACE_ALBEDO).convert("RGB")
	w, h = src.size
	pixels = src.load()

	eyes = find_eyes(pixels, w, h)
	centers = cluster_centers(eyes)
	if len(centers) >= 2:
		cx = (centers[0][0] + centers[1][0]) * 0.5
		cy = (centers[0][1] + centers[1][1]) * 0.5 + h * 0.06
		iod = abs(centers[1][0] - centers[0][0])
		# Face disk ≈ slightly wider than inter-ocular span.
		radius = iod * 1.05
		print(f"eyes L={centers[0]} R={centers[1]} iod={iod:.1f}")
	else:
		cx, cy, radius = w * 0.51, h * 0.48, w * 0.36
		print("eyes not found — using fallback circle")

	print(f"face center=({cx:.1f},{cy:.1f}) radius={radius:.1f}")

	# Single-channel directional map (R). Right-side lighting uses this UV;
	# left-side lighting mirrors UV.x around the island symmetry axis.
	out = Image.new("L", (w, h), 255)
	out_px = out.load()

	for y in range(h):
		for x in range(w):
			dx = (x - cx) / max(radius, 1.0)
			dy = (y - cy) / max(radius, 1.0)
			dist = math.hypot(dx, dy)

			if dist > 1.08:
				out_px[x, y] = 255
				continue

			radial = max(0.0, 1.0 - dist * dist)

			# Light from right — shadow forms on +X cheek first.
			cheek = 0.28 + 0.18 * radial - 0.55 * max(dx, 0.0) - 0.04 * max(dy, 0.0)

			# Protect eye band.
			if abs(dy + 0.12) < 0.18 and abs(dx) < 0.55:
				cheek += 0.12

			cheek = max(0.08, min(0.92, cheek))
			out_px[x, y] = int(cheek * 255)

	OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
	out.save(OUT_PATH)

	# Sanity print at key UVs.
	for name, x, y in (
		("center", int(cx), int(cy)),
		("left", int(cx - radius * 0.45), int(cy)),
		("right", int(cx + radius * 0.45), int(cy)),
	):
		v = out_px[x, y]
		print(f"  {name} ({x},{y}) R={v / 255:.3f}")
	print(f"wrote {OUT_PATH} (debug fixture; does not replace looks/raiden_face_shadow.png)")


if __name__ == "__main__":
	main()
