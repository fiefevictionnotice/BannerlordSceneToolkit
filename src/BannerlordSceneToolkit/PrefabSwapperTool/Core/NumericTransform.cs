using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.Core
{
    // Blender-style numeric transform: open it, type a number, watch it apply live, Enter commits
    // and Esc puts everything back.
    //
    // WHY IT IS NOT BOUND TO THE EDITOR'S OWN Z-ROTATE. MBEditor exposes no manipulation state at
    // all - no IsRotating, no gizmo axis, no drag state (its entire static surface was checked) -
    // so there is no way to know the editor is in rotate mode and switch it to numeric entry. This
    // is a parallel tool that happens to feel the same, not a hook into the native gizmo.
    //
    // THE SELECTION IS CAPTURED ONCE, AT OPEN, AND HELD.
    // This is the crux, not an optimisation. Typing a number in the editor usually DESELECTS -
    // the editor binds the number row to its own things - so a modal that re-read the selection on
    // every keystroke would watch its target evaporate the moment you typed "4". Holding the
    // GameEntity references taken at open makes that irrelevant: the editor can deselect all it
    // likes, the entities are still the entities, and the transform still lands on them.
    //
    // Every keystroke recomputes from the ORIGINAL frames rather than accumulating, so typing
    // "4" then "5" gives 4 degrees and then 45 degrees - not 4 and then 49.
    public static class NumericTransform
    {
        public enum Mode { Rotate, Translate }
        public enum Axis { X, Y, Z }

        private static readonly List<KeyValuePair<GameEntity, MatrixFrame>> Original =
            new List<KeyValuePair<GameEntity, MatrixFrame>>();

        // Parallel to Original: what the drag moved each entity by, captured at takeover
        // (zero on a cold open). Translate-Apply preserves the component perpendicular to
        // the edited axis - see BeginFromDrag.
        private static readonly List<Vec3> DragDeltas = new List<Vec3>();

        public static bool IsActive { get; private set; }
        public static Mode CurrentMode { get; private set; } = Mode.Rotate;
        public static Axis CurrentAxis { get; private set; } = Axis.Z;

        // ONE AXIS AT A TIME (2026-08-23, the two-axis saga's final spec, user's words: "the
        // numeric transform menu only needs to allow movement on 1 axis at a time. when you
        // type a transform during a 2 axis transform, it should reset the active unfinished
        // transform on the one you aren't transforming"): a typed value produces a PURE
        // single-axis move - the other axes reset to their pre-drag positions. With nothing
        // typed the entities hold the full drag, so opening the takeover and right-click reset
        // never jump; the reset happens exactly when a number is entered.
        public static string Typed { get; private set; } = "";

        public static int CapturedCount => Original.Count;

        // SLIDER RANGE for Move mode, cyclable (direct request 2026-08-23: "I literally cannot
        // type higher than 20" - the slider's clamp echoed back over the typed value; the VM
        // fixes the echo, this makes the range itself adjustable). Rotate is fixed at +/-360.
        public static float MoveSliderRange { get; private set; } = 20f;

        public static void CycleMoveRange()
        {
            MoveSliderRange = MoveSliderRange >= 500f ? 5f
                            : MoveSliderRange >= 100f ? 500f
                            : MoveSliderRange >= 50f ? 100f
                            : MoveSliderRange >= 20f ? 50f
                            : 20f;
        }

        // WORLD vs LOCAL axes (direct request 2026-08-23). World is the default and now applies
        // to BOTH modes - note rotate previously always used LOCAL axes; for the common upright
        // entity local Z == world Z, so the everyday turntable spin is unchanged.
        public static bool UseLocalAxes { get; private set; }

        public static void ToggleAxesSpace()
        {
            UseLocalAxes = !UseLocalAxes;
            Apply();
        }

        // Pointer-set comparison against the captured selection - the layer commits the modal
        // when the user genuinely selects something ELSE (direct request: selecting the next
        // entity should confirm the pending input, not ignore it).
        public static bool MatchesCaptured(List<GameEntity> selection)
        {
            if (selection == null || selection.Count != Original.Count) return false;
            foreach (var e in selection)
            {
                if (e == null) return false;
                bool found = false;
                foreach (var kv in Original)
                    if (kv.Key != null && kv.Key.Pointer == e.Pointer) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        // Set when this modal took over a shift-drag (a duplicate). Carried through to the
        // repeat recorder so Shift+R replays copy-AND-move, not just the move.
        private static bool _cameFromDuplicate;

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // Returns null on success, or the reason it could not start.
        public static string Begin()
        {
            if (IsActive) return null;

            if (!EntitySelector.HasOpenScene) return "No scene is currently open.";
            // Live read, not the cached one - the cache is only maintained while the F8 panel is
            // open, and this is a hotkey (see EntitySelector.GetLiveManualSelection).
            var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            if (selection.Count == 0) return "Nothing selected in the editor.";

            Original.Clear();
            foreach (var e in selection)
            {
                if (!Alive(e)) continue;
                try { Original.Add(new KeyValuePair<GameEntity, MatrixFrame>(e, e.GetGlobalFrame())); }
                catch (Exception ex) { Log.Warn($"[NumericTransform] could not read '{e.Name}': {ex.Message}"); }
            }
            if (Original.Count == 0) return "Nothing usable in the selection.";

            // Cold open: no drag to remember, residuals are zero (see DragDeltas).
            DragDeltas.Clear();
            for (int i = 0; i < Original.Count; i++) DragDeltas.Add(new Vec3(0f, 0f, 0f, 0f));

            Backup.BackupManager.BackupNow("before-apply");

            // Captured up front so Enter needs to do nothing but stop. Esc discards this step
            // rather than leaving a no-op that would eat the next Undo press.
            BannerlordSceneToolkit.EditUndo.CaptureFrames("Numeric transform", Original.Select(kv => kv.Key));

            Typed = "";
            IsActive = true;
            _cameFromDuplicate = false;   // a cold open is never a duplicate
            Log.Info($"[NumericTransform] begin: {Original.Count} entity(ies), mode={CurrentMode}, axis={CurrentAxis}");
            return null;
        }

        // Takeover entry point: adopts frames captured BEFORE a gizmo drag, so the number you
        // type replaces the drag rather than stacking on top of it. Mode and axis come from what
        // the drag actually did (see ManipulationWatcher) and can still be changed with R/G/XYZ.
        public static string BeginFromDrag(List<KeyValuePair<GameEntity, MatrixFrame>> snapshot, Mode mode, Axis axis)
        {
            if (IsActive) return null;
            if (snapshot == null || snapshot.Count == 0) return "Nothing captured from that drag.";

            Original.Clear();
            foreach (var kv in snapshot) if (Alive(kv.Key)) Original.Add(kv);
            if (Original.Count == 0) return "Those entities are gone.";

            // WHAT THE DRAG ACTUALLY MOVED, per entity, read BEFORE RestoreOriginals snaps
            // everything back (2026-08-23, "a two axis translate ... forgets about 1 of the 2
            // axes"): the typed number used to fully replace the drag along ONE axis, silently
            // resetting the other axis to its pre-drag position. Apply keeps the component of
            // this vector perpendicular to the edited axis, so typing refines the axis you're
            // on without undoing the rest of the drag. Zero on a cold open - behaviour there
            // is unchanged.
            DragDeltas.Clear();
            foreach (var kv in Original)
            {
                var moved = new Vec3(0f, 0f, 0f, 0f);
                try { moved = kv.Key.GetGlobalFrame().origin - kv.Value.origin; } catch { }
                DragDeltas.Add(moved);
            }
            if (DragDeltas.Count > 0)
                Log.Info($"[NumericTransform] drag vector captured: ({DragDeltas[0].x:0.##}, {DragDeltas[0].y:0.##}, {DragDeltas[0].z:0.##}) " +
                         "- the off-axis part of this survives typing. All zeros here on a takeover means the capture missed the drag.");

            Backup.BackupManager.BackupNow("before-apply");
            BannerlordSceneToolkit.EditUndo.CaptureFrames("Numeric transform", Original.Select(kv => kv.Key));

            CurrentMode = mode;
            CurrentAxis = axis;
            Typed = "";
            _cameFromDuplicate = ManipulationWatcherWasDuplicate;
            IsActive = true;

            // Puts the entities back where the drag started, so the preview begins from a clean
            // slate instead of from wherever the mouse happened to leave them.
            RestoreOriginals();

            Log.Info($"[NumericTransform] took over a drag: {Original.Count} entity(ies), {mode} {axis}");
            return null;
        }

        public static void SetMode(Mode mode) { CurrentMode = mode; Apply(); }
        public static void SetAxis(Axis axis) { CurrentAxis = axis; Apply(); }

        public static void AppendChar(char c)
        {
            // One decimal point, and a minus only at the front - anything else silently produces
            // an unparseable string and a preview that stops responding for no visible reason.
            if (c == '.' && Typed.Contains(".")) return;
            if (c == '-') { Typed = Typed.StartsWith("-") ? Typed.Substring(1) : "-" + Typed; Apply(); return; }
            if (Typed.Length > 12) return;
            Typed += c;
            Apply();
        }

        // Used by the slider. Writes the text the keyboard would have produced, so there is
        // exactly one source of truth for the value and Apply() needs no special case.
        public static void SetTypedValue(float value)
        {
            Typed = value.ToString("0.##", CultureInfo.InvariantCulture);
            Apply();
        }

        public static void Backspace()
        {
            if (Typed.Length == 0) return;
            Typed = Typed.Substring(0, Typed.Length - 1);
            Apply();
        }

        // Right-click resets. Cleared to EMPTY rather than to the string "0": empty means "no
        // number entered", so Apply() puts the entities back exactly where the drag started,
        // which is what a reset should do. Typing "0" would be a 0-degree transform of the same
        // shape but leaves the field looking like you had entered something.
        public static void ResetValue()
        {
            if (!IsActive) return;
            Typed = "";
            Apply();
            Log.Info("[NumericTransform] value reset");
        }

        // Ctrl+V. Takes the first number it can find in the clipboard rather than demanding the
        // clipboard hold nothing else - copying "37.5" out of a spreadsheet or "x: 12.25" out of
        // the editor's own transform panel should both work.
        public static string PasteValue()
        {
            if (!IsActive) return null;

            string text;
            try { text = TaleWorlds.InputSystem.Input.GetClipboardText(); }
            catch (Exception ex) { Log.Warn("[NumericTransform] clipboard read failed: " + ex.Message); return "Could not read the clipboard."; }

            if (string.IsNullOrWhiteSpace(text)) return "Clipboard is empty.";

            var match = System.Text.RegularExpressions.Regex.Match(text, @"-?\d+(\.\d+)?");
            if (!match.Success) return $"No number in the clipboard ('{Trim(text)}').";

            if (!float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return $"Could not read '{match.Value}' as a number.";

            Typed = value.ToString("0.####", CultureInfo.InvariantCulture);
            Apply();
            Log.Info($"[NumericTransform] pasted {Typed}");
            return null;
        }

        private static string Trim(string t)
        {
            t = t.Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length <= 24 ? t : t.Substring(0, 24) + "...";
        }

        public static bool TryGetValue(out float value) =>
            float.TryParse(Typed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        // Recomputes from the captured originals every time.
        public static void Apply()
        {
            if (!IsActive) return;

            // Every keystroke rewrites the frames; none of it is a user operation.
            ManipulationWatcher.SuppressSelfEdit(1.5f);

            RestoreOriginals();

            bool hasValue = TryGetValue(out var amount) && Math.Abs(amount) >= 0.0001f;
            // Rotate keeps its original all-or-nothing behaviour; translate falls through even
            // with no value so the drag's off-axis residual is re-applied (see DragDeltas).
            if (CurrentMode == Mode.Rotate && !hasValue) return;

            try
            {
                if (CurrentMode == Mode.Rotate)
                {
                    // Axes-space toggle (2026-08-23): World by default; Local uses the first
                    // entity's own basis, same convention as the grid axis pickers.
                    var axis = UseLocalAxes
                        ? (CurrentAxis == Axis.X ? PrefabDistributor.DistributionAxis.LocalX
                         : CurrentAxis == Axis.Y ? PrefabDistributor.DistributionAxis.LocalY
                         : PrefabDistributor.DistributionAxis.LocalZ)
                        : (CurrentAxis == Axis.X ? PrefabDistributor.DistributionAxis.WorldX
                         : CurrentAxis == Axis.Y ? PrefabDistributor.DistributionAxis.WorldY
                         : PrefabDistributor.DistributionAxis.WorldZ);

                    var live = Original.Where(kv => Alive(kv.Key)).Select(kv => kv.Key).ToList();
                    if (live.Count == 0) return;

                    PrefabDistributor.RotateGroup(EntitySelector.CurrentScene, live, axis, amount,
                                                  live[0].GetGlobalFrame());
                }
                else
                {
                    // ONE AXIS AT A TIME (see the Typed comment): a typed value is a PURE
                    // single-axis move from the pre-drag position - "it should reset the
                    // active unfinished transform on the one you aren't transforming". With
                    // nothing typed, hold the full drag (no jump on open or right-click reset).
                    var axisUnit = UnitForAxis(CurrentAxis);
                    var typedOffset = axisUnit * amount;

                    for (int i = 0; i < Original.Count; i++)
                    {
                        var kv = Original[i];
                        if (!Alive(kv.Key)) continue;

                        var dragged = i < DragDeltas.Count ? DragDeltas[i] : new Vec3(0f, 0f, 0f, 0f);
                        var offset = hasValue ? typedOffset : dragged;

                        var frame = kv.Value;
                        frame.origin = frame.origin + offset;
                        try
                        {
                            kv.Key.SetGlobalFrame(ref frame, true);
                            EditorFrameSync.Sync(kv.Key);
                        }
                        catch (Exception ex) { Log.Warn($"[NumericTransform] move failed on '{kv.Key.Name}': {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("[NumericTransform] apply failed: " + ex.Message); }
        }

        // Unit direction for one axis: world, or the first captured entity's own basis
        // (unit-normalized - basis length is scale in this engine, and a scaled entity must
        // not scale the move).
        private static Vec3 UnitForAxis(Axis a)
        {
            if (UseLocalAxes && Original.Count > 0)
            {
                var rot = Original[0].Value.rotation;
                var v = a == Axis.X ? rot.s : a == Axis.Y ? rot.f : rot.u;
                var len = (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
                return len > 0.0001f ? v / len : v;
            }
            return a == Axis.X ? new Vec3(1f, 0f, 0f, 0f)
                 : a == Axis.Y ? new Vec3(0f, 1f, 0f, 0f)
                 : new Vec3(0f, 0f, 1f, 0f);
        }

        private static void RestoreOriginals()
        {
            foreach (var kv in Original)
            {
                if (!Alive(kv.Key)) continue;
                var frame = kv.Value;
                try { kv.Key.SetGlobalFrame(ref frame, true); EditorFrameSync.Sync(kv.Key); }
                catch { }
            }
        }

        public static string Commit()
        {
            if (!IsActive) return "";

            var had = TryGetValue(out var amount) && Math.Abs(amount) > 0.0001f;
            var count = Original.Count;
            var mode = CurrentMode;
            var axis = CurrentAxis;

            if (!had)
            {
                // Nothing was typed - treat it as a cancel so the undo stack does not gain a step
                // that would undo nothing.
                BannerlordSceneToolkit.EditUndo.DiscardLast();
                End();
                return "Numeric transform cancelled - nothing entered.";
            }

            // Recorded for Shift+R. Rotations only: replaying a translate is safe too, but the
            // repeat feature stores one entry, and rotation is what it was built around.
            if (mode == Mode.Rotate)
            {
                var distAxis = axis == Axis.X ? PrefabDistributor.DistributionAxis.LocalX
                             : axis == Axis.Y ? PrefabDistributor.DistributionAxis.LocalY
                             : PrefabDistributor.DistributionAxis.LocalZ;
                RepeatLastTransform.RecordRotation(distAxis, amount, false, default(Vec3));
            }
            else
            {
                // A shift-drag duplicated before it moved, so replaying just the move would
                // silently drop the copy - the whole point of the gesture.
                if (_cameFromDuplicate) RepeatLastTransform.RecordCopyTranslation(axis, amount);
                else RepeatLastTransform.RecordTranslation(axis, amount);
            }

            End();
            Log.Info($"[NumericTransform] committed {mode} {axis} {amount} on {count} entity(ies)");
            return $"{mode} {axis} by {amount:0.##} applied to {count} entity(ies). Undo is available.";
        }

        public static string Cancel()
        {
            if (!IsActive) return "";
            RestoreOriginals();
            BannerlordSceneToolkit.EditUndo.DiscardLast();
            var count = Original.Count;
            End();
            Log.Info($"[NumericTransform] cancelled, {count} entity(ies) put back");
            return $"Cancelled - {count} entity(ies) put back.";
        }

        private static void End()
        {
            IsActive = false;
            Original.Clear();
            Typed = "";
        }

        // A scene switch invalidates every held reference; there is nothing safe left to restore.
        public static void Clear(string reason)
        {
            if (!IsActive) return;
            Log.Info($"[NumericTransform] dropped {Original.Count} entity(ies): {reason}");
            End();
        }

        // Read through a property so the watcher's state is sampled at exactly the moment the
        // modal adopts the drag, not later when it may already have been reset.
        private static bool ManipulationWatcherWasDuplicate => ManipulationWatcher.WasDuplicate;
    }
}
