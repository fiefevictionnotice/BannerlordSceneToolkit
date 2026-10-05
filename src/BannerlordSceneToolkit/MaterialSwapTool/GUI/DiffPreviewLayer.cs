using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Same left-side slot as DocumentationLayer - the two are read-only "extra info" panels that
    // wouldn't normally be open at the same time, so reusing one slot instead of inventing a
    // third screen position keeps things simple.
    public static class DiffPreviewLayer
    {
        private static GauntletLayer _layer;
        private static DiffPreviewVM _dataSource;
        private static Widget _rootWidget;

        private const float InitialXOffset = -(380f + 60f + 310f);
        private const float InitialYOffset = 100f;
        private static readonly PanelDrag Drag = new PanelDrag("DiffPreviewPanel", InitialXOffset, InitialYOffset);

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

        public static void Open(string headerText, System.Collections.Generic.List<string> lines)
            => Open(headerText, lines, 0, null);

        // The overload with a regenerate callback is what puts the "Show first N" row on the
        // flyout. Callers whose report is complete as generated (the Scene Analyzer) use the
        // short overload above and the row stays hidden.
        public static void Open(string headerText, System.Collections.Generic.List<string> lines,
                                int initialLimit, Func<int, DiffPreviewContent> regenerate)
        {
            if (IsOpen) Close();

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                Log.Error("DiffPreviewLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new DiffPreviewVM(Close, Drag.BeginDrag, headerText, initialLimit, regenerate);
                _dataSource.SetLines(lines);

                _layer = new GauntletLayer("DiffPreviewPanel", 4003) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("DiffPreviewPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "DiffPreviewPanel");
                Log.Info("DiffPreviewLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("DiffPreviewLayer.Open failed: " + ex);
                Close();
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;
            try
            {
                _layer.InputRestrictions.ResetInputRestrictions();
                ScreenManager.TopScreen?.RemoveLayer(_layer);
            }
            catch (Exception ex)
            {
                Log.Warn("DiffPreviewLayer teardown issue: " + ex.Message);
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "DiffPreviewPanel");

            // Ctrl/Shift+Backspace clears the focused text field (see TextFieldShortcuts).
            BannerlordSceneToolkit.TextFieldShortcuts.Tick(_rootWidget);

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
