//* Libraries imports
using Godot;

//* Local imports
//* ...

/// <summary>
/// One part of a character look: how to match a mesh, which preset, outline flags.
/// </summary>
[GlobalClass]
public partial class LookSlot : Resource
{
	[Export]
	public string SlotName { get; set; } = "";

	/// <summary>
	/// Case-insensitive wildcards matched against MeshInstance3D.Name (`*face*`).
	/// </summary>
	[Export]
	public string[] NamePatterns { get; set; } = System.Array.Empty<string>();

	/// <summary>
	/// Case-insensitive wildcards matched against albedo texture ResourcePath.
	/// </summary>
	[Export]
	public string[] TexturePatterns { get; set; } = System.Array.Empty<string>();

	[Export]
	public ToonPreset Preset { get; set; }

	[ExportGroup("Flags")]
	[Export]
	public bool DoubleSided { get; set; }

	[Export]
	public bool EnableOutline { get; set; } = true;

	[Export(PropertyHint.Range, "0.0,12.0")]
	public float OutlineWidth { get; set; } = 1.25f;

	[Export(PropertyHint.Range, "0.0,0.4")]
	public float OutlineDepthBias { get; set; }

	public bool Matches(string meshName, string texturePath)
	{
		if (AnyPatternMatches(NamePatterns, meshName))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(texturePath) && AnyPatternMatches(TexturePatterns, texturePath))
		{
			return true;
		}

		return false;
	}

	private static bool AnyPatternMatches(string[] patterns, string haystack)
	{
		if (patterns == null)
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
}
