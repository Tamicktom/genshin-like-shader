extends Node

## Slow Y-axis turntable for lookdev demos. Rotates the parent Node3D.

@export var radians_per_second: float = 0.2


func _process(delta: float) -> void:
	var parent_3d := get_parent() as Node3D
	if parent_3d == null:
		return
	parent_3d.rotate_y(delta * radians_per_second)
