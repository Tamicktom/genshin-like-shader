//* Libraries imports
using System;
using System.Collections.Generic;
using Godot;

//* Local imports
//* ...

/// <summary>
/// After the demo scene finishes loading (required node + settle frames),
/// captures one viewport PNG for lookdev / agent verification.
/// User args: <c>--grade-ab</c> cycles tonemap modes; <c>--fog-ab</c> cycles fog densities;
/// <c>--face-ab</c> toggles face-shadow map vs NdotL;
/// <c>--hair-ab</c> toggles hair highlight mask vs Kajiya-Kay;
/// <c>--outline-ab</c> cycles compositor outline debug / final modes;
/// <c>--dither-ab</c> toggles terminator Bayer dither off vs on (hair/cloth).
/// </summary>
public partial class CaptureSceneScreenshot : Node
{
	[Export]
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// Node that must exist before capturing (character root).
	/// </summary>
	[Export]
	public NodePath RequiredNodePath { get; set; } = new NodePath("RaidenShogun");

	/// <summary>
	/// Extra process frames after the required node is ready so deferred
	/// camera framing and the first shaded draw can land.
	/// </summary>
	[Export(PropertyHint.Range, "1,120")]
	public int SettleFrames { get; set; } = 12;

	/// <summary>
	/// Written under <c>res://screenshots/</c> (gitignored). Folder is created if missing.
	/// </summary>
	[Export]
	public string FileName { get; set; } = "scene_loaded.png";

	private int _settleCount;
	private bool _waitingForSettle;
	private bool _captured;

	private enum SweepKind
	{
		None,
		GradeAb,
		FogAb,
		FaceAb,
		HairAb,
		OutlineAb,
		DitherAb,
	}

	private SweepKind _sweep;
	private Godot.Environment _environment;
	private List<EnvShot> _shots;
	private List<FlagShot> _flagShots;
	private List<OutlineShot> _outlineShots;
	private List<DitherShot> _ditherShots;
	private List<(ShaderMaterial Material, float SavedStrength)> _ditherTargets;
	private int _shotIndex;
	private float _savedSaturation = 1.1f;
	private float _savedFogDensity = 0.0012f;
	private bool _savedFogEnabled = true;
	private bool _shotApplied;
	private List<ShaderMaterial> _flagMaterials;
	private string _flagUseParam;
	private SpinY _spinY;
	private bool _spinWasProcessing;
	private ToonOutlineCompositorEffect _outlineEffect;
	private bool _savedOutlineEnabled;
	private float _savedHighlightStrength;
	private ToonOutlineCompositorEffect.OutlineDebugMode _savedDebugMode;

	private readonly struct EnvShot
	{
		public EnvShot(
			string fileName,
			Godot.Environment.ToneMapper? mode,
			float? saturation,
			bool? fogEnabled,
			float? fogDensity)
		{
			FileName = fileName;
			Mode = mode;
			Saturation = saturation;
			FogEnabled = fogEnabled;
			FogDensity = fogDensity;
		}

		public string FileName { get; }
		public Godot.Environment.ToneMapper? Mode { get; }
		public float? Saturation { get; }
		public bool? FogEnabled { get; }
		public float? FogDensity { get; }
	}

	private readonly struct FlagShot
	{
		public FlagShot(string fileName, bool useFlag)
		{
			FileName = fileName;
			UseFlag = useFlag;
		}

		public string FileName { get; }
		public bool UseFlag { get; }
	}

	private readonly struct OutlineShot
	{
		public OutlineShot(
			string fileName,
			bool enabled,
			ToonOutlineCompositorEffect.OutlineDebugMode debugMode,
			float highlightStrength)
		{
			FileName = fileName;
			Enabled = enabled;
			DebugMode = debugMode;
			HighlightStrength = highlightStrength;
		}

		public string FileName { get; }
		public bool Enabled { get; }
		public ToonOutlineCompositorEffect.OutlineDebugMode DebugMode { get; }
		public float HighlightStrength { get; }
	}

