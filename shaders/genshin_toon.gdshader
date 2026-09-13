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
// Terminator in the wrapped N·L domain. With light_wrap 0.5 that domain is
// Half-Lambert [0, 1] where threshold = (signed_terminator + 1) / 2.
uniform float shadow_threshold : hint_range(0.0, 1.0) = 0.5;
uniform float shadow_smoothness : hint_range(0.001, 0.35) = 0.04;
// Remap of signed N·L: mix(raw, 1, wrap). 0.5 = Half-Lambert.
uniform float light_wrap : hint_range(0.0, 1.0) = 0.5;
// false = two-sided step around threshold (old look); true = one-sided Genshin-style step.
uniform bool use_one_sided_step = false;
// How cast shadows (shadow map) blend into the same cel tint instead of going black.
uniform float cast_shadow_softness : hint_range(0.001, 0.8) = 0.22;
uniform float light_intensity : hint_range(0.0, 2.0) = 0.75;
uniform float ambient_strength : hint_range(0.0, 1.0) = 0.22;
uniform vec4 ambient_color : source_color = vec4(0.78, 0.8, 0.9, 1.0);
// 0 = additive ambient emission (legacy). 1 = subtract ambient from direct so
// a single key light approximates albedo * max(indirect, direct).
uniform float ambient_max_blend : hint_range(0.0, 1.0) = 1.0;

group_uniforms OuterShadow;
// Thin second terminator band inside the main shadow. Strength 0 = off (current look).
uniform vec4 outer_shadow_color : source_color = vec4(0.5, 0.35, 0.58, 1.0);
uniform float outer_shadow_offset : hint_range(0.0, 0.35) = 0.06;
uniform float outer_shadow_smoothness : hint_range(0.001, 0.2) = 0.02;
uniform float outer_shadow_strength : hint_range(0.0, 1.0) = 0.0;

group_uniforms Dither;
// Ordered Bayer on shade only, gated to the terminator. 0 = today's look.
uniform float dither_strength : hint_range(0.0, 0.08) = 0.0;
// Integer-ish pixel scale of the Bayer matrix (1 = one matrix cell per pixel).
uniform float dither_scale : hint_range(0.25, 4.0) = 1.0;

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
// Single-channel face lightmap in R (data, not sRGB). Sampled with mirrored UV
// when the light is on the left. Off = mesh NdotL.
uniform sampler2D face_shadow_tex : filter_linear, repeat_disable;
uniform bool use_face_shadow = false;
// Horizontal UV symmetry axis of the face island (0.5 = centered).
uniform float face_mirror_axis : hint_range(0.0, 1.0) = 0.5;
// Softness for face map comparison. Independent of body shadow_smoothness.
uniform float face_shadow_softness : hint_range(0.001, 0.35) = 0.04;
// Extra yaw (radians) applied to light XZ before face dots.
uniform float face_yaw_offset = 0.0;
// +1 / -1. Negate to flip head forward for the angular threshold.
uniform float face_forward_sign = 1.0;
// +1 / -1. Negate to swap left/right mirrored-UV selection.
uniform float face_side_sign = 1.0;
uniform vec3 head_forward = vec3(0.0, 0.0, 1.0);
uniform vec3 head_right = vec3(1.0, 0.0, 0.0);
// Bound for look compatibility. Do not assign DEPTH from this shader: a
// fragment depth write disables MSAA coverage and stipples the whole mesh.
uniform float outline_depth_flatten : hint_range(0.0, 1.0) = 0.0;
// Packed control / detail normal — bound but unused until later phases.
uniform sampler2D control_tex : filter_linear_mipmap, repeat_disable;
uniform bool use_control_tex = false;
// Greyscale hair streak mask (data, not sRGB). Off = Kajiya-Kay / Phong.
uniform sampler2D hair_highlight_tex : filter_linear, repeat_disable;
uniform bool use_hair_highlight = false;
// 0 = keep anisotropic/Phong, 1 = replace with mask * shade * (1 - fresnel).
uniform float hair_highlight_blend : hint_range(0.0, 1.0) = 1.0;
uniform float hair_highlight_fresnel : hint_range(0.5, 8.0) = 5.0;
uniform sampler2D detail_normal_tex : hint_normal, filter_linear_mipmap, repeat_disable;
uniform bool use_detail_normal = false;

