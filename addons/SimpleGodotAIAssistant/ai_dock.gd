@tool
extends EditorDock

signal screenshot_captured(result: String)

# UI Elements
var _main_layout: VBoxContainer
var _chat_display: RichTextLabel
var _input_box: TextEdit
var _send_btn: Button
var _stop_btn: Button
var _settings_btn: Button
var _settings_panel: PanelContainer
var _context_label: Label
var _image_status_container: HBoxContainer
var _url_edit: LineEdit
var _key_edit: LineEdit
var _model_edit: LineEdit
var _continue_btn: Button
var _profile_label: Label
var _profile_dialog: AcceptDialog
var _profile_list: ItemList
var _profile_name_edit: LineEdit
var _skills_btn: Button
var _skills_panel: PanelContainer
var _skills_list: ItemList

# Logic
var _profiles: Dictionary
var _active_profile: String
var _config: Dictionary
var _enabled_skills: Array = []
var _last_response_raw: String = ""
var _chat_history: Array = []
var _http_request: HTTPRequest
var _pending_image_base64: String = ""
var _is_processing: bool = false
var _stop_requested: bool = false
var _screenshot_request_active: bool = false
var _screenshot_frames_remaining: int = 0
var _screenshot_max_width: int = 1280
var _screenshot_original_modulate: Color

func _ready():
	var data = AiConfigManager.load_data()
	_profiles = data["profiles"]
	_active_profile = data["active_profile"]
	_config = _profiles[_active_profile]
	_enabled_skills = data.get("enabled_skills", [])
	_setup_ui()
	
	_http_request = HTTPRequest.new()
	add_child(_http_request)
	_http_request.request_completed.connect(_on_request_completed)

func _exit_tree():
	if _screenshot_request_active:
		_screenshot_request_active = false
		screenshot_captured.emit("Error: AI Assistant dock was unloaded.")

func capture_screenshot_without_dock(max_width: int = 1280) -> String:
	if _screenshot_request_active:
		return "Error: A screenshot request is already in progress."

	_screenshot_max_width = max_width
	_screenshot_original_modulate = modulate
	modulate.a = 0.0
	_screenshot_frames_remaining = 2
	_screenshot_request_active = true
	set_process(true)
	return await screenshot_captured

func _process(_delta):
	if not _screenshot_request_active:
		set_process(false)
		return

	_screenshot_frames_remaining -= 1
	if _screenshot_frames_remaining > 0:
		return

	set_process(false)
	_screenshot_request_active = false
	var result = _capture_editor_image(_screenshot_max_width)
	modulate = _screenshot_original_modulate
	screenshot_captured.emit(result)

func _capture_editor_image(max_width: int) -> String:
	var base_control = EditorInterface.get_base_control()
	if not base_control:
		return "Error: Could not access editor base control."

	var viewport_texture = base_control.get_viewport().get_texture()
	if not viewport_texture:
		return "Error: Could not access editor viewport texture."

	var img = viewport_texture.get_image()
	if not img or img.is_empty():
		return "Error: Screenshot capture failed (empty image)."

	if max_width > 0 and img.get_width() > max_width:
		var scale_factor = float(max_width) / img.get_width()
		img.resize(max_width, int(img.get_height() * scale_factor), Image.INTERPOLATE_BILINEAR)

	var buffer = img.save_png_to_buffer()
	return "data:image/png;base64," + Marshalls.raw_to_base64(buffer)

