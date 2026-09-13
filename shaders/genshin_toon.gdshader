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
// Lit-band tint. White = today's albedo × light look; warm/cool tints both bands.
uniform vec4 lit_color : source_color = vec4(1.0);
uniform float shadow_threshold : hint_range(0.0, 1.0) = 0.5;
uniform float shadow_smoothness : hint_range(0.001, 0.35) = 0.04;
// 0 = raw NdotL, 0.5 = Half-Lambert (current default).
uniform float light_wrap : hint_range(0.0, 1.0) = 0.5;
// false = two-sided step around threshold (old look); true = one-sided Genshin-style step.
uniform bool use_one_sided_step = false;
// How cast shadows (shadow map) blend into the same cel tint instead of going black.
uniform float cast_shadow_softness : hint_range(0.001, 0.8) = 0.22;
uniform float light_intensity : hint_range(0.0, 2.0) = 0.75;
uniform float ambient_strength : hint_range(0.0, 1.0) = 0.22;
uniform vec4 ambient_color : source_color = vec4(0.78, 0.8, 0.9, 1.0);

group_uniforms OuterShadow;
// Thin second terminator band inside the main shadow. Strength 0 = off (current look).
uniform vec4 outer_shadow_color : source_color = vec4(0.5, 0.35, 0.58, 1.0);
uniform float outer_shadow_offset : hint_range(0.0, 0.35) = 0.06;
uniform float outer_shadow_smoothness : hint_range(0.001, 0.2) = 0.02;
uniform float outer_shadow_strength : hint_range(0.0, 1.0) = 0.0;

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
// Half-vector 1D ramp (Genshin metal). Off = Phong / Kajiya-Kay path.
uniform bool use_metallic_gradient = false;
uniform sampler2D metallic_gradient_tex : source_color, filter_linear, repeat_disable;
uniform float metallic_strength : hint_range(0.0, 2.0) = 1.0;

group_uniforms Rim;
uniform vec4 rim_color : source_color = vec4(0.7, 0.78, 1.0, 1.0);
uniform float rim_power : hint_range(0.5, 8.0) = 4.0;
uniform float rim_strength : hint_range(0.0, 1.0) = 0.18;

group_uniforms ExtraMaps;
// R/G face lightmap (data, not sRGB). Off = mesh NdotL.
uniform sampler2D face_shadow_tex : filter_linear, repeat_disable;
uniform bool use_face_shadow = false;
uniform vec3 head_forward = vec3(0.0, 0.0, 1.0);
uniform vec3 head_right = vec3(1.0, 0.0, 0.0);
// Packed control / detail normal — bound but unused until later phases.
uniform sampler2D control_tex : filter_linear_mipmap, repeat_disable;
uniform bool use_control_tex = false;
uniform sampler2D detail_normal_tex : hint_normal, filter_linear_mipmap, repeat_disable;
uniform bool use_detail_normal = false;

// Passed fragment → light (TANGENT/BINORMAL/UV are not available inside light()).
varying vec3 v_tangent;
varying vec3 v_binormal;
varying vec2 v_uv;

void fragment() {
	v_tangent = TANGENT;
	v_binormal = BINORMAL;
	v_uv = UV;

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
	float soft = max(shadow_smoothness, 0.001);
	float wrap_ndl;
	float ndl_shade;

	if (use_face_shadow) {
		// Genshin face lightmap: head XZ vs light XZ → sample R (0–180°) or G (180–360°).
		// LIGHT is view-space; convert to world for dots with script head axes.
		vec3 light_world = normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz);
		vec2 light_xz = normalize(light_world.xz);
		vec2 forward_xz = normalize(head_forward.xz);
		vec2 right_xz = normalize(head_right.xz);

		float dot_f = dot(forward_xz, light_xz);
		float dot_r = clamp(dot(right_xz, light_xz), -1.0, 1.0);
		float front = smoothstep(-soft, soft, dot_f);

		// acos/PI*2 ∈ [0,2]. Remap so front ≈ 0 and side ≈ 1: lit when map > angle.
		float dot_r_acos = (acos(dot_r) / PI) * 2.0;
		float dot_r_acos_dir = (dot_r < 0.0) ? (dot_r_acos - 1.0) : (1.0 - dot_r_acos);

		vec2 map_rg = texture(face_shadow_tex, v_uv).rg;
		float tex_shadow_dir = (dot_r < 0.0) ? map_rg.g : map_rg.r;

		// Soft comparison against the painted SDF instead of a hard step.
		ndl_shade = smoothstep(-soft, soft, tex_shadow_dir - dot_r_acos_dir) * front;
		// Outer band uses the map value as its "wrap" source so strength 0 still no-ops.
		wrap_ndl = tex_shadow_dir;
	} else {
		float n_dot_l = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
		// light_wrap 0.5 == mix(n, 1, 0.5) == Half-Lambert (bit-identical to old path).
		wrap_ndl = mix(n_dot_l, 1.0, light_wrap);

		ndl_shade = use_one_sided_step
			? smoothstep(0.0, soft, wrap_ndl - shadow_threshold)
			: smoothstep(shadow_threshold - soft, shadow_threshold + soft, wrap_ndl);
	}

	// Genshin-style cast shadows: remap shadow-map attenuation into the same
	// cel band. Multiplying DIFFUSE by ATTENUATION would crush shadowed areas
	// to black/ambient instead of the cool shadow tint.
	// Widen the remap with fwidth so the silhouette tracks screen pixels
	// instead of crawling across shadow-map texels as the caster rotates.
	float cast_soft = max(cast_shadow_softness, 0.001);
	float cast_aa = max(cast_soft, fwidth(ATTENUATION) * 1.5);
	float cast_shade = smoothstep(0.0, cast_aa, ATTENUATION);
	float shade = min(ndl_shade, cast_shade);

	// Second NdotL cut inset into the shadow: a narrow tinted stripe at the
	// terminator. Strength 0 leaves main_term alone (today's single band).
	// Outer band stays two-sided around the offset threshold (not one-sided).
	float outer_soft = max(outer_shadow_smoothness, 0.001);
	float outer_threshold = shadow_threshold - outer_shadow_offset;
	float shade_outer = smoothstep(outer_threshold - outer_soft, outer_threshold + outer_soft, wrap_ndl);
	shade_outer = min(shade_outer, cast_shade);

	vec3 main_term = mix(shadow_color.rgb, lit_color.rgb, shade);
	float outer_band = (1.0 - shade) * shade_outer;
	vec3 lit_term = mix(main_term, outer_shadow_color.rgb, outer_band * outer_shadow_strength);
	DIFFUSE_LIGHT += lit_term * LIGHT_COLOR * light_intensity;

	vec3 half_dir = normalize(VIEW + LIGHT);

	if (use_metallic_gradient) {
		// 1D matcap-style band: dark → gold → white core along N·H.
		float u = clamp(dot(NORMAL, half_dir), 0.0, 1.0);
		vec3 grad = texture(metallic_gradient_tex, vec2(u, 0.5)).rgb;
		SPECULAR_LIGHT += grad * metallic_strength * shade * LIGHT_COLOR;
	} else {
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
	}

	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), rim_power);
	SPECULAR_LIGHT += rim_color.rgb * fresnel * rim_strength * shade * LIGHT_COLOR;
}
