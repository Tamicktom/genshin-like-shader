shader_type spatial;

render_mode unshaded, cull_front, depth_draw_opaque, shadows_disabled, ambient_light_disabled;

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform bool use_albedo_texture = true;
uniform float texture_lod_bias : hint_range(-4.0, 4.0) = -0.75;
uniform vec4 outline_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float outline_darken : hint_range(0.0, 1.0) = 0.28;
uniform float outline_saturation : hint_range(0.0, 2.0) = 1.15;
uniform float outline_width : hint_range(0.0, 12.0) = 1.25;
uniform float outline_z_offset : hint_range(-1.0, 1.0) = -0.001;

void vertex() {
	vec4 clip_pos = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec4 clip_normal = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(NORMAL, 0.0);

	vec2 offset = normalize(clip_normal.xy + vec2(1e-5)) / VIEWPORT_SIZE * outline_width * clip_pos.w * 2.0;
	clip_pos.xy += offset;
	clip_pos.z += outline_z_offset * clip_pos.w;
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
