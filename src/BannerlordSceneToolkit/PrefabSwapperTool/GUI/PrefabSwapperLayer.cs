using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace PrefabSwapperTool.GUI
{
    // F6-bound, entirely separate identity from Material Swap Tool (F9), Flora Swap Tool (F8), and
    // Scene Analyzer (F7) - general-purpose "swap this entity/prefab for a different one" tool,
    // the unspecialized version of Scene Analyzer's curated BrokenPrefabFixer.
    public static class PrefabSwapperLayer
    {
        private static GauntletLayer _layer;
        private static PrefabSwapperVM _dataSource;
        private static Widget _rootWidget;

        private const float InitialYOffset = 100f;
        private static readonly PanelDrag Drag = new PanelDrag("PrefabSwapperPanel", 0f, InitialYOffset);

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
                Log.Error("PrefabSwapperLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new PrefabSwapperVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("PrefabSwapperPanel", 4012) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("PrefabSwapperPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "PrefabSwapperPanel");
                Core.EntitySelector.RefreshManualSelectionCache();
                Log.Info("PrefabSwapperLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("PrefabSwapperLayer.Open failed: " + ex);
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
                Log.Warn("PrefabSwapperLayer teardown issue: " + ex.Message);
            }
            _layer = null;
            _dataSource = null;
            _rootWidget = null;

            // Opened FROM this panel - no reason for it to keep floating around once the parent's
            // gone, same as PrefabCreatorLayer closing its own Texture Set/Pairing flyouts.
            PrefabHistoryLayer.Close();
            SwapSetBrowserLayer.Close();
            TextureSetBrowserLayer.Close();
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "PrefabSwapperPanel");

            // Ctrl/Shift+Backspace clears the focused text field (see TextFieldShortcuts).
            BannerlordSceneToolkit.TextFieldShortcuts.Tick(_rootWidget);

            if (Input.IsKeyPressed(InputKey.LeftMouseButton))
                Core.EntitySelector.RefreshManualSelectionCache();

            if (_rootWidget != null && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                BannerlordSceneToolkit.PanelFocus.DropFocusOnOutsideClick(_rootWidget);
            }

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
