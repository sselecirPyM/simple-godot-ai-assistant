@tool
extends RefCounted
class_name AiConfigManager

const CONFIG_PATH = "user://godot_ai_assistant_config.json"

static func load_config() -> Dictionary:
	var config = {
		"endpoint": "https://api.openai.com/v1/chat/completions",
		"api_key": "",
		"model": "gpt-4o"
	}
	
	if FileAccess.file_exists(CONFIG_PATH):
		var file = FileAccess.open(CONFIG_PATH, FileAccess.READ)
		var text = file.get_as_text()
		var json = JSON.parse_string(text)
		if json and json is Dictionary:
			config.merge(json, true)
			
	return config

static func save_config(config: Dictionary):
	var file = FileAccess.open(CONFIG_PATH, FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(config, "\t"))