group_uniforms Debug;
// 0 = production. Non-zero replaces lighting with a raw diagnostic signal:
// 1 signed N·L (as 0.5*raw+0.5), 2 wrapped N·L, 3 shade, 4 cast_shade,
// 5 face map value, 6 face angular threshold, 7 slot ID palette.
uniform int debug_view : hint_range(0, 7) = 0;
// Slot index uploaded per material so mismatched presets are visible.
uniform float debug_slot_id : hint_range(0.0, 8.0) = 0.0;

// Passed fragment → light (TANGENT/BINORMAL/UV/FRAGCOORD are not available inside light()).
varying vec3 v_tangent;
varying vec3 v_binormal;
varying vec2 v_uv;
varying vec2 v_fragcoord;

// 4x4 Bayer matrix, values normalized to [0, 1).
float bayer4(vec2 frag_xy) {
	ivec2 p = ivec2(mod(floor(frag_xy), 4.0));
	int index = p.y * 4 + p.x;
	// Standard ordered dither thresholds / 16.
	const float[16] bayer = float[16](
		0.0 / 16.0, 8.0 / 16.0, 2.0 / 16.0, 10.0 / 16.0,
		12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0, 6.0 / 16.0,
		3.0 / 16.0, 11.0 / 16.0, 1.0 / 16.0, 9.0 / 16.0,
		15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0, 5.0 / 16.0
	);
	return bayer[index];
}

void fragment() {
	v_tangent = TANGENT;
	v_binormal = BINORMAL;
	v_uv = UV;
	v_fragcoord = FRAGCOORD.xy;

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
	// Combined with ambient_max_blend in light() to approximate max(indirect, direct).
	EMISSION = ALBEDO * ambient_color.rgb * ambient_strength;

	// Debug views need a clean signal: albedo multiplies DIFFUSE_LIGHT and
	// ambient emission would wash grayscale diagnostics.
	if (debug_view != 0) {
		ALBEDO = vec3(1.0);
		EMISSION = vec3(0.0);
	}

	// Do not write DEPTH in this shader. A conditional DEPTH assignment still
	// disables early-Z / MSAA coverage and stipples every mesh that shares
	// genshin_toon (ground, cloth, hair). Face inner-ink is suppressed by the
	// compositor's relative depth threshold instead.
}

vec3 slot_debug_color(float slot_id) {
	int id = int(slot_id + 0.5);
	if (id == 0) {
		return vec3(1.0, 0.2, 0.2);
	}
	if (id == 1) {
		return vec3(1.0, 0.7, 0.15);
	}
	if (id == 2) {
		return vec3(0.95, 0.95, 0.2);
	}
	if (id == 3) {
		return vec3(0.25, 0.85, 0.3);
	}
	if (id == 4) {
		return vec3(0.25, 0.7, 1.0);
	}
	if (id == 5) {
		return vec3(0.65, 0.35, 1.0);
	}
	if (id == 6) {
		return vec3(1.0, 0.4, 0.85);
	}
	return vec3(0.55, 0.55, 0.55);
}

