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

Classification: **confirmed active divergence**

[`toon_outline.glsl`, lines 40–66](../../shaders/toon_outline.glsl) computes a depth Sobel from the resolved scene depth. There is no object ID, render layer, stencil, or character mask.

Consequences:

- the ground/background horizon receives a dark line;
- unrelated props and environment occlusions are outlined;
- the effect cannot use different thresholds for face, hair, body, and environment;
- face-outline suppression cannot be targeted;
- UI or transparent ordering may be affected by the `PostTransparent` callback depending on scene composition.

The current `scene_loaded.png` and dither A/B captures visibly show a line along the ground horizon. That line comes from scene depth, not the character style.

Recommendation: render or derive a character mask and multiply both dark-outline and highlight masks by it. If the desired game style includes environment outlines, use a separate weaker environment profile.

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

Classification: **active divergence**

Hair, cloth, metal, body, and fallback slots can receive the hull while the compositor processes their depth silhouettes again.

Typical result:

```text
base silhouette
  + outward albedo hull
  + depth edge at base geometry
  = two nearby dark bands
```

Depending on width and anti-aliasing, these bands merge into a line that is thicker or darker than intended. The issue varies by camera distance because the hull is pixel-sized but the Sobel threshold is relative depth with integer-rounded thickness.

Recommendation:

- use the hull for locally colored silhouette/material lines;
- use the compositor only for missing inter-object depth edges and the far-side highlight;
- suppress compositor darkening where the hull already covers the silhouette, or disable the hull on slots where the compositor is sufficient;
- capture isolated hull-only, Sobel-only, and combined comparisons at identical framing.

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

Classification: **inactive-in-practice feature**

The compositor computes a far-side depth highlight and offsets it vertically, matching the broader anime edge-highlight concept. The active resource relies on default `HighlightStrength = 0.01`.

At one percent additive strength, it is unlikely to read as a deliberate white anime edge at normal viewing distance. Meanwhile hair and cloth rim strengths are zero, so neither system clearly owns a strong rim.

Recommendation:

- validate the mask in a debug output at strength 1;
- verify side selection under reverse Z;
- tune width, Y offset, and final strength at gameplay distance;
- apply the character mask before increasing strength;
- avoid reintroducing a generic Fresnel rim as a substitute.

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
