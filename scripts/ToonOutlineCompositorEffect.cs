//* Libraries imports
using Godot;
using Godot.Collections;

//* Local imports
//* ...

/// <summary>
/// Forward+ compositor pass: reverse-Z depth Sobel dark outline + optional
/// far-side white edge highlight. Hull next-pass stays for albedo-tinted lines.
/// Optional character mask gates both dark outline and highlight.
/// </summary>
/// <remarks>
/// <see cref="CompositorEffect.NeedsNormalRoughness"/> stays off — enabling it
/// blacks out this project's custom <c>light()</c> toon materials. Edges come
/// from resolved depth only (relative linear-Z Sobel), not color luma.
/// </remarks>
[Tool]
[GlobalClass]
public partial class ToonOutlineCompositorEffect : CompositorEffect
{
	public enum OutlineDebugMode
	{
		Final = 0,
		RawDepth = 1,
		NormalEdges = 2, // unused (NR disabled); kept for sweep enum stability
		DepthEdges = 3,
		Combined = 4,
		CharacterMask = 5,
	}

	[ExportGroup("Detection")]
	[Export(PropertyHint.Range, "0.5,8.0")]
	public float Thickness { get; set; } = 1.0f;

	/// <summary>
	/// Relative linear-depth Sobel threshold (lower = more edges).
	/// </summary>
	[Export(PropertyHint.Range, "0.001,1.0")]
	public float DepthThreshold { get; set; } = 0.18f;

	[Export(PropertyHint.Range, "0.01,2.0")]
	public float NormalThreshold { get; set; } = 0.4f;

	[ExportGroup("Dark Outline")]
	[Export]
	public Color OutlineColor { get; set; } = new Color(0.12f, 0.08f, 0.16f, 1.0f);

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float OutlineStrength { get; set; } = 0.75f;

	[ExportGroup("Edge Highlight")]
	[Export]
	public Color HighlightColor { get; set; } = new Color(1.0f, 1.0f, 1.0f, 1.0f);

	[Export(PropertyHint.Range, "0.0,1.0")]
	public float HighlightStrength { get; set; } = 0.12f;

	[Export(PropertyHint.Range, "0.0,8.0")]
	public float HighlightYOffset { get; set; } = 1.0f;

	[ExportGroup("Character Mask")]
	/// <summary>
	/// When true and a mask Rid is available, gate outline/highlight by the
	/// character coverage texture from CharacterMaskPass.
	/// </summary>
	[Export]
	public bool UseCharacterMask { get; set; } = true;

	/// <summary>
	/// Outline strength multiplier for pixels outside the character mask
	/// (0 = no environment edges, 1 = full scene outline).
	/// </summary>
	[Export(PropertyHint.Range, "0.0,1.0")]
	public float EnvironmentOutlineStrength { get; set; }

	[ExportGroup("Debug")]
	[Export]
	public OutlineDebugMode DebugMode { get; set; } = OutlineDebugMode.Final;

	private RenderingDevice _rd;
	private Rid _shader;
	private Rid _pipeline;
	private Rid _nearestSampler;
	private Rid _linearSampler;
	private Rid _depthView;
	private Rid _depthViewSource;
	private Rid _characterMaskRd;
	private ViewportTexture _characterMaskViewportTexture;
	private Rid _fallbackMaskTexture;
	private bool _shaderFailed;
	private bool _loggedDepthFormat;
	private float _zNear = 0.05f;

	public ToonOutlineCompositorEffect()
	{
		EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
		AccessResolvedColor = true;
		AccessResolvedDepth = true;
		NeedsNormalRoughness = false;
		Enabled = true;
	}

	/// <summary>
	/// Called each frame by CharacterMaskPass with the SubViewport RD texture.
	/// </summary>
	public void SetCharacterMaskTexture(Rid maskRd, ViewportTexture viewportTexture)
	{
		_characterMaskRd = maskRd;
		_characterMaskViewportTexture = viewportTexture;
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
		{
			CleanupGpu();
		}
	}

