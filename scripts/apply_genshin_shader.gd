extends Node3D

## Applies a Genshin-like toon + outline material stack to every MeshInstance3D.

@export var toon_shader: Shader
@export var outline_shader: Shader
@export var apply_on_ready: bool = true
@export var outline_width: float = 1.25
@export var outline_color: Color = Color(1.0, 1.0, 1.0, 1.0)
## Keeps high mesh/detail longer when the camera pulls away (toon needs full normals).
@export var mesh_lod_bias: float = 16.0
## Negative = sharper albedo farther from camera (delays blurry mips).
@export var texture_lod_bias: float = -0.75

func _ready() -> void:
	if apply_on_ready:
		apply_to_tree(self)

func _physics_process(delta: float) -> void:
	rotate_y(delta * 0.2)

func apply_to_tree(root: Node) -> void:
	if toon_shader == null:
		toon_shader = load("res://shaders/genshin_toon.gdshader") as Shader
	if outline_shader == null:
		outline_shader = load("res://shaders/genshin_outline.gdshader") as Shader

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
	mesh_instance.lod_bias = mesh_lod_bias

	var surface_count := mesh_instance.mesh.get_surface_count()
	for surface_index in surface_count:
		var source_material := mesh_instance.get_active_material(surface_index)
		var toon_material := _create_toon_material(source_material, mesh_instance.name)
		mesh_instance.set_surface_override_material(surface_index, toon_material)


func _create_toon_material(source_material: Material, mesh_name: String) -> ShaderMaterial:
	var material := ShaderMaterial.new()
	material.shader = toon_shader

	var albedo_texture: Texture2D = null
	var albedo_color := Color.WHITE

	if source_material is BaseMaterial3D:
		var base_material := source_material as BaseMaterial3D
		albedo_texture = base_material.albedo_texture
		albedo_color = base_material.albedo_color

	material.set_shader_parameter("albedo_texture", albedo_texture)
	material.set_shader_parameter("use_albedo_texture", albedo_texture != null)
	material.set_shader_parameter("albedo_color", albedo_color)
	material.set_shader_parameter("texture_lod_bias", texture_lod_bias)
	# Keep everything opaque for correct depth. Original GLB materials are opaque.
	material.set_shader_parameter("use_alpha_scissor", false)
	material.set_shader_parameter("alpha_scissor_threshold", 0.5)

	_configure_presets(material, mesh_name, albedo_texture)

	var outline_material := ShaderMaterial.new()
	outline_material.shader = outline_shader
	outline_material.set_shader_parameter("albedo_texture", albedo_texture)
	outline_material.set_shader_parameter("use_albedo_texture", albedo_texture != null)
	outline_material.set_shader_parameter("albedo_color", albedo_color)
	outline_material.set_shader_parameter("texture_lod_bias", texture_lod_bias)
	outline_material.set_shader_parameter("outline_width", outline_width)
	outline_material.set_shader_parameter("outline_tint", outline_color)
	outline_material.set_shader_parameter("outline_darken", 0.28)
	outline_material.set_shader_parameter("outline_saturation", 1.15)
	material.next_pass = outline_material

	return material


func _configure_presets(material: ShaderMaterial, mesh_name: String, albedo_texture: Texture2D) -> void:
	var texture_path := ""
	if albedo_texture != null:
		texture_path = albedo_texture.resource_path.to_lower()

	var name_lower := mesh_name.to_lower()
	var is_face := texture_path.ends_with("2_0.png") or name_lower.contains("face") or name_lower.contains("eye")
	var is_hair := texture_path.ends_with("2_1.png") or name_lower.contains("hair")
	var is_metal := texture_path.ends_with("2_5.png") or name_lower.contains("katana") or name_lower.contains("acc")

	material.set_shader_parameter("use_alpha_scissor", false)
	material.set_shader_parameter("ambient_strength", 0.2)
	material.set_shader_parameter("light_intensity", 0.7)
	material.set_shader_parameter("cast_shadow_softness", 0.14)

	if is_face:
		# Softer, warmer terminator — faces in Genshin avoid harsh cast bands.
		material.set_shader_parameter("shadow_threshold", 0.52)
		material.set_shader_parameter("shadow_smoothness", 0.055)
		material.set_shader_parameter("cast_shadow_softness", 0.22)
		material.set_shader_parameter("shadow_color", Color(0.86, 0.74, 0.76, 1.0))
		material.set_shader_parameter("specular_strength", 0.04)
		material.set_shader_parameter("rim_strength", 0.08)
		material.set_shader_parameter("ambient_strength", 0.2)
	elif is_hair:
		# Cool purple volumes + anisotropic sheen (not isotropic plastic blobs).
		# Albedo already carries painted highlights — realtime gloss stays subtle.
		material.set_shader_parameter("shadow_threshold", 0.47)
		material.set_shader_parameter("shadow_smoothness", 0.025)
		material.set_shader_parameter("cast_shadow_softness", 0.1)
		material.set_shader_parameter("shadow_color", Color(0.52, 0.42, 0.72, 1.0))
		material.set_shader_parameter("use_anisotropic_specular", true)
		material.set_shader_parameter("hair_flow_blend", 1.0)
		material.set_shader_parameter("specular_strength", 0.07)
		material.set_shader_parameter("specular_size", 48.0)
		material.set_shader_parameter("specular_smoothness", 0.18)
		material.set_shader_parameter("specular_color", Color(0.78, 0.74, 0.92, 1.0))
		material.set_shader_parameter("hair_spec_shift", 0.08)
		material.set_shader_parameter("hair_spec_secondary", 0.35)
		material.set_shader_parameter("hair_spec_secondary_shift", -0.14)
		material.set_shader_parameter("hair_spec_secondary_size", 18.0)
		material.set_shader_parameter("rim_strength", 0.04)
		material.set_shader_parameter("rim_power", 5.0)
		material.set_shader_parameter("rim_color", Color(0.55, 0.5, 0.82, 1.0))
	elif is_metal:
		material.set_shader_parameter("shadow_threshold", 0.45)
		material.set_shader_parameter("shadow_smoothness", 0.03)
		material.set_shader_parameter("cast_shadow_softness", 0.1)
		material.set_shader_parameter("shadow_color", Color(0.58, 0.52, 0.74, 1.0))
		material.set_shader_parameter("specular_strength", 0.45)
		material.set_shader_parameter("specular_size", 96.0)
		material.set_shader_parameter("rim_strength", 0.15)
	else:
		# Cloth/body: clean two-tone band. Fabric albedo already has paint detail —
		# keep realtime gloss/rim soft so cloth doesn't read as plastic.
		material.set_shader_parameter("shadow_threshold", 0.5)
		material.set_shader_parameter("shadow_smoothness", 0.032)
		material.set_shader_parameter("cast_shadow_softness", 0.12)
		material.set_shader_parameter("shadow_color", Color(0.66, 0.56, 0.8, 1.0))
		material.set_shader_parameter("specular_strength", 0.035)
		material.set_shader_parameter("specular_size", 32.0)
		material.set_shader_parameter("specular_smoothness", 0.14)
		material.set_shader_parameter("specular_color", Color(0.88, 0.86, 0.94, 1.0))
		material.set_shader_parameter("rim_strength", 0.06)
		material.set_shader_parameter("rim_power", 4.5)
		material.set_shader_parameter("rim_color", Color(0.62, 0.66, 0.88, 1.0))
