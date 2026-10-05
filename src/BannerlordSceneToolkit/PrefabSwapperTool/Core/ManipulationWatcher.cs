using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace PrefabSwapperTool.Core
{
    // Notices that a transform HAPPENED, without caring how it was performed.
    //
    // WHY IT NO LONGER WATCHES THE MOUSE. The first version keyed everything off the left mouse
    // button: capture on press, measure on release. That only ever caught gizmo drags. Holding G
    // to translate or Z to rotate and moving the mouse does not hold the button the same way, so
    // the watcher never started and those operations were invisible - which is exactly the
    // reported "it works if I drag the gizmo but not if I hit G".
    //
    // This version polls the selected entities' frames and looks for the shape of an operation:
    // a period of CHANGE followed by a period of STILLNESS. That is true of a gizmo drag, a G/Z
    // modal drag, and a number typed into the editor's own transform panel alike, because it
    // describes the result rather than the input method.
    //
    // The frames from before the change began are kept, which is what lets a typed number REPLACE
    // the operation instead of stacking on top of it.
    public static class ManipulationWatcher
    {
        // Poll rate. Fast enough that the "before" snapshot is genuinely from before the operation,
        // slow enough that reading up to 64 frames is nothing.
        private const float SampleInterval = 0.06f;

        // How long everything must sit still before the operation counts as finished. Comfortably
        // longer than the gap between mouse samples mid-drag, short enough to feel immediate.
        private const float SettleSeconds = 0.22f;

        private const float MoveEpsilon = 0.001f;
        private const float RotationEpsilon = 0.0005f;

        // How long after an operation settles that typing a number still takes it over.
        private const float TakeoverWindowSeconds = 30f;

        // Frames as they were BEFORE the current change started.
        private static readonly List<KeyValuePair<GameEntity, MatrixFrame>> Before =
            new List<KeyValuePair<GameEntity, MatrixFrame>>();

        // The most recent stable sample, promoted to Before when motion starts.
        private static readonly List<KeyValuePair<GameEntity, MatrixFrame>> LastStable =
            new List<KeyValuePair<GameEntity, MatrixFrame>>();

        private static float _sinceSample;
        private static float _stillFor;
        private static bool _inMotion;
        private static bool _wasDuplicate;

        private static float _sinceSettled = float.MaxValue;
        private static bool _haveOperation;

        private static NumericTransform.Mode _detectedMode = NumericTransform.Mode.Rotate;
        private static NumericTransform.Axis _detectedAxis = NumericTransform.Axis.Z;
        private static string _lastOperationText = "";

        public static bool WasDuplicate => _wasDuplicate;
        public static int SnapshotCount => Before.Count;
        public static bool SawMovement => _haveOperation;
        public static float SecondsSinceRelease => _sinceSettled;
        public static NumericTransform.Mode DetectedMode => _detectedMode;
        public static NumericTransform.Axis DetectedAxis => _detectedAxis;

        // Drives the on-screen "last operation" readout.
        public static string LastOperationText => _lastOperationText;

        // True while a drag/operation is being observed - the HUD shows the running delta
        // live ("Now:" instead of "Last:") and hides the type-to-edit hint, which only
        // applies to a FINISHED operation.
        public static bool OperationInProgress => _inMotion;
        public static bool HasRecentOperation => _haveOperation && _sinceSettled <= TakeoverWindowSeconds;

        // A LIVE drag can be taken over too (2026-08-23, "now I've lost the ability to type a
        // number during the transform?"): typing mid-drag only ever worked because the
        // mid-drag settles kept flagging the operation as finished. With those gone, the
        // in-motion state itself must open the window - Before already holds the pre-drag
        // frames the takeover needs.
        public static bool CanTakeOver => Before.Count > 0 && (_inMotion || HasRecentOperation);

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // SUPPRESSION WHILE THE TOOLKIT ITSELF MOVES THINGS.
        //
        // Without this the watcher observes our OWN edits and records them as if the user had
        // performed them - and since it records the LAST operation, that silently replaces the
        // recipe that produced them. Seen in the log as:
        //
        //   recorded 'Copy + move (8.47, 0, 0)'    <- the real operation
        //   recorded 'Move (-0.32, 0, 0)'          <- our own follow-up, overwriting it
        //   applied  'Move (-0.32, 0, 0)'          <- so Shift+R stopped cloning
        //
        // which is exactly "it cloned about five times and then stopped". Every toolkit operation
        // that writes frames calls this first; the watcher then ignores what it sees and, when the
        // window closes, resyncs so the change we made is not mistaken for the start of a drag.
        private static float _suppressFor;

        public static void SuppressSelfEdit(float seconds = 1.0f)
        {
            if (seconds > _suppressFor) _suppressFor = seconds;
        }

        // Called by RepeatLastTransform.Repeat() BEFORE it reads the recipe (2026-08-23, user's
        // own diagnosis: "I never released Shift between shift+drag and Shift+R, and that
        // stopped copies"). The natural fast rhythm - release mouse, tap R with Shift still
        // held - beats the ~0.5s stillness window, so the copy was never recorded, and Repeat's
        // own SuppressSelfEdit then ate the pending observation entirely. Releasing Shift first
        // only "fixed" it by adding enough human delay for the settle to land. Settling on
        // demand closes the race: whatever is mid-observation gets classified and recorded NOW,
        // exactly as if the mouse had rested.
        public static void ForceSettlePending()
        {
            if (!_inMotion) return;

            List<KeyValuePair<GameEntity, MatrixFrame>> now;
            try { now = SampleSelection(); } catch { return; }
            if (now.Count == 0) now = new List<KeyValuePair<GameEntity, MatrixFrame>>(LastStable);
            if (now.Count == 0) return;

            _inMotion = false;
            _stillFor = 0f;
            Settle(now);
            Replace(LastStable, now);
            MaterialSwapTool.Log.Info("[Manipulation] pending observation settled on demand (Shift+R before the stillness window).");
        }

        public static void Tick(float dt)
        {
            if (_sinceSettled < float.MaxValue) _sinceSettled += dt;

            // Our own edit is in flight - keep following the frames so nothing looks like a
            // sudden jump when the window closes, but record nothing.
            if (_suppressFor > 0f)
            {
                _suppressFor -= dt;
                _sinceSample += dt;
                if (_sinceSample >= SampleInterval)
                {
                    _sinceSample = 0f;
                    try { Replace(LastStable, SampleSelection()); } catch { }
                    _inMotion = false;
                    _stillFor = 0f;
                }
                return;
            }

            _sinceSample += dt;
            if (_sinceSample < SampleInterval) return;
            var elapsed = _sinceSample;
            _sinceSample = 0f;

            List<KeyValuePair<GameEntity, MatrixFrame>> now;
            try { now = SampleSelection(); }
            catch { return; }

            // A MOMENTARILY EMPTY READ IS NOT A SELECTION CHANGE. The editor drops the
            // selection constantly - on keypresses, and transiently mid-operation - and
            // treating each of those as "the user is working on something else now" reset the
            // watcher and threw away the in-progress observation. That is why an operation was
            // only ever caught occasionally: any flicker between motion starting and settling
            // lost it. Skip the sample instead and wait for the selection to come back.
            if (now.Count == 0 && LastStable.Count > 0) return;

            // A DIFFERENT ENTITY IS SELECTED. Two very different things look identical here:
            //
            //   1. you clicked something else - abandon whatever was being observed
            //   2. a SHIFT-DRAG made a copy, and the editor selected the copy
            //
            // Case 2 is the whole point of duplicate detection, and treating it as case 1 is
            // exactly why shift-drag copies never recorded as duplicates: the watcher reset
            // the instant the copy appeared, then caught the copy's later movement as an
            // ordinary translate. The tell is that in case 2 the ORIGINAL is still there,
            // untouched, at the frame we last saw it - a copy adds an entity, it does not
            // move the one it came from.
            if (!SameEntities(now, LastStable))
            {
                if (LooksLikeDuplicate(now))
                {
                    // Measure from where the ORIGINAL sits: a copy starts life on top of its
                    // source, so source-to-copy is the offset the drag applied.
                    Replace(Before, LastStable);
                    _wasDuplicate = true;
                    _inMotion = true;
                    _stillFor = 0f;
                    Replace(LastStable, now);
                    MaterialSwapTool.Log.Info("[Manipulation] duplicate detected - tracking the new copy.");

                    // CLEANSE AT BIRTH (2026-08-23): the editor's own shift-drag duplicate
                    // INHERITS the source's runtime flags (DontSaveToScene /
                    // NonModifiableFromEditor / WaitUntilReady on children), which is how
                    // pre-fix flag contamination kept SPREADING after the clone-path fix.
                    // Every native duplicate the watcher sees gets its whole tree cleared
                    // here - safe unconditionally, since the user can only shift-drag things
                    // they could select, which excludes legitimately-flagged helpers.
                    foreach (var pair in now)
                        try { PrefabDistributor.ClearRuntimeFlagsInTree(pair.Key, 0); } catch { }
                    return;
                }

                // Diagnostic breadcrumb for "my shift-drag copy never recorded": the selection
                // changed mid-observation and it did NOT read as a clean copy - an original
                // moved, or the new selection still contains old entities (a mixed selection
                // mid-multi-copy looks like this).
                if (_inMotion)
                    MaterialSwapTool.Log.Info(
                        $"[Manipulation] selection changed mid-motion ({LastStable.Count} -> {now.Count}) " +
                        "and it does not look like a clean copy - observation dropped.");

                Replace(LastStable, now);
                _inMotion = false;
                _stillFor = 0f;
                return;
            }

            bool moved = AnyChanged(LastStable, now);

            if (moved)
            {
                if (!_inMotion)
                {
                    // Motion just began: the last stable sample IS the "before" state.
                    Replace(Before, LastStable);
                    _inMotion = true;
                    _wasDuplicate = false;
                }
                _stillFor = 0f;
                Replace(LastStable, now);
                UpdateLivePreview(now);
                return;
            }

            if (!_inMotion) return;

            // Not moving, but was. Wait for it to hold still before calling it finished - a drag
            // has plenty of frames where the mouse does not happen to move.
            _stillFor += elapsed;
            if (_stillFor < SettleSeconds) return;

            // NEVER SETTLE WHILE THE BUTTON IS STILL HELD (2026-08-23, "translate -8, move back
            // 2 ... it tells me +2"): pausing mid-gizmo-drag for 0.22s settled the operation,
            // reset the baseline to the pause point, and the rest of the SAME drag then recorded
            // as a separate fresh move measured from there. The watcher still doesn't need the
            // mouse to START an observation (G/Z modal drags don't hold the button), but a held
            // button proves the operation is NOT finished - so the pause just keeps waiting, and
            // Before keeps the true pre-drag frames until release. _stillFor is pinned at the
            // threshold so the settle lands on the first sample after the button comes up.
            if (Input.IsKeyDown(InputKey.LeftMouseButton))
            {
                _stillFor = SettleSeconds;
                return;
            }

            _inMotion = false;
            _stillFor = 0f;
            Settle(now);
            Replace(LastStable, now);
        }

        // LIVE READOUT DURING THE DRAG (2026-08-23, "I want it to stay displayed the whole
        // time"): once settle stopped firing mid-drag, the readout went silent until release -
        // the mid-drag settles had accidentally been what made it feel live. This rebuilds the
        // text every motion sample instead, ALWAYS measured against Before (the pre-drag
        // frames), so the running number is relative to the true origin no matter how many
        // times the drag changes direction. Display only - nothing is recorded here; the
        // recording still lands once, at release, via Settle().
        private static void UpdateLivePreview(List<KeyValuePair<GameEntity, MatrixFrame>> now)
        {
            if (Before.Count == 0 || now.Count == 0) return;

            if (_wasDuplicate)
            {
                var delta = Centroid(now) - Centroid(Before);
                _lastOperationText = $"Copy + move ({delta.x:0.##}, {delta.y:0.##}, {delta.z:0.##})";
                _detectedMode = NumericTransform.Mode.Translate;
                float dax = Math.Abs(delta.x), day = Math.Abs(delta.y), daz = Math.Abs(delta.z);
                _detectedAxis = daz >= dax && daz >= day ? NumericTransform.Axis.Z
                              : dax >= day ? NumericTransform.Axis.X
                              : NumericTransform.Axis.Y;
                return;
            }

            if (!TryGetMatchedPair(now, out var was, out var isNow))
            {
                was = Before[0].Value;
                isNow = now[0].Value;
            }

            // Same rotation-wins classification as Classify(), minus its log line - this runs
            // every sample and would flood tool.log. _detectedMode/_detectedAxis are kept
            // current so a MID-DRAG takeover opens the modal on the right mode.
            float rotS = Delta(isNow.rotation.s, was.rotation.s);
            float rotF = Delta(isNow.rotation.f, was.rotation.f);
            float rotU = Delta(isNow.rotation.u, was.rotation.u);
            if (rotS + rotF + rotU >= RotationEpsilon)
            {
                var axis = rotU <= rotS && rotU <= rotF ? NumericTransform.Axis.Z
                         : rotS <= rotF ? NumericTransform.Axis.X
                         : NumericTransform.Axis.Y;
                var angle = MeasureAngle(was, isNow, axis);
                _lastOperationText = $"Rotate {axis} {angle:0.##}°";
                _detectedMode = NumericTransform.Mode.Rotate;
                _detectedAxis = axis;
            }
            else
            {
                var d = isNow.origin - was.origin;
                _lastOperationText = $"Move ({d.x:0.##}, {d.y:0.##}, {d.z:0.##})";
                _detectedMode = NumericTransform.Mode.Translate;
                float ax = Math.Abs(d.x), ay = Math.Abs(d.y), az = Math.Abs(d.z);
                _detectedAxis = az >= ax && az >= ay ? NumericTransform.Axis.Z
                              : ax >= ay ? NumericTransform.Axis.X
                              : NumericTransform.Axis.Y;
            }
        }

        private static void Settle(List<KeyValuePair<GameEntity, MatrixFrame>> now)
        {
            if (Before.Count == 0 || now.Count == 0) return;

            // DUPLICATE DETECTED WITHOUT COUNTING THE SCENE - fallback for a selection swap the
            // transition samples missed (LooksLikeDuplicate normally decides live). A copy means
            // the settled selection shares NO entity with the pre-motion one, checked as a
            // pointer SET: the old version compared index 0 against index 0, which read a
            // shuffled same-selection as a duplicate and a differently-ordered copy set as not
            // one - both real outcomes once more than one prefab is selected, since the engine
            // makes no ordering promise between two selection queries.
            if (!_wasDuplicate)
            {
                var beforePtrs = new HashSet<UIntPtr>();
                foreach (var kv in Before) if (kv.Key != null) beforePtrs.Add(kv.Key.Pointer);
                bool anyShared = false;
                foreach (var kv in now)
                    if (kv.Key != null && beforePtrs.Contains(kv.Key.Pointer)) { anyShared = true; break; }
                _wasDuplicate = !anyShared;
            }

            if (_wasDuplicate)
            {
                // A copy's frame can't be paired to its source by pointer (the copies are all
                // new entities), and pairing by list INDEX - what this used to do via
                // Before[0]/now[0] - measured the offset between UNRELATED entities whenever
                // the editor returned several copies in a different order than their sources.
                // That is exactly why a multi-prefab shift-drag recorded a garbage delta (or,
                // via the basis mismatch between two different prefabs, misread as a rotation)
                // and Shift+R then replayed nonsense. The group CENTROID delta is
                // ordering-proof, and a shift-drag can only translate, so translate is
                // asserted rather than classified.
                var delta = Centroid(now) - Centroid(Before);
                if (Math.Abs(delta.x) + Math.Abs(delta.y) + Math.Abs(delta.z) < MoveEpsilon)
                {
                    _lastOperationText = "";
                    return;
                }

                _detectedMode = NumericTransform.Mode.Translate;
                float ax = Math.Abs(delta.x), ay = Math.Abs(delta.y), az = Math.Abs(delta.z);
                _detectedAxis = az >= ax && az >= ay ? NumericTransform.Axis.Z
                              : ax >= ay ? NumericTransform.Axis.X
                              : NumericTransform.Axis.Y;

                RepeatLastTransform.RecordCopyTranslationVector(delta);
                _lastOperationText = $"Copy + move ({delta.x:0.##}, {delta.y:0.##}, {delta.z:0.##})";

                // For the numeric takeover: typing right after a shift-drag must act on the
                // COPIES, from where they STARTED (their source's position) - see TakeSnapshot.
                _lastSettleWasDuplicate = true;
                _lastSettleDelta = delta;

                _haveOperation = true;
                _sinceSettled = 0f;
                MaterialSwapTool.Log.Info(
                    $"[Manipulation] operation settled: {_lastOperationText} ({now.Count} entity(ies))");
                return;
            }

            // Ordinary move/rotate: measure a pointer-MATCHED pair, preferring one that actually
            // changed - index-vs-index was the same multi-selection ordering trap as above.
            if (!TryGetMatchedPair(now, out var was, out var isNow))
            {
                was = Before[0].Value;
                isNow = now[0].Value;
            }

            if (!Changed(was, isNow)) return;

            Classify(was, isNow);
            RecordForRepeat(was, isNow);

            _lastSettleWasDuplicate = false;

            _haveOperation = true;
            _sinceSettled = 0f;
            MaterialSwapTool.Log.Info($"[Manipulation] operation settled: {_lastOperationText}");
        }

        private static Vec3 Centroid(List<KeyValuePair<GameEntity, MatrixFrame>> list)
        {
            var sum = new Vec3(0f, 0f, 0f, 0f);
            foreach (var kv in list) sum = sum + kv.Value.origin;
            return sum * (1f / Math.Max(1, list.Count));
        }

        // First pointer-matched (Before, now) pair that actually moved; any matched pair as a
        // fallback. False only when nothing matches at all.
        private static bool TryGetMatchedPair(List<KeyValuePair<GameEntity, MatrixFrame>> now,
                                              out MatrixFrame was, out MatrixFrame isNow)
        {
            var beforeFrames = new Dictionary<UIntPtr, MatrixFrame>();
            foreach (var kv in Before) if (kv.Key != null) beforeFrames[kv.Key.Pointer] = kv.Value;

            bool haveAny = false;
            was = default(MatrixFrame);
            isNow = default(MatrixFrame);
            foreach (var kv in now)
            {
                if (kv.Key == null || !beforeFrames.TryGetValue(kv.Key.Pointer, out var b)) continue;
                if (!haveAny) { was = b; isNow = kv.Value; haveAny = true; }
                if (Changed(b, kv.Value)) { was = b; isNow = kv.Value; return true; }
            }
            return haveAny;
        }

        private static List<KeyValuePair<GameEntity, MatrixFrame>> SampleSelection()
        {
            var list = new List<KeyValuePair<GameEntity, MatrixFrame>>();
            if (!MaterialSwapTool.Core.EntitySelector.HasOpenScene) return list;

            var selection = MaterialSwapTool.Core.EntitySelector.GetLiveManualSelection();

            // Feeds SelectionMemory while we are here. Hotkeys cannot read the selection at the
            // moment they fire - the editor clears it on the keypress first - so they read this
            // instead. Costs nothing: the poll is already happening.
            MaterialSwapTool.Core.SelectionMemory.Update(selection, SampleInterval);

            if (selection.Count == 0 || selection.Count > 64) return list;

            foreach (var e in selection)
            {
                if (!Alive(e)) continue;
                try { list.Add(new KeyValuePair<GameEntity, MatrixFrame>(e, e.GetGlobalFrame())); }
                catch { }
            }
            return list;
        }

        // True when the previously-selected entities all still exist AND have not moved, while
        // something different is now selected. That is the signature of a duplicate: the
        // source is still sitting there and a new entity has taken the selection.
        private static bool LooksLikeDuplicate(List<KeyValuePair<GameEntity, MatrixFrame>> now)
        {
            if (LastStable.Count == 0 || now.Count == 0) return false;

            foreach (var kv in LastStable)
            {
                if (!Alive(kv.Key)) return false;          // the old one is gone - not a copy
                MatrixFrame current;
                try { current = kv.Key.GetGlobalFrame(); } catch { return false; }
                if (Changed(kv.Value, current)) return false;   // the old one moved - not a copy
            }

            // And the new selection must genuinely be different entities.
            foreach (var kv in now)
                if (LastStable.Any(p => p.Key != null && kv.Key != null && p.Key.Pointer == kv.Key.Pointer))
                    return false;

            return true;
        }

        // Compared by POINTER (separate queries hand back separate managed wrappers for the
        // same entity, so reference equality would report a change every sample) and as a SET,
        // not index-by-index. GetSelectedEntities makes no ordering promise, and with several
        // entities selected the same selection can come back in a different order between two
        // samples - the old index-wise compare then reported "different entities", resetting
        // the watcher mid-drag. That is why multi-prefab operations (a shift-drag copy of
        // several at once, most visibly) recorded only sometimes or not at all while
        // single-entity ones always worked: with one entity there is no order to shuffle.
        private static bool SameEntities(List<KeyValuePair<GameEntity, MatrixFrame>> a,
                                         List<KeyValuePair<GameEntity, MatrixFrame>> b)
        {
            if (a.Count != b.Count) return false;
            if (a.Count == 1)
                return a[0].Key != null && b[0].Key != null && a[0].Key.Pointer == b[0].Key.Pointer;

            var pointers = new HashSet<UIntPtr>();
            foreach (var kv in a)
            {
                if (kv.Key == null) return false;
                pointers.Add(kv.Key.Pointer);
            }
            foreach (var kv in b)
            {
                if (kv.Key == null || !pointers.Contains(kv.Key.Pointer)) return false;
            }
            return true;
        }

        // Paired by pointer for the same reason - index i in one sample is not necessarily the
        // same entity at index i in the next, and comparing mismatched frames read pure
        // selection-order shuffle as huge phantom "movement".
        private static bool AnyChanged(List<KeyValuePair<GameEntity, MatrixFrame>> a,
                                       List<KeyValuePair<GameEntity, MatrixFrame>> b)
        {
            if (a.Count == 1 && b.Count == 1) return Changed(a[0].Value, b[0].Value);

            var frames = new Dictionary<UIntPtr, MatrixFrame>();
            foreach (var kv in a) if (kv.Key != null) frames[kv.Key.Pointer] = kv.Value;
            foreach (var kv in b)
                if (kv.Key != null && frames.TryGetValue(kv.Key.Pointer, out var was) && Changed(was, kv.Value))
                    return true;
            return false;
        }

        private static void Replace(List<KeyValuePair<GameEntity, MatrixFrame>> target,
                                    List<KeyValuePair<GameEntity, MatrixFrame>> source)
        {
            target.Clear();
            target.AddRange(source);
        }

        private static bool Changed(MatrixFrame was, MatrixFrame now)
        {
            var d = now.origin - was.origin;
            float moved = Math.Abs(d.x) + Math.Abs(d.y) + Math.Abs(d.z);
            float rotated = Delta(now.rotation.s, was.rotation.s)
                          + Delta(now.rotation.f, was.rotation.f)
                          + Delta(now.rotation.u, was.rotation.u);
            return moved >= MoveEpsilon || rotated >= RotationEpsilon;
        }

        private static void Classify(MatrixFrame was, MatrixFrame now)
        {
            float rotS = Delta(now.rotation.s, was.rotation.s);
            float rotF = Delta(now.rotation.f, was.rotation.f);
            float rotU = Delta(now.rotation.u, was.rotation.u);
            float rotated = rotS + rotF + rotU;

            var d = now.origin - was.origin;
            float moved = Math.Abs(d.x) + Math.Abs(d.y) + Math.Abs(d.z);

            // Rotation wins when present: rotating a group about its centre also moves each
            // origin, so "the origin moved" alone would call every rotation a move.
            if (rotated >= RotationEpsilon)
            {
                _detectedMode = NumericTransform.Mode.Rotate;
                _detectedAxis = rotU <= rotS && rotU <= rotF ? NumericTransform.Axis.Z
                              : rotS <= rotF ? NumericTransform.Axis.X
                              : NumericTransform.Axis.Y;
            }
            else
            {
                _detectedMode = NumericTransform.Mode.Translate;
                float ax = Math.Abs(d.x), ay = Math.Abs(d.y), az = Math.Abs(d.z);
                _detectedAxis = az >= ax && az >= ay ? NumericTransform.Axis.Z
                              : ax >= ay ? NumericTransform.Axis.X
                              : NumericTransform.Axis.Y;
            }

            MaterialSwapTool.Log.Info(
                $"[Manipulation] {_detectedMode} on {_detectedAxis}" +
                $"{(_wasDuplicate ? " (duplicate)" : "")} rot={rotated:0.####} move={moved:0.###}");
        }

        private static void RecordForRepeat(MatrixFrame was, MatrixFrame now)
        {
            if (_detectedMode == NumericTransform.Mode.Rotate)
            {
                var angle = MeasureAngle(was, now, _detectedAxis);
                if (Math.Abs(angle) < 0.01f)
                {
                    MaterialSwapTool.Log.Info($"[Manipulation] rotation not recorded: measured {angle:0.####} deg on {_detectedAxis}.");
                    _lastOperationText = "";
                    return;
                }
                RepeatLastTransform.RecordRotation(
                    _detectedAxis == NumericTransform.Axis.X ? PrefabDistributor.DistributionAxis.LocalX
                  : _detectedAxis == NumericTransform.Axis.Y ? PrefabDistributor.DistributionAxis.LocalY
                  : PrefabDistributor.DistributionAxis.LocalZ,
                    angle, false, default(Vec3));

                _lastOperationText = $"Rotate {_detectedAxis} {angle:0.##}°";
            }
            else
            {
                var delta = now.origin - was.origin;
                if (Math.Abs(delta.x) + Math.Abs(delta.y) + Math.Abs(delta.z) < MoveEpsilon) { _lastOperationText = ""; return; }

                if (_wasDuplicate) RepeatLastTransform.RecordCopyTranslationVector(delta);
                else RepeatLastTransform.RecordTranslationVector(delta);

                _lastOperationText = (_wasDuplicate ? "Copy + move " : "Move ") +
                    $"({delta.x:0.##}, {delta.y:0.##}, {delta.z:0.##})";
            }

            MaterialSwapTool.Log.Info(
                $"[Manipulation] recorded for repeat: {_detectedMode}{(_wasDuplicate ? " (duplicate)" : "")}");
        }

        // Signed angle turned about the given world axis, using the standard
        // atan2(dot(cross(a,b), axis), dot(a,b)) on vectors projected into the plane perpendicular
        // to it - sign-correct for all three axes by construction, unlike the hand-picked
        // per-axis reference pairs it replaced (which had Y backwards).
        private static float MeasureAngle(MatrixFrame was, MatrixFrame now, NumericTransform.Axis axis)
        {
            var axisVec = axis == NumericTransform.Axis.X ? new Vec3(1f, 0f, 0f, 0f)
                        : axis == NumericTransform.Axis.Y ? new Vec3(0f, 1f, 0f, 0f)
                        : new Vec3(0f, 0f, 1f, 0f);

            var a = PickPerpendicular(was, axisVec, out int which);
            var b = which == 0 ? now.rotation.s : which == 1 ? now.rotation.f : now.rotation.u;

            var pa = ProjectOntoPlane(a, axisVec);
            var pb = ProjectOntoPlane(b, axisVec);
            if (Length(pa) < 0.0001f || Length(pb) < 0.0001f) return 0f;

            pa = Normalize(pa);
            pb = Normalize(pb);

            double sin = Vec3.DotProduct(Cross(pa, pb), axisVec);
            double cos = Vec3.DotProduct(pa, pb);
            double degrees = Math.Atan2(sin, cos) * 180.0 / Math.PI;
            while (degrees > 180.0) degrees -= 360.0;
            while (degrees < -180.0) degrees += 360.0;
            return (float)degrees;
        }

        // Whichever basis vector is most perpendicular to the axis - so there is always something
        // meaningful to measure however the prefab is already oriented.
        private static Vec3 PickPerpendicular(MatrixFrame frame, Vec3 axisVec, out int which)
        {
            float ds = Math.Abs(Vec3.DotProduct(frame.rotation.s, axisVec));
            float df = Math.Abs(Vec3.DotProduct(frame.rotation.f, axisVec));
            float du = Math.Abs(Vec3.DotProduct(frame.rotation.u, axisVec));

            if (ds <= df && ds <= du) { which = 0; return frame.rotation.s; }
            if (df <= du) { which = 1; return frame.rotation.f; }
            which = 2; return frame.rotation.u;
        }

        private static Vec3 ProjectOntoPlane(Vec3 v, Vec3 normal) => v - normal * Vec3.DotProduct(v, normal);

        private static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x, 0f);

        private static float Length(Vec3 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

        private static Vec3 Normalize(Vec3 v) { var l = Length(v); return l < 0.0001f ? v : v / l; }

        private static float Delta(Vec3 a, Vec3 b) =>
            Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) + Math.Abs(a.z - b.z);


        // Whether the last settled operation was a shift-drag DUPLICATE, and the delta it
        // applied - TakeSnapshot needs both to hand the numeric takeover the right targets.
        private static bool _lastSettleWasDuplicate;
        private static Vec3 _lastSettleDelta;

        public static List<KeyValuePair<GameEntity, MatrixFrame>> TakeSnapshot()
        {
            // CONFIRMED BUG, fixed 2026-08-23 ("if I shift click and drag a copy ... when I
            // type, it affects the ORIGINAL"): for a duplicate, Before holds the ORIGINALS at
            // their pre-copy frames, so the takeover re-positioned the originals and left the
            // copy where it fell. The takeover must adopt the COPIES, anchored at where they
            // STARTED - which is their current frame minus the settled delta (ordering-proof:
            // no source-to-copy pairing needed, the delta is the group's own recorded offset).
            _haveOperation = false;

            // MID-DRAG TAKEOVER: the observation ends here - the modal owns the transform now,
            // and letting the release also settle would record the raw drag on top of it.
            if (_inMotion)
            {
                _inMotion = false;
                _stillFor = 0f;

                // A shift-drag duplicate that has NOT settled yet: Before holds the ORIGINALS
                // (the takeover must not touch them - the old "typing affects the original"
                // bug). Hand over the COPIES, anchored at their start: current frame minus the
                // live centroid delta, same ordering-proof arithmetic as the settled path.
                if (_wasDuplicate && LastStable.Count > 0)
                {
                    var liveDelta = Centroid(LastStable) - Centroid(Before);
                    var live = new List<KeyValuePair<GameEntity, MatrixFrame>>();
                    foreach (var kv in LastStable)
                    {
                        if (!Alive(kv.Key)) continue;
                        try
                        {
                            var frame = kv.Key.GetGlobalFrame();
                            frame.origin = frame.origin - liveDelta;
                            live.Add(new KeyValuePair<GameEntity, MatrixFrame>(kv.Key, frame));
                        }
                        catch { }
                    }
                    if (live.Count > 0) return live;
                }

                return Before.Where(kv => Alive(kv.Key)).ToList();
            }

            if (_lastSettleWasDuplicate)
            {
                var copies = new List<KeyValuePair<GameEntity, MatrixFrame>>();
                foreach (var kv in LastStable)
                {
                    if (!Alive(kv.Key)) continue;
                    try
                    {
                        var frame = kv.Key.GetGlobalFrame();
                        frame.origin = frame.origin - _lastSettleDelta;
                        copies.Add(new KeyValuePair<GameEntity, MatrixFrame>(kv.Key, frame));
                    }
                    catch { }
                }
                if (copies.Count > 0) return copies;
            }

            return Before.Where(kv => Alive(kv.Key)).ToList();
        }

        // Explicit "I have seen it" from the user. Clears the readout AND the takeover, since
        // dismissing means done - a digit typed afterwards should not reach back and rewrite
        // an operation that has been acknowledged.
        public static void DismissLastOperation()
        {
            if (string.IsNullOrEmpty(_lastOperationText) && !_haveOperation) return;
            _lastOperationText = "";
            _haveOperation = false;
            MaterialSwapTool.Log.Info("[Manipulation] last-operation readout dismissed.");
        }

        public static void Reset()
        {
            Before.Clear();
            LastStable.Clear();
            _inMotion = false;
            _haveOperation = false;
            _lastOperationText = "";
            _sinceSettled = float.MaxValue;
        }
    }
}
