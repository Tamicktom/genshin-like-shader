# Reference baseline and comparison method

## Scope

This analysis uses three distinct baselines:

1. **Literal URP parity** — behavior implemented by `URPSimpleGenshinShaders`.
2. **Genshin visual target** — the broader character-rendering style, including features absent from the minimal URP repository.
3. **Godot constraints** — differences that are required or beneficial in Godot 4.7 Forward+.

The distinction matters because the reference repository calls itself a minimal proof of concept. It is primarily a facial shadow-map example built on a simplified URP toon shader. Specular models, rim lighting, hair shadows, metal ramps, normal maps, baked outline normals, and many other production features are explicitly absent.

The Godot project should therefore not delete a useful feature merely because the URP sample lacks it. Literal differences are defects only when they break the intended image or violate a required data contract.

## Evidence categories

Every finding in this package uses one of these categories:

| Category | Meaning |
| --- | --- |
| Confirmed defect | Code or data cannot produce its documented behavior. |
| Active divergence | Working behavior differs materially from the selected target. |
| Intentional adaptation | A deliberate Godot-specific solution with a defensible result. |
| Inactive configuration | A property, resource, or path exists but has no runtime effect. |
| Asset limitation | Shader support exists, but source textures or mesh data are insufficient. |
| Reference limitation | The URP proof of concept is too narrow to define the complete target. |

## What the URP reference actually renders

The reference has four active passes and one unimplemented pass:

| Pass | State and purpose |
| --- | --- |
| `ForwardLit` | Back-face culling, opaque color, depth write, GI, lights, emission, and fog. |
| `Outline` | Front-face culling on an expanded hull; runs the same lighting and then multiplies the result by the outline color. |
| `ShadowCaster` | Back-face culling, alpha clipping, shadow bias, depth-only output. |
| `DepthOnly` | Writes the expanded outline hull into the depth prepass. |
| `DepthNormals` | Mentioned but commented out. |

Sources: [`SimpleGenshinFacial.shader`, lines 123–301](../../../URPSimpleGenshinShaders/SimpleGenshinFacial.shader) and [`SimpleGenshinFacial_Shared.hlsl`, lines 160–227](../../../URPSimpleGenshinShaders/SimpleGenshinFacial_Shared.hlsl).

### Surface data

The fragment path reads:

- RGBA albedo from `_BaseMap * _BaseColor`;
- optional RGB emission selected by a channel mask;
- optional occlusion selected by a channel mask;
- the face map twice: original UV for left lighting and horizontally mirrored UV for right lighting;
- world-space interpolated normal, world-space position, and world-space view direction.

The face map is non-color data. The reference README requires sRGB to be disabled or the texture to be imported as a directional lightmap.

### Indirect light

The URP implementation deliberately removes directional spherical-harmonic detail:

```text
indirect = max(SampleSH(0), indirect_min_color)
indirect *= lerp(1, occlusion, 0.5)
```

This flat average environment term reduces the 3D appearance while preventing fully black unlit surfaces.

### Direct cel light

For ordinary meshes:

```text
NoL = dot(normal_ws, light_direction_ws)
cel = smoothstep(midpoint - softness, midpoint + softness, NoL)
cel *= occlusion
cel *= lerp(1, shadow_attenuation, receive_shadow_amount)
light_tint = lerp(shadow_color, white, cel)
```

The default midpoint is `-0.5` and softness is `0.05`. Importantly, `NoL` is **not clamped before the step**. The default therefore illuminates most of the visible hemisphere and leaves a shadow band on surfaces facing sufficiently away from the light.

For a face without the special map, `_IsFace` remaps the cel result from `[0, 1]` to `[0.5, 1]`. With the face map enabled, its binary result replaces this remapped `NoL` value.

### Final composition

Each additional light is reduced to 25% intensity. Direct and indirect terms are not added conventionally:

```text
direct = main_light + additional_lights
raw_light = max(indirect, direct) // component-wise
final = albedo * raw_light + emission
```

This `max` prevents the flat ambient floor from washing out direct-light contrast. It is a significant part of the reference look, not an implementation detail.

Four properties exposed by the Unity material are dead in this version: `_IndirectLightMultiplier`, `_DirectLightMultiplier`, `_MainLightIgnoreCelShade`, and `_AdditionalLightIgnoreCelShade`. They should not be treated as reference behavior.

## Face shadow-map contract

The reference does not use an R/G-packed map. It samples one channel from one authored texture:

1. Rotate the light around world Y by `_FaceDirectionOffset`.
2. Read object forward and right axes from the object-to-world matrix.
3. Project those vectors and the light onto XZ.
4. Use the sign of `right · light` to choose the original or horizontally mirrored UV.
5. Map `forward · light` from `[-1, 1]` to an angular threshold.
6. Compare that threshold with the texture's red channel using a hard `step`.

In compact form:

```text
map_value = right_dot_light > 0
  ? texture(face_map, vec2(1 - uv.x, uv.y)).r
  : texture(face_map, uv).r

angle = (-forward_dot_light + 1) * 0.5
face_lit = step(angle, map_value)
```

The source includes upright correction, direction flipping, modulo wrapping, and a configurable angular offset. These details are easy to lose during a port.

Source: [`SimpleGenshinFacial_LightingEquation.hlsl`, lines 26–50](../../../URPSimpleGenshinShaders/SimpleGenshinFacial_LightingEquation.hlsl).

## Outline contract

The URP hull expands in world space along the world normal. Expansion is multiplied by view-space distance and camera FOV, producing approximately constant screen width:

```text
world_expansion = outline_width * abs(view_z) * camera_fov * 0.00005
position_ws += normal_ws * world_expansion
```

It then samples a vertex-stage Z-offset mask. Black areas receive a configurable view-space depth pull; face materials receive an additional hard-coded pull. The expanded hull participates in the depth-only pass and the color pass uses full toon lighting plus fog before outline tinting.

Sources: [`NiloOutlineUtil.hlsl`, lines 8–51](../../../URPSimpleGenshinShaders/NiloOutlineUtil.hlsl), [`NiloZOffset.hlsl`, lines 6–35](../../../URPSimpleGenshinShaders/NiloZOffset.hlsl), and [`SimpleGenshinFacial_Shared.hlsl`, lines 194–207](../../../URPSimpleGenshinShaders/SimpleGenshinFacial_Shared.hlsl).

## Reference limitations

The following are not available as literal guidance from this repository:

- hair highlight masks or anisotropic highlights;
- metallic half-vector ramps;
- rim lighting;
- a second cel-shadow band;
- screen-space outlines or edge highlights;
- normal maps and packed control maps;
- production-quality face self-shadow suppression;
- baked smooth outline normals;
- alpha-card-specific rendering.

Recommendations for these features are evaluated against the broader Genshin target and the project's existing Mendez notes, not against absent URP code.

## Comparison rule

A Godot behavior is considered worth changing when at least one is true:

- its documented feature is mathematically inactive or uses invalid data;
- it erases stable light/shadow separation;
- it introduces outlines or lighting on unrelated scene geometry;
- it makes a character-specific art map ineffective;
- it prevents a high-impact Genshin feature that is already implemented;
- it creates scale-, camera-, or material-order-dependent artifacts without a clear benefit.

Differences that improve screen-space consistency, preserve opaque sorting, or avoid known Godot renderer failures are retained and documented as adaptations.