void light() {
	float soft = max(shadow_smoothness, 0.001);
	float face_soft = max(face_shadow_softness, 0.001);
	float wrap_ndl;
	float ndl_shade;
	float signed_ndl = dot(NORMAL, LIGHT);
	float face_map_value = 0.0;
	float face_angle = 0.0;

	if (use_face_shadow) {
		// URP-compatible face lightmap: one directional gradient in R, mirrored
		// by light side. LIGHT is view-space; convert to world for head axes.
		vec3 light_world = normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz);
		vec2 light_xz = normalize(light_world.xz);
		// Rotate light around Y by face_yaw_offset (radians).
		float cy = cos(face_yaw_offset);
		float sy = sin(face_yaw_offset);
		light_xz = vec2(cy * light_xz.x - sy * light_xz.y, sy * light_xz.x + cy * light_xz.y);

		vec2 forward_xz = normalize(head_forward.xz);
		vec2 right_xz = normalize(head_right.xz);

		float dot_f = face_forward_sign * dot(forward_xz, light_xz);
		float dot_r = clamp(face_side_sign * dot(right_xz, light_xz), -1.0, 1.0);
		float front = smoothstep(-face_soft, face_soft, dot_f);

		// acos/PI*2 ∈ [0,2]. Remap so front ≈ 0 and side ≈ 1: lit when map > angle.
		float dot_r_acos = (acos(dot_r) / PI) * 2.0;
		face_angle = (dot_r < 0.0) ? (dot_r_acos - 1.0) : (1.0 - dot_r_acos);

		vec2 face_uv = vec2(2.0 * face_mirror_axis - v_uv.x, v_uv.y);
		vec2 sample_uv = (dot_r < 0.0) ? face_uv : v_uv;
		face_map_value = texture(face_shadow_tex, sample_uv).r;

		// Soft comparison against the painted SDF instead of a hard step.
		ndl_shade = smoothstep(-face_soft, face_soft, face_map_value - face_angle) * front;
		// Outer band uses the map value as its "wrap" source so strength 0 still no-ops.
		wrap_ndl = face_map_value;
	} else {
		// Signed [-1, 1]. Clamping here pinned every back-facing pixel to light_wrap,
		// which equals the threshold on hair and cloth and froze them at half shade.
		wrap_ndl = mix(signed_ndl, 1.0, light_wrap);

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

	// Ordered dither only across the terminator (peaks at shade = 0.5).
	// Strength 0 is a no-op so old presets stay identical.
	float dither_band = 4.0 * shade * (1.0 - shade);
	float dither = (bayer4(v_fragcoord * dither_scale) - 0.5) * dither_strength * dither_band;
	shade = clamp(shade + dither, 0.0, 1.0);

	if (debug_view != 0) {
		vec3 signal = vec3(0.0);
		if (debug_view == 1) {
			signal = vec3(signed_ndl * 0.5 + 0.5);
		} else if (debug_view == 2) {
			signal = vec3(wrap_ndl);
		} else if (debug_view == 3) {
			signal = vec3(shade);
		} else if (debug_view == 4) {
			signal = vec3(cast_shade);
		} else if (debug_view == 5) {
			signal = vec3(face_map_value);
		} else if (debug_view == 6) {
			signal = vec3(face_angle);
		} else if (debug_view == 7) {
			signal = slot_debug_color(debug_slot_id);
		}
		DIFFUSE_LIGHT = signal;
		SPECULAR_LIGHT = vec3(0.0);
	} else {
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
		vec3 direct = lit_term * LIGHT_COLOR * light_intensity;
		// Approximate albedo * max(indirect, direct) under Godot's additive light():
		// emission already carries ambient; subtract it from direct when blend = 1.
		vec3 ambient_term = ambient_color.rgb * ambient_strength;
		DIFFUSE_LIGHT += max(direct - ambient_term * ambient_max_blend, vec3(0.0));

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

			// Painted streak replaces (or mixes with) Kajiya-Kay when a mask is bound.
			if (use_hair_highlight) {
				float spec_mask = texture(hair_highlight_tex, v_uv).r;
				float hl_fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), hair_highlight_fresnel);
				float mask_spec = spec_mask * shade * (1.0 - hl_fresnel);
				spec = mix(spec, mask_spec, hair_highlight_blend);
			}

			SPECULAR_LIGHT += specular_color.rgb * spec * specular_strength * LIGHT_COLOR;
		}

		float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), rim_power);
		// Additive rim fights hair-mask Fresnel suppress — gate it when the mask drives spec.
		float rim_gate = 1.0 - (use_hair_highlight ? hair_highlight_blend : 0.0);
		SPECULAR_LIGHT += rim_color.rgb * fresnel * rim_strength * shade * LIGHT_COLOR * rim_gate;
	}
}
