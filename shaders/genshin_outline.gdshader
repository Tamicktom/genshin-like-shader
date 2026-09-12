shader_type spatial;

render_mode unshaded, cull_front, depth_draw_never, shadows_disabled, ambient_light_disabled;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform bool use_albedo_texture = true;
uniform float texture_lod_bias : hint_range(-4.0, 4.0) = -0.75;
uniform vec4 outline_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float outline_darken : hint_range(0.0, 1.0) = 0.28;
uniform float outline_saturation : hint_range(0.0, 2.0) = 1.15;
uniform float outline_width : hint_range(0.0, 12.0) = 1.25;
// Pulls the hull toward the camera so thick limbs keep their rim in front of
// clothes sitting just behind them (skirt behind thighs, ground under feet).
uniform float outline_depth_bias : hint_range(0.0, 0.4) = 0.015;

void vertex() {
	vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec3 view_normal = normalize(MODELVIEW_NORMAL_MATRIX * NORMAL);

	vec3 toward_camera = normalize(-view_pos.xyz);
	view_pos.xyz += toward_camera * outline_depth_bias;

	vec4 clip_pos = PROJECTION_MATRIX * view_pos;
	vec3 view_normal_flat = view_normal;
	// Kill the view-Z component so pointed vertices (fingertips, nose) do not
	// extrude along their tangent and grow claw/nail spikes.
	view_normal_flat.z = 0.0;
	vec4 clip_normal = PROJECTION_MATRIX * vec4(normalize(view_normal_flat + vec3(1e-5, 0.0, 0.0)), 0.0);

	// Expand in clip XY only. Extruding along the 3D normal grows fingertips
	// into fake nails and turns the nose/chin into spikes.
	vec2 offset = normalize(clip_normal.xy + vec2(1e-5));
	offset *= outline_width * clip_pos.w * 2.0 / VIEWPORT_SIZE;
	clip_pos.xy += offset;

	POSITION = clip_pos;
}

vec3 adjust_saturation(vec3 color, float amount) {
	float luma = dot(color, vec3(0.299, 0.587, 0.114));
	return mix(vec3(luma), color, amount);
}

void fragment() {
	vec4 tex = use_albedo_texture ? texture(albedo_texture, UV, texture_lod_bias) : vec4(1.0);
	vec3 base = albedo_color.rgb * tex.rgb;

	// Darkened, slightly boosted local texture color for anime-style ink lines.
	vec3 line_color = adjust_saturation(base, outline_saturation) * outline_darken;
	line_color *= outline_tint.rgb;

	ALBEDO = line_color;
}
