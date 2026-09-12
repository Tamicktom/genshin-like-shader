class_name ToonPreset
extends Resource

## Shared cel / specular / rim knobs for one material kind (face, hair, cloth…).
## Albedo and matching live elsewhere; this resource is shading only.

@export_group("Cel Shading")
@export var shadow_color: Color = Color(0.62, 0.55, 0.78, 1.0)
@export_range(0.0, 1.0) var shadow_threshold: float = 0.5
@export_range(0.001, 0.35) var shadow_smoothness: float = 0.04
@export_range(0.001, 0.8) var cast_shadow_softness: float = 0.22
@export_range(0.0, 2.0) var light_intensity: float = 0.7
@export_range(0.0, 1.0) var ambient_strength: float = 0.2
@export var ambient_color: Color = Color(0.78, 0.8, 0.9, 1.0)

@export_group("Specular")
@export var specular_color: Color = Color(1.0, 0.96, 0.92, 1.0)
@export_range(1.0, 256.0) var specular_size: float = 48.0
@export_range(0.001, 0.5) var specular_smoothness: float = 0.08
@export_range(0.0, 1.0) var specular_strength: float = 0.18
@export var use_anisotropic_specular: bool = false
@export_range(0.0, 1.0) var hair_flow_blend: float = 1.0
@export_range(-1.0, 1.0) var hair_spec_shift: float = 0.08
@export_range(0.0, 1.0) var hair_spec_secondary: float = 0.4
@export_range(-1.0, 1.0) var hair_spec_secondary_shift: float = -0.12
@export_range(1.0, 256.0) var hair_spec_secondary_size: float = 24.0

@export_group("Rim")
@export var rim_color: Color = Color(0.7, 0.78, 1.0, 1.0)
@export_range(0.5, 8.0) var rim_power: float = 4.0
@export_range(0.0, 1.0) var rim_strength: float = 0.18


func apply_to_material(material: ShaderMaterial) -> void:
	material.set_shader_parameter("shadow_color", shadow_color)
	material.set_shader_parameter("shadow_threshold", shadow_threshold)
	material.set_shader_parameter("shadow_smoothness", shadow_smoothness)
	material.set_shader_parameter("cast_shadow_softness", cast_shadow_softness)
	material.set_shader_parameter("light_intensity", light_intensity)
	material.set_shader_parameter("ambient_strength", ambient_strength)
	material.set_shader_parameter("ambient_color", ambient_color)

	material.set_shader_parameter("specular_color", specular_color)
	material.set_shader_parameter("specular_size", specular_size)
	material.set_shader_parameter("specular_smoothness", specular_smoothness)
	material.set_shader_parameter("specular_strength", specular_strength)
	material.set_shader_parameter("use_anisotropic_specular", use_anisotropic_specular)
	material.set_shader_parameter("hair_flow_blend", hair_flow_blend)
	material.set_shader_parameter("hair_spec_shift", hair_spec_shift)
	material.set_shader_parameter("hair_spec_secondary", hair_spec_secondary)
	material.set_shader_parameter("hair_spec_secondary_shift", hair_spec_secondary_shift)
	material.set_shader_parameter("hair_spec_secondary_size", hair_spec_secondary_size)

	material.set_shader_parameter("rim_color", rim_color)
	material.set_shader_parameter("rim_power", rim_power)
	material.set_shader_parameter("rim_strength", rim_strength)
