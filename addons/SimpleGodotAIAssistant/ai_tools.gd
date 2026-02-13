@tool
class_name AiTools
extends RefCounted

const IMAGE_EXTENSIONS = ["png", "jpg", "jpeg", "webp", "svg", "bmp", "tga"]

# --- Tool Definitions ---

static func get_tool_definitions() -> Array[Dictionary]:
	return [
		{
			"type": "function",
			"function": {
				"name": "list_directory",
				"description": "List files and folders in a specific directory within the project (res://).",
				"parameters": {
					"type": "object",
					"properties": {
						"path": { "type": "string", "description": "The path to list (e.g., 'res://scripts/')" }
					},
					"required": ["path"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "read_file",
				"description": "Read the content of a specific file.",
				"parameters": {
					"type": "object",
					"properties": {
						"path": { "type": "string", "description": "The full path of the file to read (e.g., 'res://main.gd')" }
					},
					"required": ["path"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "search_files",
				"description": "Search for files where the filename contains a specific name keyword.",
				"parameters": {
					"type": "object",
					"properties": {
						"keyword": { "type": "string", "description": "The partial filename to search for." }
					},
					"required": ["keyword"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "get_scene_tree",
				"description": "Get the tree structure of the currently open scene. Returns Name, Type, and Instance ID.",
				"parameters": {
					"type": "object",
					"properties": {
						"node_id": { "type": "string", "description": "The Instance ID of the node to inspect. Pass '0' to get the children of current scene root." }
					},
					"required": ["node_id"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "get_selected_nodes",
				"description": "Get the list of nodes currently selected in the Godot Editor. Returns Name, NodePath, Class, and ID.",
				"parameters": { "type": "object", "properties": { } }
			}
		},
		{
			"type": "function",
			"function": {
				"name": "get_object_properties", 
				"description": "Get the properties of a specific object (Node or Resource) by its Instance ID.",
				"parameters": {
					"type": "object",
					"properties": {
						"object_id": { "type": "string", "description": "The Instance ID of the object." }
					},
					"required": ["object_id"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "get_node_properties_by_path",
				"description": "Get the properties of a node by its scene path (relative to the edited scene root).",
				"parameters": {
					"type": "object",
					"properties": {
						"path": { "type": "string", "description": "The node path (e.g. 'Player/Camera3D' or '.' for root)." }
					},
					"required": ["path"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "get_node_property_value",
				"description": "Get a specific property value or sub-resource from a node using a path. " +
				"Useful for accessing nested resources like 'mesh/material/albedo_color' or 'surface_material_override/0'.",
				"parameters": {
					"type": "object",
					"properties": {
						"node_path": { "type": "string", "description": "Path to the scene node (e.g. 'Player/MeshInstance')." },
						"property_path": { "type": "string", "description": "Path to the property or sub-resource (e.g. 'mesh:material:albedo_color'). Slashes are automatically converted to colons where appropriate." }
					},
					"required": ["node_path", "property_path"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "create_file",
				"description": "Create or overwrite a text file (e.g., .gd, .tscn, .txt) at a specific path. If directory missing, it creates it. ",
				"parameters": {
					"type": "object",
					"properties": {
						"path": { "type": "string", "description": "The full path (e.g., 'res://scripts/my_script.gd')." },
						"content": { "type": "string", "description": "The text content to write into the file." }
					},
					"required": ["path", "content"]
				}
			}
		},
		{
			"type": "function",
			"function": {
				"name": "run_gdscript",
				"description": "Execute a temporary GDScript snippet immediately and return the result. " +
					"The script MUST contain a 'func run():' method which returns a value (String, Dictionary, or basic type). " +
					"The \"tool\" keyword was removed in Godot 4. Use the \"@tool\" annotation instead. " +
					"Cannot use get_tree(), use EditorInterface.get_edited_scene_root() instead.",
				"parameters": {
					"type": "object",
					"properties": {
						"code": { "type": "string", "description": "The full GDScript code. It must extend RefCounted and implement 'func run()'." }
					},
					"required": ["code"]
				}
			}
		}
	]

# --- File Operations ---

static func list_directory(path: String) -> String:
	var dir = DirAccess.open(path)
	if not dir:
		return "Error: Could not open directory %s. %s" % [path, DirAccess.get_open_error()]
	
	dir.list_dir_begin()
	var file_name = dir.get_next()
	var files: PackedStringArray = []
	
	while file_name != "":
		if dir.current_is_dir():
			files.append("[DIR] " + file_name)
		else:
			files.append("[FILE] " + file_name)
		file_name = dir.get_next()
	
	return "\n".join(files)

static func read_file(path: String) -> String:
	if not FileAccess.file_exists(path):
		return "Error: File not found."
	
	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return "Error: Could not open file."
		
	var length = file.get_length()
	var extension = path.get_extension().to_lower()
	var is_image = extension in IMAGE_EXTENSIONS
	
	if not is_image and length > 10240:
		return "Error: File is too large (%d bytes). Text file limit is 10KB." % length
		
	if is_image:
		var buffer = file.get_buffer(length)
		var base64 = Marshalls.raw_to_base64(buffer)
		var mime_type = "jpeg" if extension == "jpg" else ("svg+xml" if extension == "svg" else extension)
		return "data:image/%s;base64,%s" % [mime_type, base64]
	
	return file.get_as_text()

static func search_files(keyword: String) -> String:
	return _search_recursive("res://", keyword)

static func _search_recursive(dir_path: String, keyword: String) -> String:
	var dir = DirAccess.open(dir_path)
	if not dir: return ""
	
	dir.list_dir_begin()
	var file_name = dir.get_next()
	var results: PackedStringArray = []
	
	while file_name != "":
		if file_name == "." or file_name == "..":
			file_name = dir.get_next()
			continue
			
		var full_path = dir_path.path_join(file_name)
		if dir.current_is_dir():
			var sub_res = _search_recursive(full_path, keyword)
			if not sub_res.is_empty():
				results.append(sub_res)
		elif keyword.to_lower() in file_name.to_lower():
			results.append(full_path)
			
		file_name = dir.get_next()
		
	return "\n".join(results).strip_edges()

# --- Node Operations ---

static func _get_node_from_id(id_str: String) -> Node:
	var root = _get_edited_root()
	if not root: return null
	
	if id_str == "0": return root
	
	if id_str.is_valid_int():
		var obj = instance_from_id(id_str.to_int())
		return obj as Node
	return null

static func get_scene_tree(node_id_str: String) -> String:
	var node = _get_node_from_id(node_id_str)
	if not node: return "Error: Could not find node or no scene is open."
	
	var output = []
	_build_tree_recursive(node, output, 0)
	return "".join(output)

static func _build_tree_recursive(node: Node, output: Array, depth: int) -> void:
	var indent = "  ".repeat(depth)
	var line = "%s- %s (%s) [ID: %d]" % [indent, node.name, node.get_class(), node.get_instance_id()]
	
	if not node.scene_file_path.is_empty():
		line += " [Scene: %s]" % node.scene_file_path
		
	output.append(line + "\n")
	
	if depth >= 1: return
	
	for child in node.get_children():
		_build_tree_recursive(child, output, depth + 1)

static func _get_edited_root() -> Node:
	return EditorInterface.get_edited_scene_root()

static func get_selected_nodes() -> String:
	var selection = EditorInterface.get_selection()
	var nodes = selection.get_selected_nodes()
	
	if nodes.is_empty(): return "No nodes currently selected."
	
	var result_list = []
	var root = EditorInterface.get_edited_scene_root()
	
	for node in nodes:
		var path = str(root.get_path_to(node)) if root else str(node.get_path())
		result_list.append({
			"Name": node.name,
			"Class": node.get_class(),
			"Path": path,
			"InstanceId": str(node.get_instance_id()),
			"SceneFile": node.scene_file_path
		})
	
	return JSON.stringify(result_list, "  ")

static func get_node_properties_by_path(path: String) -> String:
	var root = _get_edited_root()
	if not root: return "Error: No scene currently open."
	
	var target_node: Node
	if path.is_empty() or path == ".":
		target_node = root
	else:
		target_node = root.get_node_or_null(path)
		
	if not target_node: return "Error: Could not find node at path '%s'." % path
	return _serialize_godot_object(target_node)

static func get_object_properties(object_id_str: String) -> String:
	if object_id_str.is_valid_int():
		var obj = instance_from_id(object_id_str.to_int())
		if not obj: return "Error: Could not find object with that ID."
		return _serialize_godot_object(obj)
		
	return "Error: Invalid ID format."

static func get_node_property_value(node_path: String, property_path: String) -> String:
	var root = _get_edited_root()
	if not root: return "Error: No scene currently open."
	
	var target_node = root if (node_path.is_empty() or node_path == ".") else root.get_node_or_null(node_path)
	if not target_node: return "Error: Could not find node at path '%s'." % node_path
	
	# Try access
	var val = target_node.get_indexed(property_path)
	
	if val == null and "/" in property_path:
		# Try replacing / with : as backup syntax
		var alt_path = property_path.replace("/", ":")
		val = target_node.get_indexed(alt_path)
		
	if val == null:
		# Check if the top level property actually exists to determine if it's truly null or invalid
		var top_prop = property_path.split(":")[0].split("/")[0]
		if target_node.get(top_prop) == null:
			return "Error: Property path '%s' returned Nil (or path is invalid)." % property_path
			
	if val is Object:
		return _serialize_godot_object(val)
	else:
		return str(val)

# --- Serialization ---

static func _serialize_godot_object(obj: Object) -> String:
	if not obj: return "null"
	
	var prop_dict = {}
	prop_dict["_Info_"] = {
		"Class": obj.get_class(),
		"InstanceId": str(obj.get_instance_id()),
		"ToString": str(obj)
	}
	
	if obj is Node:
		prop_dict["_NodeInfo_"] = {
			"Name": obj.name,
			"Path": str(obj.get_path())
		}
		if obj is Node2D:
			prop_dict["GlobalPosition"] = str(obj.global_position)
			prop_dict["Position"] = str(obj.position)
			prop_dict["RotationDegrees"] = obj.rotation_degrees
		elif obj is Node3D:
			prop_dict["GlobalPosition"] = str(obj.global_position)
			prop_dict["Position"] = str(obj.position)
			prop_dict["RotationDegrees"] = str(obj.rotation_degrees)
			
	var properties = obj.get_property_list()
	
	for prop in properties:
		var name = prop["name"]
		var usage = prop["usage"]
		
		# Filter flags
		var is_script_var = (usage & PROPERTY_USAGE_SCRIPT_VARIABLE) != 0
		var is_storage = (usage & PROPERTY_USAGE_STORAGE) != 0
		var is_editor = (usage & PROPERTY_USAGE_EDITOR) != 0
		
		if not (is_script_var or is_storage or is_editor): continue
		if name.begins_with("metadata/") or "script/source" in name: continue
		if name.ends_with(".cs") or name.ends_with(".gd"): continue
		
		var val = obj.get(name)
		
		if val is Object:
			var res_path = ""
			if val is Resource:
				res_path = val.resource_path
			prop_dict[name] = "<Object: %s (ID: %d) %s>" % [val.get_class(), val.get_instance_id(), res_path]
		else:
			var val_str = str(val)
			if val_str.length() > 200:
				val_str = val_str.substr(0, 200) + "...(truncated)"
			prop_dict[name] = val_str
			
	return JSON.stringify(prop_dict, "  ")

# --- File Creation ---

static func create_file(path: String, content: String) -> String:
	var dir = path.get_base_dir()
	var dir_access = DirAccess.open("res://")
	
	if dir_access == null:
		return "Error: Cannot access res:// directory."

	if not dir_access.dir_exists(dir):
		var err = dir_access.make_dir_recursive(dir)
		if err != OK:
			return "Error creating directory '%s': %s" % [dir, error_string(err)]

	var is_shader = path.get_extension().to_lower() == "gdshader"

	if is_shader:
		return _update_shader_with_cache_bypass(path, content)
	else:
		var file = FileAccess.open(path, FileAccess.WRITE)
		if file == null:
			return "Error: Could not open file '%s' for writing." % path
		
		file.store_string(content)
		file.flush()
		file.close()

		_call_deferred_refresh()
		return "Success: File created/overwritten at '%s'." % path

static func _call_deferred_refresh() -> void:
	if Engine.is_editor_hint():
		EditorInterface.get_resource_filesystem().call_deferred("scan")


static func _update_shader_with_cache_bypass(path: String, content: String) -> String:
	if not FileAccess.file_exists(path):
		var f = FileAccess.open(path, FileAccess.WRITE)
		if f:
			f.store_string("")
			f.close()

	var shader = ResourceLoader.load(path, "Shader", ResourceLoader.CACHE_MODE_IGNORE_DEEP) as Shader
	if shader == null:
		return "Error: Could not load shader resource."

	shader.code = content
	var save_err = ResourceSaver.save(shader, path)
	if save_err != OK:
		return "Error saving shader resource: %s" % error_string(save_err)

	var fs_dock = EditorInterface.get_file_system_dock()
	fs_dock.file_removed.emit(path)

	var fs = EditorInterface.get_resource_filesystem()
	fs.reimport_files([path])

	EditorInterface.edit_resource.call_deferred(load(path))

	return "Success: Shader updated and synchronized at '%s'." % path

# --- Custom Logger for Error Capture (Godot 4.5+) ---

# Internal class to capture errors during GDScript execution
class ErrorCaptureLogger extends Logger:
	var _log_buffer: String = ""
	var _is_capturing: bool = false
	
	func start_capturing() -> void:
		_log_buffer = ""
		_is_capturing = true
		
	func stop_capturing() -> String:
		_is_capturing = false
		return _log_buffer.strip_edges()
		
	# Override _log_message (Godot 4.5+)
	func _log_message(message: String, error: bool) -> void:
		if _is_capturing and error:
			_log_buffer += "[Message]: %s\n" % message

	# Override _log_error (Godot 4.5+)
	func _log_error(function: String, file: String, line: int, code: String, rationale: String, editor_notify: bool, error_type: int, script_backtraces: Array[ScriptBacktrace]) -> void:
		if _is_capturing:
			_log_buffer += "[Line %d]: %s\n" % [line, rationale]

# --- Run GDScript ---

static var _error_logger: ErrorCaptureLogger

static func run_gdscript(code: String) -> String:
	if not code.contains("@tool"):
		code = "@tool\n" + code

	if not _error_logger:
		_error_logger = ErrorCaptureLogger.new()
		OS.add_logger(_error_logger)
		
	var script = GDScript.new()
	script.source_code = code
	
	# Start Capture
	_error_logger.start_capturing()
	
	var err = script.reload()
	
	if err != OK:
		var detailed_error = _error_logger.stop_capturing()
		if not detailed_error.is_empty():
			return "GDScript Error (%s):\n%s\n\nPlease check your code." % [error_string(err), detailed_error]
		return "GDScript Syntax Error: %s. (No details captured)." % error_string(err)
		
	var instance: Object
	if script.can_instantiate():
		instance = script.new()
	else:
		_error_logger.stop_capturing()
		return "Error: Script cannot be instantiated. Ensure it extends a valid class."
		
	if not instance.has_method("run"):
		_error_logger.stop_capturing()
		# Clean up if it's a Node that was added (unlikely here but good practice)
		if instance is Node and not instance.is_inside_tree():
			instance.free()
		return "Error: The provided GDScript does not contain a 'func run():' method."
		
	# Execute
	var result = instance.call("run")
	
	# Stop Capture
	_error_logger.stop_capturing()
	
	# Cleanup temporary node
	if instance is Node:
		if instance.is_inside_tree():
			instance.queue_free()
		else:
			instance.free()
			
	if result is Object:
		var res_str = "[Object: %s ID:%d]" % [result.get_class(), result.get_instance_id()]
		return res_str
		
	return str(result)
