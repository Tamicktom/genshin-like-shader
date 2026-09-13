# Materials, assets, and binding divergences

## Summary

The data-driven look architecture is one of the project's strongest decisions. Most problems in this area come from active values, ambiguous matching, or resources that imply a feature is enabled when it is not.

## 1. Architecture is appropriately separated

Classification: **intentional adaptation**

The project separates:

- `CharacterLook`: character-level shaders, ordered slots, outline defaults;
- `LookSlot`: matching, per-part flags, and character-specific maps;
- `ToonPreset`: reusable shading parameters;
- `ApplyCharacterLook`: runtime material construction and uniform binding.

This is easier to maintain than one character-specific shader and should be preserved. It also makes future official face/light maps character data rather than shared material data.

Sources: [`CharacterLook.cs`](../../scripts/resources/CharacterLook.cs), [`LookSlot.cs`](../../scripts/resources/LookSlot.cs), [`ToonPreset.cs`](../../scripts/resources/ToonPreset.cs), and [`ApplyCharacterLook.cs`](../../scripts/ApplyCharacterLook.cs).

## 2. `Hair_Accs` resolves to metal

Classification: **resolved (P1.6)**

`looks/raiden_shogun.tres` now inserts a `HairAccessory` slot matching `*hair_accs*` before Metal. `Accs.002` remains Metal; `Hair_Accs.002` uses the hair preset and highlight map. Enable `LogSlotResolution` on `ApplyCharacterLook` or use `debug_view = 7` / `--debug-ab` to verify.

## 3. Metallic gradient

Classification: **deferred (P1.4)** — harness ready, Raiden off

[`metal.tres`](../../materials/presets/metal.tres) keeps `UseMetallicGradient = false` with `gold_metallic_gradient.tres` assigned for opt-in. Weapon stays Phong. Compare with `$GODOT --path . -- --metal-ab` when revisiting the look.
## 4. Control and detail-normal maps are plumbing only

Classification: **inactive configuration**

`LookSlot` exposes `ControlTex` and `DetailNormalTex`. `ApplyCharacterLook` binds them and their `use_*` booleans. The toon shader declares the samplers but never samples them.

Consequences:

- assigning either texture has no visual effect;
- the API and inspector overstate current capabilities;
- metallic/specular behavior cannot be masked within a mixed mesh;
- detail normals cannot warp the metallic ramp or influence lighting.

Recommendation:

- mark both exports as reserved/unused in inspector documentation;
- implement a written channel contract before sampling `ControlTex`;
- avoid adding more packed textures until surface classification and base lighting are correct.

A plausible future contract must state exact semantics, color space, and defaults, for example:

| Channel | Candidate meaning |
| --- | --- |
| R | material/AO or cel-region mask |
| G | specular intensity |
| B | metallic-ramp mask |
| A | outline width or emission mask |

This table is illustrative, not a recommendation to assume Hoyoverse's packing.

## 5. Face map and hair map are generated approximations

Classification: **asset limitation**

### Face

The face map is not an official directional lightmap. Its active channels also fail the current R/G contract; see [face-shadow divergences](03-face-shadow-divergences.md).

### Hair

The hair highlight mask is extracted from bright purple regions of the albedo through color and luma heuristics. This can:

- bake painted diffuse highlights into a light-dependent specular mask;
- miss darker authored streaks;
- include purple accessories;
- produce no motion relative to hair flow beyond light-side gating;
- double-count highlights already present in albedo.

The fallback Kajiya-Kay path is useful and should remain available. Final quality requires an authored hair lightmap or a clearly designed procedural band.

## 6. Alpha behavior differs from imported material intent

Classification: **active limitation**

`ApplyCharacterLook` forces:

```text
use_alpha_scissor = false
```

for every imported surface, even though the shader supports discard-based scissoring. This preserves opaque depth sorting, but it ignores source material transparency and alpha-card intent.

Potential effects:

