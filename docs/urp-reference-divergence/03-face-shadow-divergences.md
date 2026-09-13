# Face-shadow divergences

## Summary

The Godot face path is structurally plausible, but its active texture does not satisfy the shader's documented R/G contract. The face capture also lacks enough resolution to validate direction, cheek shape, or temporal stability.

This is the most important distinction in the report:

- the **shader path exists**;
- the **active asset does not demonstrate the path**;
- the existing A/B screenshots do not prove visual correctness.

## 1. Reference and Godot now share a single-channel mirrored-UV contract

Classification: **resolved (P0.2)**

### URP reference

The reference samples the red channel of one texture. Right-side lighting is handled by horizontally mirroring UV:

```text
value = right_dot_light > 0
  ? texture(face_map, vec2(1 - uv.x, uv.y)).r
  : texture(face_map, uv).r
```

### Godot (current)

Godot now matches that contract, with a tunable symmetry axis:

```text
face_uv = vec2(2 * face_mirror_axis - uv.x, uv.y)
sample_uv = right_dot_light < 0 ? face_uv : uv
value = texture(face_shadow_tex, sample_uv).r
```

`FaceMirrorAxis` defaults to `0.5` and is stored per `LookSlot`. Face softness is decoupled via `face_shadow_softness` / `FaceShadowSoftness`.

## 2. The active face map is a single grayscale directional gradient

Classification: **resolved data understanding (was reported as R/G defect)**

The active file is [`looks/raiden_face_shadow.png`](../../looks/raiden_face_shadow.png). Channel inspection found:

| Measurement | Result |
| --- | ---: |
| Size | 1024 × 1024 |
| Mode | RGBA, 8-bit |
| Pixels where R differs from G | 2 out of 1,048,576 |
| Center pixel | `(128, 128, 128, 128)` |
| Corner pixel | `(0, 0, 0, 0)` |

The channels are a **replicated grayscale** directional map, not an independently authored R/G pair. It does **not** match the older packed-R/G generator output (that script now writes a luminance fixture to `looks/raiden_face_shadow_fixture.png` and does not overwrite the committed asset).

Validate with `tools/check_face_map.py` and `$GODOT --path . -- --face-yaw`.

## 3. Angular equations differ substantially

Classification: **active divergence**

### URP angle

The reference primarily uses forward alignment:

```text
angle = -0.5 * dot(forward_xz, light_xz) + 0.5
face_lit = step(angle, map_value)
```

It chooses the map side from `right · light`, but the threshold itself comes from `forward · light`.

It also supports:

- `_FaceDirectionOffset`, a Y-axis rotation in radians;
- `_FlipFaceDirection`;
- upright correction based on object up and light Y;
- modulo wrapping.

### Godot angle

The Godot path derives its threshold mostly from right alignment:

```text
dot_f = dot(forward_xz, light_xz)
front = smoothstep(-soft, soft, dot_f)

dot_r = dot(right_xz, light_xz)
right_angle = acos(dot_r) / PI * 2
directional_angle = hemisphere_remap(right_angle)

face_lit = smoothstep(
  -soft,
  soft,
  map_value - directional_angle
) * front
```

This can cover the front hemisphere, but it is not a direct port of the reference:

- no configurable direction offset;
- no direction flip;
- no upright correction;
- a front-facing gate forces back light toward shadow;
- an `acos` right-axis angle replaces the reference's forward-dot threshold;
- soft comparison replaces the reference hard step.

Soft comparison is visually defensible. Missing orientation controls are a practical problem because the imported model and `HeadAxes` marker may not align exactly with the authored map.

## 4. Head axes are correctly separated from mesh normals

Classification: **intentional adaptation**

[`ApplyCharacterLook.cs`, lines 64–80](../../scripts/ApplyCharacterLook.cs) uploads world-space `basis.Z` as forward and `basis.X` as right every frame. The shader converts Godot's view-space `LIGHT` to world space before taking XZ dots.

This is a sound adaptation and avoids deriving facial direction from deformed or smoothed vertex normals. It also rotates correctly with the character root.

Limitations:

- `HeadAxes` has identity rotation in [`raiden-shogun.tscn`, lines 12–14](../../scenes/raiden-shogun.tscn), so there is no per-character correction currently serialized.
- `head_position` is uploaded but never read.
- a root marker is less robust than a head bone for animated neck/head rotation.

Recommended extension:

- expose a yaw offset or rotate `HeadAxes` explicitly per character;
- support a head-bone target when a skeleton exists;
- add a debug view for `dot_f`, `dot_r`, selected channel, and final face shade.