	private readonly struct DitherShot
	{
		public DitherShot(string fileName, bool enabled)
		{
			FileName = fileName;
			Enabled = enabled;
		}

		public string FileName { get; }
		public bool Enabled { get; }
	}

	public override void _Ready()
	{
		if (!Enabled)
		{
			SetProcess(false);
			return;
		}

		if (HasUserArg("--grade-ab"))
		{
			_sweep = SweepKind.GradeAb;
			_shots = BuildGradeShots();
			_shotIndex = 0;
			_shotApplied = false;
		}
		else if (HasUserArg("--fog-ab"))
		{
			_sweep = SweepKind.FogAb;
			_shots = BuildFogShots();
			_shotIndex = 0;
			_shotApplied = false;
		}
		else if (HasUserArg("--face-ab"))
		{
			_sweep = SweepKind.FaceAb;
			_flagShots = BuildFaceShots();
			_flagUseParam = ShaderParams.UseFaceShadow;
			_shotIndex = 0;
			_shotApplied = false;
		}
		else if (HasUserArg("--hair-ab"))
		{
			_sweep = SweepKind.HairAb;
			_flagShots = BuildHairShots();
			_flagUseParam = ShaderParams.UseHairHighlight;
			_shotIndex = 0;
			_shotApplied = false;
		}
		else if (HasUserArg("--outline-ab"))
		{
			_sweep = SweepKind.OutlineAb;
			_outlineShots = BuildOutlineShots();
			_shotIndex = 0;
			_shotApplied = false;
		}
		else if (HasUserArg("--dither-ab"))
		{
			_sweep = SweepKind.DitherAb;
			_ditherShots = BuildDitherShots();
			_shotIndex = 0;
			_shotApplied = false;
		}
		else
		{
			_sweep = SweepKind.None;
		}

		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!Enabled)
		{
			return;
		}

		if (_sweep == SweepKind.FaceAb || _sweep == SweepKind.HairAb)
		{
			ProcessFlagSweep();
			return;
		}

		if (_sweep == SweepKind.OutlineAb)
		{
			ProcessOutlineSweep();
			return;
		}

		if (_sweep == SweepKind.DitherAb)
		{
			ProcessDitherSweep();
			return;
		}

		if (_sweep != SweepKind.None)
		{
			ProcessEnvSweep();
			return;
		}

		if (_captured)
		{
			return;
		}

		if (!_waitingForSettle)
		{
			if (!IsSceneReady())
			{
				return;
			}

			_waitingForSettle = true;
			_settleCount = 0;
		}

		_settleCount++;
		if (_settleCount < SettleFrames)
		{
			return;
		}

