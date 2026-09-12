//* Libraries imports
using Godot;
using Godot.Collections;

//* Local imports
//* ...

/// <summary>
/// Places the camera so the target character fills the view, including on resize.
/// </summary>
public partial class FrameCharacterCamera : Camera3D
{
	[Export]
	public NodePath TargetPath { get; set; }

	[Export]
	public float Padding { get; set; } = 1.35f;

	[Export]
	public float HeightBias { get; set; } = 0.15f;

	[Export]
	public float YawDegrees { get; set; } = 28.0f;

	[Export]
	public float PitchDegrees { get; set; } = -8.0f;

	public override void _Ready()
	{
		GetViewport().SizeChanged += OnViewportSizeChanged;
		CallDeferred(MethodName.FrameTarget);
	}

	public override void _ExitTree()
	{
		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			viewport.SizeChanged -= OnViewportSizeChanged;
		}
	}

	private void OnViewportSizeChanged()
	{
		FrameTarget();
	}

	private void FrameTarget()
	{
		Node target = GetNodeOrNull(TargetPath);
		if (target == null)
		{
			return;
		}

		Aabb bounds = ComputeAabb(target);
		if (bounds.Size == Vector3.Zero)
		{
			return;
		}

		Vector3 center = bounds.GetCenter() + new Vector3(0.0f, bounds.Size.Y * HeightBias, 0.0f);
		float radius = bounds.Size.Length() * 0.5f * Padding;

		// Account for viewport aspect so the character stays framed when resizing.
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float aspect = viewportSize.X / Mathf.Max(viewportSize.Y, 1.0f);
		float verticalFov = Mathf.DegToRad(Fov);
		float horizontalFov = 2.0f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * aspect);
		float limitingFov = Mathf.Min(verticalFov, horizontalFov);
		float distance = radius / Mathf.Max(Mathf.Tan(limitingFov * 0.5f), 0.001f);

		float yaw = Mathf.DegToRad(YawDegrees);
		float pitch = Mathf.DegToRad(PitchDegrees);
		Vector3 offset = new Vector3(
			Mathf.Sin(yaw) * Mathf.Cos(pitch),
			-Mathf.Sin(pitch),
			Mathf.Cos(yaw) * Mathf.Cos(pitch)
		) * distance;

		GlobalPosition = center + offset;
		LookAt(center, Vector3.Up);
	}

	private static Aabb ComputeAabb(Node root)
	{
		bool hasBounds = false;
		Aabb bounds = new Aabb();

		foreach (MeshInstance3D meshInstance in CollectMeshes(root))
		{
			if (meshInstance.Mesh == null)
			{
				continue;
			}

			Aabb localAabb = meshInstance.Mesh.GetAabb();
			Aabb globalAabb = meshInstance.GlobalTransform * localAabb;
			if (!hasBounds)
			{
				bounds = globalAabb;
				hasBounds = true;
			}
			else
			{
				bounds = bounds.Merge(globalAabb);
			}
		}

		return hasBounds ? bounds : new Aabb();
	}

	private static Array<MeshInstance3D> CollectMeshes(Node root)
	{
		var result = new Array<MeshInstance3D>();
		CollectMeshesRecursive(root, result);
		return result;
	}

	private static void CollectMeshesRecursive(Node node, Array<MeshInstance3D> result)
	{
		if (node is MeshInstance3D meshInstance)
		{
			result.Add(meshInstance);
		}

		foreach (Node child in node.GetChildren())
		{
			CollectMeshesRecursive(child, result);
		}
	}
}
