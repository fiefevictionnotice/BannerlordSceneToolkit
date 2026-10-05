using HarmonyLib;
using MaterialSwapTool.Backup;
using MaterialSwapTool.Core;
using MaterialSwapTool.GUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Patches
{
    // MBEditor.TickSceneEditorPresentation is the real per-frame hook during plain editing -
    // confirmed empirically via live heap-dump counters (8821 hits in under 90 seconds, 100% of
    // them with IsEditModeOn true). MBEditor.TickEditMode, the originally assumed hook, never
    // fired once and has been dropped.
    [HarmonyPatch(typeof(MBEditor), nameof(MBEditor.TickSceneEditorPresentation))]
    public static class MBEditorTickScenePresentationPatch
    {
        private const InputKey ToggleKey = InputKey.F8;
        // F9 was the retired Flora Swap tool (retired 2026-08-18, only a placeholder panel left).
        // Reassigned to Backups - a live tool earns the slot more than a tombstone does. Flora
        // code stays in the tree per the "retired, not deleted" convention, just unbound.
        private const InputKey BackupToggleKey = InputKey.F9;
        private const InputKey SceneAnalyzerToggleKey = InputKey.F7;

        // Whether the once-per-session "Shift+O Isolate mode active..." hint has fired yet -
        // static, so "session" means this editor process. See IsolateHintEnabled.
        private static bool _isolateHintShown;
        private static int _recenterDiagLogged;   // first few Ctrl+0 presses log modifier state (see the recentre hotkey)
        // F6 (Prefab Swapper and Distribution) moved to its own mod, PrefabSwapperTool -
        // split off deliberately to isolate it for crash A/B diagnosis, since it's the most
        // native-call-heavy tool here (bulk entity create/destroy/reparent) and the newest,
        // least-proven code (raycast placement).

        // TickSceneEditorPresentation is the single opaque native call that drives the scene
        // editor's WASD camera movement - there's no managed hook to intercept individual keys
        // inside it. Skipping the call entirely while a text field in any of our own panels is
        // focused is the only available way to stop typing "wasd" into a color/tag/name box from
        // also dragging the camera around. This only pauses camera/viewport presentation for that
        // one frame; our own GauntletLayers' keystroke handling runs through a separate managed
        // UI input pipeline and isn't affected, and Postfix below (which ticks all the panels,
        // including drag/hotkey/backup logic) still runs regardless of whether this was skipped.
        public static bool Prefix()
        {
            return !(MaterialSwapLayer.IsFocusedOnInput
                || PresetBrowserLayer.IsFocusedOnInput
                || DocumentationLayer.IsFocusedOnInput
                || TechnicalDocsLayer.IsFocusedOnInput
                || DiffPreviewLayer.IsFocusedOnInput
                || CulturePresetGeneratorLayer.IsFocusedOnInput
                || BatchHistoryLayer.IsFocusedOnInput
                || ContinuousRecolorLayer.IsFocusedOnInput
                || CategoryEditorLayer.IsFocusedOnInput
                || PresetHistoryLayer.IsFocusedOnInput
                || CultureEditorLayer.IsFocusedOnInput
                || FloraSwapLayer.IsFocusedOnInput
                || SceneAnalyzerLayer.IsFocusedOnInput
                || InteriorWhitelistLayer.IsFocusedOnInput
                || NotificationSettingsLayer.IsFocusedOnInput
                || ShortcutSettingsLayer.IsFocusedOnInput
                || NumericTransformLayer.IsFocusedOnInput
                || SelectionGrowLayer.IsFocusedOnInput
                || BackupPanelLayer.IsFocusedOnInput);
        }

        public static void Postfix(float dt)
        {
            TickDiagnostics.TickScenePresentationHits++;
            if (!MBEditor.IsEditModeOn) return;
            TickDiagnostics.TickScenePresentationHitsWhileEditModeOn++;

            // Stutter check. Cheap - it only looks at dt - and it is what surfaces a performance
            // problem instead of leaving it to be noticed as "the editor feels bad lately".
            Core.PerformanceWatchdog.Tick(dt);

            // Ctrl+Shift+0 (number row) - pop every OPEN toolkit panel to the centre of the
            // screen. Deliberately outside the panel-focus gate below: the panels are the thing
            // being rescued, and the one you need back may be the one holding focus off-screen.
            // Shift is what separates it from Ctrl+0 (panel-size reset, which now ignores Shift).
            // 2026-10-04 live test: Ctrl+Shift+0 never arrived (no request logged; the plain
            // Ctrl+0 size reset fired instead at the same instant). Windows reserves
            // Ctrl+Shift+<digit> for input-language switching and can swallow it before the
            // game sees it. So: Ctrl+Shift+Numpad0 and Ctrl+Alt+0 are accepted as well, and
            // the first few Ctrl+0 presses log the modifier state the engine actually reports.
            {
                bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
                bool shift = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
                bool alt = Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt);
                bool zero = Input.IsKeyPressed(InputKey.D0);
                bool numZero = Input.IsKeyPressed(InputKey.Numpad0);
                if (ctrl && (zero || numZero) && _recenterDiagLogged < 5)
                {
                    _recenterDiagLogged++;
                    Log.Info($"[PanelRecenter] zero pressed: key={(zero ? "D0" : "Numpad0")} ctrl={ctrl} shift={shift} alt={alt}");
                }
                if (ctrl && ((zero && (shift || alt)) || (numZero && shift)))
                    BannerlordSceneToolkit.PanelRecenter.Request();
            }

            // Only ever needs to run right before a click resolves - selection in the editor only
            // ever changes in response to a click, so scanning on every tick in between (as this
            // used to do, unconditionally, even while the panel was closed) was a full scene
            // entity enumeration plus one native IsEntitySelected call per entity, 60 times a
            // second, for a value that was only ever going to be read at click time anyway.
            // Gating on IsKeyPressed (the single frame a click starts) cuts that from "every tick"
            // to "however often you actually click" while still refreshing strictly before this
            // frame's click gets dispatched to the panel - MaterialSwapLayer.Open() does one extra
            // scan on open so the cache isn't empty for the very first click of a session.
            // FloraSwapLayer also reads this cache for its own Manual selection mode - gating on
            // MaterialSwapLayer alone left Flora's Manual mode picking up whatever was last cached
            // while the Material Swap Tool happened to be open, which is wrong. PrefabSwapperLayer
            // refreshes its own copy of this cache directly in its Tick instead of relying on this
            // shared gate, since it's a brand new tool and there's no reason to couple it in here.
            if ((MaterialSwapLayer.IsOpen || FloraSwapLayer.IsOpen) && Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                EntitySelector.RefreshManualSelectionCache();
                // Armed recolor immediately picks up whatever was just selected - still no
                // per-tick polling, this only runs on the same click-triggered event as the
                // selection cache refresh above.
                ContinuousRecolorLayer.ReapplyIfArmed();
            }

            if (Input.IsKeyPressed(ToggleKey))
                MaterialSwapLayer.Toggle();
            if (Input.IsKeyPressed(BackupToggleKey))
                BackupPanelLayer.Toggle();
            if (Input.IsKeyPressed(SceneAnalyzerToggleKey))
                SceneAnalyzerLayer.Toggle();

            // OPTIONAL SHORTCUTS. Both are modifier combos rather than bare letters: the editor
            // owns most single keys, and a stray press on either of these acts on your selection.
            // Both are switchable from F9 -> Keyboard Shortcuts.
            //
            // (see _isolateHintShown below the handler for the once-per-session hint state)
            // Shift+O - isolate the selection / restore. Visibility only, never destructive.
            // THIRD binding in one evening, each collision confirmed live: Ctrl+Shift+I - the
            // editor's bare-I (place matching ground rotation) fires with modifiers held;
            // Ctrl+Shift+O - a native combo HID the selection instead (the exact inverse).
            // Shift+O with Ctrl explicitly NOT held avoids both. Known cost: typing a capital O
            // into a text box may trigger it - if that bites, rebind again.
            if (Core.ShortcutSettings.Current.IsolateEnabled
                && Input.IsKeyPressed(InputKey.O)
                && (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                && !(Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)))
            {
                var msg = Core.IsolationManager.Toggle();

                // First activation each session announces the exit key (see IsolateHintEnabled) -
                // to the editor overlay AND the log, per direct request.
                if (Core.IsolationManager.IsActive && Core.ShortcutSettings.Current.IsolateHintEnabled && !_isolateHintShown)
                {
                    _isolateHintShown = true;
                    const string hint = "Shift+O Isolate mode active, Shift+O to reverse";
                    MBEditor.AddEditorWarning(hint);
                    Log.Warn(hint);
                }
                MBEditor.AddEditorWarning(msg);
            }

            // Ctrl+Shift+P - promote the selection to its top-level prefab roots. Composite
            // prefabs keep their visible meshes on CHILD entities, so a viewport click routinely
            // selects a part when the whole prefab is what a swap/isolate/transform wants.
            // Selection-only - nothing in the scene is modified - and applied through the same
            // deferred queue every select-in-editor path uses.
            if (Core.ShortcutSettings.Current.SelectRootEnabled
                && Input.IsKeyPressed(InputKey.P)
                && (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                && (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)))
            {
                var selection = Core.SelectionMemory.GetSelection();
                if (selection.Count == 0)
                    MBEditor.AddEditorWarning("Select Root: nothing selected.");
                else
                {
                    var roots = Core.EntitySelector.PromoteToRoots(selection);
                    Core.LiveSceneChecks.SetEditorSelection(roots);
                    MBEditor.AddEditorWarning(
                        $"Selected {roots.Count} top-level prefab(s) from {selection.Count} selected part(s).");
                    Log.Info($"[SelectRoot] {selection.Count} selected -> {roots.Count} root(s).");
                }
            }

            // Ctrl+Numpad+ - grow the selection by proximity, in a live modal (see
            // SelectionGrow). Ctrl+Numpad- is deliberately NOT a second entry point: the radius
            // is measured from the selection captured when the modal opens, so "shrink" only
            // means anything once there is a radius to reduce. Pressing it cold says so rather
            // than silently doing nothing.
            if (Core.ShortcutSettings.Current.SelectionGrowEnabled
                && (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                && !SelectionGrowLayer.IsOpen)
            {
                // NUMPAD ONLY (2026-10-04). The main-row Ctrl+Equals alias (added 2026-08-23)
                // was the same key as "enlarge the panel" from the 09-13 scaling pass, so which
                // one fired depended on whether a panel had focus. Split: Grow owns the numpad,
                // panel scaling owns the number row. Inside the open panel both rows still step
                // the radius - only the OPENER is numpad-only.
                if (Input.IsKeyPressed(InputKey.NumpadPlus))
                {
                    var refusal = SelectionGrowLayer.OpenOrReason();
                    if (refusal != null) MBEditor.AddEditorWarning(refusal);
                    else SelectionGrowLayer.ConsumeKeysThisFrame();
                }
                else if (Input.IsKeyPressed(InputKey.NumpadMinus))
                {
                    MBEditor.AddEditorWarning("Nothing to shrink yet - Ctrl and + grows the selection first, then - shrinks it back.");
                }
            }

            // Shift+R - replay the last drag onto the CURRENT selection. This is the one
            // that can quietly transform the wrong things, so it captures undo before acting.
            // DIAGNOSTIC LEVEL 2 (2026-08-23, "Shift+R doesn't work at all now" with ZERO
            // handler-fired lines in the log): logs EVERY R press this patch sees, with the
            // exact modifier and setting state, so a dead Shift+R self-reports as one of:
            // no line at all (the key never reaches the patch - something upstream eats it),
            // shift=False (modifier state misread), or repeatEnabled=False (setting off).
            if (Input.IsKeyPressed(InputKey.R))
                Log.Info($"[RepeatLast] R pressed: shift={Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)}, " +
                         $"ctrl={Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)}, " +
                         $"repeatEnabled={Core.ShortcutSettings.Current.RepeatLastEnabled}");

            if (Core.ShortcutSettings.Current.RepeatLastEnabled
                && Input.IsKeyPressed(InputKey.R)
                && (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                && !Input.IsKeyDown(InputKey.LeftControl) && !Input.IsKeyDown(InputKey.RightControl))
            {
                // DIAGNOSTIC (2026-08-23, "shift+r doesn't work on copies at all"): the user's
                // test presses left ZERO log traces, so it is not yet known whether the combo
                // even reaches this handler or Repeat() bails at an early check. Logging both
                // the fire and the outcome makes the next failing press self-diagnosing.
                Log.Info("[RepeatLast] Shift+R handler fired.");
                var msg = PrefabSwapperTool.Core.RepeatLastTransform.Repeat();
                Log.Info("[RepeatLast] Shift+R -> " + msg);
                MBEditor.AddEditorWarning(msg);
            }

            // Ctrl+Shift+T - numeric transform. Type a number, watch it apply, Enter or Esc.
            // It captures the selection on open precisely because typing digits in the editor
            // usually deselects; see NumericTransform.
            if (Core.ShortcutSettings.Current.NumericTransformEnabled
                && Input.IsKeyPressed(InputKey.T)
                && (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                && (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)))
            {
                var refusal = NumericTransformLayer.OpenOrReason();
                if (refusal != null) MBEditor.AddEditorWarning(refusal);
            }

            // Before the panels tick: a queued selection must land on a frame where the editor
            // is not still digesting the click that queued it (see DeferredSelection).
            DeferredSelection.Tick();

            // Watches for a gizmo drag so typing a number right after one can take it over.
            // The editor exposes no manipulation state, so this infers it from selected entities'
            // frames changing while the mouse is down - see ManipulationWatcher.
            if (Core.ShortcutSettings.Current.NumericTransformEnabled)
            {
                PrefabSwapperTool.Core.ManipulationWatcher.Tick(dt);

                // The readout is created lazily and only while the feature is on. The close-out
                // for the disabled case lives in the else branch BELOW this block - it used to
                // sit here as a same-condition else that could never run, which left the HUD
                // stuck on screen if the feature was switched off in F9 while it was visible.
                LastOperationHudLayer.EnsureOpen();
                LastOperationHudLayer.Tick(dt);

                // Left-click dismisses the readout: it already means "I am selecting something
                // else now", which is the same moment the last operation stops mattering.
                TickLeftClickDismiss();

                // Standing diagnostic: if typing a digit ever fails to take over, this line
                // says whether the key was even seen and what the watcher thought at the time.
                // Grep tool.log for [TakeoverDiag].
                if (!NumericTransformLayer.IsOpen)
                {
                    var probe = FirstDigitPressed();
                    if (probe.HasValue && !PrefabSwapperTool.Core.ManipulationWatcher.CanTakeOver)
                        Log.Info($"[TakeoverDiag] digit '{probe.Value}' seen but no drag to take over " +
                                 $"(snapshot={PrefabSwapperTool.Core.ManipulationWatcher.SnapshotCount}, " +
                                 $"sawMovement={PrefabSwapperTool.Core.ManipulationWatcher.SawMovement}, " +
                                 $"sinceRelease={PrefabSwapperTool.Core.ManipulationWatcher.SecondsSinceRelease:0.0}s).");
                }

                // A bare digit takes over the drag - no modifier, exactly like Blender. Only
                // while a drag is live or just finished, so digits are otherwise left alone.
                if (!NumericTransformLayer.IsOpen && PrefabSwapperTool.Core.ManipulationWatcher.CanTakeOver)
                {
                    var digit = FirstDigitPressed();
                    if (digit.HasValue)
                    {
                        var refusal = NumericTransformLayer.OpenFromDrag();
                        if (refusal == null)
                        {
                            PrefabSwapperTool.Core.NumericTransform.AppendChar(digit.Value);
                            NumericTransformLayer.ConsumeKeysThisFrame();
                        }
                        else
                            MBEditor.AddEditorWarning(refusal);
                    }
                }
            }
            else
            {
                // Feature switched off (possibly mid-session, with the readout visible): tear
                // the HUD down and keep ticking it so the teardown actually completes.
                LastOperationHudLayer.Close();
                LastOperationHudLayer.Tick(dt);
            }

            MaterialSwapLayer.Tick(dt);
            PresetBrowserLayer.Tick(dt);
            DocumentationLayer.Tick(dt);
            TechnicalDocsLayer.Tick(dt);
            DiffPreviewLayer.Tick(dt);
            CulturePresetGeneratorLayer.Tick(dt);
            BatchHistoryLayer.Tick(dt);
            ContinuousRecolorLayer.Tick(dt);
            CategoryEditorLayer.Tick(dt);
            PresetHistoryLayer.Tick(dt);
            CultureEditorLayer.Tick(dt);
            FloraSwapLayer.Tick(dt);
            BackupPanelLayer.Tick(dt);
            SceneAnalyzerLayer.Tick(dt);
            InteriorWhitelistLayer.Tick(dt);
            NotificationSettingsLayer.Tick(dt);
            ShortcutSettingsLayer.Tick(dt);
            NumericTransformLayer.Tick(dt);
            SelectionGrowLayer.Tick(dt);
            BackupManager.Tick(dt);
            Core.IsolationManager.Tick();   // deferred tag recovery + save-while-isolated warning
        }

        // The digit that started a numeric takeover, or null. Numpad included - a number pad is
        // the natural thing to reach for when typing a measurement.
        private static char? FirstDigitPressed()
        {
            if (Input.IsKeyPressed(InputKey.D0) || Input.IsKeyPressed(InputKey.Numpad0)) return '0';
            if (Input.IsKeyPressed(InputKey.D1) || Input.IsKeyPressed(InputKey.Numpad1)) return '1';
            if (Input.IsKeyPressed(InputKey.D2) || Input.IsKeyPressed(InputKey.Numpad2)) return '2';
            if (Input.IsKeyPressed(InputKey.D3) || Input.IsKeyPressed(InputKey.Numpad3)) return '3';
            if (Input.IsKeyPressed(InputKey.D4) || Input.IsKeyPressed(InputKey.Numpad4)) return '4';
            if (Input.IsKeyPressed(InputKey.D5) || Input.IsKeyPressed(InputKey.Numpad5)) return '5';
            if (Input.IsKeyPressed(InputKey.D6) || Input.IsKeyPressed(InputKey.Numpad6)) return '6';
            if (Input.IsKeyPressed(InputKey.D7) || Input.IsKeyPressed(InputKey.Numpad7)) return '7';
            if (Input.IsKeyPressed(InputKey.D8) || Input.IsKeyPressed(InputKey.Numpad8)) return '8';
            if (Input.IsKeyPressed(InputKey.D9) || Input.IsKeyPressed(InputKey.Numpad9)) return '9';
            if (Input.IsKeyPressed(InputKey.Minus)) return '-';
            return null;
        }

        // LEFT-click dismisses the readout. Left-click already deselects or changes what is
        // selected, so it is the obvious "I am done with that" gesture - and unlike right
        // click it is not the camera. Nothing is consumed: selecting still works normally.
        //
        // Not suppressed while the numeric panel is open, because a click inside that panel
        // never reaches here as a viewport click and a click outside it confirms anyway.
        private static void TickLeftClickDismiss()
        {
            if (!Input.IsKeyPressed(InputKey.LeftMouseButton)) return;
            PrefabSwapperTool.Core.ManipulationWatcher.DismissLastOperation();
        }
    }
}
