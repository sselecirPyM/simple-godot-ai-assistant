@tool
class_name OutputPatchRules
extends RefCounted

static func apply_shader_patches(content: String) -> String:
	if "hint_color" in content:
		content = content.replace("hint_color", "source_color")
	return content

static func apply_script_patches(code: String) -> String:
	if not code.contains("@tool"):
		code = "@tool\n" + code
	return code
