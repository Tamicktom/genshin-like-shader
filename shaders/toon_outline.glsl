#version 450

// Toon outline + edge highlight from reverse-Z linear depth Sobel.
// Color is read/written as storage image; depth is sampled (no normal_roughness —
// NeedsNormalRoughness blacks out this project's custom light() toon mats).
// Optional character mask (set 2) gates dark outline and far-side highlight.
// Push constants stay <= 128 bytes (Godot RD limit).

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(set = 1, binding = 0) uniform sampler2D depth_sampler;
layout(set = 2, binding = 0) uniform sampler2D mask_sampler;

layout(push_constant, std430) uniform Params {
	vec2 raster_size;
	float thickness;
	float depth_threshold; // relative linear-depth Sobel threshold

	float environment_outline_strength; // outside character mask
	float outline_strength;
	float highlight_strength;
	float highlight_y_offset;

	vec4 outline_color;
	vec4 highlight_color;

	float z_near;
	float debug_mode; // 0 Final, 1 RawDepth, 3 DepthEdges, 4 Combined, 5 CharacterMask
	float use_character_mask; // 0/1
	float _pad1;
} params;

ivec2 clamp_coord(ivec2 c) {
	return clamp(c, ivec2(0), ivec2(params.raster_size) - ivec2(1));
}

float raw_depth(ivec2 c) {
	return texelFetch(depth_sampler, clamp_coord(c), 0).r;
}

// Reverse-Z, typically infinite far: view Z = z_near / raw (near≈1, far≈0).
// Clamp sky (raw≈0) so Sobel does not explode.
float linear_depth(ivec2 c) {
	float d = raw_depth(c);
	float max_z = 80.0;
	return params.z_near / max(d, params.z_near / max_z);
}

float sobel_depth(ivec2 coord) {
	int t = max(int(round(params.thickness)), 1);
	float nw = linear_depth(coord + ivec2(-t, -t));
	float n = linear_depth(coord + ivec2(0, -t));
	float ne = linear_depth(coord + ivec2(t, -t));
	float w = linear_depth(coord + ivec2(-t, 0));
	float e = linear_depth(coord + ivec2(t, 0));
	float sw = linear_depth(coord + ivec2(-t, t));
	float s = linear_depth(coord + ivec2(0, t));
	float se = linear_depth(coord + ivec2(t, t));

	float gx = -nw + ne - 2.0 * w + 2.0 * e - sw + se;
	float gy = -nw - 2.0 * n - ne + sw + 2.0 * s + se;
	float edge = sqrt(gx * gx + gy * gy);
	float center = linear_depth(coord);
	float rel = edge / max(center, 0.05);
	float thresh = max(params.depth_threshold, 0.001);
	return smoothstep(thresh, thresh * 2.5, rel);
}

// Far side of a 1px depth jump (larger linear Z = farther from camera).
// Independent of outline Thickness so the white rim stays a hairline.
float highlight_mask(ivec2 coord) {
	float c = linear_depth(coord);
	float n = linear_depth(coord + ivec2(0, -1));
	float s = linear_depth(coord + ivec2(0, 1));
	float e = linear_depth(coord + ivec2(1, 0));
	float w = linear_depth(coord + ivec2(-1, 0));
	float mx = max(max(n, s), max(e, w));
	float mn = min(min(n, s), min(e, w));
	float jump = (mx - mn) / max(c, 0.05);
	float thresh = max(params.depth_threshold, 0.001) * 1.7;
	float edge = smoothstep(thresh, thresh * 1.6, jump);
	edge *= edge;
	float farther = step((mx + mn) * 0.5, c);
	return edge * farther;
}

// Sample character coverage; max over a small neighborhood so hull/Sobel
// dilation still sees the silhouette when the mask is half-resolution.
float character_mask(ivec2 coord) {
	if (params.use_character_mask < 0.5) {
		return 1.0;
	}
	vec2 uv = (vec2(coord) + 0.5) / params.raster_size;
	float m = 0.0;
	vec2 px = 1.0 / params.raster_size;
	m = max(m, texture(mask_sampler, uv).a);
	m = max(m, texture(mask_sampler, uv + vec2(px.x, 0.0)).a);
	m = max(m, texture(mask_sampler, uv - vec2(px.x, 0.0)).a);
	m = max(m, texture(mask_sampler, uv + vec2(0.0, px.y)).a);
	m = max(m, texture(mask_sampler, uv - vec2(0.0, px.y)).a);
	// Opaque character pixels may write alpha=1 or opaque RGB with alpha=1;
	// also accept luma when alpha is unused.
	float rgb = max(texture(mask_sampler, uv).r, max(texture(mask_sampler, uv).g, texture(mask_sampler, uv).b));
	return max(m, rgb);
}

void main() {
	ivec2 uv = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = ivec2(params.raster_size);
	if (uv.x >= size.x || uv.y >= size.y) {
		return;
	}

	float mask = character_mask(uv);
	float edge = sobel_depth(uv);
	float gated = edge * mix(params.environment_outline_strength, 1.0, mask);
	int mode = int(params.debug_mode + 0.5);

	if (mode == 1) {
		float raw = raw_depth(uv);
		float lin = linear_depth(uv);
		float vis = 1.0 - clamp(lin / 12.0, 0.0, 1.0);
		imageStore(color_image, uv, vec4(vis, vis, raw, 1.0));
		return;
	}
	if (mode == 3 || mode == 4) {
		imageStore(color_image, uv, vec4(vec3(gated), 1.0));
		return;
	}
	if (mode == 5) {
		imageStore(color_image, uv, vec4(vec3(mask), 1.0));
		return;
	}

	vec4 color = imageLoad(color_image, uv);
	color.rgb *= mix(vec3(1.0), params.outline_color.rgb, clamp(gated * params.outline_strength, 0.0, 1.0));

	if (params.highlight_strength > 0.001) {
		int y_off = int(round(params.highlight_y_offset));
		float hi = highlight_mask(uv + ivec2(0, y_off)) * params.highlight_strength * mask;
		color.rgb += params.highlight_color.rgb * clamp(hi, 0.0, 1.0);
	}

	imageStore(color_image, uv, color);
}
