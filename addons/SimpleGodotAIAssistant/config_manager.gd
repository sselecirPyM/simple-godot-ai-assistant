@tool
extends RefCounted
class_name AiConfigManager

const CONFIG_PATH = "user://godot_ai_assistant_config.json"

static func get_default_profile() -> Dictionary:
	return {
		"endpoint": "https://api.openai.com/v1/chat/completions",
		"api_key": "",
		"model": "gpt-5.6-sol"
	}

static func get_default_profiles() -> Dictionary:
	return {
		"OpenAI": {
			"endpoint": "https://api.openai.com/v1/chat/completions",
			"api_key": "",
			"model": "gpt-5.6-sol"
		},
		"Gemini": {
			"endpoint": "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
			"api_key": "",
			"model": "gemini-3.1-pro-preview"
		},
		"DeepSeek": {
			"endpoint": "https://api.deepseek.com/chat/completions",
			"api_key": "",
			"model": "deepseek-v4-pro"
		},
		"Moonshot": {
			"endpoint": "https://api.moonshot.cn/v1/chat/completions",
			"api_key": "",
			"model": "kimi-k3"
		},
		"Zhipu": {
			"endpoint": "https://open.bigmodel.cn/api/paas/v4/chat/completions",
			"api_key": "",
			"model": "glm-5.2"
		}
	}

static func load_data() -> Dictionary:
	var data = {
		"profiles": get_default_profiles(),
		"active_profile": "OpenAI",
		"enabled_skills": []
	}

	if FileAccess.file_exists(CONFIG_PATH):
		var file = FileAccess.open(CONFIG_PATH, FileAccess.READ)
		var json = JSON.parse_string(file.get_as_text())
		if json and json is Dictionary:
			if json.has("enabled_skills") and json["enabled_skills"] is Array:
				data["enabled_skills"] = json["enabled_skills"]
			if json.has("profiles"):
				data["profiles"].merge(json["profiles"], true)
				if json.has("active_profile"):
					data["active_profile"] = json["active_profile"]
			elif json.has("endpoint"):
				var p = get_default_profile()
				p.merge(json, true)
				data["profiles"]["Default"] = p

	if data["profiles"].is_empty():
		data["profiles"]["Default"] = get_default_profile()
	if not data["profiles"].has(data["active_profile"]):
		data["active_profile"] = data["profiles"].keys()[0]

	return data

static func save_data(data: Dictionary):
	var file = FileAccess.open(CONFIG_PATH, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(data, "\t"))
