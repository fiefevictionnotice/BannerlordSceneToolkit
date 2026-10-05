using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.Core
{
    // "Do that again to what I have selected now" - Blender's Shift+R.
    //
    // WHY THIS IS THE RISKY ONE, AND WHAT IS DONE ABOUT IT. Every other shortcut acts on something
    // you can see; this one replays an action you may have taken minutes ago, onto a selection
    // that is by definition different from the one it was recorded against. Get the selection
    // wrong and it silently transforms the wrong objects. Three mitigations:
    //
    //   1. It is off unless enabled, and switchable from F9 -> Keyboard Shortcuts.
    //   2. The binding is a modifier combo (Shift+R, with Ctrl excluded), not a bare letter, so it is not
    //      something a stray hand lands on.
    //   3. It captures undo BEFORE acting, so a wrong repeat is one Undo away rather than
    //      something to hunt down by eye.
    //
    // Only ROTATION is recorded. Mirror and Distribute create entities, and "repeat" for those
    // would mean making more copies - almost never what is wanted, and destructive-by-accretion
    // in a way rotation is not. Recording only what is safe to replay beats recording everything
    // and hoping.
    public static class RepeatLastTransform
    {
        public enum Kind { Rotation, Translation, CopyTranslation }

        private class Recorded
        {
            public Kind What;
            public NumericTransform.Axis MoveAxis;
            // A gizmo drag moves on all three axes at once, so the full delta is kept.
            // Axis+amount is only used by the numeric modal, which really is single-axis.
            public Vec3 MoveDelta;
            public bool HasVector;
            public string Description;
            public PrefabDistributor.DistributionAxis Axis;
            public float AngleDegrees;
            public bool UsedCustomPivot;
            public Vec3 CustomPivot;
        }

        private static Recorded _last;

        public static bool HasRecorded => _last != null;

        public static string LastDescription => _last?.Description;

        public static void RecordRotation(PrefabDistributor.DistributionAxis axis, float angleDegrees,
                                          bool usedCustomPivot, Vec3 customPivot)
        {
            _last = new Recorded
            {
                What = Kind.Rotation,
                Description = $"Rotate {angleDegrees:0.##} deg",
                Axis = axis,
                AngleDegrees = angleDegrees,
                UsedCustomPivot = usedCustomPivot,
                CustomPivot = customPivot,
            };
            Log.Info($"[RepeatLast] recorded '{_last.Description}'");
        }

        // Translation is repeatable for the same reason rotation is: it moves existing
        // entities and creates nothing, so replaying it onto a different selection cannot
        // accrete geometry the way repeating a Mirror or Distribute would. AngleDegrees
        // carries the distance here - one numeric field, two meanings, decided by What.
        public static void RecordTranslation(NumericTransform.Axis axis, float amount)
        {
            _last = new Recorded
            {
                What = Kind.Translation,
                MoveAxis = axis,
                Description = $"Move {axis} by {amount:0.##}",
                AngleDegrees = amount,
            };
            Log.Info($"[RepeatLast] recorded '{_last.Description}'");
        }

        // Shift-drag duplicates and moves in one gesture, so repeating it has to do both.
        // Feasible only because EntityCloner copies the per-instance material and colour
        // overrides too - a copy that silently reverted to the bare prefab would look plausible
        // and be wrong.
        public static void RecordCopyTranslation(NumericTransform.Axis axis, float amount)
        {
            _last = new Recorded
            {
                What = Kind.CopyTranslation,
                MoveAxis = axis,
                Description = $"Copy + move {axis} by {amount:0.##}",
                AngleDegrees = amount,
            };
            Log.Info($"[RepeatLast] recorded '{_last.Description}'");
        }

        // MICRO-MOVES DO NOT OVERWRITE THE RECIPE (2026-08-23, "shift+r of copies is broken
        // again"): clicking an entity to select it drifts the mouse a few pixels, the editor
        // treats that as a drag, and the watcher recorded it - a stream of sub-half-metre
        // "Move (0.22, -0.02, 0)" entries clobbering the Copy+move recipe the user actually
        // wanted to replay, one per selection click (confirmed in tool.log, 00:53). A move this
        // small is essentially never the thing someone wants to repeat; a deliberate tiny shim
        // is what Numeric Transform (Ctrl+Shift+T) is for. Copies are never skipped - a
        // shift-drag is always deliberate.
        private const float MinRecordableMoveDistance = 0.5f;

        public static void RecordTranslationVector(Vec3 delta)
        {
            var distance = (float)Math.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
            if (distance < MinRecordableMoveDistance)
            {
                Log.Info($"[RepeatLast] ignored micro-move ({delta.x:0.##}, {delta.y:0.##}, {delta.z:0.##}) - " +
                         $"under {MinRecordableMoveDistance}m, keeping '{_last?.Description ?? "nothing"}'.");
                return;
            }

            _last = new Recorded
            {
                What = Kind.Translation,
                MoveDelta = delta,
                HasVector = true,
                Description = $"Move ({delta.x:0.##}, {delta.y:0.##}, {delta.z:0.##})",
            };
            Log.Info($"[RepeatLast] recorded '{_last.Description}'");
        }

        public static void RecordCopyTranslationVector(Vec3 delta)
        {
            _last = new Recorded
            {
                What = Kind.CopyTranslation,
                MoveDelta = delta,
                HasVector = true,
                Description = $"Copy + move ({delta.x:0.##}, {delta.y:0.##}, {delta.z:0.##})",
            };
            Log.Info($"[RepeatLast] recorded '{_last.Description}'");
        }

        // Copies made by the previous repeat - the reliable half of "the copies become the
        // selection" (see the chaining fix in Repeat). Pointer-validated before use, so a
        // deleted/scene-switched copy simply drops out.
        private static List<GameEntity> _lastMadeCopies;

        // The ORIGINAL entities a copy chain started from, plus how many steps the chain has
        // taken - press N clones these at N times the offset, so no clone is ever made from
        // another clone (see the never-clone-a-clone note in Repeat).
        private static List<GameEntity> _chainSources;
        private static int _chainStep;

        public static string Repeat()
        {
            // A drag that JUST ended may not have settled into a recipe yet - close that race
            // before reading _last (see ManipulationWatcher.ForceSettlePending).
            ManipulationWatcher.ForceSettlePending();

            if (_last == null) return "Nothing to repeat - rotate something first.";
            if (!EntitySelector.HasOpenScene) return "No scene is currently open.";

            var selection = MaterialSwapTool.Core.SelectionMemory.GetSelection();
            Log.Info($"[RepeatLast] repeat check: selection={selection.Count}, lastMade={_lastMadeCopies?.Count ?? 0}, last='{_last.Description}'");

            // CHAINING FIX (2026-08-22, "loses selection after one copy"): a repeat SELECTS its
            // new copies so the next press steps along - but that selection is applied on a
            // 2-tick deferred queue tuned for mouse clicks, and the R KEY'S RELEASE lands several
            // frames later and clears the editor selection again AFTER the deferred apply ran.
            // Whether the copies stay selected is therefore a race against how long the key was
            // held. So chaining no longer depends on the editor at all: the copies from the
            // previous repeat are remembered HERE, and an empty selection falls back to them.
            if (selection.Count == 0 && _lastMadeCopies != null)
            {
                var alive = _lastMadeCopies.Where(e => e != null && e.Pointer != UIntPtr.Zero).ToList();
                if (alive.Count > 0) selection = alive;
            }
            if (selection.Count == 0) return "Nothing selected - select what to repeat it on.";

            try
            {
                // We are about to move things ourselves - do not let the watcher record it as a
                // user operation and overwrite the very recipe being replayed.
                ManipulationWatcher.SuppressSelfEdit();

                Backup.BackupManager.BackupNow("before-apply");

                // Before, not after: a repeat aimed at the wrong selection is the failure mode this
                // whole feature has to answer for.
                BannerlordSceneToolkit.EditUndo.CaptureFrames($"Repeat: {_last.Description}", selection);

                if (_last.What == Kind.CopyTranslation)
                {
                    var delta = _last.HasVector ? _last.MoveDelta : AxisDelta(_last.MoveAxis, _last.AngleDegrees);

                    // NEVER CLONE A CLONE (2026-08-23, after 2-3 hours of "copies become locked"):
                    // a gen-1 CopyFrom of a real, editor-born entity is CONFIRMED selectable (the
                    // user's own report: "only the first copy is selectable"), while a CopyFrom of
                    // a CopyFrom-product comes out locked in some native way that survived every
                    // fix so far (flags recursively cleared - sweep proves 0 remain; AttachEntity;
                    // UpdateSceneTree; SetReadyToRender). So the chain sidesteps generation 2
                    // entirely: the ORIGINAL selection is remembered, and press N places a fresh
                    // gen-1 clone of the ORIGINAL at N times the offset. Same visual result -
                    // copies marching along - built exclusively from the one operation that is
                    // confirmed to work.
                    bool continuingChain = _chainSources != null && _lastMadeCopies != null
                        && selection.Count == _lastMadeCopies.Count
                        && selection.All(s => _lastMadeCopies.Any(c => c != null && c.Pointer == s.Pointer));

                    List<GameEntity> sources;
                    if (continuingChain)
                    {
                        sources = _chainSources.Where(e => e != null && e.Pointer != UIntPtr.Zero).ToList();
                        if (sources.Count == 0) { sources = selection; _chainSources = new List<GameEntity>(selection); _chainStep = 0; }
                        _chainStep++;
                    }
                    else
                    {
                        sources = selection;
                        _chainSources = new List<GameEntity>(selection);
                        _chainStep = 1;
                    }
                    var offset = delta * (float)_chainStep;

                    var made = new List<GameEntity>();
                    int failed = 0;
                    var problems = new List<string>();

                    foreach (var e in sources)
                    {
                        if (e == null || e.Pointer == UIntPtr.Zero) continue;
                        var frame = e.GetGlobalFrame();
                        frame.origin = frame.origin + offset;

                        // CloneSourceAt = prefab route first, live CopyFrom fallback (flags
                        // cleared, editor-attached) second - see PrefabDistributor.
                        var copy = PrefabDistributor.CloneSourceAt(EntitySelector.CurrentScene, e, frame, out var cloneError);
                        if (copy != null) made.Add(copy);
                        else { failed++; if (problems.Count < 3) problems.Add($"'{e.Name}': {cloneError}"); }
                    }
                    Log.Info($"[RepeatLast] chain step {_chainStep}: {made.Count} gen-1 clone(s) of the original source(s) at offset x{_chainStep}.");

                    if (made.Count == 0)
                        return "Nothing could be copied. " + string.Join("; ", problems);

                    // Created, not moved - so undo has to DELETE these, not restore frames.
                    BannerlordSceneToolkit.EditUndo.CaptureCreated($"Repeat: {_last.Description}", made);

                    // THE NEW COPIES BECOME THE SELECTION, so pressing again steps along instead of
                    // stacking. Without this the source never moves, so every press cloned the same
                    // entity to the same offset and the second copy landed exactly on the first -
                    // which looks precisely like "it made a copy but did not translate it".
                    // The editor selection is best-effort (see the chaining fix above - the key
                    // release can wipe it); the remembered list and SelectionMemory are what
                    // actually guarantee the next press continues from these copies.
                    _lastMadeCopies = made;
                    MaterialSwapTool.Core.SelectionMemory.Update(made, 0f);
                    MaterialSwapTool.Core.LiveSceneChecks.SetEditorSelection(made);

                    var note = failed > 0 ? $" {failed} could not be copied: {string.Join("; ", problems)}" : "";
                    return $"Copied and moved {made.Count} entity(ies).{note} Undo is available.";
                }
                else if (_last.What == Kind.Translation)
                {
                    var delta = _last.HasVector ? _last.MoveDelta : AxisDelta(_last.MoveAxis, _last.AngleDegrees);

                    foreach (var e in selection)
                    {
                        if (e == null || e.Pointer == UIntPtr.Zero) continue;
                        var frame = e.GetGlobalFrame();
                        frame.origin = frame.origin + delta;
                        e.SetGlobalFrame(ref frame, true);
                        EditorFrameSync.Sync(e);
                    }
                }
                else
                {
                    var referenceFrame = selection[0].GetGlobalFrame();
                    Vec3? pivot = _last.UsedCustomPivot ? _last.CustomPivot : (Vec3?)null;

                    var result = PrefabDistributor.RotateGroup(
                        EntitySelector.CurrentScene, selection, _last.Axis, _last.AngleDegrees, referenceFrame, pivot);

                    if (!result.Success) return "Repeat failed: " + result.Error;
                }

                Log.Info($"[RepeatLast] applied '{_last.Description}' to {selection.Count} entity(ies)");
                return $"Repeated '{_last.Description}' on {selection.Count} entity(ies). Undo is available.";
            }
            catch (Exception ex)
            {
                Log.Error("RepeatLastTransform failed: " + ex);
                return "Repeat failed: " + ex.Message;
            }
        }

        private static Vec3 AxisDelta(NumericTransform.Axis axis, float amount) =>
            axis == NumericTransform.Axis.X ? new Vec3(amount, 0f, 0f, 0f)
          : axis == NumericTransform.Axis.Y ? new Vec3(0f, amount, 0f, 0f)
          : new Vec3(0f, 0f, amount, 0f);
    }
}
