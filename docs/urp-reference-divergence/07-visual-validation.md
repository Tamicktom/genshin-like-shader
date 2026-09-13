# Visual validation protocol

## Purpose

Shader correctness cannot be established from one attractive final-color screenshot. Each feature needs a framing, motion, light angle, and debug signal that can expose its specific failure mode.

The existing screenshot harness is a strong starting point, but most current captures use a full-body 1280×720 view. That framing is suitable for silhouette and grade comparisons, not face-map channels, dither placement, hair streak shape, or small metal highlights.

## General capture rules

For every comparison:

- use identical camera, character pose, light transform, exposure, and background;
- change one variable only;
- include a debug capture and a final-color capture;
- store parameter values beside the image;
- use lossless PNG;
- capture at least one still and one full turntable;
- inspect both image difference and temporal stability;
- avoid judging a feature while another known defect controls its input.

Recommended naming:

```text
<feature>/<camera>_<light-angle>_<variant>_<debug-or-final>.png
<feature>/<camera>_<variant>_turntable.mp4
```

## Required cameras

### Full body

Use for:

- silhouette width;
- ground/environment edge contamination;
- overall lit/shadow balance;
- grade, fog, and saturation;
- large cast shadows.

Target: character fills roughly 65–75% of image height.

### Face close-up

Use for:

- selected face channel;
- cheek-shadow shape;
- eye/nose/mouth contamination;
- front/side/back transition;
- face/hair outline intersections.

Target: face fills at least 40% of image height. Disable spin during individual angle captures.

### Hair close-up

Use front three-quarter and back three-quarter views for:

- highlight mask shape;
- Kajiya-Kay fallback;
- silhouette suppression;
- outer shadow;
- dither confinement;
- hull gaps on hair cards.

### Metal close-up

Frame one known ornament and the weapon separately. Use a dark neutral background so the highlight band is visible.

### Outline stress close-up

Frame fingers, nose/chin profile, skirt/thigh overlaps, hair/face boundary, and sword edge.

## Lighting angle matrix

Use controlled horizontal light angles relative to head/character forward:

| Angle | Purpose |
| ---: | --- |
| 0° | Front-lit baseline |
| +45° | Early cheek/cloth terminator |
| +90° | Right-side map and silhouette |
| +135° | Back-side transition |
| 180° | Back-lit symmetry and rim |
| -135° | Opposite back-side transition |
| -90° | Left-side map and silhouette |
| -45° | Opposite early terminator |

For face tests, capture at minimum `0°, +90°, 180°, -90°`. A continuous yaw video is required to detect snapping.

## Test matrix

### 1. Base cel signal

Prerequisites: dither off, outer shadow off, specular off, rim off, compositor off.

Captures:

- signed `N·L` debug;
- wrapped value debug;
- final `shade` debug;
- final color;
- turntable.

Pass conditions:

- negative `N·L` remains a gradient in debug, not a uniform clamped field;
- back-facing surfaces reach near-black in shade debug;
- terminator occupies a narrow controllable band;
- no material remains lit solely because wrap equals threshold.

### 2. Indirect/direct balance

Variants:

1. ambient emission 0;
2. current ambient;
3. candidate indirect-floor composition.

Pass conditions:

- shadow color remains readable but distinct;
- cast-shadow regions do not turn black;
- lit regions do not wash to pastel;
- adding ambient does not materially move the terminator.

### 3. Face map

Debug variants:

- R channel;
- G channel;
- selected channel;
- angular threshold;
- final face shade;
- NdotL fallback.

Capture every required horizontal angle with the face close-up.

Pass conditions:

- R and G debug images are visibly different if packed encoding is used;
- +90° and -90° select opposite sides;
- cheek shapes are mirrored or independently authored as intended;
- no sudden channel/angle discontinuity at front or back;
- face shading remains stable while body normals rotate;
- fallback and map captures are meaningfully distinguishable.

Current `face_ndl.png` and `face_map.png` are useful historical records but fail the close-up criterion.

### 4. Cast shadows

Use a simple occluder crossing the face, cloth, and ground.

Variants:

- cast remap debug;
- receive amount/softness sweep;
- self-shadow on/off where engine controls permit.

Pass conditions:

