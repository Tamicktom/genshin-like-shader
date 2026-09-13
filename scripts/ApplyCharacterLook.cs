//* Libraries imports
using System.Collections.Generic;
using Godot;

//* Local imports
//* ...

/// <summary>
/// Applies a CharacterLook table to every MeshInstance3D under this node.
/// Binding albedo + first matching slot only — no character-specific ifs.
/// </summary>
public partial class ApplyCharacterLook : Node3D
{
	[Export]
	public CharacterLook Look { get; set; }

	[Export]
	public bool ApplyOnReady { get; set; } = true;

	/// <summary>
	/// Multiplies each slot's OutlineWidth (demo uses 2.0 for a thicker silhouette).
	/// </summary>
	[Export]
	public float OutlineWidthScale { get; set; } = 1.0f;

	/// <summary>
	/// Node whose world +Z / +X drive face-shadow head axes.
	/// Empty = this node (character root). Prefer a child Marker3D named HeadAxes.
	/// </summary>
	[Export]
	public NodePath HeadNodePath { get; set; } = new NodePath("HeadAxes");

	private readonly List<ShaderMaterial> _faceShadowMaterials = new();
	private readonly List<ShaderMaterial> _headAxisMaterials = new();
	private Node3D _headNode;

	public override void _Ready()
	{
		if (ApplyOnReady)
		{
			ApplyToTree(this);
		}

		ResolveHeadNode();
		SetProcess(_headAxisMaterials.Count > 0);
	}

	public override void _Process(double delta)
	{
		if (_headAxisMaterials.Count == 0)
		{
			return;
		}

		if (_headNode == null || !GodotObject.IsInstanceValid(_headNode))
		{
			ResolveHeadNode();
			if (_headNode == null)
			{
				return;
			}
		}

		Basis basis = _headNode.GlobalTransform.Basis;
		// Unity-style forward/right: Godot +Z faces the camera when the character does.
		Vector3 forward = basis.Z.Normalized();
		Vector3 right = basis.X.Normalized();
		Vector3 position = _headNode.GlobalTransform.Origin;

		foreach (ShaderMaterial material in _headAxisMaterials)
		{
			if (material == null || !GodotObject.IsInstanceValid(material))
			{
				continue;
			}

			material.SetShaderParameter(ShaderParams.HeadForward, forward);
			material.SetShaderParameter(ShaderParams.HeadRight, right);
			material.SetShaderParameter(ShaderParams.HeadPosition, position);
		}
	}

	public void ApplyToTree(Node root)
	{
		_faceShadowMaterials.Clear();
		_headAxisMaterials.Clear();

		if (Look == null)
		{
			GD.PushWarning($"apply_character_look: no CharacterLook assigned on {Name}");
			return;
		}

		if (Look.ToonShader == null)
		{
			Look.ToonShader = GD.Load<Shader>("res://shaders/genshin_toon.gdshader");
		}

		if (Look.OutlineShader == null)
		{
			Look.OutlineShader = GD.Load<Shader>("res://shaders/genshin_outline.gdshader");
		}

		ApplyRecursive(root);
		ResolveHeadNode();
		SetProcess(_headAxisMaterials.Count > 0);
	}

	private void ResolveHeadNode()
	{
		_headNode = null;
		if (HeadNodePath != null && !HeadNodePath.IsEmpty)
		{
			_headNode = GetNodeOrNull<Node3D>(HeadNodePath);
		}

		if (_headNode == null)
		{
			_headNode = this;
		}
	}

	private void ApplyRecursive(Node node)
	{
		if (node is MeshInstance3D meshInstance)
		{
			ApplyToMeshInstance(meshInstance);
		}

		foreach (Node child in node.GetChildren())
		{
			ApplyRecursive(child);
		}
	}

	private void ApplyToMeshInstance(MeshInstance3D meshInstance)
	{
		if (meshInstance.Mesh == null)
		{
			return;
		}

		// Prefer the full-resolution mesh as long as possible for cel shading.
		meshInstance.LodBias = Look.MeshLodBias;
		// Inflated hull must stay inside the AABB used for frustum culling.
		meshInstance.ExtraCullMargin = Mathf.Max(meshInstance.ExtraCullMargin, 0.25f);

		int surfaceCount = meshInstance.Mesh.GetSurfaceCount();
		for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
		{
			Material sourceMaterial = meshInstance.GetActiveMaterial(surfaceIndex);
			ShaderMaterial toonMaterial = CreateToonMaterial(sourceMaterial, meshInstance.Name);
			meshInstance.SetSurfaceOverrideMaterial(surfaceIndex, toonMaterial);
		}
	}

