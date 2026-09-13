# Outline and post-process divergences

## Summary

The project currently combines two independent outline systems:

1. an albedo-tinted inverted hull on selected materials;
2. a fixed-color, scene-wide depth Sobel compositor.

This is not inherently wrong, but both systems frequently describe the same silhouette. The result can be heavier and less controlled than either the minimal URP hull or a production depth-and-normal character post-process.

## 1. Hull expansion uses a valid but different method

Classification: **intentional adaptation**

### URP reference

The reference expands vertices in world space along the world normal, scaled by camera distance and FOV. It preserves all normal components and approximately stabilizes width in screen space.

### Godot

[`genshin_outline.gdshader`, lines 17–37](../../shaders/genshin_outline.gdshader) transforms the vertex and normal to view space, removes the normal's Z component, projects the flattened normal, and offsets clip-space XY by a pixel width:

```text
view_normal.z = 0
clip_normal = projection * vec4(normalize(view_normal), 0)
pixel_offset = normalize(clip_normal.xy)
pixel_offset *= outline_width * clip_w * 2 / viewport_size
clip_xy += pixel_offset
```

Advantages:

- width is directly expressed in screen pixels;
- distance and FOV behavior is more predictable;
- eliminating view-normal Z reduces nose, chin, and fingertip spikes.

Risks:

- normals near the view axis lose their meaningful direction after flattening;
- the artificial X epsilon can bias degenerate cases;
- hard or split normals still cause gaps;
- silhouette expansion no longer follows the true 3D normal;
- foreshortened surfaces can produce uneven corners.

This should be retained as a Godot-native baseline unless close-up tests show systematic gaps. Baked smooth outline normals or vertex-color width control would be a higher-quality next step than literal world-space parity.

## 2. Outline depth behavior differs from the reference

Classification: **active divergence**

The URP outline pass uses normal opaque depth defaults and the expanded hull also appears in `DepthOnly`. It additionally supports a texture-masked view-space Z offset.

The Godot hull uses:

```text
render_mode unshaded, cull_front, depth_draw_never
```

It still depth-tests, but never updates depth. This avoids one shell erasing another mesh's silhouette, yet it also means:

- overlapping hull surfaces do not occlude one another through hull depth;
- ordering between adjacent outlined meshes depends more heavily on the base depth;
- the compositor sees base geometry depth, not expanded hull depth;
- hull color and post-process edge can occupy slightly different screen locations.

The hull is also pulled toward the camera by an absolute view-space amount. Since the character root is scaled to `0.1`, a fixed bias can be large relative to fingers, accessories, and cloth thickness.

Recommendation: keep `depth_draw_never` as a tested fallback, but validate hull overlaps with a depth-debug view. Express depth bias relative to character scale or remove per-slot bias where clip-space expansion alone is sufficient.

## 3. The reference's Z-offset mask has no Godot equivalent

Classification: **missing high-value control**

The URP pass samples `_OutlineZOffsetMaskTex` in the vertex shader. Black regions receive a remappable depth pull, and face materials receive an additional offset. This gives artists spatial control over where the shell wins depth tests.

Godot exposes only one scalar `outline_depth_bias` per slot. It cannot:

- suppress the outline around selected UV regions;
- treat eyes, brows, nose, and mouth differently;
- reduce depth pull on thin accessories while retaining it elsewhere;
- reproduce face-specific outline control.

For the Genshin target, a packed per-vertex or texture mask is more useful than globally increasing the bias.

## 4. Hull color is not lit

Classification: **active divergence with acceptable intent**

The reference runs complete toon lighting and fog on the hull, then multiplies by `_OutlineColor`.

Godot's hull is unshaded:

```text
line = adjust_saturation(albedo) * outline_darken * outline_tint
```

This gives stable locally colored ink, which can be preferable to outlines that brighten under lights. It does, however, differ in these ways:

- no shadow-band variation on the outline;
- no material emission in the line;
- no direct-light color;
- line color is driven by albedo details, which can make outlines noisy;
- behavior under fog depends on Godot's unshaded spatial fog handling rather than explicit shader composition.

For the broader target, stable material-family colors are reasonable. A dedicated outline-color map or preset color would provide more control than full albedo sampling.

## 5. The compositor outlines the whole scene

Classification: **resolved (P1.3)**

[`CharacterMaskPass`](../../scripts/CharacterMaskPass.cs) renders a half-resolution SubViewport of meshes on the character mask layer. [`toon_outline.glsl`](../../shaders/toon_outline.glsl) gates both dark outline and far-side highlight by that coverage. `EnvironmentOutlineStrength` (default `0`) controls residual scene edges. Face slots set `IncludeInOutlineMask = false` so internal face edges stay quiet without writing fragment `DEPTH`.

