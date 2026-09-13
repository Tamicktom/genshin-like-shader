# Genshin-like refactor plan

Staged plan to move the current Godot look closer to the Genshin character style described in [`genshin-impact-character-shader-breakdown.md`](genshin-impact-character-shader-breakdown.md). Gap analysis lives in [`shader-comparison-with-genshin-breakdown.md`](shader-comparison-with-genshin-breakdown.md).

This is not a 1:1 recreation. Each phase should leave `scenes/main.tscn` runnable (F5) with Raiden still readable. Prefer feature flags that default **off**, then turn them on per preset.

---

## How to work this plan

- Finish one phase before starting the next. Do not mix lighting, post-process, and new maps in the same change.
- After every step: run the demo, spin the character (`SpinY`), and keep a screenshot. If the look regresses, revert that step only.
- New uniforms must have defaults that reproduce today’s image. Old `.tres` files should load without a re-save.
- Keep the current architecture: `ToonPreset` = shading knobs, `LookSlot` = matching + per-part flags/maps, `ApplyCharacterLook` = bind albedo + extras, shaders stay opaque (`discard`, never `ALPHA`).
- There are no automated tests. The test is visual: turntable, face close-up, hair backlit, gold ornaments, cloth terminator, inner silhouettes (belt/skirt).

---

## Principles

1. **Cheap, no-art wins first.** Outer shadow and lighting knobs need no new textures and teach the two-band terminator. Face maps and compositor passes come later because they depend on assets or a new pipeline.
2. **Do not replace working approximations until the replacement is on.** Keep the inverted-hull outline until a compositor outline is proven. Keep Kajiya-Kay hair until a highlight mask exists. Keep Fresnel rim quiet until Sobel edge-highlight exists.
3. **Character maps on the slot, shared look on the preset.** A face SDF belongs to Raiden’s face slot. A metal gradient ramp can be a shared texture on the metal preset.
4. **One key light.** Extra fill lights flatten cel bands in Godot’s additive `light()`. Do not “fix” the shader by adding scene lights.

---

## Target vs current (reminder)

| Goal | Current | Phase |
| --- | --- | --- |
| Painted two-band terminator | Single `smoothstep` | 1–2 |
| Stable cheek shadow | NdotL on face normals | 5 |
| Sweeping metal band | Round Blinn-Phong blob | 4 |
| Saturated cartoon grade | Filmic/ACES + glow | 3 |
| Inner + outer ink, white anime rim | Hull + Fresnel | 7 |
| Painted hair streak | Kajiya-Kay lobes | 6 |

Visual-impact order in the comparison put **face** first. Implementation order below puts **no-art lighting** first so each F5 still teaches something, then face once the extra-map seam exists.

---

## Phase 0 — Extra-map seam (no visual change) ✅

**Why first:** every later texture feature (face, metal mask, hair streak) needs `ApplyCharacterLook` to bind more than albedo. Do the plumbing while the image is still the known baseline.

**Done when:** F5 looks identical to HEAD. Inspector shows empty optional map slots. Shader compiles with the new uniforms unused.

**Result:** Landed with Phase 5. `LookSlot` has nullable `FaceShadowTex` / `ControlTex` / `DetailNormalTex`; `ApplyCharacterLook` binds them with `use_*`; shader uniforms exist. Control / detail-normal remain unsampled.

### Steps

1. Add optional textures on `LookSlot` (not on `ToonPreset`): `FaceShadowTex`, `ControlTex` (packed light/spec/metal mask), `DetailNormalTex`. All nullable. Do not wire them yet.
2. Add matching uniform names on `ShaderParams` and unused `sampler2D` + `bool use_*` uniforms on `genshin_toon.gdshader`. Sample nothing in `light()` / `fragment()` yet.
3. In `ApplyCharacterLook`, if a resolved slot has a texture, `SetShaderParameter` it and set the `use_*` flag. If null, leave the flag false.
4. Re-open `looks/raiden_shogun.tres` in the inspector, save, confirm the demo is unchanged.

**Do not:** invent placeholder noise textures; missing maps must mean “old path”.

---

## Phase 1 — Outer shadow band

**Why:** largest cheap win. Genshin’s terminator is two stacked NdotL cuts, not one soft step. No new art.

