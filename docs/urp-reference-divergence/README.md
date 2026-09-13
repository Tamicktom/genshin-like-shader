# Genshin shader divergence analysis

## Purpose

This package explains why the Godot result differs from both the local Unity URP sample and the broader Genshin character look. It separates shader defects from intentional adaptations, asset limitations, and features that the minimal Unity sample does not contain.

The analysis targets the clean `experimentation-1` branch and does not propose literal Unity emulation when a Godot-native approach is visually stronger.

## Executive conclusion

The project has more features than the reference, but several high-impact paths are not producing the result described by the current documentation.

### Highest-priority findings

| Priority | Finding | Classification | Consequence |
| --- | --- | --- | --- |
| P0 | Hair and cloth used `light_wrap == shadow_threshold` with a centered two-sided step after clamping `N·L` to zero. | **Resolved** — signed `N·L` restored; presets normalized to Half-Lambert `LightWrap = 0.5` | Back-facing samples can reach the full shadow band. |
| P0 | The active face texture had practically identical R and G channels (actually a grayscale map replicated into RGBA), while the shader expected distinct left/right angular masks. | **Resolved** — mirrored-UV single-channel contract | Left/right light sides sample mirrored UVs of the R channel. |
| P0 | The face A/B images were full-body captures where the face occupied too few pixels to validate cheek-shadow direction or stability. | **Resolved** — `--face-yaw` close-up + `--debug-ab` harness | Face path can be validated at controlled light yaw. |
| P1 | The Godot ambient term is always added through emission, whereas the URP reference takes the component-wise maximum of indirect and direct light. | **Resolved** — `ambient_max_blend` subtracts the emission floor from direct | Shadow and lit bands stay separated under a single key light. |
| P1 | Hull outlines and a scene-wide depth Sobel are active together. | **Resolved** — ownership split + masked compositor | Character lines no longer double with an unmasked ground horizon. |
| P1 | The compositor has no normal-edge input or character mask. | **Resolved** (mask) / engine compromise (normals) — `CharacterMaskPass` gates edges; normals still blocked by `NeedsNormalRoughness` | Environment edges stay off in character-only mode; normal creases remain hull-owned. |
| P1 | `Hair_Accs` resolves to the metal slot because `*acc*` is checked before hair. | **Resolved** — `HairAccessory` slot before Metal | Hair accessories keep hair preset, mask, and dither path. |
| P1 | The metallic gradient path exists but is disabled on the Raiden metal preset. | **Deferred** — code path + `--metal-ab` remain; Raiden keeps Phong | Gold stays a round Phong highlight until the ramp look is preferred. |
| P2 | Face depth flattening, `head_position`, control-map, and detail-normal configuration are bound but unused. | Inactive configuration (`head_position` removed) | The resources imply capabilities that do not exist at runtime. |
| P2 | Documentation values for outline scale, glow, and exposure do not exactly match serialized scene data. | **Resolved** — README synced to `OutlineWidthScale = 1.8` and engine-default glow/exposure | Reproduction and visual tuning stay aligned with `main.tscn`. |

## What is already sound

The following choices should generally be preserved:

- data-driven `CharacterLook`, `LookSlot`, and `ToonPreset` separation;
- opaque rendering and `discard` instead of writing `ALPHA`;
- world-space head axes uploaded by the character script;
- screen-pixel hull width as a practical Godot alternative;
- cast-shadow remapping into the cel tint instead of multiplying the surface to black;
- feature fallback paths for missing face and hair maps;
- keeping `NeedsNormalRoughness` disabled while it breaks custom `light()` materials;
- visual comparison sweeps and a rotating character test scene.

## Recommended reading order

1. [Reference baseline and comparison method](01-reference-baseline.md)
2. [Lighting divergences](02-lighting-divergences.md)
3. [Face-shadow divergences](03-face-shadow-divergences.md)
4. [Outline and post-process divergences](04-outline-and-post-process-divergences.md)
5. [Materials, assets, and binding](05-materials-assets-and-binding.md)
6. [Prioritized improvement roadmap](06-prioritized-roadmap.md)
7. [Visual validation protocol](07-visual-validation.md)

## Existing documentation relationship

[`shader-comparison-with-genshin-breakdown.md`](../shader-comparison-with-genshin-breakdown.md) is a useful feature inventory. This package goes deeper into equations, active resource values, renderer constraints, and cases where a feature marked “Close” is not actually demonstrated by the current runtime data.

[`genshin-like-refactor-plan.md`](../genshin-like-refactor-plan.md) records why features were introduced. A completed phase means the code path exists; it does not by itself prove that the active preset, texture, or capture demonstrates the intended result.

## Severity interpretation

- **P0 — correctness:** fix or isolate before further look tuning.
- **P1 — visual impact:** major contributors to the gap after correctness is restored.
- **P2 — polish and maintainability:** important, but unlikely to transform the image alone.

See [the roadmap](06-prioritized-roadmap.md) for implementation order, expected impact, and regression risks.
