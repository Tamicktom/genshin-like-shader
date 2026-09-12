//* Libraries imports
using Godot;

//* Local imports
//* ...

/// <summary>
/// Slow Y-axis turntable for lookdev demos. Rotates the parent Node3D.
/// </summary>
public partial class SpinY : Node
{
	[Export]
	public float RadiansPerSecond { get; set; } = 0.2f;

	public override void _Process(double delta)
	{
		if (GetParent() is not Node3D parent3D)
		{
			return;
		}

		parent3D.RotateY((float)delta * RadiansPerSecond);
	}
}
