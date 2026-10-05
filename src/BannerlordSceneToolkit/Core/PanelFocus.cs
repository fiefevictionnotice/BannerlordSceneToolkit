using TaleWorlds.GauntletUI.BaseTypes;

namespace BannerlordSceneToolkit
{
    // Shared by every panel's per-tick outside-click handler (2026-08-23, "the placement
    // preview stops working after a while of having the F7 menu open"): each panel used to
    // null the SCREEN-WIDE EventManager.FocusedWidget on any click outside its own rect. The
    // intent was to defocus the panel's own text fields when clicking into the viewport - but
    // the same click stole focus from EVERY other panel, including the editor's own resource
    // browser at the exact moment a drag-placement starts, which is what killed the native
    // drag ghost whenever any toolkit panel was open. Focus is now dropped ONLY when the
    // focused widget actually belongs to the calling panel; clicks that focus other panels
    // are none of our business.
    public static class PanelFocus
    {
        public static void DropFocusOnOutsideClick(Widget root)
        {
            if (root == null) return;
            var em = root.EventManager;
            if (em == null) return;

            var mouse = em.MousePosition;
            if (root.AreaRect.IsPointInside(in mouse)) return;

            var focused = em.FocusedWidget;
            for (var w = focused; w != null; w = w.ParentWidget)
            {
                if (w == root)
                {
                    em.FocusedWidget = null;
                    return;
                }
            }
        }
    }
}