	private ShaderMaterial CreateToonMaterial(Material sourceMaterial, string meshName)
	{
		Texture2D albedoTexture = null;
		Color albedoColor = Colors.White;

		if (sourceMaterial is BaseMaterial3D baseMaterial)
		{
			albedoTexture = baseMaterial.AlbedoTexture;
			albedoColor = baseMaterial.AlbedoColor;
		}
		else if (sourceMaterial is ShaderMaterial shaderMaterial)
		{
			Variant texParam = shaderMaterial.GetShaderParameter(ShaderParams.AlbedoTexture);
			if (texParam.VariantType == Variant.Type.Object && texParam.AsGodotObject() is Texture2D texture)
			{
				albedoTexture = texture;
			}

			Variant colorParam = shaderMaterial.GetShaderParameter(ShaderParams.AlbedoColor);
			if (colorParam.VariantType == Variant.Type.Color)
			{
				albedoColor = colorParam.AsColor();
			}
		}

		string texturePath = "";
		if (albedoTexture != null)
		{
			texturePath = albedoTexture.ResourcePath;
		}

		ResolvedLookSlot resolved = Look.ResolveSlot(meshName, texturePath);
		ToonPreset preset = resolved.Preset;

		var material = new ShaderMaterial
		{
			Shader = Look.ToonShader,
		};

		material.SetShaderParameter(ShaderParams.AlbedoTexture, albedoTexture);
		material.SetShaderParameter(ShaderParams.UseAlbedoTexture, albedoTexture != null);
		material.SetShaderParameter(ShaderParams.AlbedoColor, albedoColor);
		material.SetShaderParameter(ShaderParams.TextureLodBias, Look.TextureLodBias);
		// Keep everything opaque for correct depth. Original GLB materials are opaque.
		material.SetShaderParameter(ShaderParams.UseAlphaScissor, false);
		material.SetShaderParameter(ShaderParams.AlphaScissorThreshold, 0.5f);
		material.SetShaderParameter(ShaderParams.DoubleSided, resolved.DoubleSided);

		BindExtraMap(material, ShaderParams.FaceShadowTex, ShaderParams.UseFaceShadow, resolved.FaceShadowTex);
		BindExtraMap(material, ShaderParams.ControlTex, ShaderParams.UseControlTex, resolved.ControlTex);
		BindExtraMap(material, ShaderParams.HairHighlightTex, ShaderParams.UseHairHighlight, resolved.HairHighlightTex);
		BindExtraMap(material, ShaderParams.DetailNormalTex, ShaderParams.UseDetailNormal, resolved.DetailNormalTex);

		material.SetShaderParameter(ShaderParams.FaceMirrorAxis, resolved.FaceMirrorAxis);
		material.SetShaderParameter(ShaderParams.DebugSlotId, (float)resolved.SlotIndex);
		material.SetShaderParameter(ShaderParams.DebugView, 0);

		float flatten = resolved.FlattenOutlineDepth ? resolved.OutlineDepthFlatten : 0.0f;
		material.SetShaderParameter(ShaderParams.OutlineDepthFlatten, flatten);

		preset?.ApplyToMaterial(material);

		if (resolved.FaceShadowTex != null || flatten > 0.001f)
		{
			_headAxisMaterials.Add(material);
		}

		if (resolved.FaceShadowTex != null)
		{
			_faceShadowMaterials.Add(material);
		}

		if (!resolved.EnableOutline)
		{
			return material;
		}

		var outlineMaterial = new ShaderMaterial
		{
			Shader = Look.OutlineShader,
		};
		outlineMaterial.SetShaderParameter(ShaderParams.AlbedoTexture, albedoTexture);
		outlineMaterial.SetShaderParameter(ShaderParams.UseAlbedoTexture, albedoTexture != null);
		outlineMaterial.SetShaderParameter(ShaderParams.AlbedoColor, albedoColor);
		outlineMaterial.SetShaderParameter(ShaderParams.TextureLodBias, Look.TextureLodBias);
		outlineMaterial.SetShaderParameter(ShaderParams.OutlineTint, Look.OutlineTint);
		outlineMaterial.SetShaderParameter(ShaderParams.OutlineDarken, Look.OutlineDarken);
		outlineMaterial.SetShaderParameter(ShaderParams.OutlineSaturation, Look.OutlineSaturation);
		outlineMaterial.SetShaderParameter(
			ShaderParams.OutlineWidth,
			resolved.OutlineWidth * OutlineWidthScale
		);
		outlineMaterial.SetShaderParameter(ShaderParams.OutlineDepthBias, resolved.OutlineDepthBias);
		material.NextPass = outlineMaterial;

		return material;
	}

	private static void BindExtraMap(
		ShaderMaterial material,
		string texParam,
		string useParam,
		Texture2D texture)
	{
		if (texture == null)
		{
			material.SetShaderParameter(useParam, false);
			return;
		}

		material.SetShaderParameter(texParam, texture);
		material.SetShaderParameter(useParam, true);
	}
}
