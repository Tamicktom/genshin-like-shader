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
	/// Multiplies each slot's OutlineWidth (demo uses 1.8 for a thicker silhouette).
	/// </summary>
	[Export]
	public float OutlineWidthScale { get; set; } = 1.0f;

	/// <summary>
	/// Node whose world +Z / +X drive face-shadow head axes.
	/// Empty = this node (character root). Prefer a child Marker3D named HeadAxes,
	/// or a Skeleton3D when <see cref="HeadBoneName"/> is set.
	/// </summary>
	[Export]
	public NodePath HeadNodePath { get; set; } = new NodePath("HeadAxes");

	/// <summary>
	/// Optional bone name when <see cref="HeadNodePath"/> points at a Skeleton3D.
	/// Empty = use the node transform directly.
	/// </summary>
	[Export]
	public string HeadBoneName { get; set; } = "";

	/// <summary>
	/// When true, print mesh surface → slot / preset / extra maps during ApplyToTree.
	/// </summary>
	[Export]
	public bool LogSlotResolution { get; set; }

	/// <summary>
	/// Case-insensitive wildcards; matching meshes are hidden (e.g. MMD <c>*spa*</c> shells).
	/// </summary>
	[Export]
	public string[] SkipNamePatterns { get; set; } = System.Array.Empty<string>();

	/// <summary>
	/// Visual layer bit used when <see cref="LookSlot.IncludeInOutlineMask"/> is true.
	/// Default layer 2 (bit 1). CharacterMaskPass culls to this layer.
	/// </summary>
	[Export(PropertyHint.Layers3DRender)]
	public uint CharacterMaskLayer { get; set; } = 2;

	private readonly List<ShaderMaterial> _faceShadowMaterials = new();
	private readonly List<ShaderMaterial> _headAxisMaterials = new();
	private Node3D _headNode;
	private Skeleton3D _headSkeleton;
	private int _headBoneIndex = -1;

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

		if (!TryGetHeadBasis(out Basis basis))
		{
			ResolveHeadNode();
			if (!TryGetHeadBasis(out basis))
			{
				return;
			}
		}

		// Unity-style forward/right: Godot +Z faces the camera when the character does.
		Vector3 forward = basis.Z.Normalized();
		Vector3 right = basis.X.Normalized();

		foreach (ShaderMaterial material in _headAxisMaterials)
		{
			if (material == null || !GodotObject.IsInstanceValid(material))
			{
				continue;
			}

			material.SetShaderParameter(ShaderParams.HeadForward, forward);
			material.SetShaderParameter(ShaderParams.HeadRight, right);
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
		_headSkeleton = null;
		_headBoneIndex = -1;

		if (HeadNodePath != null && !HeadNodePath.IsEmpty)
		{
			_headNode = GetNodeOrNull<Node3D>(HeadNodePath);
		}

		if (_headNode == null)
		{
			_headNode = this;
		}

		if (!string.IsNullOrEmpty(HeadBoneName) && _headNode is Skeleton3D skeleton)
		{
			int boneIndex = skeleton.FindBone(HeadBoneName);
			if (boneIndex >= 0)
			{
				_headSkeleton = skeleton;
				_headBoneIndex = boneIndex;
			}
			else
			{
				GD.PushWarning(
					$"apply_character_look: HeadBoneName '{HeadBoneName}' not found on {_headNode.Name}");
			}
		}
	}

	private bool TryGetHeadBasis(out Basis basis)
	{
		basis = default;
		if (_headSkeleton != null
			&& GodotObject.IsInstanceValid(_headSkeleton)
			&& _headBoneIndex >= 0)
		{
			basis = _headSkeleton.GetBoneGlobalPose(_headBoneIndex).Basis;
			return true;
		}

		if (_headNode == null || !GodotObject.IsInstanceValid(_headNode))
		{
			return false;
		}

		basis = _headNode.GlobalTransform.Basis;
		return true;
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

	private static bool MatchesAnyPattern(string[] patterns, string haystack)
	{
		if (patterns == null || string.IsNullOrEmpty(haystack))
		{
			return false;
		}

		foreach (string pattern in patterns)
		{
			if (string.IsNullOrEmpty(pattern))
			{
				continue;
			}

			if (haystack.MatchN(pattern))
			{
				return true;
			}
		}

		return false;
	}

	private void ApplyToMeshInstance(MeshInstance3D meshInstance)
	{
		if (meshInstance.Mesh == null)
		{
			return;
		}

		if (MatchesAnyPattern(SkipNamePatterns, meshInstance.Name))
		{
			meshInstance.Visible = false;
			return;
		}

		// Prefer the full-resolution mesh as long as possible for cel shading.
		meshInstance.LodBias = Look.MeshLodBias;
		// Inflated hull must stay inside the AABB used for frustum culling.
		meshInstance.ExtraCullMargin = Mathf.Max(meshInstance.ExtraCullMargin, 0.25f);

		int surfaceCount = meshInstance.Mesh.GetSurfaceCount();
		bool anyMask = false;
		for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
		{
			Material sourceMaterial = meshInstance.GetActiveMaterial(surfaceIndex);
			ShaderMaterial toonMaterial = CreateToonMaterial(
				sourceMaterial,
				meshInstance.Name,
				out bool includeInMask);
			meshInstance.SetSurfaceOverrideMaterial(surfaceIndex, toonMaterial);
			anyMask |= includeInMask;
		}

		// Per-mesh layer (not per-surface): include if any surface opts into the mask.
		if (anyMask && CharacterMaskLayer != 0)
		{
			meshInstance.Layers |= CharacterMaskLayer;
		}
		else if (CharacterMaskLayer != 0)
		{
			meshInstance.Layers &= ~CharacterMaskLayer;
		}
	}

	private ShaderMaterial CreateToonMaterial(
		Material sourceMaterial,
		string meshName,
		out bool includeInMask)
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
		includeInMask = resolved.IncludeInOutlineMask;

		if (LogSlotResolution)
		{
			string presetName = preset?.ResourcePath;
			if (string.IsNullOrEmpty(presetName))
			{
				presetName = preset != null ? preset.GetType().Name : "null";
			}

			string extras = "";
			if (resolved.FaceShadowTex != null)
			{
				extras += " face_shadow";
			}

			if (resolved.HairHighlightTex != null)
			{
				extras += " hair_hl";
			}

			if (resolved.ControlTex != null)
			{
				extras += " control";
			}

			if (resolved.DetailNormalTex != null)
			{
				extras += " detail_n";
			}

			if (string.IsNullOrEmpty(extras))
			{
				extras = " (none)";
			}

			GD.Print(
				$"apply_character_look: {meshName} -> slot[{resolved.SlotIndex}]={resolved.SlotName}"
					+ $" preset={presetName} outline={resolved.EnableOutline}"
					+ $" mask={resolved.IncludeInOutlineMask} extras={extras.Trim()}"
					+ $" tex={texturePath}");
		}

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
		material.SetShaderParameter(
			ShaderParams.FaceYawOffset,
			Mathf.DegToRad(resolved.FaceYawOffsetDegrees));
		material.SetShaderParameter(
			ShaderParams.FaceForwardSign,
			resolved.FaceForwardFlip ? -1.0f : 1.0f);
		material.SetShaderParameter(
			ShaderParams.FaceSideSign,
			resolved.FaceSwapSides ? -1.0f : 1.0f);
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
