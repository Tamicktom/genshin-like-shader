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

`CaptureSceneScreenshot` on `scenes/main.tscn` waits until `RaidenShogun` is ready (meshes present), settles a few frames, then writes:

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
$GODOT --path . -- --outline-ab # compositor outline A/B → screenshots/outline_*.png
$GODOT --path . -- --dither-ab  # terminator dither off/on → screenshots/dither_*.png
```