	public override void _RenderCallback(int effectCallbackType, RenderData renderData)
	{
		if (_rd == null)
		{
			_rd = RenderingServer.GetRenderingDevice();
			if (_rd == null)
			{
				return;
			}
		}

		if (effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent)
		{
			return;
		}

		if (!EnsurePipeline())
		{
			return;
		}

		RenderSceneBuffers renderSceneBuffers = renderData.GetRenderSceneBuffers();
		if (renderSceneBuffers is not RenderSceneBuffersRD sceneBuffers)
		{
			return;
		}

		RenderSceneData sceneData = renderData.GetRenderSceneData();
		if (sceneData == null)
		{
			return;
		}

		Vector2I size = sceneBuffers.GetInternalSize();
		if (size.X == 0 || size.Y == 0)
		{
			return;
		}

		uint xGroups = (uint)((size.X - 1) / 8 + 1);
		uint yGroups = (uint)((size.Y - 1) / 8 + 1);

		Projection camProj = sceneData.GetCamProjection();
		_zNear = Mathf.Abs(camProj.GetZNear());
		if (_zNear < 1e-5f)
		{
			_zNear = 0.05f;
		}

		bool maskActive = UseCharacterMask && _characterMaskRd.IsValid;
		byte[] pushConstants = BuildPushConstants(size, maskActive);
		uint viewCount = sceneBuffers.GetViewCount();

		for (uint view = 0; view < viewCount; view++)
		{
			Rid colorImage = sceneBuffers.GetTexture("render_buffers", "color");
			if (!colorImage.IsValid)
			{
				colorImage = sceneBuffers.GetColorLayer(view, false);
			}

			Rid depthTex = sceneBuffers.GetTexture("render_buffers", "depth");
			if (!depthTex.IsValid)
			{
				depthTex = sceneBuffers.GetDepthLayer(view, false);
			}

			if (!colorImage.IsValid || !depthTex.IsValid)
			{
				continue;
			}

			Rid depthSample = EnsureDepthSampleView(depthTex);

			var colorUniform = new RDUniform
			{
				UniformType = RenderingDevice.UniformType.Image,
				Binding = 0,
			};
			colorUniform.AddId(colorImage);

			var depthUniform = new RDUniform
			{
				UniformType = RenderingDevice.UniformType.SamplerWithTexture,
				Binding = 0,
			};
			depthUniform.AddId(_nearestSampler);
			depthUniform.AddId(depthSample);

			Rid colorSet = UniformSetCacheRD.GetCache(_shader, 0, new Array<RDUniform> { colorUniform });
			Rid depthSet = UniformSetCacheRD.GetCache(_shader, 1, new Array<RDUniform> { depthUniform });

			Rid maskSample = maskActive ? _characterMaskRd : EnsureFallbackMaskTexture();
			if (!maskSample.IsValid)
			{
				continue;
			}

			var maskUniform = new RDUniform
			{
				UniformType = RenderingDevice.UniformType.SamplerWithTexture,
				Binding = 0,
			};
			maskUniform.AddId(_linearSampler);
			maskUniform.AddId(maskSample);
			Rid maskSet = UniformSetCacheRD.GetCache(_shader, 2, new Array<RDUniform> { maskUniform });

			if (!colorSet.IsValid || !depthSet.IsValid || !maskSet.IsValid)
			{
				continue;
			}

			long computeList = _rd.ComputeListBegin();
			_rd.ComputeListBindComputePipeline(computeList, _pipeline);
			_rd.ComputeListBindUniformSet(computeList, colorSet, 0);
			_rd.ComputeListBindUniformSet(computeList, depthSet, 1);
			_rd.ComputeListBindUniformSet(computeList, maskSet, 2);
			_rd.ComputeListSetPushConstant(computeList, pushConstants, (uint)pushConstants.Length);
			_rd.ComputeListDispatch(computeList, xGroups, yGroups, 1);
			_rd.ComputeListEnd();
		}
	}

	private Rid EnsureFallbackMaskTexture()
	{
		if (_fallbackMaskTexture.IsValid)
		{
			return _fallbackMaskTexture;
		}

		if (_rd == null)
		{
			return default;
		}

		// 1x1 opaque white so character_mask() returns 1 when masking is off.
		var format = new RDTextureFormat
		{
			Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
			Width = 1,
			Height = 1,
			Depth = 1,
			ArrayLayers = 1,
			Mipmaps = 1,
			TextureType = RenderingDevice.TextureType.Type2D,
			UsageBits = RenderingDevice.TextureUsageBits.SamplingBit
				| RenderingDevice.TextureUsageBits.CanUpdateBit,
		};
		var view = new RDTextureView();
		byte[] white = { 255, 255, 255, 255 };
		_fallbackMaskTexture = _rd.TextureCreate(format, view, new Array<byte[]> { white });
		return _fallbackMaskTexture;
	}

	private Rid EnsureDepthSampleView(Rid depthTex)
	{
		if (_depthView.IsValid && _depthViewSource == depthTex)
		{
			return _depthView;
		}

		_depthView = default;
		_depthViewSource = depthTex;

		RenderingDevice.DataFormat format = _rd.TextureGetFormat(depthTex).Format;
		if (!_loggedDepthFormat)
		{
			_loggedDepthFormat = true;
			GD.Print($"toon_outline_compositor: depth format={format}");
		}

		if (format == RenderingDevice.DataFormat.D32SfloatS8Uint
			|| format == RenderingDevice.DataFormat.D24UnormS8Uint)
		{
			var view = new RDTextureView
			{
				FormatOverride = RenderingDevice.DataFormat.D32Sfloat,
			};
			Rid shared = _rd.TextureCreateShared(view, depthTex);
			if (shared.IsValid)
			{
				_depthView = shared;
				return _depthView;
			}
		}

		return depthTex;
	}

