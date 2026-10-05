using System;
using MaterialSwapTool.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;

namespace MaterialSwapTool.GUI
{
    // GLOBAL UI scaling, shared by every toolkit flyout (MaterialSwapTool, PrefabCreatorTool,
    // PrefabSwapperTool - one assembly, so all three folders call this). There is one scale value
    // for all panels (PanelScaleStore); change it on any panel and every panel opens at that size,
    // including windows popped out afterward.
    //
    // HOW IT WORKS: each panel is its own GauntletLayer with its own UIContext. The engine computes
    // that layer's on-screen scale from the screen resolution and then multiplies by
    // UIContext.ScaleModifier (default 1.0). Apply() sets that layer's ScaleModifier to the shared
    // value when the panel opens. ScaleModifier is a stored value the engine reads on every scale
    // recompute, so it is set once on open (and again when the user changes it); there is no need
    // to re-assert it per tick, which would dirty the layout every frame.
    //
    // LIVE KEYS (while the panel holds input focus):
    //   Ctrl + Minus   shrink 10% (all panels)
    //   Ctrl + Equals  grow 10%   (the unshifted '+' key)
    //   Ctrl + 0       reset to the default size (PanelScaleStore.DefaultScale)
    // Numpad -, + and 0 work too. The chosen value is saved globally via PanelScaleStore, so it
    // persists across sessions. Uses the global TaleWorlds.InputSystem.Input (like
    // TextFieldShortcuts) because that is what works while a panel owns focus.
    public static class PanelScale
    {
        private const float Step = 0.1f;

        // Apply the shared global scale to this layer. Call once, right after the movie loads and
        // the layer is added to the screen. (panelKey is unused - kept for call-site stability.)
        public static void Apply(GauntletLayer layer, string panelKey)
        {
            if (layer == null) return;
            try
            {
                layer.UIContext.ScaleModifier = PanelScaleStore.Get(panelKey);
            }
            catch (Exception ex)
            {
                Log.Warn("[PanelScale] apply failed for '" + panelKey + "': " + ex.Message);
            }
        }

        // Handle the live scale keys AND keep this panel matched to the shared global scale. Called
        // every tick the panel is open. First it re-syncs this layer to the global value, so a
        // change made from ANY other open panel is reflected here within a frame - and because it
        // only writes when the value actually differs, steady state is a float read + compare with
        // no layout churn. Then it processes an actual Ctrl+key press. Returns true if a key press
        // changed the scale.
        //
        // handleKeys=false keeps only the resync. Needed by the Grow Selection panel (2026-10-04):
        // its radius keys are +/- with Ctrl meaning "0.25 step", so with the scale keys live on
        // that layer too, Ctrl+Numpad+ inside it stepped the radius AND grew the panel at once.
        public static bool HandleHotkeys(GauntletLayer layer, string panelKey, bool handleKeys = true)
        {
            if (layer == null) return false;

            // Live-sync to the global scale (propagates changes made from other panels).
            try
            {
                float g = PanelScaleStore.Get();
                if (Math.Abs(layer.UIContext.ScaleModifier - g) > 0.0001f)
                    layer.UIContext.ScaleModifier = g;
            }
            catch (Exception ex)
            {
                Log.Warn("[PanelScale] sync failed for '" + panelKey + "': " + ex.Message);
            }

            if (!handleKeys) return false;

            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            if (!ctrl) return false;
            // Ctrl+SHIFT+0 is "centre all panels" (Core/PanelRecenter), not a scale reset.
            if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)) return false;

            // NUMBER ROW ONLY (2026-10-04). The numpad aliases collided with Grow Selection's
            // Ctrl+Numpad+ opener and in-panel Ctrl-step; the numpad now belongs to Grow.
            bool reset = Input.IsKeyPressed(InputKey.D0);
            bool smaller = Input.IsKeyPressed(InputKey.Minus);
            bool bigger = Input.IsKeyPressed(InputKey.Equals);
            if (!reset && !smaller && !bigger) return false;

            float current = PanelScaleStore.Get(panelKey);
            float next = reset ? PanelScaleStore.DefaultScale
                       : smaller ? PanelScaleStore.Clamp(current - Step)
                       : PanelScaleStore.Clamp(current + Step);

            // On reset, always re-apply to this layer even if the stored value did not move, so a
            // panel whose scale drifted still snaps back. Otherwise skip a no-op at a clamp limit.
            if (!reset && Math.Abs(next - current) < 0.0001f) return false;

            PanelScaleStore.Set(panelKey, next);
            try
            {
                layer.UIContext.ScaleModifier = next;
            }
            catch (Exception ex)
            {
                Log.Warn("[PanelScale] live set failed for '" + panelKey + "': " + ex.Message);
            }
            Log.Info("[PanelScale] global scale -> " + next.ToString("0.00"));
            return true;
        }
    }
}
