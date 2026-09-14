# Agent notes — genshin-like-shader

## Godot (this Linux)

Engine binary for F5 / CLI runs on this machine:

```
/home/tamicktom/Downloads/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64
```

- Version: **Godot 4.7 stable mono** (C# / Forward+)
- Project root: this repo (`project.godot` features `4.7`, `C#`, `Forward Plus`)
- Main scene: `res://scenes/main.tscn`

Example:

```bash
GODOT=/home/tamicktom/Downloads/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64
$GODOT --path .
$GODOT --path . --headless --quit-after 60
```

Prefer this path when a Godot binary is needed; it is not on `PATH`.

## Auto screenshot on run

`CaptureSceneScreenshot` on `scenes/main.tscn` waits until `Characters` is ready (meshes present), settles a few frames, then writes:

```
screenshots/scene_loaded.png
```

(gitignored). Disable via the node’s `Enabled` export if needed.

Lookdev sweeps (windowed; Forward+ needs a real GPU):

```bash
$GODOT --path . -- --grade-ab   # tonemap A/B → screenshots/grade_*.png
$GODOT --path . -- --fog-ab     # fog A/B → screenshots/fog_*.png
$GODOT --path . -- --face-ab    # face NdotL vs map → screenshots/face_*.png
$GODOT --path . -- --hair-ab    # hair Kajiya-Kay vs mask → screenshots/hair_*.png
$GODOT --path . -- --outline-ab # hull / masked compositor / combined → screenshots/outline/
$GODOT --path . -- --dither-ab  # terminator dither off/on → screenshots/dither_*.png
$GODOT --path . -- --debug-ab   # shader debug views 0–7 → screenshots/debug/*.png
$GODOT --path . -- --face-yaw   # face close-up light yaw → screenshots/face_yaw/*.png
$GODOT --path . -- --ambient-ab # ambient zero / additive / max-floor → screenshots/ambient/
$GODOT --path . -- --metal-ab   # Phong vs metallic ramp × light yaw → screenshots/metal/
```

Debug view indices (`debug_view` uniform):

| View | Signal |
| ---: | --- |
| 0 | production final color |
| 1 | signed N·L as `0.5*raw+0.5` |
| 2 | wrapped N·L |
| 3 | final shade |
| 4 | cast-shadow term |
| 5 | face map sample |
| 6 | face angular threshold |
| 7 | slot ID palette |

Numeric checks:

```bash
python3 tools/check_shade_coverage.py
python3 tools/check_face_map.py
python3 tools/check_band_separation.py screenshots/ambient/ambient_max.png
```