- hair cards or lace render as solid polygons;
- texture backgrounds can become visible geometry;
- silhouette and depth Sobel receive card-shaped edges;
- double-sided card surfaces add more depth discontinuities.

The current GLB is described as opaque, so this may not affect Raiden's active surfaces. For reusable architecture, alpha mode should be copied or explicitly configured per slot rather than globally disabled.

## 7. Culling is implemented with fragment discard

Classification: **intentional adaptation with cost**

The base shader uses `cull_disabled` globally, then discards back faces in `fragment()` when a slot is not double-sided.

Advantages:

- one shader supports mixed culling requirements through a uniform;
- per-slot runtime material creation stays simple.

Costs:

- back faces still reach rasterization;
- early-Z and MSAA behavior can be less efficient or stable;
- a uniform branch/discard is used where fixed-function culling would be preferable;
- shadow-pass behavior may not exactly match visible culling.

For a small demo this is acceptable. For production, use separate shader/material variants for single- and double-sided rendering.

## 8. Texture LOD policy favors sharpness over stability

Classification: **intentional choice requiring validation**

Character albedo uses a negative LOD bias (`-0.75`) and high mesh LOD bias. This preserves detail in the full-body shot but can increase shimmer during motion, especially with:

- high-contrast anime linework in albedo;
- MSAA plus FXAA;
- rotating character;
- thin hair and ornament geometry.

Face and hair data maps use no mipmaps. This preserves authored thresholds at close range but makes small-distance sampling less stable.

Recommendation: evaluate temporal footage, not still images alone. Sharp screenshots are not evidence of stable motion.

## 9. Scene and documentation values drift

Classification: **resolved for outline scale / glow / exposure claims**

Serialized truth in [`main.tscn`](../../scenes/main.tscn):

- `OutlineWidthScale = 1.8`
- Filmic tonemap; exposure and glow left at engine defaults unless a sweep overrides them
- Face flattening remains serialized but unused (MSAA); compositor mask replaces it for face-edge suppression

## 10. Existing feature-state matrix

| Feature | Code path | Active on Raiden | Data quality |
| --- | --- | --- | --- |
| Standard cel | Yes | Yes | Signed N·L + Half-Lambert wrap |
| Face directional map | Yes | Face | Mirrored-UV single-channel |
| Hair mask highlight | Yes | Hair / HairAccessory | Heuristic albedo extraction |
| Kajiya-Kay | Yes | Fallback when mask blend < 1 | Fallback |
| Outer shadow | Yes | Off pending retune | Strength 0 |
| Dither | Yes | Off pending retune | Strength 0 |
| Metallic gradient | Yes | No (Raiden) | Ramp assigned, flag off |
| Control map | Declared/bound | No effective behavior | No contract |
| Detail normal | Declared/bound | No effective behavior | No implementation |
| Face depth flatten | Declared/bound | No effective behavior | Replaced by outline mask |
| Hull outline | Yes | Hair/cloth/metal/body/fallback | Combined with masked compositor |
| Normal-based post outline | No | No | Blocked by renderer issue |
| Depth compositor | Yes | Character-masked | CharacterMaskPass |

## 11. Recommended data validation

Add non-rendering validation around look creation:

- log resolved slot per mesh surface in debug builds;
- warn when a slot has a texture for an unused shader path;
- warn when a metallic texture is assigned while the feature is disabled;
- validate that a packed face R/G map has a meaningful channel difference;
- report source alpha mode before forcing opaque behavior;
- expose a material-ID or slot debug color view.

These checks would have revealed the face-channel and mixed `Hair_Accs` issues before look tuning.

## Priority

1. Resolve mixed `Hair_Accs` classification.
2. Repair the face texture contract.
3. Enable and isolate the metallic ramp for true metal surfaces.
4. Remove or clearly label dead exports.
5. Decide per-slot alpha and culling policy.
6. Define control-map semantics only after the active lighting path is stable.
7. Synchronize documentation with serialized scene values.
