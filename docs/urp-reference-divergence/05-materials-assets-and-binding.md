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

Classification: **confirmed binding defect**

Slot resolution is first-match-wins. The metal slot appears before hair and matches `*acc*`; the model contains a mesh named `Hair_Accs.002`.

As a result, that surface receives:

- metal preset instead of hair preset;
- isotropic tight Phong instead of hair highlight behavior;
- no hair highlight map;
- no hair outer-shadow band;
- no hair dither;
- single-sided rendering unless otherwise overridden.

The current README describes this order as intentional so hair accessories become metal. The mesh name, however, combines hair and accessories; name-only classification cannot determine whether every surface should be metal.

Recommended correction:

1. inspect the mesh surface visually and by texture islands;
2. add a specific `Hair_Accs` slot before broad patterns;
3. assign an explicit preset based on actual geometry;
4. avoid relying on `*acc*` for mixed-content meshes.

If the mesh truly is only ornaments, rename the slot and document that evidence. The current heuristic is fragile.

## 3. Metallic gradient exists but is disabled

Classification: **inactive high-impact feature**

The metal preset assigns [`gold_metallic_gradient.tres`](../../materials/textures/gold_metallic_gradient.tres) but sets:

```text
UseMetallicGradient = false
```

`ToonPreset.ApplyToMaterial` therefore sets `use_metallic_gradient` to false. Raiden's metal continues through the isotropic half-vector power path.

This explains a likely visual gap:

- Phong produces a round, localized glint;
- the Genshin-style ramp produces a broad, colored moving band;
- merely having a ramp resource does not affect the demo.

Before enabling it globally, ensure the metal slot contains only metal. Otherwise hair accessories or mixed meshes may receive a gold band.

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

Classification: **documentation defect**

Examples:

- README says demo `OutlineWidthScale` is `2.0`; [`main.tscn`, line 127](../../scenes/main.tscn) serializes `1.8`.
- Docs describe glow as explicitly false; the scene omits `glow_enabled`, relying on the engine default.
- Docs describe exposure `1.0`; the scene omits an explicit exposure property.
- Face flattening is set in the look resource even though code marks it unused.
- Phase documentation says all phases are complete while metal ramp and several map paths remain inactive in the active Raiden look.

These do not all change the image, but they make exact reproduction difficult. Serialized state should be treated as implementation truth.

## 10. Existing feature-state matrix

| Feature | Code path | Active on Raiden | Data quality |
| --- | --- | --- | --- |
| Standard cel | Yes | Yes | Base equation currently defective |
| Face directional map | Yes | Face | Active R/G data effectively identical |
| Hair mask highlight | Yes | Hair | Heuristic albedo extraction |
| Kajiya-Kay | Yes | Replaced when hair mask blend = 1 | Fallback only |
| Outer shadow | Yes | Hair and cloth | Depends on defective shade floor |
| Dither | Yes | Hair and cloth | Depends on defective shade floor |
| Metallic gradient | Yes | No | Ramp assigned, flag off |
| Control map | Declared/bound | No effective behavior | No contract |
| Detail normal | Declared/bound | No effective behavior | No implementation |
| Face depth flatten | Declared/bound | No effective behavior | Deliberately disabled |
| Hull outline | Yes | Hair/cloth/metal/body/fallback | Combined with compositor |
| Normal-based post outline | No | No | Blocked by renderer issue |
| Depth compositor | Yes | Whole scene | No character mask |

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
