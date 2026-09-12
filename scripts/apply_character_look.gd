extends Node3D

## Applies a CharacterLook table to every MeshInstance3D under this node.
## Binding albedo + first matching slot only — no character-specific ifs.

@export var look: CharacterLook
@export var apply_on_ready: bool = true
## Multiplies each slot's outline_width (demo uses 2.0 for a thicker silhouette).
@export var outline_width_scale: float = 1.0


func _ready() -> void:
	if apply_on_ready:
		apply_to_tree(self)


func apply_to_tree(root: Node) -> void:
	if look == null:
		push_warning("apply_character_look: no CharacterLook assigned on %s" % name)
		return

	if look.toon_shader == null:
		look.toon_shader = load("res://shaders/genshin_toon.gdshader") as Shader
	if look.outline_shader == null:
		look.outline_shader = load("res://shaders/genshin_outline.gdshader") as Shader

	_apply_recursive(root)


func _apply_recursive(node: Node) -> void:
	if node is MeshInstance3D:
		_apply_to_mesh_instance(node as MeshInstance3D)

	for child in node.get_children():
		_apply_recursive(child)


func _apply_to_mesh_instance(mesh_instance: MeshInstance3D) -> void:
	if mesh_instance.mesh == null:
		return

	# Prefer the full-resolution mesh as long as possible for cel shading.
	mesh_instance.lod_bias = look.mesh_lod_bias
	# Inflated hull must stay inside the AABB used for frustum culling.
	mesh_instance.extra_cull_margin = maxf(mesh_instance.extra_cull_margin, 0.25)

	var surface_count := mesh_instance.mesh.get_surface_count()
	for surface_index in surface_count:
		var source_material := mesh_instance.get_active_material(surface_index)
		var toon_material := _create_toon_material(source_material, mesh_instance.name)
		mesh_instance.set_surface_override_material(surface_index, toon_material)


func _create_toon_material(source_material: Material, mesh_name: String) -> ShaderMaterial:
	var albedo_texture: Texture2D = null
	var albedo_color := Color.WHITE

	if source_material is BaseMaterial3D:
		var base_material := source_material as BaseMaterial3D
		albedo_texture = base_material.albedo_texture
		albedo_color = base_material.albedo_color
	elif source_material is ShaderMaterial:
		var shader_material := source_material as ShaderMaterial
		var tex_param = shader_material.get_shader_parameter("albedo_texture")
		if tex_param is Texture2D:
			albedo_texture = tex_param as Texture2D
		var color_param = shader_material.get_shader_parameter("albedo_color")
		if color_param is Color:
			albedo_color = color_param as Color

	var texture_path := ""
	if albedo_texture != null:
		texture_path = albedo_texture.resource_path

	var resolved := look.resolve_slot(mesh_name, texture_path)
	var preset: ToonPreset = resolved.get("preset") as ToonPreset

	var material := ShaderMaterial.new()
	material.shader = look.toon_shader

	material.set_shader_parameter("albedo_texture", albedo_texture)
	material.set_shader_parameter("use_albedo_texture", albedo_texture != null)
	material.set_shader_parameter("albedo_color", albedo_color)
	material.set_shader_parameter("texture_lod_bias", look.texture_lod_bias)
	# Keep everything opaque for correct depth. Original GLB materials are opaque.
	material.set_shader_parameter("use_alpha_scissor", false)
	material.set_shader_parameter("alpha_scissor_threshold", 0.5)
	material.set_shader_parameter("double_sided", resolved.get("double_sided", false))

	if preset != null:
		preset.apply_to_material(material)

	if not resolved.get("enable_outline", false):
		return material

	var outline_material := ShaderMaterial.new()
	outline_material.shader = look.outline_shader
	outline_material.set_shader_parameter("albedo_texture", albedo_texture)
	outline_material.set_shader_parameter("use_albedo_texture", albedo_texture != null)
	outline_material.set_shader_parameter("albedo_color", albedo_color)
	outline_material.set_shader_parameter("texture_lod_bias", look.texture_lod_bias)
	outline_material.set_shader_parameter("outline_tint", look.outline_tint)
	outline_material.set_shader_parameter("outline_darken", look.outline_darken)
	outline_material.set_shader_parameter("outline_saturation", look.outline_saturation)
	outline_material.set_shader_parameter(
		"outline_width",
		float(resolved.get("outline_width", 1.25)) * outline_width_scale
	)
	outline_material.set_shader_parameter(
		"outline_depth_bias",
		resolved.get("outline_depth_bias", 0.0)
	)
	material.next_pass = outline_material

	return material