func _setup_ui():
	# Clear existing children if reloading
	for child in get_children():
		if child != _http_request: # Don't remove the HTTP node
			child.queue_free()

	_main_layout = VBoxContainer.new()
	_main_layout.layout_mode = 1
	_main_layout.set_anchors_preset(LayoutPreset.PRESET_FULL_RECT)
	add_child(_main_layout)

	# --- Toolbar ---
	var tool_bar = HBoxContainer.new()
	
	_settings_btn = Button.new()
	_settings_btn.text = "Settings"
	_settings_btn.toggle_mode = true
	_settings_btn.toggled.connect(_on_settings_toggled)
	tool_bar.add_child(_settings_btn)
	
	var clear_btn = Button.new()
	clear_btn.text = "Clear Chat"
	clear_btn.pressed.connect(_on_clear_pressed)
	tool_bar.add_child(clear_btn)

	_skills_btn = Button.new()
	_skills_btn.text = "Skills"
	_skills_btn.toggle_mode = true
	_skills_btn.toggled.connect(_on_skills_toggled)
	tool_bar.add_child(_skills_btn)

	var spacer = Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tool_bar.add_child(spacer)
	
	_context_label = Label.new()
	_context_label.text = "Tokens: 0"
	_context_label.modulate = Color(0.7, 0.7, 0.7)
	tool_bar.add_child(_context_label)
	
	_main_layout.add_child(tool_bar)
	
	# --- Settings Panel ---
	_create_settings_panel()
	_main_layout.add_child(_settings_panel)

	# --- Skills Panel ---
	_create_skills_panel()
	_main_layout.add_child(_skills_panel)

	# --- Chat Display ---
	_chat_display = RichTextLabel.new()
	_chat_display.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_chat_display.focus_mode = Control.FOCUS_CLICK
	_chat_display.selection_enabled = true
	_chat_display.bbcode_enabled = true
	_chat_display.scroll_following = true
	_main_layout.add_child(_chat_display)
	
	# --- Image Status ---
	_image_status_container = HBoxContainer.new()
	_image_status_container.visible = false
	var img_lbl = Label.new()
	img_lbl.text = "🖼 Image attached from clipboard"
	img_lbl.modulate = Color.LIGHT_GREEN
	img_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_image_status_container.add_child(img_lbl)
	
	var clr_img_btn = Button.new()
	clr_img_btn.text = "x"
	clr_img_btn.flat = true
	clr_img_btn.pressed.connect(_clear_pending_image)
	_image_status_container.add_child(clr_img_btn)
	
	_main_layout.add_child(_image_status_container)
	
	# --- Input Area ---
	var input_container = HBoxContainer.new()
	input_container.custom_minimum_size = Vector2(0, 100)
	
	_input_box = TextEdit.new()
	_input_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_input_box.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	_input_box.gui_input.connect(_on_input_gui_input)
	input_container.add_child(_input_box)
	
	_send_btn = Button.new()
	_send_btn.text = "Send"
	_send_btn.pressed.connect(_on_send_pressed)
	input_container.add_child(_send_btn)
	
	_continue_btn = Button.new()
	_continue_btn.text = "Continue"
	_continue_btn.visible = false
	_continue_btn.pressed.connect(_on_continue_pressed)
	input_container.add_child(_continue_btn)
	
	_stop_btn = Button.new()
	_stop_btn.text = "Stop"
	_stop_btn.visible = false
	_stop_btn.pressed.connect(_on_stop_pressed)
	input_container.add_child(_stop_btn)
	
	_main_layout.add_child(input_container)

	_update_skills_btn_text()
	_append_system_message("AI Assistant Ready. Configure settings to start. Paste images directly (Ctrl+V).")

func _create_settings_panel():
	_settings_panel = PanelContainer.new()
	_settings_panel.visible = false
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_bottom", 10)
	margin.add_theme_constant_override("margin_left", 10)
	margin.add_theme_constant_override("margin_right", 10)
	_settings_panel.add_child(margin)
	
	var vbox = VBoxContainer.new()
	margin.add_child(vbox)

	# --- Profile Row ---
	var profile_row = HBoxContainer.new()
	_profile_label = Label.new()
	_profile_label.text = "Profile: " + _active_profile
	_profile_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	profile_row.add_child(_profile_label)

	var switch_btn = Button.new()
	switch_btn.text = "Switch / Manage..."
	switch_btn.pressed.connect(_show_profile_dialog)
	profile_row.add_child(switch_btn)
	vbox.add_child(profile_row)

	var grid = GridContainer.new()
	grid.columns = 2
	
	grid.add_child(_create_label("Endpoint URL:"))
	_url_edit = LineEdit.new()
	_url_edit.text = _config.get("endpoint", "")
	_url_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(_url_edit)
	
	grid.add_child(_create_label("API Key:"))
	_key_edit = LineEdit.new()
	_key_edit.text = _config.get("api_key", "")
	_key_edit.secret = true
	_key_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(_key_edit)
	
	grid.add_child(_create_label("Model Name:"))
	_model_edit = LineEdit.new()
	_model_edit.text = _config.get("model", "")
	_model_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(_model_edit)
	
	vbox.add_child(grid)
	
	var save_btn = Button.new()
	save_btn.text = "Save Settings & Close"
	save_btn.pressed.connect(_save_settings)
	vbox.add_child(save_btn)

	# --- Profile Dialog ---
	_profile_dialog = AcceptDialog.new()
	_profile_dialog.title = "AI Profiles"
	_profile_dialog.ok_button_text = "Close"

	var dvbox = VBoxContainer.new()
	dvbox.custom_minimum_size = Vector2(360, 260)
	_profile_dialog.add_child(dvbox)

	_profile_list = ItemList.new()
	_profile_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_profile_list.item_activated.connect(_on_profile_item_activated)
	dvbox.add_child(_profile_list)

	var row = HBoxContainer.new()
	_profile_name_edit = LineEdit.new()
	_profile_name_edit.placeholder_text = "New profile name"
	_profile_name_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(_profile_name_edit)

	var add_btn = Button.new()
	add_btn.text = "Add"
	add_btn.pressed.connect(_on_add_profile)
	row.add_child(add_btn)

	var del_btn = Button.new()
	del_btn.text = "Delete"
	del_btn.pressed.connect(_on_delete_profile)
	row.add_child(del_btn)
	dvbox.add_child(row)

	var use_btn = Button.new()
	use_btn.text = "Use Selected Profile"
	use_btn.pressed.connect(_on_use_profile_pressed)
	dvbox.add_child(use_btn)

	add_child(_profile_dialog)