Validate with `$GODOT --path . -- --outline-ab` (`outline/mask.png`, `outline/comp_masked.png` vs `outline/comp_unmasked.png`).

## 6. The compositor lacks normal edges

Classification: **engine constraint**

The broader Genshin post-process reference combines depth and normal discontinuities. Godot's compositor intentionally sets `NeedsNormalRoughness = false` because enabling it blacked out the custom `light()` materials.

`NormalThreshold` remains serialized and packed into push constants, but the GLSL never uses it. Current behavior is depth-only:

- silhouettes and depth-separated overlaps are detected;
- coplanar creases with different normals are missed;
- shallow internal contours can disappear;
- depth noise can trigger while important normal changes do not.

This limitation should be stated explicitly in UI and docs. An unused `NormalThreshold` suggests a capability that is absent.

Possible alternatives:

1. render character normals to a dedicated auxiliary viewport or pass;
2. encode an octahedral normal and character mask in a custom buffer;
3. use hull lines for material/internal contours and reserve Sobel for silhouettes;
4. investigate why normal-roughness prepass breaks the custom light path before enabling it globally.

Option 3 is the lowest-risk current architecture.

## 7. Hull and Sobel can double the same edge

Classification: **resolved ownership split (P1.2)**

Controlled modes live under `--outline-ab` (`outline/hull`, `outline/comp_masked`, `outline/combined`, close-ups). Default production uses:

- hull for locally colored silhouettes on hair/cloth/metal/body;
- masked compositor for inter-mesh depth edges and the far-side highlight (`HighlightStrength = 0.12`);
- face excluded from the mask instead of dead `outline_depth_flatten`.

Combined mode is intentional when both systems add value; use the A/B captures to judge thickness before widening either path.

## 8. Face depth flattening is dead configuration

Classification: **inactive configuration**

The face slot sets `FlattenOutlineDepth = true` and `OutlineDepthFlatten = 0.85`. The value is uploaded as `outline_depth_flatten`, but [`genshin_toon.gdshader`, lines 75–77 and 137–140](../../shaders/genshin_toon.gdshader) explicitly does not assign `DEPTH`.

This was disabled for a valid reason: any fragment `DEPTH` write caused MSAA coverage and early-Z problems across materials sharing the shader.

Current practical behavior:

- face hull is disabled;
- face base depth remains unchanged;
- compositor uses the same relative depth threshold as the entire scene;
- the serialized flatten values do nothing.

Do not re-enable a shared fragment-depth write. Prefer a character/face mask, a separate depth-only face pass, or compositor logic that suppresses internal face edges by material identity.

## 9. Highlight is too weak to establish the intended rim

Classification: **resolved (tuned after mask)**

`HighlightStrength` defaults to `0.12` on [`toon_outline_effect.tres`](../../materials/compositor/toon_outline_effect.tres). Validate with `outline/highlight.png` under the character mask so environment pixels do not bloom.

## 10. Thickness is quantized

Classification: **minor implementation limitation**

The compositor rounds `thickness` to an integer:

```text
t = max(int(round(thickness)), 1)
```

Although the C# property accepts continuous values from 0.5 to 8, many inspector values produce identical sampling offsets. The Sobel becomes a sparse wider kernel rather than a true thickened one-pixel edge.

For stable thickness:

- compute a one-pixel edge first;
- dilate the mask in a separate pass or with nearby samples;
- or expose the value as an integer in the resource.

## 11. Recommended ownership split

The most practical architecture with current Godot constraints is:

| Responsibility | Preferred system |
| --- | --- |
| Locally colored outer silhouette | Inverted hull with smooth outline normals |
| Inter-mesh depth intersections | Character-masked depth compositor |
| Normal-only internal contour | Hull/geometry initially; auxiliary normal buffer later |
| Face internal-line suppression | Face/material mask in compositor |
| White far-side anime edge | Character-masked compositor |
| Environment edges | Separate optional profile, not character defaults |

This keeps the useful Godot pixel-width hull while preventing two systems from blindly drawing every edge.

## Priority

1. Add character masking to the compositor.
2. Produce hull-only, compositor-only, and combined captures.
3. Remove or suppress duplicated dark silhouettes.
4. Replace dead face-depth flattening with mask-based suppression.
5. Validate and tune the far-side highlight.
6. Add a per-vertex or texture outline-width/depth mask.
7. Pursue an auxiliary normal buffer only after the current ownership split is stable.
