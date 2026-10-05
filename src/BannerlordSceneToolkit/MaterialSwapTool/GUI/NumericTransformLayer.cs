using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace MaterialSwapTool.GUI
{
    // Opened from the F9 Backup panel's "Keyboard Shortcuts" button. Own layer above
    // BackupPanel so it draws on top of the panel that opened it.
    public static class NumericTransformLayer
    {
        private static GauntletLayer _layer;
        private static NumericTransformVM _dataSource;
        private static Widget _rootWidget;

        // Seconds remaining in which a selection change may commit the modal - opened only by a
        // click OUTSIDE the panel (see the commit-on-selection-change gate in TickInner).
        private static float _outsideClickWindow;

        // Offset down-right of the Backup panel so both stay readable at once.
        private const float InitialXOffset = 200f;
        private const float InitialYOffset = 280f;
        private static readonly PanelDrag Drag = new PanelDrag("NumericTransformPanel", InitialXOffset, InitialYOffset);

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

        public static string OpenOrReason()
        {
            if (IsOpen) return null;

            // Begin captures the selection up front. If it refuses, do not open an empty modal.
            var refusal = PrefabSwapperTool.Core.NumericTransform.Begin();
            if (refusal != null) return refusal;
            return OpenShell();
        }

        // Opened by typing a digit right after a gizmo drag - see ManipulationWatcher.
        public static string OpenFromDrag()
        {
            if (IsOpen) return null;

            var snapshot = PrefabSwapperTool.Core.ManipulationWatcher.TakeSnapshot();
            var refusal = PrefabSwapperTool.Core.NumericTransform.BeginFromDrag(
                snapshot,
                PrefabSwapperTool.Core.ManipulationWatcher.DetectedMode,
                PrefabSwapperTool.Core.ManipulationWatcher.DetectedAxis);
            if (refusal != null) return refusal;
            return OpenShell();
        }

        private static string OpenShell()
        {

            var screen = ScreenManager.TopScreen;
            if (screen == null)
            {
                Log.Error("NumericTransformLayer.Open: ScreenManager.TopScreen is null.");
                PrefabSwapperTool.Core.NumericTransform.Cancel();
                return "No editor screen to draw on.";
            }

            try
            {
                _dataSource = new NumericTransformVM(Close, Drag.BeginDrag);
                _layer = new GauntletLayer("NumericTransformPanel", 4014) { IsFocusLayer = true };
                BannerlordSceneToolkit.ToolkitCursor.ApplyInputRestrictions(_layer);
                _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
                var movieIdentifier = _layer.LoadMovie("NumericTransformPanel", _dataSource);
                _rootWidget = movieIdentifier.Movie.RootWidget;
                Drag.ApplyInitialPosition(_rootWidget);
                screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                MaterialSwapTool.GUI.PanelScale.Apply(_layer, "NumericTransformPanel");
                Log.Info("NumericTransformLayer opened.");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("NumericTransformLayer.Open failed: " + ex);
                PrefabSwapperTool.Core.NumericTransform.Cancel();
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
            catch (Exception ex)
            {
                Log.Warn("NumericTransformLayer teardown issue: " + ex.Message);
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
            MaterialSwapTool.GUI.PanelScale.HandleHotkeys(_layer, "NumericTransformPanel");

            // Ctrl/Shift+Backspace clears the focused text field (see TextFieldShortcuts).
            BannerlordSceneToolkit.TextFieldShortcuts.Tick(_rootWidget);

            if (_rootWidget != null && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                BannerlordSceneToolkit.PanelFocus.DropFocusOnOutsideClick(_rootWidget);
            }

            HandleKeys();

            // COMMIT ON SELECTION CHANGE (2026-08-23, direct request: "the window doesn't
            // close/confirm the last input when I select the next entity"). The editor clears
            // the selection on most keypresses (which is why the captured set exists), so an
            // EMPTY live selection means nothing - only a NON-EMPTY selection that differs from
            // the captured set is the user genuinely clicking their next target, and that click
            // now applies the pending value and closes, exactly like Enter.
            //
            // GATED ON AN OUTSIDE CLICK (2026-08-23 follow-up, "releasing the slider immediately
            // applies"): clicks ON our panels leak through to the editor underneath (the
            // documented click-through hazard), so a slider drag's release also selected
            // whatever entity sat BEHIND the panel - which this check then read as "the user
            // picked their next target" and applied+closed. Only a click that lands OUTSIDE the
            // panel opens a short window in which a selection change commits.
            bool mouseInsidePanel = false;
            if (_rootWidget != null)
            {
                var mouse = _rootWidget.EventManager.MousePosition;
                mouseInsidePanel = _rootWidget.AreaRect.IsPointInside(in mouse);
            }
            if (Input.IsKeyPressed(InputKey.LeftMouseButton) && !mouseInsidePanel)
                _outsideClickWindow = 0.6f;

            // WAIT FOR THE RELEASE (2026-08-23 follow-up, "specifically when you click and drag
            // ... a box selection"): a marquee/box selection is a DRAG, and evaluating per tick
            // committed the instant the box first touched ANY entity - mid-drag, against a
            // half-made selection. While the button is held the timer freezes and no commit is
            // evaluated; on release, the FINISHED selection is what commits.
            bool lmbDown = Input.IsKeyDown(InputKey.LeftMouseButton);
            if (_outsideClickWindow > 0f && !lmbDown)
            {
                _outsideClickWindow -= dt;
                var liveSelection = Core.EntitySelector.GetLiveManualSelection();
                if (liveSelection.Count > 0 && !PrefabSwapperTool.Core.NumericTransform.MatchesCaptured(liveSelection))
                {
                    var commitMsg = PrefabSwapperTool.Core.NumericTransform.Commit();
                    Close();
                    if (!string.IsNullOrEmpty(commitMsg)) MBEditor.AddEditorWarning("Selection changed - " + commitMsg);
                    return;
                }
            }

            _dataSource?.RefreshFromState();
        }

        // Polled here rather than bound to widgets: the whole point of this modal is to read a
        // number as you type it, and Gauntlet gives no keypress event for arbitrary keys.
        //
        // The editor also sees these keys - a focused layer does not reliably stop that in plain
        // edit mode - which is exactly why NumericTransform captured the selection at open. The
        // editor deselecting on a number press no longer matters.
        private static readonly (InputKey key, char ch)[] Digits =
        {
            (InputKey.D0, '0'), (InputKey.D1, '1'), (InputKey.D2, '2'), (InputKey.D3, '3'), (InputKey.D4, '4'),
            (InputKey.D5, '5'), (InputKey.D6, '6'), (InputKey.D7, '7'), (InputKey.D8, '8'), (InputKey.D9, '9'),
            (InputKey.Numpad0, '0'), (InputKey.Numpad1, '1'), (InputKey.Numpad2, '2'), (InputKey.Numpad3, '3'),
            (InputKey.Numpad4, '4'), (InputKey.Numpad5, '5'), (InputKey.Numpad6, '6'), (InputKey.Numpad7, '7'),
            (InputKey.Numpad8, '8'), (InputKey.Numpad9, '9'),
        };

        // Set when the tick patch already consumed this frame's digit to open the modal.
        // Without it that same keypress is read a second time here, in the same frame, and
        // the digit lands twice.
        private static bool _skipKeysThisFrame;

        public static void ConsumeKeysThisFrame() => _skipKeysThisFrame = true;

        private static void HandleKeys()
        {
            if (_skipKeysThisFrame) { _skipKeysThisFrame = false; return; }

            var nt = typeof(PrefabSwapperTool.Core.NumericTransform);

            foreach (var d in Digits)
                if (Input.IsKeyPressed(d.key)) PrefabSwapperTool.Core.NumericTransform.AppendChar(d.ch);

            if (Input.IsKeyPressed(InputKey.Period)) PrefabSwapperTool.Core.NumericTransform.AppendChar('.');
            if (Input.IsKeyPressed(InputKey.Minus)) PrefabSwapperTool.Core.NumericTransform.AppendChar('-');
            if (Input.IsKeyPressed(InputKey.BackSpace)) PrefabSwapperTool.Core.NumericTransform.Backspace();

            // Right-click clears the number. Safe to claim while this modal is open - the
            // editor's own right-drag camera look is not something you want mid-entry anyway.
            if (Input.IsKeyPressed(InputKey.RightMouseButton))
                PrefabSwapperTool.Core.NumericTransform.ResetValue();

            // Ctrl+V pastes the first number found in the clipboard.
            if (Input.IsKeyPressed(InputKey.V)
                && (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)))
            {
                var problem = PrefabSwapperTool.Core.NumericTransform.PasteValue();
                if (problem != null) MBEditor.AddEditorWarning(problem);
            }

            if (Input.IsKeyPressed(InputKey.R)) PrefabSwapperTool.Core.NumericTransform.SetMode(PrefabSwapperTool.Core.NumericTransform.Mode.Rotate);
            if (Input.IsKeyPressed(InputKey.G)) PrefabSwapperTool.Core.NumericTransform.SetMode(PrefabSwapperTool.Core.NumericTransform.Mode.Translate);

            // L toggles World/Local axes (2026-08-23) - mirrors the panel button.
            if (Input.IsKeyPressed(InputKey.L)) PrefabSwapperTool.Core.NumericTransform.ToggleAxesSpace();

            if (Input.IsKeyPressed(InputKey.X)) PrefabSwapperTool.Core.NumericTransform.SetAxis(PrefabSwapperTool.Core.NumericTransform.Axis.X);
            if (Input.IsKeyPressed(InputKey.Y)) PrefabSwapperTool.Core.NumericTransform.SetAxis(PrefabSwapperTool.Core.NumericTransform.Axis.Y);
            // C also selects Y (2026-08-23, direct request): the editor's own rotate gizmo keys
            // are Z/X/C, so C is where muscle memory reaches for the third axis - both work.
            if (Input.IsKeyPressed(InputKey.C)) PrefabSwapperTool.Core.NumericTransform.SetAxis(PrefabSwapperTool.Core.NumericTransform.Axis.Y);
            if (Input.IsKeyPressed(InputKey.Z)) PrefabSwapperTool.Core.NumericTransform.SetAxis(PrefabSwapperTool.Core.NumericTransform.Axis.Z);

            if (Input.IsKeyPressed(InputKey.Enter) || Input.IsKeyPressed(InputKey.NumpadEnter))
            {
                var msg = PrefabSwapperTool.Core.NumericTransform.Commit();
                Close();
                if (!string.IsNullOrEmpty(msg)) MBEditor.AddEditorWarning(msg);
                return;
            }

            if (Input.IsKeyPressed(InputKey.Escape))
            {
                var msg = PrefabSwapperTool.Core.NumericTransform.Cancel();
                Close();
                if (!string.IsNullOrEmpty(msg)) MBEditor.AddEditorWarning(msg);
            }
        }
    }
}
