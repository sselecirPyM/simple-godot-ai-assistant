@tool
extends RefCounted
class_name AiConfigManager

const PROJECT_CONFIG_PATH = "user://godot_ai_assistant_config.json"
const GLOBAL_CONFIG_DIR = "SimpleGodotAIAssistant"
const GLOBAL_CONFIG_FILE = "global_config.json"


static func get_global_config_path() -> String:
	var dir = OS.get_data_dir().path_join(GLOBAL_CONFIG_DIR)
	DirAccess.make_dir_recursive_absolute(dir)
	return dir.path_join(GLOBAL_CONFIG_FILE)

static func get_default_profile() -> Dictionary:
	return {
		"endpoint": "https://api.openai.com/v1/chat/completions",
		"api_key": "",
		"model": "gpt-6.1-sol",
		"reasoning_effort": "high",
		"system_prompt": ""
	}

static func get_default_profiles() -> Dictionary:
	return {
		"OpenAI": {
			"endpoint": "https://api.openai.com/v1/chat/completions",
			"api_key": "",
			"model": "gpt-6.1-sol",
			"reasoning_effort": "high"
		},
		"Gemini": {
			"endpoint": "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
			"api_key": "",
			"model": "gemini-3.1-pro-preview",
			"reasoning_effort": "high"
		},
		"DeepSeek": {
			"endpoint": "https://api.deepseek.com/chat/completions",
			"api_key": "",
			"model": "deepseek-v4.1-flash",
			"reasoning_effort": "high"
		},
		"Moonshot": {
			"endpoint": "https://api.moonshot.cn/v1/chat/completions",
			"api_key": "",
			"model": "kimi-k3",
			"reasoning_effort": "high"
		},
		"Kimi Coding plan": {
			"endpoint": "https://api.kimi.com/coding/v1",
			"api_key": "",
			"model": "k3-256k",
			"reasoning_effort": "high"
		},
		"Zhipu": {
			"endpoint": "https://open.bigmodel.cn/api/paas/v4/chat/completions",
			"api_key": "",
			"model": "glm-5.3",
			"reasoning_effort": "high"
		},
		"Zhipu Coding plan": {
			"endpoint": "https://open.bigmodel.cn/api/coding/paas/v4",
			"api_key": "",
			"model": "glm-5.3",
			"reasoning_effort": "high"
		}
	}

static func _read_json(path: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {}
	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {}
	var json = JSON.parse_string(file.get_as_text())
	if json and json is Dictionary:
		return json
	return {}


static func _write_json(path: String, data: Dictionary):
	var file = FileAccess.open(path, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(data, "\t"))


static func load_data() -> Dictionary:
	var data = {
		"profiles": get_default_profiles(),
		"active_profile": "OpenAI",
		"enabled_skills": [],
		"multimodal_enabled": true
	}

	var global_path = get_global_config_path()
	var global_json = _read_json(global_path)
	var project_json = _read_json(PROJECT_CONFIG_PATH)

	if global_json.has("profiles"):
		data["profiles"].merge(global_json["profiles"], true)
	elif global_json.has("endpoint"):
		var p = get_default_profile()
		p.merge(global_json, true)
		data["profiles"]["Default"] = p
	elif project_json.has("profiles"):
		data["profiles"].merge(project_json["profiles"], true)
		_write_json(global_path, {
			"profiles": project_json["profiles"]
		})

	if project_json.has("active_profile"):
		data["active_profile"] = project_json["active_profile"]
	elif global_json.has("active_profile"):
		data["active_profile"] = global_json["active_profile"]
	if project_json.has("multimodal_enabled"):
		data["multimodal_enabled"] = bool(project_json["multimodal_enabled"])
	if project_json.has("enabled_skills") and project_json["enabled_skills"] is Array:
		data["enabled_skills"] = project_json["enabled_skills"]

	if data["profiles"].is_empty():
		data["profiles"]["Default"] = get_default_profile()
	if not data["profiles"].has(data["active_profile"]):
		data["active_profile"] = data["profiles"].keys()[0]

	return data

static func save_data(data: Dictionary):
	_write_json(get_global_config_path(), {
		"profiles": data.get("profiles", {})
	})
	_write_json(PROJECT_CONFIG_PATH, {
		"active_profile": data.get("active_profile", ""),
		"enabled_skills": data.get("enabled_skills", []),
		"multimodal_enabled": data.get("multimodal_enabled", true)
	})