**Done when:** hair and cloth show a thin second band inside the main shadow on the turntable. Face/weapon unchanged. `outer_shadow_strength = 0` on a preset matches today’s look.

### Steps

1. Add to `ToonPreset` + shader: `OuterShadowColor`, `OuterShadowOffset` (threshold shift), `OuterShadowSmoothness`, `OuterShadowStrength` (0 = off).
2. In `light()`, after the current `shade`:
   - `shade_outer = smoothstep(threshold + offset - soft, threshold + offset + soft, wrap_ndl)`
   - `lit_term = mix(outer_color, mix(shadow_color, vec3(1.0), shade), mix(1.0, shade_outer, strength))`
   - Keep `min(..., cast_shade)` so the shadow map still wins.
3. Leave all presets at `OuterShadowStrength = 0`. Confirm identical image.
4. Enable on **hair** first (cool main + slightly warmer/darker inner band, small offset, harder smoothness). Tune on the turntable.
5. Enable on **cloth** (dress/body). Keep face and metal at 0 until Phase 2/5.

**Watch:** two bands plus Half-Lambert can eat the lit side. If the character goes muddy, lower strength rather than raising `light_intensity`.

---

## Phase 2 — Cel lighting closer to Genshin

**Why:** we wrap with Half-Lambert and only tint the shadow. Genshin uses a harder NdotL cut and tints both bands (`lerp(shadow, litColor, shade)`).

**Done when:** there is a wrap slider and a lit tint. Defaults still match today. Hair/cloth can opt into a harder cut without retouching the face yet.

### Steps

1. Replace the hardcoded Half-Lambert with `wrap` in `0..1` (`0` = raw NdotL, `0.5` = current Half-Lambert). Default `0.5`.
2. Add `LitColor` (default white). `mix(shadow_color, lit_color, shade)` instead of `mix(shadow_color, vec3(1.0), shade)`.
3. Optionally switch the main step to one-sided `smoothstep(0, smoothness, ndl - threshold)` behind a flag. Default stays two-sided around `shadow_threshold` so old presets do not jump.
4. Tune hair toward less wrap / slightly harder smoothness. Cloth next. Leave face on the old wrap until Phase 5.
5. Re-check cast-shadow remap: the outer band from Phase 1 must still sit *inside* the main shadow, including under the character’s own mesh.

**Do not:** add a second directional light to “fix” wrap. Do not raise `Sun.light_energy` to compensate; that fights later tonemap work.

---

## Phase 3 — Cartoon tonemap (scene grade) ✅

**Why:** judging Phases 1–2 on a desaturated grade hides whether the bands are right. Mendez’s plates use a saturation-preserving curve (Gran Turismo). Confirm Godot 4.7’s `tonemap_mode` enum before changing it (`2` is Filmic in 4.x, `3` is ACES, `4` may be AGX). The comparison doc assumed ACES; verify in the inspector.

**Done when:** bright whites (hair shine, metal) hold color instead of going grey, and the cel bands still read. Glow is weaker or off if it milks the image.

**Result:** Filmic (`tonemap_mode = 2`) + `adjustment_saturation = 1.1`, glow off, exposure `1.0`. GT compositor **skipped** (native Filmic passed the bar). Fog left at `0.0012`. Sweep helpers: `CaptureSceneScreenshot` user args `--grade-ab` / `--fog-ab`.

### Steps

1. Screenshot the current grade (ACES/Filmic + glow `0.35`).
2. A/B: Filmic vs ACES vs AGX vs Linear, exposure around `0.9–1.0`, glow off. Pick the one that keeps cloth purple and skin warm.
3. If none keep saturation, add a tiny `CompositorEffect` or a full-screen quad that applies a GT-style curve **after** the 3D pass. Do not bake tonemap into `genshin_toon.gdshader`.
4. Only then nudge fog. Scene fog at `0.0012` is invisible at character scale; if you want the Lumine-plate wrap, use a dedicated height/distance grade, not a global density that eats the ground.

**Do not:** start the outline compositor in this phase. Tonemap is a grade; outline is a new pass.

---

## Phase 4 — Metallic half-vector gradient ✅

