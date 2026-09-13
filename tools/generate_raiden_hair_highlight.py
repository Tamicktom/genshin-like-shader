#!/usr/bin/env python3
"""Extract a greyscale hair-highlight mask from Raiden's hair albedo UV.

Uses high luma on purple / violet hair islands only — gold ornaments, red
tassels, and metal hardware stay black so the streak map does not light up Accs.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
HAIR_ALBEDO = ROOT / "assets/raiden-shogun/textures/gltf_embedded_1.png"
OUT_PATH = ROOT / "looks/raiden_hair_highlight.png"


def luma(r: int, g: int, b: int) -> float:
	return 0.299 * r + 0.587 * g + 0.114 * b


def is_gold(r: int, g: int, b: int) -> bool:
	# Warm gold / brass hardware and fan ribs.
	return r > 120 and g > 80 and r >= b and (r + g) > (b * 2.2) and (r - b) > 25


def is_red_tassel(r: int, g: int, b: int) -> bool:
	return r > 100 and r > g * 1.35 and r > b * 1.25


def is_beige_cord(r: int, g: int, b: int) -> bool:
	# Straw / beige tassels and cords near bottom of the atlas.
	return r > 140 and g > 120 and b > 80 and abs(r - g) < 45 and r > b * 1.05


def is_metal_grey(r: int, g: int, b: int) -> bool:
	# Neutral bright metal plates (hex pin, rings) — low chroma, mid/high luma.
	mx = max(r, g, b)
	mn = min(r, g, b)
	return mx > 90 and (mx - mn) < 28 and abs(r - g) < 18 and abs(g - b) < 18


def is_purple_hair(r: int, g: int, b: int) -> bool:
	"""Hair islands: indigo / violet / lavender. Excludes ornaments."""
	if r + g + b < 20:
		return False
	if is_gold(r, g, b) or is_red_tassel(r, g, b) or is_beige_cord(r, g, b) or is_metal_grey(r, g, b):
		return False
	# Soft purple / blue bias; allow cyan-lavender highlight streaks (high B).
	# Require blue to lead or tie green so gold leftovers stay out.
	if b < g * 0.9:
		return False
	if (b + r) < g * 1.35:
		return False
	# Magenta jewel centers are high-R high-B but not hair.
	if r > 160 and b > 140 and g < 80:
		return False
	return True


def smoothstep(edge0: float, edge1: float, x: float) -> float:
	if edge1 <= edge0:
		return 1.0 if x >= edge1 else 0.0
	t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
	return t * t * (3.0 - 2.0 * t)


def main() -> None:
	src = Image.open(HAIR_ALBEDO).convert("RGB")
	w, h = src.size
	pixels = src.load()

	# First pass: collect luma on purple hair for percentile threshold.
	hair_lumas: list[float] = []
	for y in range(h):
		for x in range(w):
			r, g, b = pixels[x, y]
			if is_purple_hair(r, g, b):
				hair_lumas.append(luma(r, g, b))

	hair_lumas.sort()
	n = len(hair_lumas)
	if n == 0:
		raise SystemExit("no purple hair pixels found — check heuristics")

	p50 = hair_lumas[int(n * 0.50)]
	p75 = hair_lumas[int(n * 0.75)]
	p90 = hair_lumas[int(n * 0.90)]
	p95 = hair_lumas[min(n - 1, int(n * 0.95))]
	print(f"purple hair pixels={n} luma p50={p50:.1f} p75={p75:.1f} p90={p90:.1f} p95={p95:.1f}")

	# Soft ramp: only the painted cyan/lavender streaks (top of the hair luma).
	# p75 captures structural lavender shading — too wide for a spec mask.
	lo = p90
	hi = max(p95, p90 + 8.0)
	print(f"smoothstep lo={lo:.1f} hi={hi:.1f}")

	out = Image.new("RGB", (w, h), (0, 0, 0))
	out_px = out.load()
	lit = 0

	# Hair UV islands live mostly in the upper ~70% of this atlas; the bottom-right
	# quadrant packs fans / jewels / cords. Zero that corner to avoid Accs bleed.
	acc_x0 = int(w * 0.62)
	acc_y0 = int(h * 0.62)

	for y in range(h):
		for x in range(w):
			if x >= acc_x0 and y >= acc_y0:
				continue
			r, g, b = pixels[x, y]
			if not is_purple_hair(r, g, b):
				continue
			y_val = luma(r, g, b)
			mask = smoothstep(lo, hi, y_val)
			if mask < 0.02:
				continue
			v = int(round(mask * 255.0))
			out_px[x, y] = (v, v, v)
			lit += 1

	OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
	out.save(OUT_PATH)
	print(f"lit pixels={lit} ({100.0 * lit / (w * h):.2f}%) wrote {OUT_PATH}")


if __name__ == "__main__":
	main()
