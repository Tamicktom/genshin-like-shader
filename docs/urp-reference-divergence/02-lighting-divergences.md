# Lighting divergences

## Summary

The strongest lighting mismatch is not a subtle parameter difference. The standard Godot path clamps negative `N·L`, wraps it, and then places the smoothstep threshold exactly at the minimum wrapped value for hair and cloth. This makes the entire back-facing hemisphere evaluate to the midpoint of the transition.

Several later features—outer shadow, dither, specular gating, and rim gating—consume that compromised shade value. Correcting the base shade signal must come before tuning those features.

## 1. Back-facing surfaces were locked at half shade

Classification: **resolved (P0.1)**

Previous code in [`genshin_toon.gdshader`](../../shaders/genshin_toon.gdshader) clamped `dot(NORMAL, LIGHT)` to `[0, 1]` before wrapping. With hair/cloth `light_wrap == shadow_threshold`, every back-facing pixel landed at the smoothstep midpoint (`shade ≈ 0.5`).

### Fix applied

Signed `N·L` is preserved through the wrap:

```text
raw_ndl = dot(NORMAL, LIGHT)              // [-1, 1]
wrapped_ndl = mix(raw_ndl, 1, light_wrap)
shade = smoothstep(threshold - soft,
                   threshold + soft,
                   wrapped_ndl)
```

All active presets now use `LightWrap = 0.5` (Half-Lambert domain) so thresholds share one meaning: `threshold = (signed_terminator + 1) / 2`. Outer-band and dither strengths on hair/cloth were zeroed pending retune against the corrected terminator.

Validate with `$GODOT --path . -- --debug-ab` and `tools/check_shade_coverage.py`.

## 2. The Godot and URP threshold domains are now explicitly Half-Lambert

Classification: **resolved documentation / domain choice**

The URP reference uses:

```text
smoothstep(-0.55, -0.45, signed_NoL)
```

Godot presets now live in an explicit Half-Lambert `[0, 1]` domain after `mix(signed, 1, 0.5)`. Conversion:

```text
half_lambert_threshold = urp_signed_threshold * 0.5 + 0.5
```

URP `-0.5` corresponds to Half-Lambert `0.25`. Current starting thresholds (hair `0.425`, cloth `0.45`, metal/weapon `0.475`) sit near signed `-0.15` to `-0.05`.

## 3. Ambient composition washes the cel bands

Classification: **resolved (P1.1)**

Godot still writes ambient as emission in [`genshin_toon.gdshader`](../../shaders/genshin_toon.gdshader), but `ambient_max_blend` (default `1`) subtracts that floor from each light’s diffuse contribution:

```text
EMISSION = ALBEDO * ambient_term
DIFFUSE_LIGHT += max(direct - ambient_term * ambient_max_blend, 0)
```

With a single directional key this approximates URP’s `albedo * max(indirect, direct)`. Validate with `$GODOT --path . -- --ambient-ab` and `tools/check_band_separation.py`.

### Historical note

Before the fix, additive ambient raised both bands continuously while sun energy stayed at `0.3`, washing shadow families toward lit. Do not compensate by raising sun energy first.

## 4. Cast-shadow remapping is useful but semantically broad

Classification: **intentional adaptation with caveat**

Current code:

```text
cast_aa = max(cast_shadow_softness, fwidth(ATTENUATION) * 1.5)
cast_shade = smoothstep(0, cast_aa, ATTENUATION)
shade = min(material_shade, cast_shade)
```

This is a good NPR decision: received shadows select the same colored shadow band instead of multiplying the result to black. The `fwidth` term also reduces crawling around shadow-map texels.

The caveat is that Godot's `ATTENUATION` can represent more than binary directional shadow visibility for other light types. A wide fixed range beginning at zero tends to treat most nonzero attenuation as fully lit. If point and spot lights become important, shadow and distance behavior should be tested separately.

The URP reference uses partial shadow reception:

```text
cel *= lerp(1, shadow_attenuation, receive_shadow_amount)
```

Its default amount is `0.65`, so received shadows do not necessarily force the full shadow band. Godot currently uses `min`, which allows cast shadows to dominate completely. This can be a valid Genshin-style choice, but it is not parity.