**Why:** gold in Genshin is a moving 1D ramp on `dot(N, normalize(V+L))`, not a Phong blob. Weapon and ornaments previously shared the same numbers.

**Done when:** Raiden’s gold ( acc / ornaments ) shows a view-dependent **band** while spinning. Cloth/face/hair Phong (or lack of it) is unchanged. Weapon can stay Phong until a metal mask exists.

**Result:** `GradientTexture1D` at `materials/textures/gold_metallic_gradient.tres`; `UseMetallicGradient` + tex + strength on `ToonPreset` (default off). Shader path landed; **Raiden’s metal preset leaves the flag off** (tight Phong) — the ramp is ready for other looks. Weapon stays Phong. Metal slot ordered before Hair so `*Hair_Accs*` matches metal. Detail-normal UV warp skipped (no Phase 0 maps yet).

### Steps

1. Author a small shared 1D (or 2×256) gradient texture under `materials/textures/` (dark → saturated gold → white core). sRGB.
2. Add to `ToonPreset`: `UseMetallicGradient`, `MetallicGradientTex`, `MetallicStrength`. Default off.
3. In `light()`, when enabled: `u = clamp(dot(NORMAL, normalize(VIEW + LIGHT)), 0, 1)`; sample the gradient; `SPECULAR_LIGHT += gradient * strength * shade * LIGHT_COLOR`. Keep cel diffuse underneath.
4. Enable on the **metal** preset only. Lower or zero the old Blinn-Phong on that preset so the blob does not stack on the band.
5. Optional: warp `u` with `DetailNormalTex` from Phase 0 (NormalBlend). Only if an ornament looks too smooth.
6. Split weapon from metal if the blade should stay quieter (darker gradient or lower strength).

Packed metal masks (often a channel on a lightmap) wait until those textures exist. Until then, the metal *slot* is the mask.

---

## Phase 5 — Face shadow map ✅

**Why:** this is the real Genshin face. NdotL on nose/lips will never look right on a turntable.

**Blocker:** the Raiden GLB currently exposes albedo only (`gltf_embedded_0` etc.). Official Hoyoverse dumps usually ship a face lightmap (R = 0–180°, G = 180–360°). Ganyu/Ayaka folders in `assets/` also look like diffuse-only. This phase starts with an inventory.

**Done when:** with a map assigned, the cheek shadow stays a painted shape as the sun (or the body yaw) moves. Without a map, the face falls back to today’s NdotL.

**Result:** Phase 0 extra-map seam landed (`FaceShadowTex` / `ControlTex` / `DetailNormalTex` on `LookSlot`). No official face lightmap in assets — generated `looks/raiden_face_shadow.png` (R/G cheek SDF) via `tools/generate_raiden_face_shadow.py`. `HeadAxes` Marker3D + per-frame `head_forward` / `head_right` on `ApplyCharacterLook`. Shader `use_face_shadow` replaces NdotL with map sample (soft `smoothstep`, still `min` with cast shadows). Face slot has the map; outline stays off; outer shadow stays 0. Sweep: `--face-ab` → `screenshots/face_ndl.png` / `face_map.png`.

### Steps

1. Inventory textures for Raiden / Ganyu / Ayaka. Look for `*Face*Light*`, `*Shadow*`, packed `_LightMap`. If none, paint a simple R/G SDF in an image tool (hard-edged cheek blob, mirrored in G) and save next to the look, not inside the GLB.
2. Add `HeadForward` / `HeadRight` (or a `NodePath` to the head bone) on `ApplyCharacterLook` or `CharacterLook`. Each frame (or on `_Process`), write world-space XZ axes into shader varyings/uniforms. Yaw of the turntable must move the map, not the mesh normals.
3. Shader branch `use_face_shadow`: light dir vs head right → `acos` / `step` → sample R or G → that value **replaces** NdotL for `shade` (still `min` with cast shadows).
4. Bind `FaceShadowTex` on the Face `LookSlot`. Keep `EnableOutline = false` on the face.
5. Tune threshold/smoothness on the **sampled** value, not on mesh NdotL. Outer shadow on the face should stay off or very weak; the map already is the terminator.

**Do not:** flatten face normals as a substitute. Mendez tried that and discarded it.

