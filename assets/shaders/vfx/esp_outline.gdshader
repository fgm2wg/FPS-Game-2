shader_type spatial;
render_mode unshaded, depth_test_disabled, cull_front;

uniform vec4 outline_color : source_color = vec4(1.0, 0.88, 0.12, 0.95);
uniform float outline_width : hint_range(0.005, 0.1) = 0.035;

void vertex() {
	VERTEX += NORMAL * outline_width;
}

void fragment() {
	ALBEDO = outline_color.rgb;
	ALPHA = outline_color.a;
}
