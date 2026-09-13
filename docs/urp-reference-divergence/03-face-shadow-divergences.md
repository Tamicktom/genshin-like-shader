# Face-shadow divergences

## Summary

The Godot face path is structurally plausible, but its active texture does not satisfy the shader's documented R/G contract. The face capture also lacks enough resolution to validate direction, cheek shape, or temporal stability.

This is the most important distinction in the report:

- the **shader path exists**;
- the **active asset does not demonstrate the path**;
- the existing A/B screenshots do not prove visual correctness.

## 1. Reference and Godot use different map encodings

Classification: **active divergence**

### URP reference

The reference samples the red channel of one texture. Right-side lighting is handled by horizontally mirroring UV:

```text
value = right_dot_light > 0
  ? texture(face_map, vec2(1 - uv.x, uv.y)).r
  : texture(face_map, uv).r
```

It compares this value to an angle derived from `forward · light`.

### Godot

The Godot shader samples one UV and expects two packed channels:

```text
map_rg = texture(face_shadow_tex, uv).rg
value = right_dot_light < 0 ? map_rg.g : map_rg.r
```

Both encodings can work. They are not interchangeable:

- a one-channel URP map needs mirrored UV logic;
- an R/G map needs independently authored or generated hemispheres;
- duplicating one grayscale image into R and G without mirroring gives no directional asymmetry.

Source: [`genshin_toon.gdshader`, lines 148–170](../../shaders/genshin_toon.gdshader).

## 2. The active face map has no meaningful R/G separation

Classification: **confirmed data defect**

The active file is [`looks/raiden_face_shadow.png`](../../looks/raiden_face_shadow.png). Direct channel inspection found:

| Measurement | Result |
| --- | ---: |
| Size | 1024 × 1024 |
| Mode | RGBA, 8-bit |
| Pixels where R differs from G | 2 out of 1,048,576 |
| Maximum absolute R/G difference | 2/255 |
| Mean absolute R/G difference | approximately 0.0000029 |

The channels are effectively identical. Yet [`generate_raiden_face_shadow.py`, lines 82–96](../../tools/generate_raiden_face_shadow.py) is written to generate mirrored `cheek_r` and `cheek_g` values and save an RGB image.

This indicates a mismatch between generator intent and the currently committed/loaded artifact. Possible causes include:

- the active PNG was produced by another process or older generator;
- the expected generated file was overwritten;
- Godot is loading an older imported `.ctex`;
- the image shown under the expected path is not the output described by the current script.

The report does not infer which event occurred. The measurable result is sufficient: channel selection in the shader is currently almost a no-op.

### Required correction

Choose and enforce one contract:

1. **URP-compatible single-channel contract:** store one directional map and mirror UV by light side.
2. **Packed R/G contract:** regenerate or hand-author genuinely different channels and verify channel deltas before import.

For the broader Genshin target, either can be valid. The packed contract is convenient if official assets already use directional channels.

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

## 10. Existing A/B evidence is insufficient

Classification: **validation defect**

`screenshots/face_ndl.png` and `screenshots/face_map.png` are 1280×720 full-body captures. The visible face is only a small region of the image. They show a broad tonal difference, but they cannot answer:

- whether the shadow moves to the correct cheek;
- whether R/G selection works;
- whether the map boundary follows facial features;
- whether the transition is stable during yaw;
- whether eyes, nose, and mouth are incorrectly shadowed;
- whether the front gate snaps near side/back angles.

A valid comparison requires a fixed close-up camera and at least five controlled light/head angles. See [the visual validation protocol](07-visual-validation.md).

## Priority

1. Verify or regenerate the face map so R and G are meaningfully distinct, or switch to mirrored single-channel sampling.
2. Add a shader debug output and close-up angular sweep.
3. Add per-character direction offset/flip controls.
4. Decide front/back behavior deliberately.
5. Decouple face softness from body cel softness.
6. Replace the procedural map with authored data before final look tuning.
