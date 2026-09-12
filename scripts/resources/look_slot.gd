class_name LookSlot
extends Resource

## One part of a character look: how to match a mesh, which preset, outline flags.

@export var slot_name: String = ""
## Case-insensitive wildcards matched against MeshInstance3D.name (`*face*`).
@export var name_patterns: PackedStringArray = PackedStringArray()
## Case-insensitive wildcards matched against albedo texture resource_path.
@export var texture_patterns: PackedStringArray = PackedStringArray()
@export var preset: ToonPreset

@export_group("Flags")
@export var double_sided: bool = false
@export var enable_outline: bool = true
@export_range(0.0, 12.0) var outline_width: float = 1.25
@export_range(0.0, 0.4) var outline_depth_bias: float = 0.0


func matches(mesh_name: String, texture_path: String) -> bool:
	if _any_pattern_matches(name_patterns, mesh_name):
		return true
	if not texture_path.is_empty() and _any_pattern_matches(texture_patterns, texture_path):
		return true
	return false


func _any_pattern_matches(patterns: PackedStringArray, haystack: String) -> bool:
	for pattern in patterns:
		if pattern.is_empty():
			continue
		if haystack.matchn(pattern):
			return true
	return false
