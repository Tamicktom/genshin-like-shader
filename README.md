# Genshin-like Shader

Godot 4.7 project that explores a **Genshin Impact–inspired** character look: cel shading, rim light, and textured outlines.

The shader stack is **opt-in**. It only runs on nodes that use `apply_genshin_shader.gd`. Models without that script keep their original materials.

## Current state

| Piece | Status |
| --- | --- |
| Main scene | `scenes/main.tscn` — lighting, ground, camera, Raiden instance |
| Demo character | Raiden Shogun GLB under `assets/raiden-shogun/` |
| Toon shading | Applied via script on the Raiden scene only |
| Outline | Inverted hull, colored from local albedo; skipped on face and katana |
| Shadows | Orthogonal directional map, 8192 atlas, Ultra PCF |
| Anti-aliasing | MSAA 4x + FXAA |
| Window | 1280×720, stretch `expand`, camera reframes on resize |

Engine: **Godot 4.7** (Forward Plus). Main scene is set in `project.godot`.

The demo character slow-spins in `_process` so lighting and outlines can be judged from every angle.

## Project layout

```text
assets/raiden-shogun/     Character model + textures
materials/                Reserved for shared .tres materials
scenes/
  main.tscn               Playable demo scene
  raiden-shogun.tscn      Character + apply script
scripts/
  apply_genshin_shader.gd Opt-in material applicator
  frame_character_camera.gd  Auto-frames target on load/resize
shaders/
  genshin_toon.gdshader   Cel shading / rim / specular
  genshin_outline.gdshader Inverted-hull outline
```

## How to run

1. Open the folder in Godot 4.7+.
2. Press **F5** (runs `scenes/main.tscn`).

## Applying the look to another model (opt-in)

The shader does **not** auto-apply to every mesh in the scene.

1. Instance your model (GLB/glTF, etc.).
2. On the model root (or a parent that should own the look), attach `scripts/apply_genshin_shader.gd`.
3. Assign (or leave empty to auto-load):
   - `toon_shader` → `shaders/genshin_toon.gdshader`
   - `outline_shader` → `shaders/genshin_outline.gdshader`
4. Tune `outline_width` / `outline_color` (tint) in the inspector.
5. Run the scene. On `_ready()`, the script walks all child `MeshInstance3D` nodes and replaces surface materials.

To **exclude** a model, simply do not attach the script (or set `apply_on_ready = false`).

Requirements for a good result:

- Meshes with UV0 and an albedo texture (or solid albedo color).
- Prefer opaque materials. Writing `ALPHA` in the toon shader is avoided on purpose so depth sorting stays correct.

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
- Hair can switch to a simple Kajiya-Kay anisotropic band instead of an isotropic blob.
- Light-side rim, gated by the cel shade.

Important: the material stays in the **opaque** pipeline. Assigning `ALPHA` would push meshes into transparency sorting and can make the character look “see-through”.

### 2. `genshin_outline.gdshader` (next pass)

Inverted-hull outline:

- `cull_front` so only back faces of an extruded shell are drawn.
- `depth_draw_never` so one mesh’s hull does not erase another’s silhouette.
- Vertex stage expands in **clip XY** (screen pixels). The view-space normal’s Z is flattened first so pointed vertices (fingertips) do not grow claw spikes.
- A small `outline_depth_bias` can pull the hull toward the camera (body uses a few millimeters; must stay thinner than a finger).
- Fragment samples the **same albedo UV** as the mesh, then darkens / slightly boosts saturation so the line follows local color (purple cloth → purple line).

Wired as `material.next_pass` on each toon `ShaderMaterial` **except** face and katana (see below).

### 3. `apply_genshin_shader.gd`

For each surface:

1. Reads the active material’s albedo texture/color.
2. Classifies the mesh (face / hair / metal / katana / dress / body) from texture path and node name.
3. Builds a `ShaderMaterial` with the toon shader and a per-kind preset.
4. Builds an outline `ShaderMaterial` with the same albedo inputs, unless the kind skips the hull.
5. Sets outline as `next_pass`.
6. Assigns the stack as a surface override (original mesh materials stay untouched on disk).

**Presets** (texture atlases `2_0.png` … `2_5.png`, or `gltf_embedded_N`, plus name hints):

| Kind | Typical match | Notes |
| --- | --- | --- |
| Face | `2_0.png`, face / eye / teeth | Warm terminator, double-sided, **no outline** |
| Hair | `2_1.png`, “hair” | Cool shadow, anisotropic spec, double-sided |
| Dress | “dress” | Cloth preset, double-sided |
| Metal | `2_5.png`, “katana”, “acc” | Tighter specular |
| Katana | `2_5.png` or “katana” | Double-sided, **no outline** (painted line on the albedo) |
| Body / cloth | everything else | Two-tone cloth/skin, backfaces discarded |

Face and katana skip the inverted hull on purpose. A hull on the head shell becomes a dark oval over the forehead; a hull on the thin blade reads as a hollow sword.

## Useful knobs

**On the apply script (inspector)**

- `outline_width` — line thickness (demo uses `2.0`)
- `outline_color` — multiply tint over the textured outline (white = no tint)
- `apply_on_ready` — auto-apply when the node enters the tree

**On the toon shader**

- `shadow_threshold` / `shadow_smoothness` / `shadow_color`
- `cast_shadow_softness` — how the shadow map blends into the cel band
- `double_sided` — keep on for hair, dress, face, katana; off for body
- `light_intensity` / `ambient_strength`
- `rim_strength` / `specular_strength`

**On the outline shader**

- `outline_darken` — how dark the sampled texture becomes as ink
- `outline_saturation` — local color punch on the line
- `outline_depth_bias` — camera pull in meters (keep below thin-part thickness)

**Scene lighting** (`main.tscn`)

- One directional sun, Orthogonal shadows, max distance `14`, atlas `8192`, Ultra filter.
- PCSS (`light_angular_distance`) stays at `0` so the contact shadow does not crawl while the model rotates.
- Mild ambient + slight glow; cel bands stay readable.

## Display / AA

Configured in `project.godot`:

- Stretch mode `canvas_items`, aspect `expand` (window resize friendly)
- MSAA 3D = 4x
- Screen-space AA = FXAA
- Directional shadow size `8192`, soft filter quality Ultra

The demo camera listens to viewport `size_changed` and reframes the character.

## Notes / limits

- Not a 1:1 Genshin recreation (no face SDF light map, no post-process outline).
- Opt-in by design: you choose which roots get the script.
- Very thin lace/alpha hair may need a dedicated cutout pass later; cutout is currently disabled globally to protect depth.
- Outline is a per-mesh hull. Internal silhouettes (one part in front of another on the same mesh) are not inked.
- Face and katana rely on painted albedo lines instead of a hull.
