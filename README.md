# Genshin-like Shader

Godot 4.7 **.NET** project that explores a **Genshin Impact–inspired** character look: cel shading, rim light, and textured outlines.

The shader stack is **opt-in** and **data-driven**. Character shading lives in `CharacterLook` / `ToonPreset` resources. Nodes that use `ApplyCharacterLook.cs` bind albedo from the imported mesh and apply the first matching slot. Models without that script keep their original materials.

## Current state

| Piece | Status |
| --- | --- |
| Main scene | `scenes/main.tscn` — lighting, ground, camera, Raiden instance |
| Demo character | Raiden Shogun GLB under `assets/raiden-shogun/` |
| Look data | `looks/raiden_shogun.tres` + shared presets under `materials/presets/` |
| Toon shading | Applied via look table on the Raiden scene only |
| Outline | Inverted hull (albedo-tinted) + compositor reverse-Z depth Sobel (default on) |
| Shadows | Orthogonal directional map, 8192 atlas, Ultra PCF |
| Anti-aliasing | MSAA 4x + FXAA |
| Window | 1280×720, stretch `expand`, camera reframes on resize |

Engine: **Godot 4.7 .NET** (Forward Plus). Main scene is set in `project.godot`.

The demo character slow-spins via `SpinY.cs` so lighting and outlines can be judged from every angle.

## Project layout

```text
assets/raiden-shogun/     Character model + textures
looks/                    Per-character CharacterLook tables
materials/presets/        Shared ToonPreset resources (face, hair, …)
materials/textures/       Shared ramps (e.g. gold metallic GradientTexture1D)
materials/compositor/     ToonOutlineCompositorEffect resource
scenes/
  main.tscn               Playable demo scene
  raiden-shogun.tscn      Character + look applicator + spin
scripts/
  ApplyCharacterLook.cs   Opt-in look applicator
  ToonOutlineCompositorEffect.cs  Post-process outline / edge highlight
  SpinY.cs                Demo turntable (rotates parent)
  FrameCharacterCamera.cs Auto-frames target on load/resize
  CaptureSceneScreenshot.cs Lookdev screenshots + A/B sweeps
  ShaderParams.cs         snake_case shader uniform names
  resources/
    ToonPreset.cs         Shading knobs only
    LookSlot.cs           Match patterns + outline flags
    CharacterLook.cs      Ordered slots + shaders
shaders/
  genshin_toon.gdshader   Cel shading / rim / specular
  genshin_outline.gdshader Inverted-hull outline
  toon_outline.glsl       Compositor compute (depth Sobel)
```

## How to run

1. Open the folder in **Godot 4.7 .NET** (Mono build) with the .NET SDK installed.
2. Press **F5** (runs `scenes/main.tscn`).

## Applying the look to another model (opt-in)

The shader does **not** auto-apply to every mesh in the scene.

1. Duplicate or create a `CharacterLook` resource (see `looks/raiden_shogun.tres`).
2. Fill ordered `LookSlot`s: name/texture wildcards, which `ToonPreset`, outline / double-sided flags.
3. Instance your model (GLB/glTF, etc.).
4. On the model root, attach `scripts/ApplyCharacterLook.cs` and assign the look.
5. Optionally tune `OutlineWidthScale` on the applicator (demo uses `2.0`).
6. Run the scene. On `_Ready()`, the script walks all child `MeshInstance3D` nodes and replaces surface materials.

To **exclude** a model, simply do not attach the script (or set `ApplyOnReady = false`).

Requirements for a good result:

- Meshes with UV0 and an albedo texture (or solid albedo color).
- Prefer opaque materials. Writing `ALPHA` in the toon shader is avoided on purpose so depth sorting stays correct.

### Adding a slot

Edit the character’s `.tres` (or the Inspector):

1. Append a `LookSlot` to `Slots` (order matters — first match wins).
2. Set `NamePatterns` / `TexturePatterns` with case-insensitive wildcards (`*hair*`, `*gltf_embedded_1*`).
3. Point `Preset` at a shared `materials/presets/*.tres` (or a new one).
4. Set `EnableOutline`, `DoubleSided`, and optional `OutlineDepthBias`.

Anything that matches no slot uses `FallbackPreset` / fallback outline flags.

## How the shaders work

### 1. `genshin_toon.gdshader` (base pass)

Spatial shader, opaque depth write. Rasterizer is `cull_disabled`; solid parts still drop backfaces in the fragment shader via `double_sided = false`.

**Fragment**

- Samples albedo texture × color.
- Optional alpha cutout via `discard` (off by default).
- Soft ambient fill through `EMISSION` (kept low to avoid wash-out).
- If `double_sided` is false, non-front faces are discarded so thin volumes (hands, feet) do not read as hollow.

**Light**

- Half-Lambert term, then a soft step (`shadow_threshold` / `shadow_smoothness`) for a two-tone cel band.
- Shadow side is tinted with `shadow_color` (cool purple bias by default).
- Cast shadows from the light’s shadow map are remapped through `cast_shadow_softness` into the same cel tint (not multiplied to black).
- Hair can use a painted highlight mask (`HairHighlightTex`) gated by shade with Fresnel suppress; Kajiya-Kay remains the fallback when no mask is bound.
- Light-side rim, gated by the cel shade.

Important: the material stays in the **opaque** pipeline. Assigning `ALPHA` would push meshes into transparency sorting and can make the character look “see-through”.

### 2. `genshin_outline.gdshader` (next pass)

Inverted-hull outline:

- `cull_front` so only back faces of an extruded shell are drawn.
- `depth_draw_never` so one mesh’s hull does not erase another’s silhouette.
- Vertex stage expands in **clip XY** (screen pixels). The view-space normal’s Z is flattened first so pointed vertices (fingertips) do not grow claw spikes.
- A small `outline_depth_bias` can pull the hull toward the camera (body uses a few millimeters; must stay thinner than a finger).
- Fragment samples the **same albedo UV** as the mesh, then darkens / slightly boosts saturation so the line follows local color (purple cloth → purple line).

Wired as `material.NextPass` when the resolved slot has `EnableOutline = true`.

### 3. Look resources + `ApplyCharacterLook.cs`

For each surface:

1. Reads the active material’s albedo texture/color.
2. Resolves the first matching `LookSlot` from the assigned `CharacterLook` (node name or texture path wildcards).
3. Builds a `ShaderMaterial` with the look’s toon shader, stamps albedo, applies the slot’s `ToonPreset`.
4. Optionally builds an outline `ShaderMaterial` and sets it as `NextPass`.
5. Assigns the stack as a surface override (original mesh materials stay untouched on disk).

**Raiden slots** (`looks/raiden_shogun.tres`):

| Slot | Typical match | Notes |
| --- | --- | --- |
| Face | `*face*`, `*eye*`, `*teeth*`, `*2_0.png`, `*gltf_embedded_0*` | Warm terminator, face shadow map, double-sided, **no outline** |
| Weapon | `*katana*`, `*2_5.png`, `*gltf_embedded_5*` | Blinn-Phong metal, double-sided, **no outline** |
| Metal | `*acc*` (before Hair so `Hair_Accs` matches) | Tight Phong (half-vector gradient available, flag off), outline on |
| Hair | `*hair*`, `*2_1.png`, `*gltf_embedded_1*` | Cool shadow, highlight mask (Kajiya-Kay fallback), double-sided |
| Dress | `*dress*` | Cloth preset, double-sided |
| Body | `*body*` | Cloth preset, `OutlineDepthBias = 0.003` |
| Fallback | everything else | Cloth preset, outline on |

Face and weapon skip the inverted hull on purpose. A hull on the head shell becomes a dark oval over the forehead; a hull on the thin blade reads as a hollow sword.

Shared presets live under `materials/presets/` (`face`, `hair`, `cloth`, `metal`, `weapon`).

## Useful knobs

**On the apply script (inspector)**

- `Look` — `CharacterLook` resource
- `OutlineWidthScale` — multiplies each slot’s outline width (demo uses `2.0`)
- `ApplyOnReady` — auto-apply when the node enters the tree

**On the CharacterLook / LookSlot**

- Slot wildcards, preset reference, `EnableOutline`, `DoubleSided`, `OutlineDepthBias`
- Optional maps: `FaceShadowTex`, `HairHighlightTex`, `ControlTex`, `DetailNormalTex`
- Global `OutlineTint` / `OutlineDarken` / `OutlineSaturation`
- `MeshLodBias` / `TextureLodBias`

**On ToonPreset / toon shader**

- `ShadowThreshold` / `ShadowSmoothness` / `ShadowColor` (shader uniforms stay snake_case)
- `CastShadowSoftness` — how the shadow map blends into the cel band
- `LightIntensity` / `AmbientStrength`
- `RimStrength` / `SpecularStrength`
- `HairHighlightBlend` / `HairHighlightFresnel` — mask vs Kajiya-Kay mix and silhouette suppress
- `UseMetallicGradient` / `MetallicGradientTex` / `MetallicStrength` — 1D half-vector gold ramp (opt-in; off on Raiden metal)

**On the outline shader**

- `outline_darken` — how dark the sampled texture becomes as ink
- `outline_saturation` — local color punch on the line
- `outline_depth_bias` — camera pull in meters (keep below thin-part thickness)

**Scene lighting** (`main.tscn`)

- One directional sun, Orthogonal shadows, max distance `14`, atlas `8192`, Ultra filter.
- PCSS (`light_angular_distance`) stays at `0` so the contact shadow does not crawl while the model rotates.
- Filmic tonemap (`tonemap_mode = 2`), exposure `1.0`, glow off, slight `adjustment_saturation` (`1.1`) so cel bands stay poster-colored rather than grey. Phase 3 A/B (Linear / Reinhardt / Filmic / ACES / AGX) locked Filmic + sat `1.1`; GT compositor skipped. Fog stays at density `0.0012` (denser wraps milk the dress terminator).

## Display / AA

Configured in `project.godot`:

- Stretch mode `canvas_items`, aspect `expand` (window resize friendly)
- MSAA 3D = 4x
- Screen-space AA = FXAA
- Directional shadow size `8192`, soft filter quality Ultra

The demo camera listens to viewport `SizeChanged` and reframes the character.

## Notes / limits

- Not a 1:1 Genshin recreation (painted face SDF + hair highlight mask are lookdev extracts, not official lightmaps; compositor outline is depth Sobel without `NeedsNormalRoughness`, which blacks out custom `light()`; no vertex-color outline width).
- Opt-in by design: you choose which roots get the applicator + which look resource.
- Matching is still name/path based in the look table — the heuristics moved out of code into data.
- Very thin lace/alpha hair may need a dedicated cutout pass later; cutout is currently disabled globally to protect depth.
- Hull outline is per-mesh; the compositor adds screen-space depth edges (silhouette + occlusions). Hair/cloth/metal keep the hull for purple-tinted lines.
- Face and weapon skip the hull. Do not write `DEPTH` from `genshin_toon.gdshader` — a fragment depth write disables MSAA coverage and stipples the whole character.
