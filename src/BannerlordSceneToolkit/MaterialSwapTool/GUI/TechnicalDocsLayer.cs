using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // TECHNICAL DOCS window (2026-08-24, "Separate User Guide vs. Technical Docs"): the same
    // accordion movie as DocumentationLayer, but built with DocumentationVM(technical: true) -
    // engineering history, confirmed-bug diagnoses and engine limitations, migrated out of the
    // User Guide so instructions stay instructions. Opened from its own button on the F9 panel.
    // Same self-healing guards as every other layer (see DocumentationLayer for the history).
    public static class TechnicalDocsLayer
    {
        private static GauntletLayer _layer;
        private static DocumentationVM _dataSource;
        private static Widget _rootWidget;

        // Slightly offset from the User Guide's spot so opening both doesn't stack them
        // perfectly; PanelDrag remembers wherever it gets dragged after that.
        private const float InitialXOffset = -(380f + 20f + 341f);
        private const float InitialYOffset = 140f;
        private static readonly PanelDrag Drag = new PanelDrag("TechnicalDocsPanel", InitialXOffset, InitialYOffset);

        public static bool IsOpen => _layer != null;

        public static bool IsFocusedOnInput
        {
            get
            {
                if (_layer == null) return false;
                try { return _layer.IsFocusedOnInput(); }
                catch (Exception ex)
                {
                    Log.Warn("TechnicalDocsLayer.IsFocusedOnInput threw (stale layer after a scene switch?): " + ex.Message);
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
                Log.Error("TechnicalDocsLayer.Open: ScreenManager.TopScreen is null.");
                return;
            }

            try
            {
                _dataSource = new DocumentationVM(Close, Drag.BeginDrag, technical: true);
                _layer = new GauntletLayer("TechnicalDocsPanel", 4018) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("DocumentationPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "TechnicalDocsPanel");
                Log.Info("TechnicalDocsLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("TechnicalDocsLayer.Open failed: " + ex);
                Close();
            }
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
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
                Log.Warn("TechnicalDocsLayer teardown issue: " + ex.Message);
            }
            _layer = null;
            _dataSource = null;
            _rootWidget = null;
        }

        public static void Tick(float dt)
        {
            if (!IsOpen) return;
            try { TickInner(dt); }
            catch (Exception ex)
            {
                Log.Warn("TechnicalDocsLayer.Tick threw (stale layer after a scene switch?): " + ex.Message);
                _layer = null;
                _dataSource = null;
                _rootWidget = null;
            }
        }

        private static void TickInner(float dt)
        {
            if (!IsOpen) return;

            Drag.Tick(_rootWidget);
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "TechnicalDocsPanel");
            BannerlordSceneToolkit.TextFieldShortcuts.Tick(_rootWidget);

            bool exit = _layer.Input.IsHotKeyReleased("Exit") || _layer.Input.IsKeyReleased(InputKey.Escape);
            if (exit) Close();
        }
    }
}
