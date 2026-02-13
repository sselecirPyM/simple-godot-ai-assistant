@tool
extends EditorPlugin

var dock_instance

func _enter_tree():
	# Load the dock script
	var dock_script = load("res://addons/SimpleGodotAIAssistant/ai_dock.gd")
	dock_instance = dock_script.new()
	dock_instance.title = "AI Assistant"
	dock_instance.default_slot = DOCK_SLOT_LEFT_UL
	
	# Add the dock to the editor
	add_dock(dock_instance)

func _exit_tree():
	if dock_instance:
		remove_dock(dock_instance)
		dock_instance.queue_free()
