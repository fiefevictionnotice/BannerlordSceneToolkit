using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // F7-bound, entirely separate tool/identity from Material Swap Tool (F9) and Flora Swap Tool
    // (F8) - a place for scene-wide diagnostics and fixes that aren't about materials at all
    // (broken prefabs, MP scene requirement checklists, and whatever gets added here later).
    public static class SceneAnalyzerLayer
    {
        private static GauntletLayer _layer;
        private static SceneAnalyzerVM _dataSource;
        private static Widget _rootWidget;

        private const float InitialYOffset = 100f;
        private static readonly PanelDrag Drag = new PanelDrag("SceneAnalyzerPanel", 0f, InitialYOffset);

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
                Log.Error("SceneAnalyzerLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new SceneAnalyzerVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("SceneAnalyzerPanel", 4011) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("SceneAnalyzerPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "SceneAnalyzerPanel");
                Log.Info("SceneAnalyzerLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("SceneAnalyzerLayer.Open failed: " + ex);
                Close();
            }
        }

        public static void Close()
        {
            // Its own results view - makes no sense stranded open without this panel.
            DiffPreviewLayer.Close();

            if (!IsOpen) return;
            try
            {
                _layer.InputRestrictions.ResetInputRestrictions();
                ScreenManager.TopScreen?.RemoveLayer(_layer);
            }
            catch (Exception ex)
            {
                Log.Warn("SceneAnalyzerLayer teardown issue: " + ex.Message);
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "SceneAnalyzerPanel");

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
