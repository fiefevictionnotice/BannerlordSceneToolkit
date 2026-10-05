using System;
using TaleWorlds.Library;

namespace MaterialSwapTool.GUI
{
    // Flora Swap Tool retired from the UI - F9 now opens this instead of the real FloraSwapVM.
    // FloraSwapVM/FloraSwapEngine/FloraSwapPanel.xml are all left fully intact and unreachable
    // rather than deleted, same "retired, not deleted" convention used for Snap Tools, Identify
    // Variations, and the PrefabCreatorTool F5 scope reduction - a future revival doesn't start
    // from zero, it just needs FloraSwapLayer.Open() pointed back at the real VM/panel.
    public class FloraRetiredPlaceholderVM : ViewModel
    {
        private readonly Action _closeAction;
        private readonly Action _beginDragAction;

        public FloraRetiredPlaceholderVM(Action closeAction, Action beginDragAction)
        {
            _closeAction = closeAction;
            _beginDragAction = beginDragAction;
        }

        public void ExecuteClose() => _closeAction?.Invoke();
        public void ExecuteDragStart() => _beginDragAction?.Invoke();
    }
}
