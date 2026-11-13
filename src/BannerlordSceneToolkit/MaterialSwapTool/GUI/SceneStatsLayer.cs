using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Fly-out behind F7's Statistics and Lights buttons - one panel, three tabs (see
    // SceneStatsVM). Sits to the RIGHT of the Scene Analyzer; the results window (DiffPreview)
    // already owns the left side, and Go/Select here are meant to be used with the viewport
    // visible in between.
    public static class SceneStatsLayer
    {
        private static GauntletLayer _layer;
        private static SceneStatsVM _dataSource;
        private static Widget _rootWidget;

        private const float InitialXOffset = 310f + 60f + 380f;
        private const float InitialYOffset = 100f;
        private static readonly PanelDrag Drag = new PanelDrag("SceneStatsPanel", InitialXOffset, InitialYOffset);

        public static bool IsOpen => _layer != null;

        // Same self-healing guard as every other layer: after a scene switch tears the screen
        // down, the engine's IsFocusedOnInput throws on the dead layer (see SceneAnalyzerLayer).
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

        // Opens on the given tab, or just switches tab if the panel is already up - F7 has two
        // entry buttons (Statistics, Lights) for the one panel.
        public static void Open(SceneStatsTab tab)
        {
            if (IsOpen)
            {
                try { _dataSource?.ShowTab(tab); }
                catch (Exception ex) { Log.Warn("SceneStatsLayer tab switch failed: " + ex.Message); }
                return;
            }

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                Log.Error("SceneStatsLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new SceneStatsVM(Close, Drag.BeginDrag, tab);
                _layer = new GauntletLayer("SceneStatsPanel", 4019) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("SceneStatsPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "SceneStatsPanel");
                Log.Info("SceneStatsLayer opened (" + tab + ").");
            }
            catch (Exception ex)
            {
                Log.Error("SceneStatsLayer.Open failed: " + ex);
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
                Log.Warn("SceneStatsLayer teardown issue: " + ex.Message);
            }
            _layer = null;
            _dataSource = null;
            _rootWidget = null;
        }

        // GUARDED - a throw means the layer died under us (scene switch); drop it. No scene
        // work happens here: the panel's numbers only change on Refresh.
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "SceneStatsPanel");

            if (_rootWidget != null && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                BannerlordSceneToolkit.PanelFocus.DropFocusOnOutsideClick(_rootWidget);
            }

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
