using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace MaterialSwapTool.GUI
{
    // Hosts the panel outside of any Mission on ScreenManager.TopScreen - confirmed live: the
    // panel renders correctly during plain Qt edit mode with no mission running.
    public static class MaterialSwapLayer
    {
        private static GauntletLayer _layer;
        private static MaterialSwapVM _dataSource;
        private static Widget _rootWidget;

        // Shifted down from dead-center - PresetBrowserLayer's own X offset is measured relative
        // to this panel's position, so keep both panels' Y offset in sync if this changes. Only
        // used the very first time this panel is ever opened (no saved position yet) - after that,
        // PanelDrag restores wherever it was last dragged to.
        private const float InitialYOffset = 100f;
        private static readonly PanelDrag Drag = new PanelDrag("MaterialSwapPanel", 0f, InitialYOffset);

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

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            if (IsOpen) return;

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                Log.Error("MaterialSwapLayer.Open: ScreenManager.TopScreen is null - no managed screen " +
                          "is active to host the panel on.");
                return;
            }

            try
            {
                _dataSource = new MaterialSwapVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("MaterialSwapPanel", 4000) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("MaterialSwapPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "MaterialSwapPanel");
                // One-time scan so Manual mode isn't empty before the first click of the session -
                // after this, the tick patch only rescans on an actual click (see its comment).
                Core.EntitySelector.RefreshManualSelectionCache();
                Log.Info("MaterialSwapLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("MaterialSwapLayer.Open failed: " + ex);
                Close();
            }
        }

        public static void Close()
        {
            // Every flyout (presets, documentation, preview) is opened FROM this panel and makes
            // no sense without it - closing the main tool (whether via F9, Escape, or the Close
            // button) closes all of them too, rather than leaving them stranded open with no way
            // back to the panel that spawned them.
            PresetBrowserLayer.Close();
            DocumentationLayer.Close();
            DiffPreviewLayer.Close();
            BatchHistoryLayer.Close();
            ContinuousRecolorLayer.Close();

            if (!IsOpen) return;
            try
            {
                _layer.InputRestrictions.ResetInputRestrictions();
                ScreenManager.TopScreen?.RemoveLayer(_layer);
            }
            catch (Exception ex)
            {
                Log.Warn("MaterialSwapLayer teardown issue: " + ex.Message);
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "MaterialSwapPanel");

            // Ctrl/Shift+Backspace clears the focused text field (see TextFieldShortcuts).
            BannerlordSceneToolkit.TextFieldShortcuts.Tick(_rootWidget);

            // A click that lands outside this panel entirely (e.g. on an entity in the 3D
            // viewport) never reaches any widget in our tree, so Gauntlet's own click-to-focus
            // handling never fires and a focused rule textbox just keeps eating keystrokes -
            // that's the "asdf gets typed into my rule" bug. Force it explicitly instead of
            // relying on a focus-change event that was never going to happen.
            if (_rootWidget != null && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                BannerlordSceneToolkit.PanelFocus.DropFocusOnOutsideClick(_rootWidget);
            }

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