func _create_skills_panel():
	_skills_panel = PanelContainer.new()
	_skills_panel.visible = false

	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_bottom", 10)
	margin.add_theme_constant_override("margin_left", 10)
	margin.add_theme_constant_override("margin_right", 10)
	_skills_panel.add_child(margin)

	var vbox = VBoxContainer.new()
	margin.add_child(vbox)

	var info_lbl = Label.new()
	info_lbl.text = "Enabled skills are injected as a system message. Add .md files to res://skills/."
	info_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	info_lbl.modulate = Color(0.7, 0.7, 0.7)
	vbox.add_child(info_lbl)

	_skills_list = ItemList.new()
	_skills_list.select_mode = ItemList.SELECT_MULTI
	_skills_list.custom_minimum_size = Vector2(0, 120)
	_skills_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_skills_list.multi_selected.connect(_on_skill_multi_selected)
	vbox.add_child(_skills_list)

	var refresh_btn = Button.new()
	refresh_btn.text = "Refresh List"
	refresh_btn.pressed.connect(_refresh_skills_list)
	vbox.add_child(refresh_btn)

func _on_skills_toggled(toggled: bool):
	_skills_panel.visible = toggled
	if toggled:
		_refresh_skills_list()

func _refresh_skills_list():
	_skills_list.clear()
	var available = AiTools.get_available_skills()
	var still_valid: Array = []
	for skill_name in available:
		var data = AiTools.parse_skill_file(AiTools.SKILLS_DIR.path_join(skill_name + ".md"))
		var idx = _skills_list.add_item(skill_name)
		if not data["description"].is_empty():
			_skills_list.set_item_tooltip(idx, data["description"])
		if skill_name in _enabled_skills:
			_skills_list.select(idx, false)
			still_valid.append(skill_name)
	if still_valid.size() != _enabled_skills.size():
		_enabled_skills = still_valid
		_save_all()
	_update_skills_btn_text()

func _on_skill_multi_selected(_index: int, _selected: bool):
	_enabled_skills.clear()
	for idx in _skills_list.get_selected_items():
		_enabled_skills.append(_skills_list.get_item_text(idx))
	_save_all()
	_update_skills_btn_text()
	if not _enabled_skills.is_empty():
		_append_system_message("Skills enabled: " + ", ".join(_enabled_skills))

func _update_skills_btn_text():
	if _enabled_skills.is_empty():
		_skills_btn.text = "Skills"
	else:
		_skills_btn.text = "Skills (%d)" % _enabled_skills.size()

func _show_profile_dialog():
	_sync_fields_to_profile()
	_save_all()
	_refresh_profile_list()
	_profile_dialog.popup_centered()

func _refresh_profile_list():
	_profile_list.clear()
	for name in _profiles.keys():
		var idx = _profile_list.add_item(name)
		if name == _active_profile:
			_profile_list.select(idx)

