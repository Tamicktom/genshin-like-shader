# Shader comparison: this project vs Genshin Unity breakdown

This note compares the Godot shaders in this repo (`shaders/genshin_toon.gdshader`, `shaders/genshin_outline.gdshader`) with Adrian Mendez’s Unity URP recreation of the Genshin Impact character look ([ArtStation](https://www.artstation.com/artwork/wJZ4Gg), local copy in [`genshin-impact-character-shader-breakdown.md`](genshin-impact-character-shader-breakdown.md)).

The Unity write-up is a reconstruction, not Hoyoverse source. It is still the closest public breakdown of the *look* this project is aiming at.

---

## Snapshot

| Feature (Mendez / Genshin look) | This project | Match |
| --- | --- | --- |
| Custom cel lighting (NdotL → soft step → tint) | `light_wrap` + two-sided `smoothstep` + `mix(shadow_color, lit_color, shade)` | Close |
| Multiple lights | Godot `light()` runs per light | Yes |
| Cast shadows into the cel band | `ATTENUATION` remapped, not multiplied to black | Close |
| Outer (second) shadow band | `OuterShadow*` on hair (`0.85`) / cloth (`0.75`); face/metal/weapon off | Close |
| Anisotropic hair | Painted streak mask + Fresnel suppress; Kajiya-Kay fallback | Close |
| Face shadow texture (R/G lightmap) | Painted SDF on Face slot + head axes; NdotL fallback | Close |
| Metallic (half-vector gradient / 1D matcap) | Path + shared ramp ready; Raiden metal preset uses Phong (flag off) | Partial |
| Multiply by light color | `LIGHT_COLOR * light_intensity` | Yes |
| Fog | Scene `Environment` fog, very light | Partial |
| Outline | Reverse-Z depth Sobel compositor (default on) + inverted hull (`NextPass`) fallback | Close |
| Special face outline (suppress inner face edges) | Face/weapon hull-off; relative depth threshold (no fragment `DEPTH` write — that broke MSAA) | Partial |
| Edge highlight (white Sobel rim, not Fresnel) | Compositor far-side depth edge + Y offset; cloth/hair Fresnel rim 0 | Close |
| Custom cartoon tonemapper (Gran Turismo) | Godot Filmic (`tonemap_mode = 2`) + `adjustment_saturation = 1.1`; glow off (Phase 3 A/B; GT compositor not needed) | Partial |
| Dithering | Ordered Bayer 4×4 on `shade` across the terminator (hair/cloth); not in the compositor | Close |
| Glasses parallax | Not in original Genshin; we do not have it | N/A |

---

## Pipeline context

Mendez splits the look into **character shader** + **screen-space post-process**. Lighting, outer shadow, hair, face map, metal, and fog live in the material. Outline, face-outline suppression, edge highlight, and tonemap live after the scene is drawn. Mendez also lists dithering as a post feature.

This project keeps most lighting **on the mesh**, with Phase 7 adding a screen-space compositor pass. Terminator dither (Phase 8) stays in the toon `light()` so it only hits the cel cut:

- Base pass: `genshin_toon.gdshader` (opaque spatial, custom `light()`).
- Outline hull: `genshin_outline.gdshader` as `material.NextPass` (albedo-tinted).
- Compositor outline / edge highlight: `ToonOutlineCompositorEffect` on `WorldEnvironment` (resolved-depth Sobel, default on).
- Tonemap, glow, and fog: Godot `WorldEnvironment` in `scenes/main.tscn`.
- Per-part knobs: `ToonPreset` resources applied by `ApplyCharacterLook.cs`.

Godot also differs from Unity URP in how lighting is assembled. Albedo is written in `fragment()`, then `DIFFUSE_LIGHT` and `SPECULAR_LIGHT` accumulate in `light()`. The engine multiplies albedo by diffuse. Mendez does `lerp(shadow, base, shade) * texture` in one color. The visual intent is the same; we now also expose `lit_color` so the lit band can be warmed or cooled independently of the albedo (default white = albedo × light).

---

## Custom lighting

Mendez’s core:

1. `lDot = dot(lightDir, worldNormal)`
2. `lSmooth = smoothstep(0, _LightSmooth, lDot)` with `_LightSmooth ≈ 0.1`
3. `lerp(_ShadowColor, _BaseColor, lSmooth)`
4. Multiply by `_MainTexture`

Ours (`genshin_toon.gdshader`, `light()`):

1. `n_dot_l = clamp(dot(NORMAL, LIGHT), 0, 1)`
2. **Wrap**: `wrap_ndl = mix(n_dot_l, 1.0, light_wrap)` (`0` = raw NdotL, `0.5` = Half-Lambert)
3. Two-sided `smoothstep(threshold ± soft, wrap_ndl)` (optional one-sided step behind `use_one_sided_step`)
4. `min` with a remapped shadow-map term
5. Optional ordered Bayer dither on `shade`, gated to the terminator (`4 * shade * (1 - shade)`)
6. `mix(shadow_color, lit_color, shade)` (plus outer-band mix when strength > 0)
7. Add `LIGHT_COLOR * light_intensity`
8. Ambient as `EMISSION = ALBEDO * ambient_color * ambient_strength`

**Observations**

- Hair/cloth use less wrap (`0.2` / `0.3`) and narrower smoothness than the face, so their terminator is closer to Genshin’s hard cut while the face stays softer for the painted SDF.
- Their `_LightSmooth = 0.1` is a one-sided step from 0. Ours defaults to two-sided around a threshold; `use_one_sided_step` exists but Raiden presets keep it off.
- They tint **both** bands. We now do too via `lit_color` (white = albedo × light; cloth uses a slight warm tint).
- Cast shadows are handled in the Genshin spirit: they join the same cool cel tint instead of going black. The `fwidth(ATTENUATION)` widen is ours and is a good anti-crawl trick while the turntable spins.
- Multiple lights work in both. Extra lights in Godot **add** more `DIFFUSE_LIGHT`, so a fill light can lift the shadow tint toward white and flatten the two-tone look. Genshin characters are usually keyed by one sun; the demo matches that (`Sun` energy `0.3`).

Ambient is a flat albedo-tinted fill through `EMISSION`, with `ambient_light_disabled` on the shader so Godot’s IBL does not wash the bands. That is closer to a toon “minimum color” than to Unity’s GI.

---

## Outer shadow

Mendez adds a **second** NdotL: offset, harder step, warmer (or at least different) tint. The screenshot shows a thin extra band sitting inside the main shadow on hair, clothes, and shoes.

**Ours (Phase 1):** `OuterShadowColor` / `Offset` / `Smoothness` / `Strength` on `ToonPreset`. After the main `shade`, a second two-sided cut at `shadow_threshold - offset` paints `(1 - shade) * shade_outer` into the outer tint. Strength `0` is a no-op. Hair `0.85`, cloth `0.75`; face/metal/weapon stay off so the face map and metal Phong are not muddied.

**Why it matters:** that extra band is a large part of the “painted” terminator in Genshin. A single `smoothstep` looks like a cheap toon; two stacked steps look like a brush stroke.
---

## Anisotropic hair

Mendez: isolate a streak with a **texture mask** and `LightDot`; kill the sides with a Fresnel (`pow(1 - saturate(dot(N, V)))`). Highlight exists only in light.

**Ours (Phase 6):** Hair slot can bind `HairHighlightTex` (`looks/raiden_hair_highlight.png`, greyscale streaks extracted from the purple islands of `gltf_embedded_1`). When bound, `light()` mixes toward `mask * shade * (1 - fresnel)` with `hair_highlight_blend` (default 1) and a dedicated `hair_highlight_fresnel` (default 5). Additive rim is gated off while the mask drives spec. Missing map → dual-lobe Kajiya-Kay (`UseAnisotropicSpecular = true`, quiet `specular_strength = 0.07`). A/B: `--hair-ab`.

**Observations**

- Genshin’s hair highlight is a packed-channel mask, not a true anisotropic BRDF. The extracted albedo streaks are lookdev-quality, not Hoyoverse lightmaps — re-run `tools/generate_raiden_hair_highlight.py` to iterate.
- Dual-lobe Kajiya-Kay remains the fallback and can still be mixed in via `HairHighlightBlend < 1`.
- Fresnel polarity now matches the breakdown when the mask is on (suppress at silhouette). The general additive rim stays for cloth/face and for hair without a map.

---

## Face shadow

Mendez shows why raw NdotL fails on a face: as the key light orbits, the terminator crawls across nose and lips. Genshin uses a **face lightmap**:

- R = shadow shape for light in 0–180°
- G = shadow shape for 180–360°
- Head `forward` / `right` vs `light.xz` → angle → sample the map

The result is a stable, art-directed cheek shadow that does not follow facial topology.

**Ours (Phase 5):** Face slot binds `FaceShadowTex` (`looks/raiden_face_shadow.png`, painted R/G SDF — no official lightmap in the GLB). `ApplyCharacterLook` writes `head_forward` / `head_right` each frame from a `HeadAxes` Marker3D (Raiden has no skeleton). Shader `use_face_shadow` replaces NdotL with the map sample (soft `smoothstep`, still `min` with cast shadows). Missing map → old NdotL. Outer shadow stays 0 on the face preset. A/B: `--face-ab`.

Face preset still owns warm `ShadowColor`, smoothness on the **sampled** terminator, low rim, **no outline**.

**Observations**

- The painted SDF is readable lookdev, not Hoyoverse quality — iterate with `tools/generate_raiden_face_shadow.py` or hand-paint over the face UV.
- If the cheek lands on the wrong side, rotate `HeadAxes` 180° Y or swap R/G in the generator; do not flatten normals.

---

## Metallic

Mendez: not a PBR metal. Sample a **gradient texture** with `u = dot(N, normalize(V + L))` (half-vector), optionally warp UVs with a normal map. The highlight is a moving colored band, like a 1D matcap.

Ours: the shader path is in (`UseMetallicGradient` + `metallic_gradient_tex` sampled with `dot(NORMAL, half_dir)` into `SPECULAR_LIGHT`, gated by cel `shade`). Shared ramp: `materials/textures/gold_metallic_gradient.tres`. **Raiden’s metal preset leaves the flag off** and keeps isotropic Blinn-Phong (`specular_size = 96`, `specular_strength = 0.22`), same as weapon — flip the flag on that preset (or a future look) to enable the band. No detail-normal UV warp yet; the Metal look slot (`*acc*`, ordered before Hair so `Hair_Accs` matches) is the mask until a packed control map exists.

**Observations**

- Tight Phong can fake a bright glint on gold, but it is a round blob, not a sweeping band. The GIFs in the breakdown (Itto buckle, Sucrose brooch) are exactly that sweeping band — available when `UseMetallicGradient` is on.
- Distortion with a normal map is how Genshin puts repeating scale/pattern into metal-on-fabric. Deferred until Phase 0 detail normals + art.
- Weapon and metal share Phong numbers again on Raiden; the gradient path is opt-in per preset.

---

## Light color and fog

Mendez finishes the character shader with light color × fog.

We multiply `LIGHT_COLOR` in `light()`. Fog is **not** in the character shader; it is `Environment` fog (`density = 0.0012`, cool purple). At character scale that fog is almost invisible. The screenshot in the breakdown is a much stronger atmospheric wrap.

If the demo should match those Lumine plates, fog has to be an artistic grade on the character (or a stronger height fog), not a scene-scale aerial perspective.

---

## Outline

Mendez argues the **traditional** method (duplicate, scale, flip normals) is less precise, and uses a post-process instead:

1. Depth-edge detect
2. Normal-edge detect
3. Combine
4. On faces, push depth so eyes / nose / mouth do not ink

**Ours (Phase 7):** Both paths. The inverted hull (`genshin_outline.gdshader` as `NextPass`) still supplies albedo-tinted lines on hair/cloth/metal. A Forward+ `CompositorEffect` (`ToonOutlineCompositorEffect` + `toon_outline.glsl`) adds screen-space edges via **relative reverse-Z depth Sobel** on the MSAA-resolved `R32Sfloat` depth target (`AccessResolvedDepth`, no `NeedsNormalRoughness` — that flag blacks out custom `light()`). A fragment `DEPTH` write for face flatten was tried and **removed**: even a conditional assignment disables early-Z/MSAA coverage and stipples every mesh sharing `genshin_toon`. Face/weapon stay hull-off; inner face ink is limited by the relative threshold.

A/B: `--outline-ab` → `screenshots/outline_*.png`.

---

## Edge highlight

Mendez is explicit: the white rim around the character **is not a Fresnel**. It is a Sobel on the depth buffer, kept on the side with greater depth, then shifted slightly down so it sits like an anime backlight.

**Ours (Phase 7):** Compositor far-side depth edge + a few pixels of screen-Y offset, additive white. Cloth/hair `RimStrength = 0` so Fresnel no longer doubles as the anime rim. Face/weapon/metal may still carry a timid Fresnel for local sheen.

---
## Tonemap, dithering, glow

Godot 4.7 `Environment.tonemap_mode` enum: Linear `0`, Reinhardt `1`, **Filmic `2`**, ACES `3`, AGX `4`. Older notes in this repo that called `tonemap_mode = 2` “ACES” were wrong.

| | Mendez | This project |
| --- | --- | --- |
| Tonemap | Custom Gran Turismo (keeps saturation, tames highs) | Filmic (`tonemap_mode = 2`), exposure `1.0`, `adjustment_saturation = 1.1` |
| Dither | Listed as a feature (likely 8-bit banding), usually in post | Ordered Bayer 4×4 on `shade` in `genshin_toon.gdshader`, gated to the terminator; hair/cloth `dither_strength = 0.03` |
| Glow | Not the focus | Explicitly off (`glow_enabled = false`) |

Mendez’s own comparison: no tonemap blows out; Neutral is flat; **ACES contrast-desaturates**; GT is the cartoon pick. Phase 3 A/B on this project (same sun, glow off, exposure `1.0`):

- **ACES** — higher mid sat on some regions but punches bright whites toward grey (`bright_sat` lowest among the useful modes).
- **AGX** — darkest / most muted overall; fails the poster-color bar.
- **Linear / Reinhardt** — identical at `tonemap_white = 1.0`; punchier mean sat but darker plate; highlights do not roll off as kindly as Filmic.
- **Filmic + sat `1.0`** — readable bands, but cloth/skin chroma is quieter than with the boost.
- **Filmic + sat `1.1`** — winner: whites keep tint, cel bands still read. Locked on `main.tscn`. No GT `CompositorEffect` (gate did not trip).

Fog A/B left density at `0.0012`; `0.008` milks the dress terminator toward the fog tint.

**Ours (Phase 8):** Dither lives in the toon shader (not the compositor) so it only touches the cel terminator — a full-screen pass would re-detect shade cuts and ink every band. `dither_strength = 0` is today’s look; face/metal/weapon stay at 0. A/B: `--dither-ab` → `screenshots/dither_off.png` / `dither_on.png`.

---

## Material system vs a monolithic shader

The Unity breakdown is one character shader with many keywords. This repo splits **look data** from **shader code**:

| Slot (`looks/raiden_shogun.tres`) | Preset | Outline | Notes vs Genshin |
| --- | --- | --- | --- |
| Face | Warm terminator, weak spec | Off | Painted R/G SDF + head axes; NdotL fallback |
| Hair | Cool shadow; mask highlight (Kajiya-Kay fallback) | On | `HairHighlightTex` from albedo streaks; outer shadow + dither |
| Weapon | Metal Phong | Off | Hull would hollow the blade |
| Metal | Phong (gradient flag off on Raiden) | On | Slot `*acc*` before Hair so Hair_Accs is metal |
| Dress / body / fallback | Cloth | On (body has 3 mm depth bias) | Outer shadow + terminator dither |

That data-driven split is healthier than a 200-uniform mega-shader, and it is not in Mendez’s article. Extra maps live on `LookSlot` (`FaceShadowTex`, `HairHighlightTex`, `ControlTex`, `DetailNormalTex`); `ApplyCharacterLook` binds them with `use_*` flags. Face SDF and hair highlight are assigned on Raiden; control / detail-normal sampling waits for later phases.

---

## What we have that the breakdown does not discuss

- Half-Lambert wrap and an explicit shadow-map remap with `fwidth` AA.
- Dual-lobe shifted hair specular as a fallback when no highlight mask is bound.
- Per-slot double-sided + outline width + depth bias.
- Opaque-only discipline (`discard` instead of `ALPHA`) so depth sorting stays correct — important in Godot, invisible in the Unity post.
- MSAA 4x + FXAA, 8K orthogonal shadow atlas, Ultra PCF.
- Turntable (`SpinY`) and auto-frame camera for judging bands from every yaw.

These are production/demo choices, not Genshin features. They should stay even if the look is brought closer to the breakdown.

---

## Priority of gaps

Ordered by how much they move a still toward the Unity/Genshin plates (most items landed):

1. **Face lightmap** — painted SDF landed (Phase 5); official Hoyoverse maps still optional.
2. **Outer shadow band** — landed on hair/cloth (Phase 1).
3. **Metallic half-vector gradient** — path + ramp landed; Raiden metal preset leaves it off (Phong).
4. **Custom tonemap (GT / saturation-preserving)** — Phase 3 locked Filmic + sat `1.1`; full GT compositor still optional if that grade ever fails.
5. **Post-process outline + edge highlight** — Phase 7: depth Sobel compositor (default on) + hull fallback; cloth/hair Fresnel rim off.
6. **Hair highlight mask** — albedo-extracted streak + Fresnel suppress landed (Phase 6); Kajiya-Kay remains fallback.
7. **Dither** — Phase 8: ordered Bayer on the terminator in the toon shader (hair/cloth).

Glasses parallax is an extra in the Unity post and can stay out of scope.

---

Staged implementation plan: [`genshin-like-refactor-plan.md`](genshin-like-refactor-plan.md).

## Suggested reading order in this repo

- Unity source: [`genshin-impact-character-shader-breakdown.md`](genshin-impact-character-shader-breakdown.md)
- Toon pass: `shaders/genshin_toon.gdshader`
- Hull pass: `shaders/genshin_outline.gdshader`
- Compositor outline: `scripts/ToonOutlineCompositorEffect.cs`, `shaders/toon_outline.glsl`
- Knobs: `scripts/resources/ToonPreset.cs`, `materials/presets/*.tres`
- Binding: `scripts/ApplyCharacterLook.cs`
- Scene grade: `scenes/main.tscn` (`Environment` + `Sun`)
