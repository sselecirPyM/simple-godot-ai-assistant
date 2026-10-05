# Simple Godot AI Assistant GDScript

English | 中文

## English

A Godot AI Agent plugin adapted for multimodal models, verified on KIMI K3.

Usage:

Open Settings in the AI Assistant window and configure the API Key. Provider profiles (endpoint, model, API key) are stored globally and shared across all projects; the currently selected profile, skills, and multimodal toggle are stored per project.

You can type text in the chat box, and press Ctrl+V in the input box to paste images.

You can create a `skills` folder and place any markdown files in it to add skills to the AI. The AI only uses the file name to decide whether a skill is needed.

## 中文

这是一个适配多模态模型的Godot AI Agent插件，已在KIMI K3上验证通过。

使用方法：

在AI Assistant窗口打开设置，配置API Key。提供商配置（endpoint、模型、API Key）为全局配置，在所有项目间共享；当前使用的模型、技能、多模态开关按项目独立保存。

在聊天框可以输入文本，在输入框按下ctrl+v粘贴图片。

可以创建skills文件夹，并在文件夹中放入任何markdown，以向AI添加技能。AI仅使用文件名判断是否需要skill。