## 5. Outer shadow is driven by the wrong source on face-map materials

Classification: **latent defect**

The standard path defines `wrap_ndl`, but the face path sets:

```text
wrap_ndl = tex_shadow_dir
```

The outer band then always derives from `wrap_ndl`, using the global `shadow_threshold` and `outer_shadow_offset`. Face currently has `outer_shadow_strength = 0`, so the mismatch is inactive. Enabling the outer band on a face would compare map values rather than using a separately authored face-band contract.

Recommendation: keep face outer-band strength at zero unless the face map explicitly encodes a second band, or define a separate face-band equation.

## 6. Dither amplifies the current lighting defect

Classification: **active divergence dependent on P0**

Dither is applied to `shade` and gated by:

```text
dither_band = 4 * shade * (1 - shade)
```

That gate is mathematically reasonable for a narrow terminator. With the current hair/cloth defect, however, the full back-facing hemisphere can sit at `shade = 0.5`, where the dither amplitude is maximal. It can become a surface treatment rather than terminator anti-banding.

The current full-body `dither_off.png` and `dither_on.png` captures show almost no evaluable difference at character scale. This neither proves that the effect is harmless nor that it is useful.

Recommendation: disable dither while correcting the shade domain, then re-enable it only after a close-up diagnostic view confirms it is spatially confined to a narrow transition.

## 7. Specular and rim are valid extensions, but their masks inherit shade errors

Classification: **reference limitation plus active dependency**

The URP proof of concept has no specular or rim lighting. Their presence in Godot is not a parity defect.

All Godot specular branches are multiplied by `shade`, and rim is also shade-gated. This is appropriate for light-side anime highlights, but the broken shade floor allows highlights farther into the nominally unlit side.

Specific observations:

- Hair's extracted mask replaces Kajiya-Kay completely when `hair_highlight_blend = 1`.
- Metallic ramp code is available but inactive on Raiden.
- Face, metal, and weapon retain a small Fresnel rim while hair and cloth use zero rim.
- The compositor highlight strength is only `0.01`, so it cannot currently replace a strong authored anime rim.

Correct the base shade first, then evaluate each highlight under front, side, and back light.

## 8. Material color is multiplied in a compatible order, but light sums differ

Classification: **partially compatible**

Godot's custom diffuse term is ultimately multiplied by `ALBEDO`, matching the reference's `surface.albedo * rawLightSum`. The common claim that one shader tints “before” albedo and the other “after” is not itself the major problem for one light.

The meaningful differences are:

- Godot sums every light's full tinted diffuse contribution;
- URP reduces additional lights to 25%;
- Godot adds ambient emission;
- URP uses a component-wise maximum between direct and indirect;
- Godot adds custom specular separately;
- scene tonemapping and saturation alter the final band colors.

## 9. Recommended target equation

For the current Godot architecture, a minimal correction that preserves its features is:

```text
raw_ndl = dot(NORMAL, LIGHT)
wrapped_ndl = mix(raw_ndl, 1, light_wrap)

main_shade = smoothstep(
  shadow_threshold - shadow_smoothness,
  shadow_threshold + shadow_smoothness,
  wrapped_ndl
)

cast_shade = remap_shadow_attenuation(ATTENUATION)
shade = min(main_shade, cast_shade)

outer_shade = smoothstep(
  shadow_threshold - outer_offset - outer_softness,
  shadow_threshold - outer_offset + outer_softness,
  wrapped_ndl
)

outer_band = (1 - shade) * outer_shade
toon_tint = mix(shadow_color, lit_color, shade)
toon_tint = mix(toon_tint, outer_shadow_color, outer_band * outer_strength)
```

Then tune `light_wrap` and `shadow_threshold` independently. The minimum wrapped value must be safely below the lower smoothstep edge for any material expected to have a full shadow band.

## Priority

1. Fix signed `N·L` handling and retune thresholds.
2. Re-evaluate ambient/direct balance.
3. Validate cast-shadow semantics.
4. Re-enable and tune outer band and dither.
5. Tune specular, rim, and scene grade only after stable bands exist.
