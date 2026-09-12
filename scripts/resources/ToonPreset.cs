//* Libraries imports
using Godot;

//* Local imports
//* ...

/// <summary>
/// Shared cel / specular / rim knobs for one material kind (face, hair, cloth…).
/// Albedo and matching live elsewhere; this resource is shading only.
/// </summary>
[GlobalClass]
public partial class ToonPreset : Resource
{
	[ExportGroup("Cel Shading")]
	[Export]
	public Color ShadowColor { get; set; } = new Color(0.62f, 0.55f, 0.78f, 1.0f);

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float ShadowThreshold { get; set; } = 0.5f;

	[Export(PropertyHint.Range, "0.001,0.35")]
	public float ShadowSmoothness { get; set; } = 0.04f;

	[Export(PropertyHint.Range, "0.001,0.8")]
	public float CastShadowSoftness { get; set; } = 0.22f;

	[Export(PropertyHint.Range, "0.0,2.0")]
	public float LightIntensity { get; set; } = 0.7f;

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float AmbientStrength { get; set; } = 0.2f;

	[Export]
	public Color AmbientColor { get; set; } = new Color(0.78f, 0.8f, 0.9f, 1.0f);

	[ExportGroup("Specular")]
	[Export]
	public Color SpecularColor { get; set; } = new Color(1.0f, 0.96f, 0.92f, 1.0f);

	[Export(PropertyHint.Range, "1.0,256.0")]
	public float SpecularSize { get; set; } = 48.0f;

	[Export(PropertyHint.Range, "0.001,0.5")]
	public float SpecularSmoothness { get; set; } = 0.08f;

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float SpecularStrength { get; set; } = 0.18f;

	[Export]
	public bool UseAnisotropicSpecular { get; set; }

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float HairFlowBlend { get; set; } = 1.0f;

	[Export(PropertyHint.Range, "-1.0,1.0")]
	public float HairSpecShift { get; set; } = 0.08f;

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float HairSpecSecondary { get; set; } = 0.4f;

	[Export(PropertyHint.Range, "-1.0,1.0")]
	public float HairSpecSecondaryShift { get; set; } = -0.12f;

	[Export(PropertyHint.Range, "1.0,256.0")]
	public float HairSpecSecondarySize { get; set; } = 24.0f;

	[ExportGroup("Rim")]
	[Export]
	public Color RimColor { get; set; } = new Color(0.7f, 0.78f, 1.0f, 1.0f);

	[Export(PropertyHint.Range, "0.5,8.0")]
	public float RimPower { get; set; } = 4.0f;

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float RimStrength { get; set; } = 0.18f;

	public void ApplyToMaterial(ShaderMaterial material)
	{
		material.SetShaderParameter(ShaderParams.ShadowColor, ShadowColor);
		material.SetShaderParameter(ShaderParams.ShadowThreshold, ShadowThreshold);
		material.SetShaderParameter(ShaderParams.ShadowSmoothness, ShadowSmoothness);
		material.SetShaderParameter(ShaderParams.CastShadowSoftness, CastShadowSoftness);
		material.SetShaderParameter(ShaderParams.LightIntensity, LightIntensity);
		material.SetShaderParameter(ShaderParams.AmbientStrength, AmbientStrength);
		material.SetShaderParameter(ShaderParams.AmbientColor, AmbientColor);

		material.SetShaderParameter(ShaderParams.SpecularColor, SpecularColor);
		material.SetShaderParameter(ShaderParams.SpecularSize, SpecularSize);
		material.SetShaderParameter(ShaderParams.SpecularSmoothness, SpecularSmoothness);
		material.SetShaderParameter(ShaderParams.SpecularStrength, SpecularStrength);
		material.SetShaderParameter(ShaderParams.UseAnisotropicSpecular, UseAnisotropicSpecular);
		material.SetShaderParameter(ShaderParams.HairFlowBlend, HairFlowBlend);
		material.SetShaderParameter(ShaderParams.HairSpecShift, HairSpecShift);
		material.SetShaderParameter(ShaderParams.HairSpecSecondary, HairSpecSecondary);
		material.SetShaderParameter(ShaderParams.HairSpecSecondaryShift, HairSpecSecondaryShift);
		material.SetShaderParameter(ShaderParams.HairSpecSecondarySize, HairSpecSecondarySize);

		material.SetShaderParameter(ShaderParams.RimColor, RimColor);
		material.SetShaderParameter(ShaderParams.RimPower, RimPower);
		material.SetShaderParameter(ShaderParams.RimStrength, RimStrength);
	}
}