	private bool EnsurePipeline()
	{
		if (_pipeline.IsValid)
		{
			return true;
		}

		if (_shaderFailed || _rd == null)
		{
			return false;
		}

		if (!_nearestSampler.IsValid)
		{
			var samplerState = new RDSamplerState
			{
				MinFilter = RenderingDevice.SamplerFilter.Nearest,
				MagFilter = RenderingDevice.SamplerFilter.Nearest,
				RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
				RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
				RepeatW = RenderingDevice.SamplerRepeatMode.ClampToEdge,
			};
			_nearestSampler = _rd.SamplerCreate(samplerState);
		}

		if (!_linearSampler.IsValid)
		{
			var samplerState = new RDSamplerState
			{
				MinFilter = RenderingDevice.SamplerFilter.Linear,
				MagFilter = RenderingDevice.SamplerFilter.Linear,
				RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
				RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
				RepeatW = RenderingDevice.SamplerRepeatMode.ClampToEdge,
			};
			_linearSampler = _rd.SamplerCreate(samplerState);
		}

		string code = FileAccess.GetFileAsString("res://shaders/toon_outline.glsl");
		if (string.IsNullOrEmpty(code))
		{
			GD.PushError("toon_outline_compositor: failed to read res://shaders/toon_outline.glsl");
			_shaderFailed = true;
			return false;
		}

		var shaderSource = new RDShaderSource
		{
			Language = RenderingDevice.ShaderLanguage.Glsl,
		};
		shaderSource.SourceCompute = code;

		RDShaderSpirV spirv = _rd.ShaderCompileSpirVFromSource(shaderSource);
		string compileError = spirv.CompileErrorCompute;
		if (!string.IsNullOrEmpty(compileError))
		{
			GD.PushError($"toon_outline_compositor: GLSL compile failed:\n{compileError}");
			_shaderFailed = true;
			return false;
		}

		_shader = _rd.ShaderCreateFromSpirV(spirv);
		if (!_shader.IsValid)
		{
			GD.PushError("toon_outline_compositor: ShaderCreateFromSpirV failed");
			_shaderFailed = true;
			return false;
		}

		_pipeline = _rd.ComputePipelineCreate(_shader);
		if (!_pipeline.IsValid)
		{
			GD.PushError("toon_outline_compositor: ComputePipelineCreate failed");
			_shaderFailed = true;
			return false;
		}

		return true;
	}

	private byte[] BuildPushConstants(Vector2I size, bool maskActive)
	{
		var floats = new float[20];
		int i = 0;

		floats[i++] = size.X;
		floats[i++] = size.Y;
		floats[i++] = Thickness;
		floats[i++] = DepthThreshold;

		// Reuse former NormalThreshold slot for environment outline strength.
		floats[i++] = EnvironmentOutlineStrength;
		floats[i++] = OutlineStrength;
		floats[i++] = HighlightStrength;
		floats[i++] = HighlightYOffset;

		floats[i++] = OutlineColor.R;
		floats[i++] = OutlineColor.G;
		floats[i++] = OutlineColor.B;
		floats[i++] = OutlineColor.A;

		floats[i++] = HighlightColor.R;
		floats[i++] = HighlightColor.G;
		floats[i++] = HighlightColor.B;
		floats[i++] = HighlightColor.A;

		floats[i++] = _zNear;
		floats[i++] = (float)DebugMode;
		floats[i++] = maskActive ? 1.0f : 0.0f;
		floats[i++] = 0.0f;

		var bytes = new byte[floats.Length * sizeof(float)];
		System.Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
		return bytes;
	}

	private void CleanupGpu()
	{
		if (_rd == null)
		{
			return;
		}

		_depthView = default;
		_depthViewSource = default;
		_characterMaskRd = default;
		_characterMaskViewportTexture = null;

		if (_shader.IsValid)
		{
			_rd.FreeRid(_shader);
			_shader = default;
			_pipeline = default;
		}

		if (_nearestSampler.IsValid)
		{
			_rd.FreeRid(_nearestSampler);
			_nearestSampler = default;
		}

		if (_linearSampler.IsValid)
		{
			_rd.FreeRid(_linearSampler);
			_linearSampler = default;
		}

		if (_fallbackMaskTexture.IsValid)
		{
			_rd.FreeRid(_fallbackMaskTexture);
			_fallbackMaskTexture = default;
		}
	}
}
