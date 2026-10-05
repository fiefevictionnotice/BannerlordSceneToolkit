using System;
using TaleWorlds.MountAndBlade;

namespace BannerlordSceneToolkit
{
    // "Pop every open panel to the centre of the screen" - Ctrl+Shift+0 (number row).
    //
    // Why a generation counter and not a list of panels: the three tools each keep their own
    // PanelDrag copy (deliberate near-duplicates, see KNOWN-ISSUES), and the panels are static
    // layers that open and close independently. Rather than registering every drag helper
    // somewhere central, the hotkey just bumps a number; each PanelDrag compares it against
    // the value it last honoured on its next Tick, and an open panel re-centres itself. A
    // panel that was CLOSED when the key was pressed syncs to the current generation on open
    // (ApplyInitialPosition), so it keeps its saved position - only panels on screen at the
    // time move. Centre means offset (0,0) from the Gauntlet root's own Center/Center
    // alignment, and the new position is saved, so it sticks across sessions like a drag.
    public static class PanelRecenter
    {
        public static int Generation { get; private set; }

        public static void Request()
        {
            Generation++;
            Log.Info($"[PanelRecenter] requested (generation {Generation}) - every open toolkit panel re-centres this frame.");
            try { MBEditor.AddEditorWarning("Scene Toolkit: open panels centred (Ctrl+Shift+0)."); } catch { }
        }
    }
}
