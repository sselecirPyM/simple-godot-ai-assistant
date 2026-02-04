# Simple Godot AI Assistant

这个插件不能帮你补全代码，也不了解你正在输入的内容，它只提供一个聊天框。Agent可以执行任意代码，因此使用时必须小心。

## 使用方法

你必须使用C#支持的 Godot 4.6，将这个仓库复制到你的Godot项目/addons/GodotAIAssistant目录下。构建.NET项目，重启编辑器，然后在设置中启用此插件。

不必担心密钥被提交到仓库中，密钥保存在 %appdata%/Godot/plugins/GodotAiAssistant目录。

## 推荐模型

为了达到最佳效果，请搭配Gemini 3 Pro preview或Gemini 3 Flash Preview模型使用。出于经济考虑，我认为你不会想使用超过10k上下文。

这个插件只给了Agent几个工具，这是为了节省提示词，也是为了节省你的钱。

不接受功能请求。如果你需要复杂的功能，请寻找其它插件。