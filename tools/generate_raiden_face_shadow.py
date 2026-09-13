#!/usr/bin/env python3
"""Generate a simple R/G face-shadow SDF for Raiden from the face albedo UV."""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
FACE_ALBEDO = ROOT / "assets/raiden-shogun/textures/gltf_embedded_0.png"
OUT_PATH = ROOT / "looks/raiden_face_shadow.png"


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

	# Threshold map vs light angle (0 front-ish → 1 side).
	# Default sun angle ≈ 0.44: center stays lit (~0.55), outer cheek shadows (~0.28).
	out = Image.new("RGB", (w, h), (255, 255, 255))
	out_px = out.load()

	for y in range(h):
		for x in range(w):
			dx = (x - cx) / max(radius, 1.0)
			dy = (y - cy) / max(radius, 1.0)
			dist = math.hypot(dx, dy)

			if dist > 1.08:
				out_px[x, y] = (255, 255, 255)
				continue

			radial = max(0.0, 1.0 - dist * dist)

			# R: light from right — shadow forms on +X cheek first.
			# Tuned so default sun angle (~0.44) already clips the outer cheek.
			cheek_r = 0.28 + 0.18 * radial - 0.55 * max(dx, 0.0) - 0.04 * max(dy, 0.0)
			# G: mirrored for light from left.
			cheek_g = 0.28 + 0.18 * radial - 0.55 * max(-dx, 0.0) - 0.04 * max(dy, 0.0)

			# Protect eye band.
			if abs(dy + 0.12) < 0.18 and abs(dx) < 0.55:
				cheek_r += 0.12
				cheek_g += 0.12

			cheek_r = max(0.08, min(0.92, cheek_r))
			cheek_g = max(0.08, min(0.92, cheek_g))
			out_px[x, y] = (int(cheek_r * 255), int(cheek_g * 255), 255)

	OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
	out.save(OUT_PATH)

	# Sanity print at key UVs.
	for name, x, y in (
		("center", int(cx), int(cy)),
		("left", int(cx - radius * 0.45), int(cy)),
		("right", int(cx + radius * 0.45), int(cy)),
	):
		r, g, _ = out_px[x, y]
		print(f"  {name} ({x},{y}) R={r / 255:.3f} G={g / 255:.3f}")
	print(f"wrote {OUT_PATH}")


if __name__ == "__main__":
	main()
