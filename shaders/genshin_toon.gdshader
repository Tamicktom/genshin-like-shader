shader_type spatial;

render_mode cull_disabled, depth_draw_opaque, diffuse_lambert, specular_disabled, ambient_light_disabled;

group_uniforms Albedo;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform bool use_albedo_texture = true;
uniform float texture_lod_bias : hint_range(-4.0, 4.0) = -0.75;
uniform float alpha_scissor_threshold : hint_range(0.0, 1.0) = 0.5;
uniform bool use_alpha_scissor = false;
// Hair/dress cards need both faces. Body, hands, feet, and the sword do not —
// drawing backfaces on thin meshes reads as see-through.
uniform bool double_sided = false;

group_uniforms CelShading;
uniform vec4 shadow_color : source_color = vec4(0.62, 0.55, 0.78, 1.0);
uniform float shadow_threshold : hint_range(0.0, 1.0) = 0.5;
uniform float shadow_smoothness : hint_range(0.001, 0.35) = 0.04;
// How cast shadows (shadow map) blend into the same cel tint instead of going black.
uniform float cast_shadow_softness : hint_range(0.001, 0.8) = 0.22;
uniform float light_intensity : hint_range(0.0, 2.0) = 0.75;
uniform float ambient_strength : hint_range(0.0, 1.0) = 0.22;
uniform vec4 ambient_color : source_color = vec4(0.78, 0.8, 0.9, 1.0);

group_uniforms Specular;
uniform vec4 specular_color : source_color = vec4(1.0, 0.96, 0.92, 1.0);
uniform float specular_size : hint_range(1.0, 256.0) = 48.0;
uniform float specular_smoothness : hint_range(0.001, 0.5) = 0.08;
uniform float specular_strength : hint_range(0.0, 1.0) = 0.18;
// Hair: elongated Kajiya-Kay bands instead of isotropic plastic blobs.
uniform bool use_anisotropic_specular = false;
// 0 = mesh TANGENT (U), 1 = BINORMAL (V / typical strand flow).
uniform float hair_flow_blend : hint_range(0.0, 1.0) = 1.0;
uniform float hair_spec_shift : hint_range(-1.0, 1.0) = 0.08;
uniform float hair_spec_secondary : hint_range(0.0, 1.0) = 0.4;
uniform float hair_spec_secondary_shift : hint_range(-1.0, 1.0) = -0.12;
uniform float hair_spec_secondary_size : hint_range(1.0, 256.0) = 24.0;

group_uniforms Rim;
uniform vec4 rim_color : source_color = vec4(0.7, 0.78, 1.0, 1.0);
uniform float rim_power : hint_range(0.5, 8.0) = 4.0;
uniform float rim_strength : hint_range(0.0, 1.0) = 0.18;

// Passed fragment → light (TANGENT/BINORMAL are not available inside light()).
varying vec3 v_tangent;
varying vec3 v_binormal;

void fragment() {
	v_tangent = TANGENT;
	v_binormal = BINORMAL;

	if (!double_sided && !FRONT_FACING) {
		discard;
	}

	vec4 tex = use_albedo_texture ? texture(albedo_texture, UV, texture_lod_bias) : vec4(1.0);
	vec4 base = albedo_color * tex;

	// Use discard instead of writing ALPHA. Any ALPHA write marks the whole
	// shader as transparent in Godot and breaks depth sorting between meshes.
	if (use_alpha_scissor && base.a < alpha_scissor_threshold) {
		discard;
	}

	ALBEDO = base.rgb;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;

	// Gentle flat fill — keep this low so lit areas are not washed out.
	EMISSION = ALBEDO * ambient_color.rgb * ambient_strength;
}

void light() {
	float n_dot_l = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float half_lambert = n_dot_l * 0.5 + 0.5;

	float soft = max(shadow_smoothness, 0.001);
	float ndl_shade = smoothstep(shadow_threshold - soft, shadow_threshold + soft, half_lambert);

	// Genshin-style cast shadows: remap shadow-map attenuation into the same
	// cel band. Multiplying DIFFUSE by ATTENUATION would crush shadowed areas
	// to black/ambient instead of the cool shadow tint.
	// Widen the remap with fwidth so the silhouette tracks screen pixels
	// instead of crawling across shadow-map texels as the caster rotates.
	float cast_soft = max(cast_shadow_softness, 0.001);
	float cast_aa = max(cast_soft, fwidth(ATTENUATION) * 1.5);
	float cast_shade = smoothstep(0.0, cast_aa, ATTENUATION);
	float shade = min(ndl_shade, cast_shade);

	vec3 lit_term = mix(shadow_color.rgb, vec3(1.0), shade);
	DIFFUSE_LIGHT += lit_term * LIGHT_COLOR * light_intensity;

	vec3 half_dir = normalize(VIEW + LIGHT);
	float spec = 0.0;

	if (use_anisotropic_specular) {
		// Strand direction: prefer BINORMAL (UV.v flow on most hair cards).
		vec3 strand = normalize(mix(v_tangent, v_binormal, hair_flow_blend));

		vec3 t1 = normalize(strand + NORMAL * hair_spec_shift);
		float t1h = clamp(dot(t1, half_dir), -1.0, 1.0);
		float sin1 = sqrt(max(1.0 - t1h * t1h, 0.0));
		float primary = pow(sin1, specular_size);

		vec3 t2 = normalize(strand + NORMAL * hair_spec_secondary_shift);
		float t2h = clamp(dot(t2, half_dir), -1.0, 1.0);
		float sin2 = sqrt(max(1.0 - t2h * t2h, 0.0));
		float secondary = pow(sin2, hair_spec_secondary_size) * hair_spec_secondary;

		// Soft anime bands — avoid hardstep which reintroduces plastic edges.
		float spec_soft = max(specular_smoothness, 0.001);
		float band = smoothstep(0.35 - spec_soft, 0.35 + spec_soft, primary + secondary);
		spec = band * shade;
	} else {
		float spec_raw = pow(clamp(dot(NORMAL, half_dir), 0.0, 1.0), specular_size);
		spec = smoothstep(0.5 - specular_smoothness, 0.5 + specular_smoothness, spec_raw) * shade;
	}

	SPECULAR_LIGHT += specular_color.rgb * spec * specular_strength * LIGHT_COLOR;

	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), rim_power);
	SPECULAR_LIGHT += rim_color.rgb * fresnel * rim_strength * shade * LIGHT_COLOR;
}
