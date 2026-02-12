# Simple Godot AI Assistant GDScript

该插件已使用GDScript重写。

这个插件不能帮你补全代码，也不了解你正在输入的内容，它只提供一个聊天框。请注意，Agent可以执行任意代码，因此使用时必须小心。

## 使用方法

将这个仓库复制到你的Godot项目/addons/SimpleGodotAIAssistant目录下。然后启用这个插件。

## 推荐模型

为了达到最佳效果，请搭配Gemini 3 Pro preview或Gemini 3 Flash Preview模型使用。

不接受功能请求。如果你需要复杂的功能，请寻找其它插件。

# 提示词示例

创建一个水晶着色器，修改选中节点的材质，保持纹理不变。将文件保存到res://Material/目录。

创建一个黑洞，放置到当前选中节点的位置，保存到res://VFX/BlackHole/目录。

在选中节点的位置创建一个由魔法砖块铺成的地面特效，初始时空无一物，运行时魔法砖块从四周飞来铺成路面。特效持续10秒。相关脚本保存到res://VFX/目录。
