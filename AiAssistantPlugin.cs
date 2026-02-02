#if TOOLS
using Godot;
using GodotAiAssistant;

namespace GodotAiAssistant
{
    [Tool]
    public partial class AiAssistantPlugin : EditorPlugin
    {
        private EditorDock _dock;

        public override void _EnterTree()
        {
            // 加载并实例化 Dock
            _dock = new AiDock();
            _dock.Title = "AI Assistant";
            _dock.DefaultSlot = EditorDock.DockSlot.LeftUl;

            AddDock(_dock);
        }

        public override void _ExitTree()
        {
            // 清理插件
            RemoveDock(_dock);
            _dock.Free();
        }
    }
}
#endif