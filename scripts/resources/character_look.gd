class_name CharacterLook
extends Resource

## Ordered slot table + shaders for one character. First matching slot wins.

@export var display_name: String = ""
@export var toon_shader: Shader
@export var outline_shader: Shader

@export_group("Slots")
## Evaluated in order; first match wins.
@export var slots: Array[LookSlot] = []
@export var fallback_preset: ToonPreset
@export var fallback_double_sided: bool = false
@export var fallback_enable_outline: bool = true
@export_range(0.0, 12.0) var fallback_outline_width: float = 1.25
@export_range(0.0, 0.4) var fallback_outline_depth_bias: float = 0.0

@export_group("Mesh / Texture")
## Keeps high mesh/detail longer when the camera pulls away (toon needs full normals).
@export var mesh_lod_bias: float = 16.0
## Negative = sharper albedo farther from camera (delays blurry mips).
@export var texture_lod_bias: float = -0.75

@export_group("Outline Defaults")
@export var outline_tint: Color = Color(1.0, 1.0, 1.0, 1.0)
@export_range(0.0, 1.0) var outline_darken: float = 0.28
@export_range(0.0, 2.0) var outline_saturation: float = 1.15


## Returns { preset, double_sided, enable_outline, outline_width, outline_depth_bias }.
func resolve_slot(mesh_name: String, texture_path: String) -> Dictionary:
	for slot in slots:
		if slot == null:
			continue
		if slot.matches(mesh_name, texture_path):
			return {
				"preset": slot.preset,
				"double_sided": slot.double_sided,
				"enable_outline": slot.enable_outline,
				"outline_width": slot.outline_width,
				"outline_depth_bias": slot.outline_depth_bias,
				"slot_name": slot.slot_name,
			}

	return {
		"preset": fallback_preset,
		"double_sided": fallback_double_sided,
		"enable_outline": fallback_enable_outline,
		"outline_width": fallback_outline_width,
		"outline_depth_bias": fallback_outline_depth_bias,
		"slot_name": "fallback",
	}