func _on_add_profile():
	var name = _profile_name_edit.text.strip_edges()
	if name.is_empty() or _profiles.has(name):
		return
	_profiles[name] = AiConfigManager.get_default_profile()
	_profile_name_edit.text = ""
	_save_all()
	_refresh_profile_list()

func _on_delete_profile():
	var sel = _profile_list.get_selected_items()
	if sel.is_empty():
		return
	if _profiles.size() <= 1:
		_append_system_message("Cannot delete the last profile.")
		return
	var name = _profile_list.get_item_text(sel[0])
	_profiles.erase(name)
	if name == _active_profile:
		_active_profile = _profiles.keys()[0]
		_config = _profiles[_active_profile]
		_profile_label.text = "Profile: " + _active_profile
		_update_fields()
	_save_all()
	_refresh_profile_list()

func _on_use_profile_pressed():
	var sel = _profile_list.get_selected_items()
	if sel.is_empty():
		return
	_switch_profile(_profile_list.get_item_text(sel[0]))

func _on_profile_item_activated(index: int):
	_switch_profile(_profile_list.get_item_text(index))

func _switch_profile(name: String):
	if not _profiles.has(name) or name == _active_profile:
		return
	_sync_fields_to_profile()
	_active_profile = name
	_config = _profiles[name]
	_profile_label.text = "Profile: " + name
	_update_fields()
	_save_all()
	_append_system_message("Switched to profile: " + name)
	_profile_dialog.hide()

func _sync_fields_to_profile():
	_config["endpoint"] = _url_edit.text
	_config["api_key"] = _key_edit.text
	_config["model"] = _model_edit.text

func _save_all():
	AiConfigManager.save_data({
		"profiles": _profiles,
		"active_profile": _active_profile,
		"enabled_skills": _enabled_skills
	})

func _on_settings_toggled(toggled: bool):
	_settings_panel.visible = toggled
	if toggled:
		_profile_label.text = "Profile: " + _active_profile
		_update_fields()

func _update_fields():
	_url_edit.text = _config.get("endpoint", "")
	_key_edit.text = _config.get("api_key", "")
	_model_edit.text = _config.get("model", "")

func _save_settings():
	_sync_fields_to_profile()
	_save_all()
	_append_system_message("Settings saved to profile: " + _active_profile)
	_settings_panel.visible = false
	_settings_btn.button_pressed = false

func _on_clear_pressed():
	_chat_history.clear()
	_chat_display.text = ""
	_clear_pending_image()
	_context_label.text = "Tokens: 0"
	_continue_btn.visible = false

func _clear_pending_image():
	_pending_image_base64 = ""
	_image_status_container.visible = false

func _on_input_gui_input(event: InputEvent):
	if event is InputEventKey and event.pressed:
		# Check for Ctrl+Enter / Cmd+Enter to send
		if event.keycode == KEY_ENTER and (event.ctrl_pressed or event.meta_pressed) and not _is_processing:
			_on_send_pressed()
			get_viewport().set_input_as_handled()
			return
		# Check for Ctrl+V / Cmd+V
		if event.keycode == KEY_V and (event.ctrl_pressed or event.meta_pressed):
			if DisplayServer.clipboard_has_image():
				var img = DisplayServer.clipboard_get_image()
				if img:
					var buffer = img.save_png_to_buffer()
					_pending_image_base64 = Marshalls.raw_to_base64(buffer)
					_image_status_container.visible = true
					_append_system_message("Captured image from clipboard.")
					get_viewport().set_input_as_handled()

func _on_stop_pressed():
	_stop_requested = true
	_http_request.cancel_request()
	
	_http_request.request_completed.emit(HTTPRequest.RESULT_REQUEST_FAILED, 0, [], PackedByteArray())
	
	_is_processing = false
	_send_btn.disabled = false
	_stop_btn.visible = false
	_continue_btn.visible = true
	_append_system_message("Generation stopped.")