---

## Phase 6 — Hair highlight mask ✅

**Why:** Genshin hair shine is a painted streak, gated by light, with Fresnel *removing* the sides. Dual-lobe Kajiya-Kay is a decent fallback and should remain when no mask is bound.

**Done when:** hair with a mask shows a single anime streak in light that dies at the silhouette. Hair without a mask still uses Kajiya-Kay.

**Result:** Greyscale streak map extracted from Raiden hair albedo (`tools/generate_raiden_hair_highlight.py` → `looks/raiden_hair_highlight.png`). `HairHighlightTex` on `LookSlot` (Hair slot only); `use_hair_highlight` from bind. Shader mixes `mask * shade * (1 - fresnel)` with Kajiya-Kay via `HairHighlightBlend` (default 1). Dedicated `HairHighlightFresnel` (default 5); additive rim gated off when mask drives spec. Missing map → old path. Sweep: `--hair-ab` → `screenshots/hair_kajiya.png` / `hair_mask.png`.

### Steps

1. Find or paint a greyscale streak map (or a channel on `ControlTex`). Bind on the Hair slot.
2. Shader: `spec_mask * shade * (1 - fresnel_term)` as the hair highlight. Reuse `rim_power` or a dedicated `hair_highlight_fresnel`.
3. Lower `UseAnisotropicSpecular` influence when a mask is present (or `mix` the two with a preset weight). Do not delete Kajiya-Kay.
4. Drop hair `rim_strength` toward 0; the old Fresnel *added* rim, which is the opposite of this suppress.

---

## Phase 7 — Post-process outline + edge highlight ✅

**Why:** the hull cannot ink inner silhouettes and cannot draw the white anime rim. Mendez’s look is depth+normal Sobel (outline) plus a second Sobel kept on the far side of the silhouette (edge highlight), with a face depth hack so eyes/nose do not ink.

**Done when:** a compositor pass draws outer + inner contours; the white rim sits on the silhouette (slightly downward); the face does not grow inner scribbles. The hull can be toggled off per slot once the pass is trusted.

**Result:** `ToonOutlineCompositorEffect` + `shaders/toon_outline.glsl` on `WorldEnvironment` (`materials/compositor/toon_outline_effect.tres`, default on). **Relative reverse-Z depth Sobel** on the MSAA-resolved `R32Sfloat` depth target (`AccessResolvedDepth`). `NeedsNormalRoughness` stays off (it blacks out custom `light()`). Color-luma Sobel was a dead end (inks every cel band). Fragment `DEPTH` flatten was removed: even a unused `if` still breaks MSAA coverage and rendered the demo as black stipple. Cloth/hair `RimStrength = 0`. Hull kept for albedo-tinted lines (plan option a). Sweep: `--outline-ab` → `screenshots/outline_*.png`.

### Steps

1. Spike a Godot 4 `CompositorEffect` (Forward Plus, RenderingDevice) that reads depth (and normals if available) and draws debug edges on a full-screen blit. No color grading in this pass.
2. Combine depth edges + normal edges. Thickness in pixels. Output as a dark multiply or replace on edge pixels.
3. Face: either a stencil/layer bit, or a slightly pushed depth on the face material, so inner face edges drop below the threshold. Keep the current “no hull on face” until this works.
4. Edge highlight: Sobel on depth, keep the side with greater depth, offset a few pixels down, add a thin white/light line. This **replaces** the role of Fresnel rim — then drop `rim_strength` on cloth/hair.
5. Color the dark outline. Options: (a) keep the hull only for albedo-tinted inner lines, compositor for silhouette; (b) sample the color buffer along the edge. Prefer (a) first so cloth stays purple-lined.
6. Only after the compositor is default-on in `main.tscn`, turn `EnableOutline` off on body/dress **if** inner lines are good enough. Hair often still wants a hull.

**Do not:** delete `genshin_outline.gdshader` in the same phase you add the compositor. Hull stays the fallback.

---

## Phase 8 — Dither and finish

**Why:** dither only pays off after bands are harder (Phases 1–2). Doing it earlier fights the current wide `smoothstep`.

**Done when:** large shadow flats do not show 8-bit banding in a still, and the demo still reads as poster color rather than noise.

