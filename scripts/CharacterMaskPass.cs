//* Libraries imports
using Godot;

//* Local imports
//* ...

/// <summary>
/// Half-resolution SubViewport that renders only the character mask layer so the
/// toon outline compositor can gate depth edges to the character.
/// </summary>
public partial class CharacterMaskPass : Node
{
	[Export]
	public NodePath SourceCameraPath { get; set; } = new NodePath("../Camera3D");

	[Export]
	public NodePath CharacterRootPath { get; set; } = new NodePath("../RaidenShogun");

	/// <summary>
	/// Must match <see cref="ApplyCharacterLook.CharacterMaskLayer"/>.
	/// </summary>
	[Export(PropertyHint.Layers3DRender)]
	public uint MaskCullLayer { get; set; } = 2;

	[Export(PropertyHint.Range, "0.25,1.0")]
	public float ResolutionScale { get; set; } = 0.5f;

	[Export]
	public bool Enabled { get; set; } = true;

	private SubViewport _viewport;
	private Camera3D _maskCamera;
	private Camera3D _sourceCamera;
	private ViewportTexture _maskTexture;
	private Rid _maskRdTexture;

	public ViewportTexture MaskTexture => _maskTexture;

	public Rid MaskRdTexture
	{
		get
		{
			if (_maskTexture == null)
			{
				return default;
			}

			Rid texRid = _maskTexture.GetRid();
			if (!texRid.IsValid)
			{
				return default;
			}

			_maskRdTexture = RenderingServer.TextureGetRdTexture(texRid);
			return _maskRdTexture;
		}
	}

	public override void _Ready()
	{
		if (!Enabled)
		{
			SetProcess(false);
			return;
		}

		EnsureViewport();
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!Enabled || _maskCamera == null)
		{
			return;
		}

		if (_sourceCamera == null || !GodotObject.IsInstanceValid(_sourceCamera))
		{
			_sourceCamera = GetNodeOrNull<Camera3D>(SourceCameraPath);
			if (_sourceCamera == null)
			{
				return;
			}
		}

		SyncCamera();
		SyncViewportSize();
		PushMaskToOutlineEffect();
	}

	private void EnsureViewport()
	{
		_viewport = GetNodeOrNull<SubViewport>("MaskViewport");
		if (_viewport == null)
		{
			_viewport = new SubViewport
			{
				Name = "MaskViewport",
				TransparentBg = true,
				HandleInputLocally = false,
				RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
				Msaa3D = Viewport.Msaa.Disabled,
				OwnWorld3D = false,
			};
			AddChild(_viewport);
		}

		_maskCamera = _viewport.GetNodeOrNull<Camera3D>("MaskCamera");
		if (_maskCamera == null)
		{
			_maskCamera = new Camera3D
			{
				Name = "MaskCamera",
				CullMask = MaskCullLayer,
				Current = true,
			};
			_viewport.AddChild(_maskCamera);
		}

		_maskCamera.CullMask = MaskCullLayer;
		_maskCamera.Environment = BuildClearEnvironment();
		_maskCamera.Compositor = new Compositor();

		_maskTexture = _viewport.GetTexture();
		SyncViewportSize();
	}

	private static Godot.Environment BuildClearEnvironment()
	{
		return new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.ClearColor,
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = Colors.Black,
			AmbientLightEnergy = 0.0f,
			TonemapMode = Godot.Environment.ToneMapper.Linear,
			FogEnabled = false,
			GlowEnabled = false,
			AdjustmentEnabled = false,
		};
	}

	private void SyncCamera()
	{
		_maskCamera.GlobalTransform = _sourceCamera.GlobalTransform;
		_maskCamera.Fov = _sourceCamera.Fov;
		_maskCamera.Near = _sourceCamera.Near;
		_maskCamera.Far = _sourceCamera.Far;
		_maskCamera.Projection = _sourceCamera.Projection;
		_maskCamera.KeepAspect = _sourceCamera.KeepAspect;
		_maskCamera.CullMask = MaskCullLayer;
	}

	private void SyncViewportSize()
	{
		if (_viewport == null)
		{
			return;
		}

		Viewport root = GetViewport();
		if (root == null)
		{
			return;
		}

		Vector2 visible = root.GetVisibleRect().Size;
		int w = Mathf.Max(1, Mathf.RoundToInt(visible.X * ResolutionScale));
		int h = Mathf.Max(1, Mathf.RoundToInt(visible.Y * ResolutionScale));
		if (_viewport.Size.X != w || _viewport.Size.Y != h)
		{
			_viewport.Size = new Vector2I(w, h);
		}
	}

	private void PushMaskToOutlineEffect()
	{
		WorldEnvironment world = GetNodeOrNull<WorldEnvironment>("../WorldEnvironment");
		if (world == null)
		{
			Node root = GetTree()?.CurrentScene;
			world = root?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		}

		Compositor compositor = world?.Compositor;
		if (compositor?.CompositorEffects == null)
		{
			return;
		}

		Rid maskRid = MaskRdTexture;
		foreach (CompositorEffect effect in compositor.CompositorEffects)
		{
			if (effect is ToonOutlineCompositorEffect outline)
			{
				outline.SetCharacterMaskTexture(maskRid, _maskTexture);
			}
		}
	}
}
