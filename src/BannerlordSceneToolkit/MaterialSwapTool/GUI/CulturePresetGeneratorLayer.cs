using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Opened from the Preset Browser. Positioned at the same X as PresetBrowserLayer so it reads
    // as "belonging to" that panel, well above center rather than literally stacked
    // non-overlapping above it - the preset browser alone is nearly full-screen height already,
    // so true non-overlapping stacking isn't realistic on most resolutions. Exact placement is a
    // first guess, same as every other flyout's initial offset, and may need live tuning.
    public static class CulturePresetGeneratorLayer
    {
        private static GauntletLayer _layer;
        private static CulturePresetGeneratorVM _dataSource;
        private static Widget _rootWidget;

        private const float InitialXOffset = 380f + 120f + 390f;
        // Was -300 (overlapped the preset browser), then -588 (moved up by 90% of the panel's
        // then-height of 320), then -658 (grew 320->460 to fit the click-to-fill culture picker
        // list, shifted up by half that growth). Grew again 460->640 to fit the culture-stub
        // generator section + Edit Cultures button, so shifted up by half of THAT growth
        // (180/2 = 90): -658 - 90 = -748.
        private const float InitialYOffset = -748f;
        private static readonly PanelDrag Drag = new PanelDrag("CulturePresetGeneratorPanel", InitialXOffset, InitialYOffset);

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
                Log.Error("CulturePresetGeneratorLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new CulturePresetGeneratorVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("CulturePresetGeneratorPanel", 4004) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("CulturePresetGeneratorPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "CulturePresetGeneratorPanel");
                Log.Info("CulturePresetGeneratorLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("CulturePresetGeneratorLayer.Open failed: " + ex);
                Close();
            }
        }

        public static void Close()
        {
            // Opened FROM this panel, makes no sense without it.
            CultureEditorLayer.Close();

            if (!IsOpen) return;
            try
            {
                _layer.InputRestrictions.ResetInputRestrictions();
                ScreenManager.TopScreen?.RemoveLayer(_layer);
            }
            catch (Exception ex)
            {
                Log.Warn("CulturePresetGeneratorLayer teardown issue: " + ex.Message);
            }
            _layer = null;
            _dataSource = null;
            _rootWidget = null;
        }

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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "CulturePresetGeneratorPanel");

            // Ctrl/Shift+Backspace clears the focused text field (see TextFieldShortcuts).
            BannerlordSceneToolkit.TextFieldShortcuts.Tick(_rootWidget);

            // Same click-outside-blurs-text-focus fix as the other panels with text inputs.
            if (_rootWidget != null && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                BannerlordSceneToolkit.PanelFocus.DropFocusOnOutsideClick(_rootWidget);
            }

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
