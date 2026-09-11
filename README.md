# Genshin-like Shader

Godot 4.7 project that explores a **Genshin Impact–inspired** character look: cel shading, rim light, and textured outlines.

The shader stack is **opt-in**. It only runs on nodes that use `apply_genshin_shader.gd`. Models without that script keep their original materials.

## Current state

| Piece | Status |
| --- | --- |
| Main scene | `scenes/main.tscn` — lighting, ground, camera, Raiden instance |
| Demo character | Raiden Shogun GLB under `assets/raiden-shogun/` |
| Toon shading | Applied via script on the Raiden scene only |
| Outline | Colored from local albedo (darkened texture sample) |
| Anti-aliasing | MSAA 4x + FXAA |
| Window | 1280×720, stretch `expand`, camera reframes on resize |

Engine: **Godot 4.7** (Forward Plus). Main scene is set in `project.godot`.

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

Spatial shader, opaque depth write, double-sided (`cull_disabled`).

**Fragment**

- Samples albedo texture × color.
- Optional alpha cutout via `discard` (off by default).
- Soft ambient fill through `EMISSION` (kept low to avoid wash-out).

**Light**

- Half-Lambert term, then a soft step (`shadow_threshold` / `shadow_smoothness`) for a two-tone cel band.
- Shadow side is tinted with `shadow_color` (cool purple bias by default).
- Hard-ish specular blob and a light-side rim.

Presets in `apply_genshin_shader.gd` tweak face / hair / metal when texture paths or mesh names match (e.g. `2_0.png` face atlas).

Important: the material stays in the **opaque** pipeline. Assigning `ALPHA` would push meshes into transparency sorting and can make the character look “see-through”.

### 2. `genshin_outline.gdshader` (next pass)

Inverted-hull outline:

- `cull_front` so only back faces of an extruded shell are drawn.
- Vertex stage expands in **screen space** for a width that stays more stable with distance.
- Fragment samples the **same albedo UV** as the mesh, then darkens / slightly boosts saturation so the line follows local color (purple cloth → purple line, skin → skin-toned line).

Wired as `material.next_pass` on each toon `ShaderMaterial`.

### 3. `apply_genshin_shader.gd`

For each surface:

1. Reads the active material’s albedo texture/color.
2. Builds a `ShaderMaterial` with the toon shader.
3. Builds an outline `ShaderMaterial` with the same albedo inputs.
4. Sets outline as `next_pass`.
5. Assigns the stack as a surface override (original mesh materials stay untouched on disk).

## Useful knobs

**On the apply script (inspector)**

- `outline_width` — line thickness (demo uses `~1.25`)
- `outline_color` — multiply tint over the textured outline (white = no tint)
- `apply_on_ready` — auto-apply when the node enters the tree

**On the toon shader**

- `shadow_threshold` / `shadow_smoothness` / `shadow_color`
- `light_intensity` / `ambient_strength`
- `rim_strength` / `specular_strength`

**On the outline shader**

- `outline_darken` — how dark the sampled texture becomes as ink
- `outline_saturation` — local color punch on the line

**Scene lighting** (`main.tscn`)

- One directional sun, mild ambient, glow off — tuned so cel bands stay readable.

## Display / AA

Configured in `project.godot`:

- Stretch mode `canvas_items`, aspect `expand` (window resize friendly)
- MSAA 3D = 4x
- Screen-space AA = FXAA

The demo camera listens to viewport `size_changed` and reframes the character.

## Notes / limits

- Not a 1:1 Genshin recreation (no face SDF light map, no hair anisotropic strand system, no post outline).
- Opt-in by design: you choose which roots get the script.
- Very thin lace/alpha hair may need a dedicated cutout pass later; cutout is currently disabled globally to protect depth.
