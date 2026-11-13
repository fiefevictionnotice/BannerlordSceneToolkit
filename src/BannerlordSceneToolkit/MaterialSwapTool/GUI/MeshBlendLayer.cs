using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Fly-out behind F8's Mesh Blend button (see MeshBlendVM). Opens to the right of the
    // Material Swap panel so the selected entity stays visible while tuning. It stays open when
    // F8 closes; it closes on Esc, on its own Close button, and on a scene switch.
    public static class MeshBlendLayer
    {
        private static GauntletLayer _layer;
        private static MeshBlendVM _dataSource;
        private static Widget _rootWidget;

        private const string PanelKey = "MeshBlendPanel";
        private const float InitialXOffset = 380f + 60f + 310f;
        private const float InitialYOffset = 60f;
        private static readonly PanelDrag Drag = new PanelDrag(PanelKey, InitialXOffset, InitialYOffset);

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

        public static void Open()
        {
            if (IsOpen) return;

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                Log.Error("MeshBlendLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new MeshBlendVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer(PanelKey, 4028) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie(PanelKey, _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, PanelKey);
                Log.Info("MeshBlendLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("MeshBlendLayer.Open failed: " + ex);
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
                Log.Warn("MeshBlendLayer teardown issue: " + ex.Message);
            }
            _layer = null;
            _dataSource = null;
            _rootWidget = null;
        }

        // GUARDED - a throw means the layer died under us (scene switch); drop it. No scene work
        // happens here: drag, panel scale keys, the clear-field shortcut, focus and Esc only.
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, PanelKey);

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
