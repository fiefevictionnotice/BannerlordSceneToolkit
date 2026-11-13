using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Fly-out behind F7's Scene Diff button: the snapshot picker. The comparison itself is
    // shown in the shared results window (DiffPreviewLayer), which opens on the left; this
    // panel sits on the right so both are readable at once.
    public static class SceneDiffLayer
    {
        private static GauntletLayer _layer;
        private static SceneDiffVM _dataSource;
        private static Widget _rootWidget;

        private const float InitialXOffset = 310f + 60f + 360f;
        private const float InitialYOffset = 60f;
        private static readonly PanelDrag Drag = new PanelDrag("SceneDiffPanel", InitialXOffset, InitialYOffset);

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
                Log.Error("SceneDiffLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new SceneDiffVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("SceneDiffPanel", 4017) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("SceneDiffPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "SceneDiffPanel");
                Log.Info("SceneDiffLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("SceneDiffLayer.Open failed: " + ex);
                Close();
            }
        }

        // A comparison still running in the background when the panel closes is simply
        // abandoned: its result lands in a view model nothing polls any more.
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
                Log.Warn("SceneDiffLayer teardown issue: " + ex.Message);
            }
            _layer = null;
            _dataSource = null;
            _rootWidget = null;
        }

        // GUARDED - a throw means the layer died under us (scene switch); drop it.
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "SceneDiffPanel");

            // Hands a finished background comparison to the results window. One reference
            // read per tick when nothing is pending - no scene or file work happens here.
            _dataSource?.Poll();
            if (!IsOpen) return;

            if (_rootWidget != null && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                BannerlordSceneToolkit.PanelFocus.DropFocusOnOutsideClick(_rootWidget);
            }

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
