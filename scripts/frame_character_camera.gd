extends Camera3D

## Places the camera so the target character fills the view, including on resize.

@export var target_path: NodePath
@export var padding: float = 1.35
@export var height_bias: float = 0.15
@export var yaw_degrees: float = 28.0
@export var pitch_degrees: float = -8.0

func _ready() -> void:
	get_viewport().size_changed.connect(_on_viewport_size_changed)
	call_deferred("_frame_target")


func _exit_tree() -> void:
	var viewport := get_viewport()
	if viewport != null and viewport.size_changed.is_connected(_on_viewport_size_changed):
		viewport.size_changed.disconnect(_on_viewport_size_changed)


func _on_viewport_size_changed() -> void:
	_frame_target()


func _frame_target() -> void:
	var target := get_node_or_null(target_path)
	if target == null:
		return

	var bounds := _compute_aabb(target)
	if bounds.size == Vector3.ZERO:
		return

	var center := bounds.get_center() + Vector3(0.0, bounds.size.y * height_bias, 0.0)
	var radius := bounds.size.length() * 0.5 * padding

	# Account for viewport aspect so the character stays framed when resizing.
	var viewport_size := get_viewport().get_visible_rect().size
	var aspect := viewport_size.x / maxf(viewport_size.y, 1.0)
	var vertical_fov := deg_to_rad(fov)
	var horizontal_fov := 2.0 * atan(tan(vertical_fov * 0.5) * aspect)
	var limiting_fov := minf(vertical_fov, horizontal_fov)
	var distance := radius / maxf(tan(limiting_fov * 0.5), 0.001)

	var yaw := deg_to_rad(yaw_degrees)
	var pitch := deg_to_rad(pitch_degrees)
	var offset := Vector3(
		sin(yaw) * cos(pitch),
		-sin(pitch),
		cos(yaw) * cos(pitch)
	) * distance

	global_position = center + offset
	look_at(center, Vector3.UP)


func _compute_aabb(root: Node) -> AABB:
	var has_bounds := false
	var bounds := AABB()

	for node in _collect_meshes(root):
		var mesh_instance := node as MeshInstance3D
		if mesh_instance.mesh == null:
			continue
		var local_aabb := mesh_instance.mesh.get_aabb()
		var global_aabb := mesh_instance.global_transform * local_aabb
		if not has_bounds:
			bounds = global_aabb
			has_bounds = true
		else:
			bounds = bounds.merge(global_aabb)

	return bounds if has_bounds else AABB()


func _collect_meshes(root: Node) -> Array[MeshInstance3D]:
	var result: Array[MeshInstance3D] = []
	_collect_meshes_recursive(root, result)
	return result


func _collect_meshes_recursive(node: Node, result: Array[MeshInstance3D]) -> void:
	if node is MeshInstance3D:
		result.append(node as MeshInstance3D)
	for child in node.get_children():
		_collect_meshes_recursive(child, result)