func _on_send_pressed():
	var text = _input_box.text.strip_edges()
	
	if (text.is_empty() and _pending_image_base64.is_empty()) or _is_processing:
		return
		
	_input_box.text = ""
	
	# Display User Message
	var display_msg = text
	if not _pending_image_base64.is_empty():
		display_msg += "\n[i][color=#8FBCBB](Attached Image)[/color][/i]"
	_append_message("User", display_msg)
	
	# Build Message Object
	var content_obj
	if not _pending_image_base64.is_empty():
		var content_list = []
		if not text.is_empty():
			content_list.append({ "type": "text", "text": text })
		
		content_list.append({
			"type": "image_url",
			"image_url": { "url": "data:image/png;base64," + _pending_image_base64 }
		})
		content_obj = content_list
		_clear_pending_image()
	else:
		content_obj = text
		
	_chat_history.append({ "role": "user", "content": content_obj })
	
	_is_processing = true
	_send_btn.disabled = true
	_stop_btn.visible = true
	_continue_btn.visible = false
	_stop_requested = false
	
	var success = await _process_chat_loop()
	
	_is_processing = false
	_send_btn.disabled = false
	_stop_btn.visible = false
	_continue_btn.visible = not success

func _on_continue_pressed():
	if _is_processing or _chat_history.is_empty():
		return
		
	_append_system_message("Resuming generation...")
	
	_is_processing = true
	_send_btn.disabled = true
	_continue_btn.visible = false
	_stop_btn.visible = true
	_stop_requested = false
	
	var success = await _process_chat_loop()
	
	_is_processing = false
	_send_btn.disabled = false
	_stop_btn.visible = false
	_continue_btn.visible = not success

# --- The Async Loop ---
func _process_chat_loop() -> bool:
	var safety_loop = 0
	var keep_going = true
	var success = true
	
	while keep_going and safety_loop < 20:
		safety_loop += 1
		if _stop_requested:
			success = false
			break
		
		# 1. Send Request
		var response_dict = await _send_to_api()
		if not response_dict or _stop_requested: 
			success = false
			break
			
		if response_dict.has("usage"):
			var u = response_dict["usage"]
			_update_tokens(u.get("prompt_tokens", 0), u.get("completion_tokens", 0), u.get("total_tokens", 0))
			
		if response_dict["choices"].is_empty():
			success = false
			break
			
		var choice = response_dict["choices"][0]
		var message = choice["message"]
		
		var content = message.get("content")
		var tool_calls = message.get("tool_calls")
		
		# Handle Reasoning (DeepSeek / o1 models)
		var reasoning = message.get("reasoning_content")
		if not reasoning: reasoning = message.get("reasoning")
		
		var assistant_msg = { "role": "assistant" }
		if content != null: assistant_msg["content"] = content
		if tool_calls and tool_calls is Array:
			var normalized: Array = []
			for tc in tool_calls:
				var copy: Dictionary = tc.duplicate(true) if tc is Dictionary else { "function": tc, "id": "", "type": "function" }
				if copy.has("index") and copy["index"] != null:
					copy["index"] = int(copy["index"])
				else:
					copy["index"] = normalized.size()
				normalized.append(copy)
			assistant_msg["tool_calls"] = normalized
		
		if reasoning: 
			assistant_msg["reasoning_content"] = reasoning
			_append_system_message("🧠 Reasoning: " + str(reasoning.length()) + " chars")
		
		_chat_history.append(assistant_msg)
		
		if content:
			_append_message("AI", str(content))
			
		if tool_calls and tool_calls is Array:
			for tc in tool_calls:
				if _stop_requested:
					success = false
					break

				var id = tc["id"]
				var func_def = tc["function"]
				var func_name = func_def["name"]
				var args_json = func_def["arguments"]

				_append_system_message("🛠 [b]Calling Tool:[/b] [color=#88C0D0]%s[/color]\nArguments: [color=#D8DEE9]%s[/color]" % [func_name,args_json])

				# Small delay to let UI update
				await get_tree().process_frame
				if _stop_requested:
					success = false
					break

				var result_str = await _execute_tool(func_name, args_json)

				if result_str.begins_with("data:image/"):
					_append_system_message("✅ Result: [color=#A3BE8C](Image captured, sent as vision input)[/color]")
					_chat_history.append({
						"role": "tool",
						"tool_call_id": id,
						"content": [
							{
								"type": "image_url",
								"image_url": { "url": result_str }
							}
						]
					})
				else:
					var preview = result_str.substr(0, 150) + "..." if result_str.length() > 150 else result_str
					_append_system_message("✅ Result: [color=#A3BE8C]" + preview + "[/color]")

					_chat_history.append({
						"role": "tool",
						"tool_call_id": id,
						"content": result_str
					})
		else:
			keep_going = false

	return success