- occlusion enters the same material shadow color;
- shadow edges do not crawl during small rotations;
- face does not lose all received shadows because of an excessive lookup offset;
- point/spot attenuation does not behave like a binary shadow unexpectedly.

### 5. Outer shadow

Prerequisite: corrected base shade.

Variants:

- off;
- isolated outer band debug;
- final configured value.

Pass conditions:

- band exists only between deep shadow and the main terminator;
- it does not cover the whole back-facing hemisphere;
- it remains narrow at multiple camera distances;
- face remains unaffected unless explicitly authored.

### 6. Dither

Prerequisite: corrected base shade and outer band.

Use hair and cloth close-ups at native resolution.

Variants:

- off;
- configured strength;
- exaggerated strength for placement debug.

Pass conditions:

- exaggerated debug reveals a narrow terminator region;
- deep shadow and full light are clean;
- pattern does not swim under camera motion;
- configured strength is visible at 100% zoom but not distracting at gameplay distance.

The current full-body `dither_off.png` and `dither_on.png` pair is too wide to validate placement.

### 7. Hair highlight

Variants:

- mask only;
- Kajiya-Kay only;
- mixed;
- highlight mask debug;
- final specular debug.

Pass conditions:

- highlight appears only on intended hair islands;
- purple accessories do not light accidentally;
- highlight responds to light-side gating;
- silhouette suppression has the intended polarity;
- albedo-painted streak and shader highlight do not produce an overbright double image.

### 8. Metallic ramp

Variants:

- Phong;
- ramp;
- ramp debug sampled coordinate;
- ramp plus any future control mask.

Pass conditions:

- the highlight is a moving band, not a round blob;
- it appears only on verified metal surfaces;
- the weapon and ornaments can be tuned independently;
- no discontinuity appears at UV or mesh boundaries;
- the band survives final tonemapping without becoming white everywhere.

### 9. Outline ownership

Capture:

- no outline;
- hull only;
- depth compositor only;
- combined;
- compositor raw depth edge;
- character mask;
- far-side highlight mask.

Use full body and outline stress close-ups.

Pass conditions:

- character-only mode does not outline the ground horizon;
- hull-only width is stable with camera distance;
- compositor-only detects intended depth intersections;
- combined mode does not double the silhouette;
- nose, fingers, and sword do not grow spikes or hollow shells;
- face internal edges are controlled by a mask, not shared fragment depth;
- white highlight side remains correct throughout turntable rotation.

### 10. Grade, fog, and AA

Run last.

Variants:

- Linear, Filmic, ACES, and candidate custom curve;
- saturation 1.0 and configured value;
- fog off/current;
- MSAA only and MSAA+FXAA.

Pass conditions:

- skin, purple cloth, and gold remain separated;
- white surfaces retain hue without clipping;
- shadow bands remain distinct;
- outlines are not softened into grey halos;
- negative texture LOD bias does not shimmer in motion.

## Numeric checks

Visual tests should be accompanied by lightweight data checks:

- face R/G mean and maximum absolute difference;
- percentage of visible standard-shaded pixels with `shade < 0.05`;
- percentage of dither-affected pixels outside `0.1 < shade < 0.9`;
- compositor edge pixels outside the character mask;
- resolved slot name for every mesh surface;
- frame-time cost for base, hull, compositor, and auxiliary-mask modes.

Exact thresholds can evolve, but recording them turns “looks close” into repeatable evidence.

## Evidence manifest

For every accepted change, add a small Markdown or JSON manifest containing:

```text
commit:
godot_version:
resolution:
camera:
light_transform:
environment:
material_preset:
feature_flags:
capture_files:
observations:
```

Screenshots are currently gitignored. Either version a curated subset under a documentation-specific path or record a reproducible command and expected hashes. `.import` files without their PNG evidence are not sufficient.

## Final acceptance sequence

1. Validate signed base shade.
2. Validate face map channels and yaw.
3. Validate material-slot assignments.
4. Validate hair and metal highlights.
5. Validate hull/compositor ownership.
6. Validate cast shadows and outer band.
7. Validate dither placement.
8. Validate final grade and temporal stability.
9. Repeat at two resolutions and two camera distances.

Only then compare the final plate to the visual target.
