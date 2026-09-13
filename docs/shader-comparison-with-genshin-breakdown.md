# Shader comparison: this project vs Genshin Unity breakdown

This note compares the Godot shaders in this repo (`shaders/genshin_toon.gdshader`, `shaders/genshin_outline.gdshader`) with Adrian Mendez’s Unity URP recreation of the Genshin Impact character look ([ArtStation](https://www.artstation.com/artwork/wJZ4Gg), local copy in [`genshin-impact-character-shader-breakdown.md`](genshin-impact-character-shader-breakdown.md)).

The Unity write-up is a reconstruction, not Hoyoverse source. It is still the closest public breakdown of the *look* this project is aiming at.

---

## Snapshot

| Feature (Mendez / Genshin look) | This project | Match |
| --- | --- | --- |
| Custom cel lighting (NdotL → soft step → tint) | Half-Lambert + `smoothstep` + `shadow_color` | Close |
| Multiple lights | Godot `light()` runs per light | Yes |
| Cast shadows into the cel band | `ATTENUATION` remapped, not multiplied to black | Close |
| Outer (second) shadow band | Single terminator only | Missing |
| Anisotropic hair | Dual-lobe Kajiya-Kay, gated by shade | Partial |
| Face shadow texture (R/G lightmap) | Painted SDF on Face slot + head axes; NdotL fallback | Close |
| Metallic (half-vector gradient / 1D matcap) | Path + shared ramp ready; Raiden metal preset uses Phong (flag off) | Partial |
| Multiply by light color | `LIGHT_COLOR * light_intensity` | Yes |
| Fog | Scene `Environment` fog, very light | Partial |
| Outline | Inverted hull (`NextPass`), not depth/normal Sobel | Different approach |
| Special face outline (suppress inner face edges) | Face slot disables the hull entirely | Workaround |
| Edge highlight (white Sobel rim, not Fresnel) | Fresnel rim on the lit side | Different approach |
| Custom cartoon tonemapper (Gran Turismo) | Godot Filmic (`tonemap_mode = 2`) + `adjustment_saturation = 1.1`; glow off (Phase 3 A/B; GT compositor not needed) | Partial |
| Dithering | None | Missing |
| Glasses parallax | Not in original Genshin; we do not have it | N/A |

---

## Pipeline context

Mendez splits the look into **character shader** + **screen-space post-process**. Lighting, outer shadow, hair, face map, metal, and fog live in the material. Outline, face-outline suppression, edge highlight, tonemap, and dithering live after the scene is drawn.

This project keeps almost everything **on the mesh**:

- Base pass: `genshin_toon.gdshader` (opaque spatial, custom `light()`).
- Outline pass: `genshin_outline.gdshader` as `material.NextPass`.
- Tonemap, glow, and fog: Godot `WorldEnvironment` in `scenes/main.tscn`.
- Per-part knobs: `ToonPreset` resources applied by `ApplyCharacterLook.cs`.

That split is the main reason several Genshin-looking features are missing or approximated. Depth/normal Sobel, a white silhouette highlight, and a custom tonemapper all need a blit / compositor pass that this repo does not have.

Godot also differs from Unity URP in how lighting is assembled. Albedo is written in `fragment()`, then `DIFFUSE_LIGHT` and `SPECULAR_LIGHT` accumulate in `light()`. The engine multiplies albedo by diffuse. Mendez does `lerp(shadow, base, shade) * texture` in one color. The visual intent is the same; the light-side tint is not. We lerp toward **white**, so the lit region is “albedo × light color”. They lerp toward a separate `_BaseColor`, so the artist can warm the lit band independently of the texture.

---

## Custom lighting

Mendez’s core:

1. `lDot = dot(lightDir, worldNormal)`
2. `lSmooth = smoothstep(0, _LightSmooth, lDot)` with `_LightSmooth ≈ 0.1`
3. `lerp(_ShadowColor, _BaseColor, lSmooth)`
4. Multiply by `_MainTexture`

Ours (`genshin_toon.gdshader`, `light()`):

1. `n_dot_l = clamp(dot(NORMAL, LIGHT), 0, 1)`
2. **Half-Lambert**: `half_lambert = n_dot_l * 0.5 + 0.5`
3. `smoothstep(threshold - soft, threshold + soft, half_lambert)`
4. `min` with a remapped shadow-map term
5. `mix(shadow_color, vec3(1.0), shade)`
6. Add `LIGHT_COLOR * light_intensity`
7. Ambient as `EMISSION = ALBEDO * ambient_color * ambient_strength`

**Observations**

- Half-Lambert wraps light onto the back hemisphere. Genshin’s terminator is a hard-ish cut on raw NdotL. Ours keeps more of the mesh in the “lit” band and needs `shadow_threshold` (~0.47–0.52) to push the cut back. That is a different lighting model, not just a different smoothness.
- Their `_LightSmooth = 0.1` is a one-sided step from 0. Ours is two-sided around a threshold, with per-preset smoothness (face `0.055`, hair `0.025`, cloth `0.032`). Hair is closer to the sharp Genshin cut; face is softer on purpose.
- They tint **both** bands. We only tint the shadow. Lit cloth/skin therefore follows the albedo more faithfully; we cannot warm the lit side without changing the texture or adding a `_lit_color` uniform.
- Cast shadows are handled in the Genshin spirit: they join the same cool cel tint instead of going black. The `fwidth(ATTENUATION)` widen is ours and is a good anti-crawl trick while the turntable spins.
- Multiple lights work in both. Extra lights in Godot **add** more `DIFFUSE_LIGHT`, so a fill light can lift the shadow tint toward white and flatten the two-tone look. Genshin characters are usually keyed by one sun; the demo matches that (`Sun` energy `0.3`).

Ambient is a flat albedo-tinted fill through `EMISSION`, with `ambient_light_disabled` on the shader so Godot’s IBL does not wash the bands. That is closer to a toon “minimum color” than to Unity’s GI.

---

## Outer shadow

Mendez adds a **second** NdotL: offset, harder step, warmer (or at least different) tint. The screenshot shows a thin extra band sitting inside the main shadow on hair, clothes, and shoes.

This shader has one `shade` value. There is no offset NdotL, no second mix, no outer-shadow color.

**Why it matters:** that extra band is a large part of the “painted” terminator in Genshin. A single `smoothstep` looks like a cheap toon; two stacked steps look like a brush stroke. Hair would benefit first (cool main shadow + a slightly warmer or darker inner band).

A faithful port would look roughly like:

- `shade_main` — current term
- `shade_outer` — same NdotL with a shifted threshold and smaller smoothness
- `mix(outer_color, mix(shadow_color, lit, shade_main), shade_outer)`

It does not need a new texture. It does need two extra uniforms on `ToonPreset`.

---

## Anisotropic hair

Mendez: isolate a streak with a **texture mask** and `LightDot`; kill the sides with a Fresnel (`pow(1 - saturate(dot(N, V)))`). Highlight exists only in light.

Ours (hair preset: `UseAnisotropicSpecular = true`):

- Strand axis from `mix(TANGENT, BINORMAL, hair_flow_blend)` (default BINORMAL / UV.v).
- Two Kajiya-Kay lobes (`sin(T·H)` powers) with opposite shifts.
- Soft `smoothstep` band, multiplied by `shade` so it dies in shadow.
- A **separate** Fresnel rim (`rim_strength = 0.04` on hair), not used to mask the spec.

**Observations**

- We model *strand lighting*. They model *a painted streak that happens to be view-dependent*. Genshin’s hair highlight is often a packed-channel mask (specular shape in one channel), not a true anisotropic BRDF.
- Dual-lobe Kajiya-Kay can look more “CG hair” than “anime hair” if `specular_strength` is high. The current hair preset keeps it quiet (`0.07`), which is the right bias.
- We have no hair highlight mask, no spec shift texture, and no dedicated “remove sides with Fresnel” on the spec itself. The general rim is the opposite polarity of their hair Fresnel: they *suppress* the highlight at the silhouette; we *add* a rim there.
- `LightDot` gating is present (`spec * shade`). That part matches.

Highest-value next step for hair is a mask texture (or a packed channel from the official maps) rather than more BRDF lobes.

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

We use the method he rejects: inverted hull, `cull_front`, expand in **clip XY**, flatten view-space normal Z so fingertips do not grow spikes, optional `outline_depth_bias` to pull the hull toward the camera. Line color is darkened, slightly oversaturated albedo, so purple cloth gets a purple line.

**What we already do well**

- Screen-pixel width (`outline_width * clip_pos.w / VIEWPORT_SIZE`) is more stable than object-space extrusion.
- Flattening N.z is the right fix for claw spikes.
- Albedo-tinted ink matches Genshin’s colored outlines better than a flat black line.
- Face and weapon skip the hull. That is a pragmatic stand-in for “special face outline”: no inner face edges, at the cost of no outer head contour from this pass either. The face then relies on painted lines in the texture.

**What we cannot do with a hull**

- Internal silhouettes on the same mesh (belt over dress, hair over shoulder on one surface).
- Variable width from vertex color (Genshin often packs outline width in verts). README already flags this.
- Suppressing only *some* inner edges while keeping the jaw/hairline. Depth-hack face outline is a post-process trick.

A compositor outline (depth + normals, Roystan-style, as Mendez cites) is the real Genshin match. The hull can stay as a fallback for platforms without a blit, or for colored inner lines that depth-Sobel will miss.

---

## Edge highlight

Mendez is explicit: the white rim around the character **is not a Fresnel**. It is a Sobel on the depth buffer, kept on the side with greater depth, then shifted slightly down so it sits like an anime backlight.

We implement the thing he says it is not:

```text
fresnel = pow(1 - N·V, rim_power)
SPECULAR_LIGHT += rim_color * fresnel * rim_strength * shade * LIGHT_COLOR
```

Presets keep it timid (cloth `0.06`, hair `0.04`, face `0.08`). It still:

- follows surface curvature, not the character silhouette
- dies on backfaces / in shadow (`* shade`)
- cannot put a continuous white line around the whole figure
- cannot be offset downward in screen space

For a Genshin-like plate, this rim should probably go *down*, not up, once a Sobel edge-highlight pass exists. Until then it is a reasonable cheap substitute and should stay subtle.

---

## Tonemap, dithering, glow

Godot 4.7 `Environment.tonemap_mode` enum: Linear `0`, Reinhardt `1`, **Filmic `2`**, ACES `3`, AGX `4`. Older notes in this repo that called `tonemap_mode = 2` “ACES” were wrong.

| | Mendez | This project |
| --- | --- | --- |
| Tonemap | Custom Gran Turismo (keeps saturation, tames highs) | Filmic (`tonemap_mode = 2`), exposure `1.0`, `adjustment_saturation = 1.1` |
| Dither | Listed as a feature (likely 8-bit banding) | None |
| Glow | Not the focus | Explicitly off (`glow_enabled = false`) |

Mendez’s own comparison: no tonemap blows out; Neutral is flat; **ACES contrast-desaturates**; GT is the cartoon pick. Phase 3 A/B on this project (same sun, glow off, exposure `1.0`):

- **ACES** — higher mid sat on some regions but punches bright whites toward grey (`bright_sat` lowest among the useful modes).
- **AGX** — darkest / most muted overall; fails the poster-color bar.
- **Linear / Reinhardt** — identical at `tonemap_white = 1.0`; punchier mean sat but darker plate; highlights do not roll off as kindly as Filmic.
- **Filmic + sat `1.0`** — readable bands, but cloth/skin chroma is quieter than with the boost.
- **Filmic + sat `1.1`** — winner: whites keep tint, cel bands still read. Locked on `main.tscn`. No GT `CompositorEffect` (gate did not trip).

Fog A/B left density at `0.0012`; `0.008` milks the dress terminator toward the fog tint.

Dithering only matters after a hard posterize. Our `smoothstep` bands are already a few percent wide, so banding is mild. It becomes useful if outer-shadow + harder steps land.

---

## Material system vs a monolithic shader

The Unity breakdown is one character shader with many keywords. This repo splits **look data** from **shader code**:

| Slot (`looks/raiden_shogun.tres`) | Preset | Outline | Notes vs Genshin |
| --- | --- | --- | --- |
| Face | Warm terminator, weak spec | Off | No face map |
| Hair | Cool shadow, anisotropic on | On | No highlight mask |
| Weapon | Metal Phong | Off | Hull would hollow the blade |
| Metal | Phong (gradient flag off on Raiden) | On | Slot `*acc*` before Hair so Hair_Accs is metal |
| Dress / body / fallback | Cloth | On (body has 3 mm depth bias) | Outer shadow on cloth/hair |

That data-driven split is healthier than a 200-uniform mega-shader, and it is not in Mendez’s article. Extra maps now live on `LookSlot` (`FaceShadowTex`, `ControlTex`, `DetailNormalTex`); `ApplyCharacterLook` binds them with `use_*` flags. Face SDF is assigned on Raiden; control / detail-normal sampling waits for later phases.

---

## What we have that the breakdown does not discuss

- Half-Lambert wrap and an explicit shadow-map remap with `fwidth` AA.
- Dual-lobe shifted hair specular (more “offline anisotropic” than Genshin).
- Per-slot double-sided + outline width + depth bias.
- Opaque-only discipline (`discard` instead of `ALPHA`) so depth sorting stays correct — important in Godot, invisible in the Unity post.
- MSAA 4x + FXAA, 8K orthogonal shadow atlas, Ultra PCF.
- Turntable (`SpinY`) and auto-frame camera for judging bands from every yaw.

These are production/demo choices, not Genshin features. They should stay even if the look is brought closer to the breakdown.

---

## Priority of gaps

Ordered by how much they move a still toward the Unity/Genshin plates:

1. **Face lightmap** — without it the head will always look like generic toon.
2. **Outer shadow band** — cheap, no new textures, big “painted terminator” win (landed on hair/cloth).
3. **Metallic half-vector gradient** — path + ramp landed; Raiden metal preset leaves it off (Phong).
4. **Custom tonemap (GT / saturation-preserving)** — Phase 3 locked Filmic + sat `1.1`; full GT compositor still optional if that grade ever fails.
5. **Post-process outline + edge highlight** — hull cannot do inner edges or the white anime rim.
6. **Hair highlight mask** — Kajiya-Kay is optional once a painted streak exists.
7. **Dither** — last, after the bands get harder.

Glasses parallax is an extra in the Unity post and can stay out of scope.

---

Staged implementation plan: [`genshin-like-refactor-plan.md`](genshin-like-refactor-plan.md).

## Suggested reading order in this repo

- Unity source: [`genshin-impact-character-shader-breakdown.md`](genshin-impact-character-shader-breakdown.md)
- Toon pass: `shaders/genshin_toon.gdshader`
- Hull pass: `shaders/genshin_outline.gdshader`
- Knobs: `scripts/resources/ToonPreset.cs`, `materials/presets/*.tres`
- Binding: `scripts/ApplyCharacterLook.cs`
- Scene grade: `scenes/main.tscn` (`Environment` + `Sun`)
