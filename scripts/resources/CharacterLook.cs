//* Libraries imports
using System;
using Godot;
using Godot.Collections;

//* Local imports
//* ...

/// <summary>
/// Resolved look settings for one mesh surface (first matching slot or fallback).
/// </summary>
public readonly struct ResolvedLookSlot
{
	public ToonPreset Preset { get; init; }
	public bool DoubleSided { get; init; }
	public bool EnableOutline { get; init; }
	public bool IncludeInOutlineMask { get; init; }
	public float OutlineWidth { get; init; }
	public float OutlineDepthBias { get; init; }
	public bool FlattenOutlineDepth { get; init; }
	public float OutlineDepthFlatten { get; init; }
	public string SlotName { get; init; }
	public int SlotIndex { get; init; }
	public Texture2D FaceShadowTex { get; init; }
	public float FaceMirrorAxis { get; init; }
	public float FaceYawOffsetDegrees { get; init; }
	public bool FaceForwardFlip { get; init; }
	public bool FaceSwapSides { get; init; }
	public Texture2D ControlTex { get; init; }
	public Texture2D HairHighlightTex { get; init; }
	public Texture2D DetailNormalTex { get; init; }
}

/// <summary>
/// Ordered slot table + shaders for one character. First matching slot wins.
/// </summary>
[GlobalClass]
public partial class CharacterLook : Resource
{
	[Export]
	public string DisplayName { get; set; } = "";

	[Export]
	public Shader ToonShader { get; set; }

	[Export]
	public Shader OutlineShader { get; set; }

	[ExportGroup("Slots")]
	/// <summary>
	/// Evaluated in order; first match wins.
	/// </summary>
	[Export]
	public Array<LookSlot> Slots { get; set; } = new Array<LookSlot>();

	[Export]
	public ToonPreset FallbackPreset { get; set; }

	[Export]
	public bool FallbackDoubleSided { get; set; }

	[Export]
	public bool FallbackEnableOutline { get; set; } = true;

	[Export(PropertyHint.Range, "0.0,12.0")]
	public float FallbackOutlineWidth { get; set; } = 1.25f;

	[Export(PropertyHint.Range, "0.0,0.4")]
	public float FallbackOutlineDepthBias { get; set; }

	[ExportGroup("Mesh / Texture")]
	/// <summary>
	/// Keeps high mesh/detail longer when the camera pulls away (toon needs full normals).
	/// </summary>
	[Export]
	public float MeshLodBias { get; set; } = 16.0f;

	/// <summary>
	/// Negative = sharper albedo farther from camera (delays blurry mips).
	/// </summary>
	[Export]
	public float TextureLodBias { get; set; } = -0.75f;

	[ExportGroup("Outline Defaults")]
	[Export]
	public Color OutlineTint { get; set; } = new Color(1.0f, 1.0f, 1.0f, 1.0f);

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float OutlineDarken { get; set; } = 0.28f;

	[Export(PropertyHint.Range, "0.0,2.0")]
	public float OutlineSaturation { get; set; } = 1.15f;

	public ResolvedLookSlot ResolveSlot(string meshName, string texturePath)
	{
		for (int slotIndex = 0; slotIndex < Slots.Count; slotIndex++)
		{
			LookSlot slot = Slots[slotIndex];
			if (slot == null)
			{
				continue;
			}

			if (slot.Matches(meshName, texturePath))
			{
				return new ResolvedLookSlot
				{
					Preset = slot.Preset,
					DoubleSided = slot.DoubleSided,
					EnableOutline = slot.EnableOutline,
					IncludeInOutlineMask = slot.IncludeInOutlineMask,
					OutlineWidth = slot.OutlineWidth,
					OutlineDepthBias = slot.OutlineDepthBias,
					FlattenOutlineDepth = slot.FlattenOutlineDepth,
					OutlineDepthFlatten = slot.OutlineDepthFlatten,
					SlotName = slot.SlotName,
					SlotIndex = slotIndex,
					FaceShadowTex = slot.FaceShadowTex,
					FaceMirrorAxis = slot.FaceMirrorAxis,
					FaceYawOffsetDegrees = slot.FaceYawOffsetDegrees,
					FaceForwardFlip = slot.FaceForwardFlip,
					FaceSwapSides = slot.FaceSwapSides,
					ControlTex = slot.ControlTex,
					HairHighlightTex = slot.HairHighlightTex,
					DetailNormalTex = slot.DetailNormalTex,
				};
			}
		}

		return new ResolvedLookSlot
		{
			Preset = FallbackPreset,
			DoubleSided = FallbackDoubleSided,
			EnableOutline = FallbackEnableOutline,
			IncludeInOutlineMask = true,
			OutlineWidth = FallbackOutlineWidth,
			OutlineDepthBias = FallbackOutlineDepthBias,
			FlattenOutlineDepth = false,
			OutlineDepthFlatten = 0.0f,
			SlotName = "fallback",
			SlotIndex = Math.Max(Slots.Count, 0),
			FaceShadowTex = null,
			FaceMirrorAxis = 0.5f,
			FaceYawOffsetDegrees = 0.0f,
			FaceForwardFlip = false,
			FaceSwapSides = false,
			ControlTex = null,
			HairHighlightTex = null,
			DetailNormalTex = null,
		};
	}
}
