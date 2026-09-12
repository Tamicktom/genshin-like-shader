//* Libraries imports
using Godot;

//* Local imports
//* ...

/// <summary>
/// Shader uniform names. Must stay snake_case to match .gdshader files.
/// </summary>
public static class ShaderParams
{
	//* Albedo
	public const string AlbedoColor = "albedo_color";
	public const string AlbedoTexture = "albedo_texture";
	public const string UseAlbedoTexture = "use_albedo_texture";
	public const string TextureLodBias = "texture_lod_bias";
	public const string AlphaScissorThreshold = "alpha_scissor_threshold";
	public const string UseAlphaScissor = "use_alpha_scissor";
	public const string DoubleSided = "double_sided";

	//* Cel shading
	public const string ShadowColor = "shadow_color";
	public const string ShadowThreshold = "shadow_threshold";
	public const string ShadowSmoothness = "shadow_smoothness";
	public const string CastShadowSoftness = "cast_shadow_softness";
	public const string LightIntensity = "light_intensity";
	public const string AmbientStrength = "ambient_strength";
	public const string AmbientColor = "ambient_color";

	//* Specular
	public const string SpecularColor = "specular_color";
	public const string SpecularSize = "specular_size";
	public const string SpecularSmoothness = "specular_smoothness";
	public const string SpecularStrength = "specular_strength";
	public const string UseAnisotropicSpecular = "use_anisotropic_specular";
	public const string HairFlowBlend = "hair_flow_blend";
	public const string HairSpecShift = "hair_spec_shift";
	public const string HairSpecSecondary = "hair_spec_secondary";
	public const string HairSpecSecondaryShift = "hair_spec_secondary_shift";
	public const string HairSpecSecondarySize = "hair_spec_secondary_size";

	//* Rim
	public const string RimColor = "rim_color";
	public const string RimPower = "rim_power";
	public const string RimStrength = "rim_strength";

	//* Outline
	public const string OutlineTint = "outline_tint";
	public const string OutlineDarken = "outline_darken";
	public const string OutlineSaturation = "outline_saturation";
	public const string OutlineWidth = "outline_width";
	public const string OutlineDepthBias = "outline_depth_bias";
}