func _send_to_api() -> Dictionary:
	var headers = [
		"Content-Type: application/json",
		"Authorization: Bearer " + _config.get("api_key", ""),
		"User-Agent: SimpleGodotAIAssistant"
	]
	
	var messages = _chat_history
	var skills_prompt = AiTools.get_skills_system_prompt(_enabled_skills)
	if not skills_prompt.is_empty():
		messages = [{ "role": "system", "content": skills_prompt }] + _chat_history

	var body = {
		"model": _config.get("model", "gpt-4o"),
		"messages": messages,
		"tools": AiTools.get_tool_definitions(),
		"reasoning_effort": "high",
		"max_tokens": 16000
	}
	
	var json_str = JSON.stringify(body)
	var err = _http_request.request(_config.get("endpoint").replace("/chat/completions", "") + "/chat/completions", headers, HTTPClient.METHOD_POST, json_str)

	if err != OK:
		_last_response_raw = ""
		_append_system_message("HTTP Request Failed: " + error_string(err))
		return {}
		
	var result = await _http_request.request_completed
	
	if _stop_requested:
		return {}
		
	var response_code = result[1]
	var response_body = result[3]
	_last_response_raw = response_body.get_string_from_utf8()
	
	if response_code != 200:
		if response_code == 0 and _stop_requested:
			return {}

		var err_txt = _last_response_raw
		_append_system_message("API Error (%d): %s" % [response_code, err_txt])
		return {}
		
	var json = JSON.parse_string(_last_response_raw)
	if not json:
		_append_system_message("Failed to parse JSON response.")
		return {}
		
	if json.has("error"):
		_append_system_message("API returned error: " + str(json["error"]))
		return {}
		
	return json


func _execute_tool(name: String, json_args: String) -> String:
	var args = JSON.parse_string(json_args)
	if args == null: return "Error: Invalid JSON arguments."
	
	match name:
		"list_directory": return AiTools.list_directory(args.get("path", ""))
		"read_file": return AiTools.read_file(args.get("path", ""))
		"search_files": return AiTools.search_files(args.get("keyword", ""))
		"get_scene_tree": return AiTools.get_scene_tree(str(args.get("node_id", "0")))
		"get_selected_nodes": return AiTools.get_selected_nodes()
		"get_object_properties": return AiTools.get_object_properties(str(args.get("object_id", "")))
		"get_node_properties_by_path": return AiTools.get_node_properties_by_path(args.get("path", ""))
		"get_node_property_value": return AiTools.get_node_property_value(args.get("node_path", ""), args.get("property_path", ""))
		"create_file": return AiTools.create_file(args.get("path", ""), args.get("content", ""))
		"replace_text": return AiTools.replace_text(args.get("path", ""), args.get("old_text", ""), args.get("new_text", ""))
		"take_screenshot": return await AiTools.take_screenshot(int(args.get("max_width", 1280)), self)
		"list_skills": return AiTools.list_skills()
		"load_skill": return AiTools.load_skill(args.get("name", ""))
		"run_gdscript": return AiTools.run_gdscript(args.get("code", ""))
		_: return "Error: Unknown tool."

func _update_tokens(p_tok, c_tok, t_tok):
	_context_label.text = "Tokens: %d (In: %d / Out: %d)" % [t_tok, p_tok, c_tok]
	if t_tok > 16000: _context_label.modulate = Color.RED
	elif t_tok > 8000: _context_label.modulate = Color.YELLOW
	else: _context_label.modulate = Color(0.7, 0.7, 0.7)

func _append_message(sender: String, text: String):
	var color = "#88c0d0" if sender == "User" else "#a3be8c"
	_chat_display.append_text("[b][color=%s]%s:[/color][/b]\n%s\n\n" % [color, sender, text])

func _append_system_message(text: String):
	_chat_display.append_text("[font_size=12][i][color=#d08770]%s[/color][/i][/font_size]\n" % text)

# Signal handler helper for HTTPRequest
func _on_request_completed(_result, _response_code, _headers, _body):
	pass # Logic is handled via await in _send_to_api

func _create_label(txt: String) -> Label:
	var lbl = Label.new()
	lbl.text = txt
	return lbl
