using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Always-on readout of the last transform, bottom-centre.
    //
    // NOT A FOCUS LAYER, and that is the whole design constraint. Every other panel here takes
    // input focus, which is fine for something you deliberately opened - but this thing is up all
    // the time, and a permanently-focused layer would quietly break clicking in the viewport. It
    // is created without IsFocusLayer and never registers input restrictions or hotkeys, so it
    // draws and nothing more.
    //
    // It also hides itself when there is nothing to report, so it is invisible until you actually
    // move something.
    public static class LastOperationHudLayer
    {
        private static GauntletLayer _layer;
        private static LastOperationHudVM _dataSource;

        public static bool IsOpen => _layer != null;

        private static ScreenBase _screenItWasAddedTo;

        public static void EnsureOpen()
        {
            var screen = ScreenManager.TopScreen;
            if (screen == null) return;

            // NEVER create this while no scene is open. This is the only layer in the toolkit
            // that re-opens itself every tick, which makes it uniquely exposed to the window in
            // which the editor screen is being torn down - closing a scene runs HandleDeactivate,
            // Scene_view::clear_all and HandleFinalize across several frames, and adding a layer
            // to a screen inside that window hands the engine a widget belonging to something it
            // is in the middle of destroying.
            if (!Core.EntitySelector.HasOpenScene) { Close(); return; }

            // Re-created when the SCREEN changes, not only when _layer is null. A scene switch
            // replaces the screen and takes the layer with it while this static still points
            // at the old one - so the readout appeared once and then never again, because
            // IsOpen kept insisting it was already there.
            if (IsOpen && ReferenceEquals(_screenItWasAddedTo, screen)) return;
            if (IsOpen) Close();

            try
            {
                _dataSource = new LastOperationHudVM();
                _layer = new GauntletLayer("LastOperationHudPanel", 4015);
                _layer.LoadMovie("LastOperationHudPanel", _dataSource);
                screen.AddLayer(_layer);
                _screenItWasAddedTo = screen;
                Log.Info("LastOperationHudLayer opened.");
            }
            catch (Exception ex)
            {
                Log.Error("LastOperationHudLayer.EnsureOpen failed: " + ex);
                Close();
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;

            // REMOVED FROM THE SCREEN THAT ACTUALLY OWNS IT, not from whatever is on top now.
            // This used to call ScreenManager.TopScreen.RemoveLayer(_layer) - but the moment
            // Close() matters most is a scene change or a scene being closed, which is precisely
            // when TopScreen is NOT the screen this layer was added to. The removal then silently
            // did nothing (wrong screen) and the old, finalizing screen was left holding a layer
            // whose managed side we had already dropped. _screenItWasAddedTo was recorded for
            // exactly this and was never used.
            var owner = _screenItWasAddedTo ?? ScreenManager.TopScreen;
            try { owner?.RemoveLayer(_layer); }
            catch (Exception ex) { Log.Warn("LastOperationHudLayer teardown issue: " + ex.Message); }
            _screenItWasAddedTo = null;
            _layer = null;
            _dataSource = null;
        }

        // Guarded like every other layer tick: a scene switch tears the screen stack down while
        // this static still points at it, and an unguarded throw here would take the editor down.
        public static void Tick(float dt)
        {
            if (!IsOpen) return;
            try { _dataSource?.Refresh(); }
            catch (Exception ex)
            {
                Log.Warn("LastOperationHud tick threw (stale layer after a scene switch?): " + ex.Message);
                _layer = null;
                _dataSource = null;
            }
        }
    }
}
