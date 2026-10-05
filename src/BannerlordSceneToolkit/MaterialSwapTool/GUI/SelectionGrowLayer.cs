using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // The grow/shrink modal. Same shape as NumericTransformLayer, including its two hard-won
    // guards: IsFocusedOnInput and Tick both self-heal if the layer was torn down underneath us
    // by a scene switch, because calling into a dead GauntletLayer takes the whole editor down.
    // Layer order 4016 - the next free slot (4014 numeric transform, 4015 taken).
    public static class SelectionGrowLayer
    {
        private static GauntletLayer _layer;
        private static SelectionGrowVM _dataSource;
        private static Widget _rootWidget;

        private const float InitialXOffset = 240f;
        private const float InitialYOffset = 320f;
        private static readonly PanelDrag Drag = new PanelDrag("SelectionGrowPanel", InitialXOffset, InitialYOffset);

        public static bool IsOpen => _layer != null;

        public static bool IsFocusedOnInput
        {
            get
            {
                if (_layer == null) return false;
                try { return _layer.IsFocusedOnInput(); }
                catch (Exception ex)
                {
                    Log.Warn("SelectionGrowLayer.IsFocusedOnInput threw (stale layer after a scene switch?): " + ex.Message);
                    _layer = null;
                    return false;
                }
            }
        }

        // Returns null on success, or the reason it could not open.
        public static string OpenOrReason()
        {
            if (IsOpen) return null;

            var refusal = Core.SelectionGrow.Begin();
            if (refusal != null) return refusal;

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                Core.SelectionGrow.Cancel();
                return "No editor screen to draw on.";
            }

            try
            {
                _dataSource = new SelectionGrowVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("SelectionGrowPanel", 4016) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movie = _layer.LoadMovie("SelectionGrowPanel", _dataSource);
                _rootWidget = movie.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "SelectionGrowPanel");
                Log.Info("SelectionGrowLayer opened.");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("SelectionGrowLayer.Open failed: " + ex);
                Core.SelectionGrow.Cancel();
                Close();
                return "Could not open: " + ex.Message;
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
            catch (Exception ex) { Log.Warn("SelectionGrowLayer teardown issue: " + ex.Message); }
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
                Log.Warn("SelectionGrowLayer.Tick threw (stale layer after a scene switch?): " + ex.Message);
                _layer = null;
                _dataSource = null;
                _rootWidget = null;
            }
        }

        // Set when the tick patch already consumed the keypress that opened this panel, so the
        // same press is not read again here in the same frame and stepped twice.
        private static bool _skipKeysThisFrame;
        public static void ConsumeKeysThisFrame() => _skipKeysThisFrame = true;

        private static void TickInner(float dt)
        {
            Drag.Tick(_rootWidget);
            // Resync to the global scale only - NO scale keys on this panel. +/- are the radius
            // keys here and Ctrl is the 0.25 step modifier, so letting Ctrl+Numpad+ also mean
            // "grow the panel" made one press do both (reported 2026-10-04).
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "SelectionGrowPanel", handleKeys: false);

            // ONE sphere, at the centre of the original selection, sized to the live radius.
            // Redrawn every frame because engine debug primitives last a single frame - which is
            // also why there is nothing to clean up when this panel closes, and nothing that can
            // end up saved in the scene.
            if (Core.SelectionGrow.IsActive && Core.SelectionGrow.TryGetBaseCenter(out var center))
                BannerlordSceneToolkit.DebugDraw.Sphere(center, Core.SelectionGrow.Radius);

            if (_skipKeysThisFrame) { _skipKeysThisFrame = false; }
            else HandleKeys();

            _dataSource?.RefreshFromState();
        }

        private static readonly (InputKey key, char ch)[] Digits =
        {
            (InputKey.D0, '0'), (InputKey.D1, '1'), (InputKey.D2, '2'), (InputKey.D3, '3'), (InputKey.D4, '4'),
            (InputKey.D5, '5'), (InputKey.D6, '6'), (InputKey.D7, '7'), (InputKey.D8, '8'), (InputKey.D9, '9'),
        };

        private static void HandleKeys()
        {
            // Numpad digits are deliberately NOT typed into the radius: Numpad+/- are the step
            // keys here, and a numpad-heavy hand would otherwise turn a mis-hit into a jump to
            // some unrelated radius. The number row types an exact value.
            foreach (var d in Digits)
                if (Input.IsKeyPressed(d.key)) Core.SelectionGrow.AppendChar(d.ch);

            if (Input.IsKeyPressed(InputKey.Period)) Core.SelectionGrow.AppendChar('.');
            if (Input.IsKeyPressed(InputKey.BackSpace)) Core.SelectionGrow.Backspace();

            // THREE STEP SIZES, and the numpad pair and the arrow pair are fully
            // interchangeable - whichever keys your hand is already near do the same thing, so
            // there is no "wrong" hand position for this panel:
            //     plain    1 unit     the normal working step
            //     Shift    5 units    crossing open ground quickly
            //     Ctrl     0.25 unit  picking apart a cluttered corner
            //
            // Ctrl+Numpad+ is also what OPENS this panel, and that is deliberately not a
            // conflict: the open handler only fires while the panel is CLOSED, so keeping Ctrl
            // held after opening simply continues in fine steps rather than re-triggering.
            float step = (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)) ? 5f
                       : (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) ? 0.25f
                       : 1f;

            // Main-keyboard +/- step too (2026-08-23, same request that made them open the
            // panel): whichever pair opened it keeps working inside it.
            if (Input.IsKeyPressed(InputKey.NumpadPlus) || Input.IsKeyPressed(InputKey.Equals) ||
                Input.IsKeyPressed(InputKey.Up))
                Core.SelectionGrow.Step(step);
            if (Input.IsKeyPressed(InputKey.NumpadMinus) || Input.IsKeyPressed(InputKey.Minus) ||
                Input.IsKeyPressed(InputKey.Down))
                Core.SelectionGrow.Step(-step);

            if (Input.IsKeyPressed(InputKey.Enter) || Input.IsKeyPressed(InputKey.NumpadEnter))
            {
                var msg = Core.SelectionGrow.Commit();
                Close();
                if (!string.IsNullOrEmpty(msg)) MBEditor.AddEditorWarning(msg);
                return;
            }

            if (Input.IsKeyPressed(InputKey.Escape))
            {
                var msg = Core.SelectionGrow.Cancel();
                Close();
                if (!string.IsNullOrEmpty(msg)) MBEditor.AddEditorWarning(msg);
            }
        }
    }
}