### Steps

1. Ordered dither in the toon shader *or* in the compositor, applied only across the terminator (not on albedo).
2. Final preset pass: metal vs weapon, hair mask vs Kajiya-Kay, rim values after Sobel highlight.
3. Update README “Notes / limits” and the comparison snapshot table so they match the new baseline.

---

## Suggested commit slices

Each line should be its own commit (working demo after each):

0. Optional maps on `LookSlot` + shader uniforms + bind in applicator (no sampling).
1. Outer-shadow uniforms, strength 0 everywhere.
2. Outer shadow enabled on hair preset.
3. Outer shadow enabled on cloth preset.
4. Wrap factor + `LitColor`, defaults = current look.
5. Hair/cloth wrap and lit tint tuned.
6. Tonemap/glow A/B on `main.tscn` only.
7. Metallic gradient texture + shader path, off by default.
8. Metallic gradient on metal preset; Phong lowered.
9. Head axes uniforms + face-map shader branch, fallback NdotL.
10. Face map assigned on Raiden face slot (once art exists).
11. Hair mask path + Fresnel suppress; Kajiya-Kay remains fallback.
12. Compositor edge debug.
13. Compositor outline + face suppression.
14. Compositor edge highlight; rim strengths lowered.
15. Dither; README/comparison refresh.

---

## Decision log

- **Stay data-driven.** Do not fold everything into one mega-shader with 200 uniforms and no presets.
- **Maps on `LookSlot`, knobs on `ToonPreset`.** Character-specific textures vs shared shading.
- **Flags default off.** A missing texture or `strength = 0` is the old look.
- **Opaque pipeline stays.** Cutout later, if ever, via `discard` only.
- **Hull outline stays until Phase 7 is proven.** Face/weapon remain hull-off.
- **One directional key.** Additive `light()` is a feature, not a reason to fill-light the shadows away.
- **No glasses parallax.** Extra in the Unity post, not Genshin.
- **No automated shader tests.** Visual turntable + screenshots. If a debug view is added, put it behind a shader uniform (`debug_shade`), not a second scene.
- **Phase 3 grade = Filmic + sat 1.1.** A/B rejected ACES (grey highs), AGX (muted), and denser fog. GT compositor deferred until Filmic fails the bar.
- **Phase 4 metal = half-vector `GradientTexture1D`.** Shader path + shared ramp exist; Raiden metal preset keeps the flag off (Phong). Weapon stays Phong. Metal slot before Hair so Hair_Accs is metal, not Kajiya-Kay.
- **Phase 5 face = painted R/G SDF + head XZ axes.** No official lightmap in GLB; `looks/raiden_face_shadow.png` + `HeadAxes` Marker3D (no skeleton). Missing map → NdotL. Phase 0 extra-map seam landed with this phase.
- **Phase 6 hair = albedo-extracted greyscale streak + Fresnel suppress.** `looks/raiden_hair_highlight.png` from purple-island luma of `gltf_embedded_1`; `HairHighlightTex` on Hair slot. Mix with Kajiya-Kay via `HairHighlightBlend`; additive rim gated when mask is on. Missing map → Kajiya-Kay.
- **Phase 7 outline = resolved-depth Sobel compositor + hull fallback.** `NeedsNormalRoughness` breaks custom `light()`; do not write fragment `DEPTH` (breaks MSAA). Cloth/hair Fresnel rim zeroed; hull kept for purple-tinted lines.

---

## Out of scope

- Pixel-perfect match to the live game or to Mendez’s Unity file.
- Auto-applying the look to every mesh (opt-in applicator stays).
- Vertex-color outline width (nice later; not required for the compositor path).
- Transparency / lace hair cards (global cutout stays off to protect depth).
- Replacing C# look resources with GDScript.
- Authoring a full official-quality face lightmap if no source texture exists — a readable painted SDF is enough for the shader path.

---

## Suggested build order if time is short

If only a few sessions are available, stop after **Phase 1 + Phase 4 + Phase 3**. That is a two-band terminator, moving gold, and a cleaner grade — already much closer on a still. Face (5) and compositor (7) are the next “this looks like the game” jumps and each is a full session of their own.