## 5. The front gate changes back-light behavior

Classification: **intentional behavior requiring validation**

Godot multiplies the map result by:

```text
front = smoothstep(-soft, soft, dot_f)
```

When the light moves behind the head, the whole face tends toward the shadow color. This may be desirable for a clear anime key light, but it does not reproduce the reference's modulo behavior exactly.

The correct choice should be based on a yaw sweep:

- at 0° front light, the face should be mostly lit;
- at ±90°, the authored cheek shape should be strongest on opposite sides;
- near 180°, behavior should be deliberate and symmetric rather than snapping due to channel selection.

No current close-up sequence demonstrates this.

## 6. Hard step versus soft step

Classification: **intentional adaptation**

The URP reference uses a hard `step`; Godot uses `smoothstep(-soft, soft, map - angle)`.

Advantages of Godot's soft step:

- reduces temporal aliasing when the light or character moves;
- accommodates an 8-bit generated map;
- exposes an artist-tunable transition width.

Risks:

- using the same `shadow_smoothness` as body cel shading couples unrelated controls;
- wide smoothing turns the authored face boundary into a generic gradient;
- filtering and soft comparison together can blur a low-quality map excessively.

Recommendation: retain a soft edge, but give the face path a dedicated softness in angular/map-value units.

## 7. Texture import is mostly appropriate

Classification: **sound configuration with naming caveat**

The shader sampler omits `source_color`, so it is treated as data. The `.import` resource has no mipmaps and no HDR sRGB conversion. Linear filtering is enabled.

That is appropriate for a face threshold map. However:

- the PNG metadata reports sRGB colorspace;
- import configuration should explicitly document “non-color data” rather than relying on sampler behavior;
- no mipmaps can shimmer when the face becomes small, while mipmaps can also corrupt threshold shapes if generated naively.

For character gameplay distance, consider a controlled mip strategy or force face evaluation only where it remains visually relevant.

## 8. The generated map is not an official face lightmap

Classification: **asset limitation**

The generator infers eye positions from albedo and paints a heuristic radial cheek function. It is useful for exercising a shader path, but it cannot reproduce:

- character-specific cheek and nose shadow shapes;
- intentionally protected eye/mouth regions beyond a rough band;
- multiple authored angular transitions;
- official lightmap channel semantics;
- corrections for the actual facial UV topology and in-game art direction.

The current image also visually resembles a broad continuous face-space gradient rather than a clearly authored threshold atlas. Even after fixing R/G separation, asset quality will remain a major limit.

Recommended asset order:

1. obtain a known valid face lightmap if licensing and provenance allow;
2. otherwise hand-paint the mask over the actual UV layout;
3. use the procedural generator only as a debug fixture.

## 9. Face outline suppression is not implemented by this path

Classification: **inactive configuration**

The face slot sets:

```text
FlattenOutlineDepth = true
OutlineDepthFlatten = 0.85
```

`ApplyCharacterLook` binds `outline_depth_flatten`, but the toon shader intentionally never writes `DEPTH`. The property has no visual effect. It only causes that material to be included in the per-frame head-axis update list.

Face hull outline is disabled, while the scene-wide compositor can still detect face/hair depth discontinuities. This is not equivalent to the reference's face-specific outline Z offset or the broader post-process face-depth suppression described in the existing breakdown.

See [outline and post-process divergences](04-outline-and-post-process-divergences.md).

## 10. Close-up yaw evidence is now available

Classification: **resolved validation (P0.3)**

`$GODOT --path . -- --face-yaw` freezes spin, resets character yaw, frames a face close-up, and sweeps the sun through `0°, ±45°, ±90°, ±135°, 180°`, capturing final color plus face-map and face-angle debug views under `screenshots/face_yaw/`.

`$GODOT --path . -- --debug-ab` captures full-body diagnostic views under `screenshots/debug/` (signed N·L, wrapped N·L, shade, cast, face map/angle, slot ID).

Historical `face_ndl.png` / `face_map.png` remain useful records but fail the close-up criterion.

## Priority

1. ~~Verify or regenerate the face map so R and G are meaningfully distinct, or switch to mirrored single-channel sampling.~~ Done (mirrored UV).
2. ~~Add a shader debug output and close-up angular sweep.~~ Done (`--debug-ab`, `--face-yaw`).
3. Add per-character direction offset/flip controls.
4. Decide front/back behavior deliberately.
5. ~~Decouple face softness from body cel softness.~~ Done (`face_shadow_softness`).
6. Replace the procedural/heuristic map with authored data before final look tuning.