		CaptureOnce(FileName);
		_captured = true;
		SetProcess(false);
	}

	private void ProcessFlagSweep()
	{
		string label = _sweep == SweepKind.HairAb ? "hair-ab" : "face-ab";

		if (_flagShots == null || _shotIndex >= _flagShots.Count)
		{
			RestoreSpin();
			SetProcess(false);
			GetTree().Quit();
			return;
		}

		if (!IsSceneReady())
		{
			return;
		}

		if (_flagMaterials == null)
		{
			Node required = GetNodeOrNull(RequiredNodePath);
			_flagMaterials = CollectFlagMaterials(
				required,
				_flagUseParam,
				_sweep == SweepKind.HairAb ? ShaderParams.HairHighlightTex : ShaderParams.FaceShadowTex);
			if (_flagMaterials.Count == 0)
			{
				GD.PushError($"capture_scene_screenshot: {label} found no materials with {_flagUseParam}");
				SetProcess(false);
				GetTree().Quit();
				return;
			}

			FreezeSpin(required);
		}

		if (!_shotApplied)
		{
			ApplyFlagShot(_flagShots[_shotIndex]);
			_shotApplied = true;
			_waitingForSettle = true;
			_settleCount = 0;
			return;
		}

		_settleCount++;
		if (_settleCount < SettleFrames)
		{
			return;
		}

		FlagShot done = _flagShots[_shotIndex];
		CaptureOnce(done.FileName);
		GD.Print($"capture_scene_screenshot: {label} {_shotIndex + 1}/{_flagShots.Count} -> {done.FileName}");

		_shotIndex++;
		_shotApplied = false;
		_waitingForSettle = false;
		_settleCount = 0;

		if (_shotIndex >= _flagShots.Count)
		{
			RestoreSpin();
			SetProcess(false);
			GetTree().Quit();
		}
	}

	private void FreezeSpin(Node characterRoot)
	{
		_spinY = characterRoot?.GetNodeOrNull<SpinY>("SpinY");
		if (_spinY != null)
		{
			_spinWasProcessing = _spinY.IsProcessing();
			_spinY.SetProcess(false);
		}
	}

	private void RestoreSpin()
	{
		if (_spinY != null && GodotObject.IsInstanceValid(_spinY))
		{
			_spinY.SetProcess(_spinWasProcessing);
		}
	}

	private void ApplyFlagShot(FlagShot shot)
	{
		foreach (ShaderMaterial material in _flagMaterials)
		{
			if (material == null || !GodotObject.IsInstanceValid(material))
			{
				continue;
			}

			material.SetShaderParameter(_flagUseParam, shot.UseFlag);
		}
	}

	private static List<ShaderMaterial> CollectFlagMaterials(Node root, string useParam, string texParam)
	{
		var result = new List<ShaderMaterial>();
		if (root == null)
		{
			return result;
		}

		CollectFlagMaterialsRecursive(root, result, useParam, texParam);
		return result;
	}

	private static void CollectFlagMaterialsRecursive(
		Node node,
		List<ShaderMaterial> result,
		string useParam,
		string texParam)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
		{
			int surfaces = meshInstance.Mesh.GetSurfaceCount();
			for (int i = 0; i < surfaces; i++)
			{
				Material mat = meshInstance.GetActiveMaterial(i);
				if (mat is ShaderMaterial shaderMaterial && HasFlagOrTexture(shaderMaterial, useParam, texParam))
				{
					result.Add(shaderMaterial);
				}
			}
		}

		foreach (Node child in node.GetChildren())
		{
			CollectFlagMaterialsRecursive(child, result, useParam, texParam);
		}
	}

	private static bool HasFlagOrTexture(ShaderMaterial material, string useParam, string texParam)
	{
		Variant flag = material.GetShaderParameter(useParam);
		if (flag.VariantType == Variant.Type.Bool && flag.AsBool())
		{
			return true;
		}

		Variant tex = material.GetShaderParameter(texParam);
		return tex.VariantType == Variant.Type.Object && tex.AsGodotObject() is Texture2D;
	}

	private void ProcessEnvSweep()
	{
		if (_shots == null || _shotIndex >= _shots.Count)
		{
			SetProcess(false);
			GetTree().Quit();
			return;
		}

		if (!IsSceneReady())
		{
			return;
		}

		if (_environment == null)
		{
			WorldEnvironment world = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
			_environment = world?.Environment;
			if (_environment == null)
			{
				GD.PushError("capture_scene_screenshot: env sweep needs WorldEnvironment.Environment");
				SetProcess(false);
				GetTree().Quit();
				return;
			}

			_savedSaturation = _environment.AdjustmentSaturation;
			_savedFogDensity = _environment.FogDensity;
			_savedFogEnabled = _environment.FogEnabled;
			_environment.GlowEnabled = false;
			_environment.TonemapExposure = 1.0f;
			_environment.TonemapWhite = 1.0f;
		}

		if (!_shotApplied)
		{
			ApplyShot(_shots[_shotIndex]);
			_shotApplied = true;
			_waitingForSettle = true;
			_settleCount = 0;
			return;
		}

		_settleCount++;
		if (_settleCount < SettleFrames)
		{
			return;
		}

		EnvShot done = _shots[_shotIndex];
		CaptureOnce(done.FileName);
		string label = _sweep == SweepKind.FogAb ? "fog-ab" : "grade-ab";
		GD.Print($"capture_scene_screenshot: {label} {_shotIndex + 1}/{_shots.Count} -> {done.FileName}");

		_shotIndex++;
		_shotApplied = false;
		_waitingForSettle = false;
		_settleCount = 0;

		if (_shotIndex >= _shots.Count)
		{
			_environment.AdjustmentSaturation = _savedSaturation;
			_environment.FogEnabled = _savedFogEnabled;
			_environment.FogDensity = _savedFogDensity;
			SetProcess(false);
			GetTree().Quit();
		}
	}

	private void ApplyShot(EnvShot shot)
	{
		if (shot.Mode.HasValue)
		{
			_environment.TonemapMode = shot.Mode.Value;
		}

		if (shot.Saturation.HasValue)
		{
			_environment.AdjustmentEnabled = true;
			_environment.AdjustmentSaturation = shot.Saturation.Value;
		}

		if (shot.FogEnabled.HasValue)
		{
			_environment.FogEnabled = shot.FogEnabled.Value;
		}

		if (shot.FogDensity.HasValue)
		{
			_environment.FogDensity = shot.FogDensity.Value;
		}
	}

	private static List<EnvShot> BuildGradeShots()
	{
		return new List<EnvShot>
		{
			new("grade_linear.png", Godot.Environment.ToneMapper.Linear, 1.1f, null, null),
			new("grade_reinhardt.png", Godot.Environment.ToneMapper.Reinhardt, 1.1f, null, null),
			new("grade_filmic.png", Godot.Environment.ToneMapper.Filmic, 1.1f, null, null),
			new("grade_aces.png", Godot.Environment.ToneMapper.Aces, 1.1f, null, null),
			new("grade_agx.png", Godot.Environment.ToneMapper.Agx, 1.1f, null, null),
			new("grade_filmic_sat10.png", Godot.Environment.ToneMapper.Filmic, 1.0f, null, null),
			new("grade_filmic_sat11.png", Godot.Environment.ToneMapper.Filmic, 1.1f, null, null),
		};
	}

	private static List<EnvShot> BuildFogShots()
	{
		// Filmic + sat 1.1 stays locked; only fog changes.
		return new List<EnvShot>
		{
			new("fog_off.png", Godot.Environment.ToneMapper.Filmic, 1.1f, false, 0.0f),
			new("fog_0012.png", Godot.Environment.ToneMapper.Filmic, 1.1f, true, 0.0012f),
			new("fog_008.png", Godot.Environment.ToneMapper.Filmic, 1.1f, true, 0.008f),
		};
	}

	private static List<FlagShot> BuildFaceShots()
	{
		return new List<FlagShot>
		{
			new("face_ndl.png", false),
			new("face_map.png", true),
		};
	}

	private static List<FlagShot> BuildHairShots()
	{
		return new List<FlagShot>
		{
			new("hair_kajiya.png", false),
			new("hair_mask.png", true),
		};
	}

	private static List<OutlineShot> BuildOutlineShots()
	{
		return new List<OutlineShot>
		{
			new(
				"outline_hull.png",
				false,
				ToonOutlineCompositorEffect.OutlineDebugMode.Final,
				0.0f),
			new(
				"outline_debug_depth.png",
				true,
				ToonOutlineCompositorEffect.OutlineDebugMode.RawDepth,
				0.0f),
			new(
				"outline_debug_normal.png",
				true,
				ToonOutlineCompositorEffect.OutlineDebugMode.DepthEdges,
				0.0f),
			new(
				"outline_comp.png",
				true,
				ToonOutlineCompositorEffect.OutlineDebugMode.Final,
				0.0f),
			new(
				"outline_highlight.png",
				true,
				ToonOutlineCompositorEffect.OutlineDebugMode.Final,
				0.01f),
		};
	}

	private static List<DitherShot> BuildDitherShots()
	{
		return new List<DitherShot>
		{
			new("dither_off.png", false),
			new("dither_on.png", true),
		};
	}

	private void ProcessDitherSweep()
	{
		if (_ditherShots == null || _shotIndex >= _ditherShots.Count)
		{
			RestoreDitherStrengths();
			RestoreSpin();
			SetProcess(false);
			GetTree().Quit();
			return;
		}

		if (!IsSceneReady())
		{
			return;
		}

		if (_ditherTargets == null)
		{
			Node required = GetNodeOrNull(RequiredNodePath);
			_ditherTargets = CollectDitherTargets(required);
			if (_ditherTargets.Count == 0)
			{
				GD.PushError("capture_scene_screenshot: dither-ab found no materials with dither_strength > 0");
				SetProcess(false);
				GetTree().Quit();
				return;
			}

			FreezeSpin(required);
		}

		if (!_shotApplied)
		{
			ApplyDitherShot(_ditherShots[_shotIndex]);
			_shotApplied = true;
			_waitingForSettle = true;
			_settleCount = 0;
			return;
		}

		_settleCount++;
		if (_settleCount < SettleFrames)
		{
			return;
		}

		DitherShot done = _ditherShots[_shotIndex];
		CaptureOnce(done.FileName);
		GD.Print($"capture_scene_screenshot: dither-ab {_shotIndex + 1}/{_ditherShots.Count} -> {done.FileName}");

		_shotIndex++;
		_shotApplied = false;
		_waitingForSettle = false;
		_settleCount = 0;

		if (_shotIndex >= _ditherShots.Count)
		{
			RestoreDitherStrengths();
			RestoreSpin();
			SetProcess(false);
			GetTree().Quit();
		}
	}

	private void ApplyDitherShot(DitherShot shot)
	{
		foreach ((ShaderMaterial material, float savedStrength) in _ditherTargets)
		{
			if (material == null || !GodotObject.IsInstanceValid(material))
			{
				continue;
			}

			material.SetShaderParameter(
				ShaderParams.DitherStrength,
				shot.Enabled ? savedStrength : 0.0f);
		}
	}

	private void RestoreDitherStrengths()
	{
		if (_ditherTargets == null)
		{
			return;
		}

		foreach ((ShaderMaterial material, float savedStrength) in _ditherTargets)
		{
			if (material == null || !GodotObject.IsInstanceValid(material))
			{
				continue;
			}

			material.SetShaderParameter(ShaderParams.DitherStrength, savedStrength);
		}
	}

	private static List<(ShaderMaterial Material, float SavedStrength)> CollectDitherTargets(Node root)
	{
		var result = new List<(ShaderMaterial, float)>();
		if (root == null)
		{
			return result;
		}

		CollectDitherTargetsRecursive(root, result);
		return result;
	}

	private static void CollectDitherTargetsRecursive(
		Node node,
		List<(ShaderMaterial Material, float SavedStrength)> result)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
		{
			int surfaces = meshInstance.Mesh.GetSurfaceCount();
			for (int i = 0; i < surfaces; i++)
			{
				Material mat = meshInstance.GetActiveMaterial(i);
				if (mat is not ShaderMaterial shaderMaterial)
				{
					continue;
				}

				Variant strengthVar = shaderMaterial.GetShaderParameter(ShaderParams.DitherStrength);
				if (strengthVar.VariantType != Variant.Type.Float && strengthVar.VariantType != Variant.Type.Int)
				{
					continue;
				}

				float strength = strengthVar.AsSingle();
				if (strength > 0.0001f)
				{
					result.Add((shaderMaterial, strength));
				}
			}
		}

		foreach (Node child in node.GetChildren())
		{
			CollectDitherTargetsRecursive(child, result);
		}
	}

	private void ProcessOutlineSweep()
	{
		if (_outlineShots == null || _shotIndex >= _outlineShots.Count)
		{
			RestoreOutlineEffect();
			RestoreSpin();
			SetProcess(false);
			GetTree().Quit();
			return;
		}

		if (!IsSceneReady())
		{
			return;
		}

		if (_outlineEffect == null)
		{
			_outlineEffect = FindOutlineEffect();
			if (_outlineEffect == null)
			{
				GD.PushError("capture_scene_screenshot: outline-ab needs ToonOutlineCompositorEffect on WorldEnvironment");
				SetProcess(false);
				GetTree().Quit();
				return;
			}

			_savedOutlineEnabled = _outlineEffect.Enabled;
			_savedHighlightStrength = _outlineEffect.HighlightStrength;
			_savedDebugMode = _outlineEffect.DebugMode;
			FreezeSpin(GetNodeOrNull(RequiredNodePath));
		}

		if (!_shotApplied)
		{
			ApplyOutlineShot(_outlineShots[_shotIndex]);
			_shotApplied = true;
			_waitingForSettle = true;
			_settleCount = 0;
			return;
		}

		_settleCount++;
		if (_settleCount < SettleFrames)
		{
			return;
		}

		OutlineShot done = _outlineShots[_shotIndex];
		CaptureOnce(done.FileName);
		GD.Print($"capture_scene_screenshot: outline-ab {_shotIndex + 1}/{_outlineShots.Count} -> {done.FileName}");

		_shotIndex++;
		_shotApplied = false;
		_waitingForSettle = false;
		_settleCount = 0;

		if (_shotIndex >= _outlineShots.Count)
		{
			RestoreOutlineEffect();
			RestoreSpin();
			SetProcess(false);
			GetTree().Quit();
		}
	}

	private void ApplyOutlineShot(OutlineShot shot)
	{
		_outlineEffect.Enabled = shot.Enabled;
		_outlineEffect.DebugMode = shot.DebugMode;
		_outlineEffect.HighlightStrength = shot.HighlightStrength;
	}

	private void RestoreOutlineEffect()
	{
		if (_outlineEffect == null || !GodotObject.IsInstanceValid(_outlineEffect))
		{
			return;
		}

		_outlineEffect.Enabled = _savedOutlineEnabled;
		_outlineEffect.HighlightStrength = _savedHighlightStrength;
		_outlineEffect.DebugMode = _savedDebugMode;
	}

	private ToonOutlineCompositorEffect FindOutlineEffect()
	{
		WorldEnvironment world = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		Compositor compositor = world?.Compositor;
		if (compositor?.CompositorEffects == null)
		{
			return null;
		}

		foreach (CompositorEffect effect in compositor.CompositorEffects)
		{
			if (effect is ToonOutlineCompositorEffect outline)
			{
				return outline;
			}
		}

		return null;
	}

	private static bool HasUserArg(string flag)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (string.Equals(arg, flag, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private bool IsSceneReady()
	{
		Node required = GetNodeOrNull(RequiredNodePath);
		if (required == null || !GodotObject.IsInstanceValid(required))
		{
			return false;
		}

		if (!required.IsInsideTree() || !required.IsNodeReady())
		{
			return false;
		}

		return HasRenderableMesh(required);
	}

	private static bool HasRenderableMesh(Node root)
	{
		if (root is MeshInstance3D meshInstance && meshInstance.Mesh != null)
		{
			return true;
		}

		foreach (Node child in root.GetChildren())
		{
			if (HasRenderableMesh(child))
			{
				return true;
			}
		}

		return false;
	}

	private void CaptureOnce(string fileName)
	{
		Viewport viewport = GetViewport();
		if (viewport == null)
		{
			GD.PushError("capture_scene_screenshot: no viewport");
			return;
		}

		Image image = viewport.GetTexture()?.GetImage();
		if (image == null)
		{
			GD.PushError("capture_scene_screenshot: viewport image was null");
			return;
		}

		string dir = ProjectSettings.GlobalizePath("res://screenshots");
		DirAccess.MakeDirRecursiveAbsolute(dir);

		string safeName = string.IsNullOrWhiteSpace(fileName) ? "scene_loaded.png" : fileName.Trim();
		string path = $"{dir}/{safeName}";
		Error saveError = image.SavePng(path);
		if (saveError != Error.Ok)
		{
			GD.PushError($"capture_scene_screenshot: save_png failed ({saveError}) -> {path}");
			return;
		}

		GD.Print($"capture_scene_screenshot: saved {path}");
	}
}
