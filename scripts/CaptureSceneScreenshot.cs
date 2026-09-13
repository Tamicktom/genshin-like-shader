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
/// <c>--face-ab</c> toggles face-shadow map vs NdotL.
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
	}

	private SweepKind _sweep;
	private Godot.Environment _environment;
	private List<EnvShot> _shots;
	private List<FaceShot> _faceShots;
	private int _shotIndex;
	private float _savedSaturation = 1.1f;
	private float _savedFogDensity = 0.0012f;
	private bool _savedFogEnabled = true;
	private bool _shotApplied;
	private List<ShaderMaterial> _faceMaterials;
	private SpinY _spinY;
	private bool _spinWasProcessing;

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

	private readonly struct FaceShot
	{
		public FaceShot(string fileName, bool useFaceShadow)
		{
			FileName = fileName;
			UseFaceShadow = useFaceShadow;
		}

		public string FileName { get; }
		public bool UseFaceShadow { get; }
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
			_faceShots = BuildFaceShots();
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

		if (_sweep == SweepKind.FaceAb)
		{
			ProcessFaceSweep();
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

	private void ProcessFaceSweep()
	{
		if (_faceShots == null || _shotIndex >= _faceShots.Count)
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

		if (_faceMaterials == null)
		{
			Node required = GetNodeOrNull(RequiredNodePath);
			_faceMaterials = CollectFaceShadowMaterials(required);
			if (_faceMaterials.Count == 0)
			{
				GD.PushError("capture_scene_screenshot: face-ab found no use_face_shadow materials");
				SetProcess(false);
				GetTree().Quit();
				return;
			}

			FreezeSpin(required);
		}

		if (!_shotApplied)
		{
			ApplyFaceShot(_faceShots[_shotIndex]);
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

		FaceShot done = _faceShots[_shotIndex];
		CaptureOnce(done.FileName);
		GD.Print($"capture_scene_screenshot: face-ab {_shotIndex + 1}/{_faceShots.Count} -> {done.FileName}");

		_shotIndex++;
		_shotApplied = false;
		_waitingForSettle = false;
		_settleCount = 0;

		if (_shotIndex >= _faceShots.Count)
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

	private void ApplyFaceShot(FaceShot shot)
	{
		foreach (ShaderMaterial material in _faceMaterials)
		{
			if (material == null || !GodotObject.IsInstanceValid(material))
			{
				continue;
			}

			material.SetShaderParameter(ShaderParams.UseFaceShadow, shot.UseFaceShadow);
		}
	}

	private static List<ShaderMaterial> CollectFaceShadowMaterials(Node root)
	{
		var result = new List<ShaderMaterial>();
		if (root == null)
		{
			return result;
		}

		CollectFaceShadowMaterialsRecursive(root, result);
		return result;
	}

	private static void CollectFaceShadowMaterialsRecursive(Node node, List<ShaderMaterial> result)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
		{
			int surfaces = meshInstance.Mesh.GetSurfaceCount();
			for (int i = 0; i < surfaces; i++)
			{
				Material mat = meshInstance.GetActiveMaterial(i);
				if (mat is ShaderMaterial shaderMaterial && HasFaceShadowEnabled(shaderMaterial))
				{
					result.Add(shaderMaterial);
				}
			}
		}

		foreach (Node child in node.GetChildren())
		{
			CollectFaceShadowMaterialsRecursive(child, result);
		}
	}

	private static bool HasFaceShadowEnabled(ShaderMaterial material)
	{
		Variant flag = material.GetShaderParameter(ShaderParams.UseFaceShadow);
		if (flag.VariantType == Variant.Type.Bool && flag.AsBool())
		{
			return true;
		}

		Variant tex = material.GetShaderParameter(ShaderParams.FaceShadowTex);
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

	private static List<FaceShot> BuildFaceShots()
	{
		return new List<FaceShot>
		{
			new("face_ndl.png", false),
			new("face_map.png", true),
		};
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
