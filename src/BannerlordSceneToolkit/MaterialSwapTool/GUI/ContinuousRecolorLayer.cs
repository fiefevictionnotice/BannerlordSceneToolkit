using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Opened from the main panel, positioned below it (the user's own suggestion for where an
    // "alternate mode" tool like this should live, distinct from the left/right flyout slots the
    // read-only/utility panels use).
    public static class ContinuousRecolorLayer
    {
        private static GauntletLayer _layer;
        private static ContinuousRecolorVM _dataSource;
        private static Widget _rootWidget;

        // Sized so this panel's TOP edge lands at the start of the main panel's bottom ~30%
        // section, per live feedback (was overlapping too far up into the middle of the main
        // panel before). Main panel: Y=100, height=1112, so its bottom edge sits at 100+556=656
        // and its bottom-30% zone starts at 656-(0.3*1112)=322. This panel is now 290 tall
        // (half=145), so top-edge-at-322 means center Y = 322+145 = 467. Reasoned from panel
        // geometry, not a blind guess, but still first-pass given the "unexplained scale factor"
        // uncertainty already seen with the preset browser gap earlier this session - confirm live.
        private const float InitialYOffset = 467f;
        private static readonly PanelDrag Drag = new PanelDrag("ContinuousRecolorPanel", 0f, InitialYOffset);

        public static bool IsOpen => _layer != null;
        // CONFIRMED CRASH CAUSE (2026-08-19, full dump analysis): the engine's own
        // GauntletLayer.IsFocusedOnInput() throws a NullReferenceException if the layer's
        // underlying native movie/screen was torn down without our code being told - a scene
        // switch tears down the whole screen/layer stack, but nothing clears this static _layer
        // field, so the very next tick's Prefix check calls into a dead layer and crashes the
        // whole game. "Crashes every time you change from one loaded scene to another." Treating
        // that as "not focused" (and self-healing by clearing _layer, same as a real Close())
        // instead of letting it crash is the correct, safe fallback.
        public static bool IsFocusedOnInput
        {
            get
            {
                if (_layer == null) return false;
                try { return _layer.IsFocusedOnInput(); }
                catch (Exception ex)
                {
                    Log.Warn("IsFocusedOnInput threw (stale layer after a scene switch?): " + ex.Message);
                    _layer = null;
                    return false;
                }
            }
        }

        public static void Open()
        {
            if (IsOpen) return;

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                Log.Error("ContinuousRecolorLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new ContinuousRecolorVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("ContinuousRecolorPanel", 4006) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("ContinuousRecolorPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "ContinuousRecolorPanel");
                Log.Info("ContinuousRecolorLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("ContinuousRecolorLayer.Open failed: " + ex);
                Close();
            }
        }

        public static void Close()
        {
            // Opened FROM this panel, makes no sense without it.
            CategoryEditorLayer.Close();

            if (!IsOpen) return;
            try
            {
                _layer.InputRestrictions.ResetInputRestrictions();
                ScreenManager.TopScreen?.RemoveLayer(_layer);
            }
            catch (Exception ex)
            {
                Log.Warn("ContinuousRecolorLayer teardown issue: " + ex.Message);
            }
            _layer = null;
            _dataSource = null;
            _rootWidget = null;
        }

        // Called from the tick patch right after a click-triggered selection refresh, so an armed
        // recolor tool immediately picks up whatever you just selected - no per-tick polling.
        public static void ReapplyIfArmed() => _dataSource?.ReapplyIfArmed();

        // GUARDED. A scene switch tears down the whole screen/layer stack while this class's
        // static _layer still points at it, so the next tick calls into a dead layer -
        // _layer.Input throws and takes the editor down with it. IsFocusedOnInput has always
        // self-healed from exactly this; Tick never did, and a crash during a scene load is
        // the symptom. Treat a throw as "this layer is gone" and clear it, same as a Close().
        public static void Tick(float dt)
        {
            if (!IsOpen) return;
            try { TickInner(dt); }
            catch (Exception ex)
            {
                Log.Warn("Tick threw (stale layer after a scene switch?): " + ex.Message);
                _layer = null;
                _dataSource = null;
                _rootWidget = null;
            }
        }

        private static void TickInner(float dt)
        {
            if (!IsOpen) return;

            Drag.Tick(_rootWidget);
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "ContinuousRecolorPanel");

            // Ctrl/Shift+Backspace clears the focused text field (see TextFieldShortcuts).
            BannerlordSceneToolkit.TextFieldShortcuts.Tick(_rootWidget);

            if (_rootWidget != null && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                BannerlordSceneToolkit.PanelFocus.DropFocusOnOutsideClick(_rootWidget);
            }

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
