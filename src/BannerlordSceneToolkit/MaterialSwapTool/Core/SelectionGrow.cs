using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace MaterialSwapTool.Core
{
    // Grow / shrink the selection by proximity: everything whose ORIGIN lies within a radius of
    // any originally-selected entity's origin. Ctrl+Numpad+ opens it, then the radius is live -
    // Numpad+/Up and Numpad-/Down step it, or type an exact number. Enter commits, Esc puts the
    // original selection back.
    //
    // THE RADIUS IS ALWAYS MEASURED FROM THE ORIGINAL SELECTION, captured once when the modal
    // opens - never from the last result. That single decision is what makes shrinking exact
    // rather than a guess: "contract" is just a smaller radius recomputed from the same base, so
    // stepping 5 -> 10 -> 5 lands on exactly the selection you had at 5, and radius 0 is exactly
    // what you started with. The alternative (dilate the current result, then try to erode it)
    // cannot round-trip, and erosion has no sane definition in a dense scene anyway: in a city
    // scene practically every selected entity is within a few metres of an unselected one, so a
    // literal erode would clear almost everything on the first press.
    //
    // WHAT THIS COSTS, and why it is not the naive thing.
    //
    // The expensive part of any "search the scene" feature here is NEVER the arithmetic - it is
    // the managed/native boundary. Reading one entity's frame is a native call; doing that per
    // entity per keypress on a 7,700-entity scene (CC_74_battle) or a 10,800-entity one
    // (CC_76_battle) is the same mistake that made every hotkey stutter until 2026-08-22.
    //
    // So the origins are read ONCE into a plain managed array and cached for the whole modal
    // session (and re-used across sessions for a few seconds). After that a radius change costs
    // only float maths over that array, which is nothing: even a pathological 10,000 candidates
    // x 500 base entities is ~5M comparisons, single-digit milliseconds, with no interop at all.
    // An AABB pre-filter (the base selection's bounding box, expanded by the radius) throws out
    // most candidates with six comparisons before any distance is computed, so the real number is
    // far smaller than that. No spatial grid is needed at this scale, and one would have to be
    // rebuilt whenever the radius changed anyway.
    public static class SelectionGrow
    {
        // A grown selection has to be applied, and applying is the one part that is not free -
        // see LiveSceneChecks.ApplyEditorSelectionNow. This cap is a backstop against a radius
        // typo ("500") quietly selecting an entire scene.
        private const int MaxResult = 2000;

        public const float DefaultRadius = 5f;
        private const float MinRadius = 0f;
        private const float MaxRadius = 500f;

        // --- cached scene origins ----------------------------------------------------------
        private static string _indexScene;
        private static DateTime _indexBuiltUtc = DateTime.MinValue;
        private static GameEntity[] _indexEntities = new GameEntity[0];
        private static Vec3[] _indexOrigins = new Vec3[0];

        // Long enough that a burst of presses reuses one index, short enough that entities added
        // or deleted in between are picked up without anything having to invalidate it manually.
        private static readonly TimeSpan IndexMaxAge = TimeSpan.FromSeconds(10);

        // --- live modal state --------------------------------------------------------------
        private static readonly List<GameEntity> Base = new List<GameEntity>();
        private static float _radius = DefaultRadius;
        private static string _typed = "";
        private static int _resultCount;
        private static bool _cappedLastRun;

        public static bool IsActive { get; private set; }
        public static float Radius => _radius;

        // Centre of the ORIGINAL selection, for the on-screen radius sphere. One sphere, at the
        // middle of the box the base occupies.
        //
        // Honest about what it shows: the actual test is per-entity - a candidate joins if it is
        // within the radius of ANY originally-selected entity - so with one entity selected (the
        // usual case) the sphere is exactly the tested volume, and with several spread out it is
        // an indication of reach rather than a precise boundary. Drawing one sphere per selected
        // entity would be precise and unreadable.
        public static bool TryGetBaseCenter(out Vec3 center)
        {
            center = Vec3.Zero;
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;

            foreach (var e in Base)
            {
                if (!Alive(e)) continue;
                Vec3 p;
                try { p = e.GetGlobalFrame().origin; } catch { continue; }
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
                any = true;
            }
            if (!any) return false;

            center = new Vec3((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f, 0f);
            return true;
        }
        public static int BaseCount => Base.Count;
        public static int ResultCount => _resultCount;
        public static bool WasCapped => _cappedLastRun;

        // What the panel shows in its number box: the typed text while typing, otherwise the
        // stepped value formatted.
        public static string RadiusText =>
            _typed.Length > 0 ? _typed : _radius.ToString("0.##", CultureInfo.InvariantCulture);

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // Returns null on success, or the reason it could not start.
        public static string Begin()
        {
            if (IsActive) return null;
            if (!EntitySelector.HasOpenScene) return "No scene is currently open.";

            // Live read first, remembered selection as the fallback - the editor clears the
            // selection on the keypress that got us here, which is exactly what SelectionMemory
            // exists for.
            var selection = SelectionMemory.GetSelection();
            if (selection.Count == 0) return "Nothing selected - select something to grow from first.";

            Base.Clear();
            foreach (var e in selection) if (Alive(e)) Base.Add(e);
            if (Base.Count == 0) return "Nothing usable in the selection.";

            EnsureIndex();

            _radius = DefaultRadius;
            _typed = "";
            IsActive = true;
            Log.Info($"[SelectionGrow] begin: base={Base.Count}, indexed={_indexEntities.Length}, radius={_radius}");
            Apply();
            return null;
        }

        public static void Step(float delta)
        {
            if (!IsActive) return;
            // Stepping abandons a half-typed number rather than trying to merge with it.
            _typed = "";
            SetRadius(_radius + delta);
        }

        public static void SetRadius(float radius)
        {
            if (!IsActive) return;
            _radius = Math.Max(MinRadius, Math.Min(MaxRadius, radius));
            Apply();
        }

        public static void AppendChar(char c)
        {
            if (!IsActive) return;
            if (c == '.' && _typed.Contains(".")) return;
            if (_typed.Length > 8) return;
            _typed += c;
            if (float.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                _radius = Math.Max(MinRadius, Math.Min(MaxRadius, value));
                Apply();
            }
        }

        public static void Backspace()
        {
            if (!IsActive || _typed.Length == 0) return;
            _typed = _typed.Substring(0, _typed.Length - 1);
            if (_typed.Length == 0) return;
            if (float.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                _radius = Math.Max(MinRadius, Math.Min(MaxRadius, value));
                Apply();
            }
        }

        // Recomputes from the ORIGINAL base every time - see the class comment.
        private static void Apply()
        {
            if (!IsActive) return;

            var result = Compute(out var capped);
            _resultCount = result.Count;
            _cappedLastRun = capped;

            // Queued like every other select-in-editor path; a rapid burst of radius changes
            // coalesces because Request replaces whatever was pending.
            LiveSceneChecks.SetEditorSelection(result);
        }

        private static List<GameEntity> Compute(out bool capped)
        {
            capped = false;

            var live = Base.Where(Alive).ToList();
            var result = new List<GameEntity>(live);
            var chosen = new HashSet<UIntPtr>(live.Select(e => e.Pointer));

            if (_radius <= 0.0001f || _indexEntities.Length == 0) return result;

            // Base origins, read fresh (the base is small, and it may have moved since the
            // index was built).
            var baseOrigins = new List<Vec3>(live.Count);
            foreach (var e in live)
            {
                try { baseOrigins.Add(e.GetGlobalFrame().origin); } catch { }
            }
            if (baseOrigins.Count == 0) return result;

            // AABB pre-filter: the base's own bounding box, expanded by the radius. Anything
            // outside it cannot possibly be within the radius of any base entity, and rejecting
            // it costs six comparisons instead of a distance per base entity.
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (var p in baseOrigins)
            {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            minX -= _radius; minY -= _radius; minZ -= _radius;
            maxX += _radius; maxY += _radius; maxZ += _radius;

            float radiusSq = _radius * _radius;

            for (int i = 0; i < _indexEntities.Length; i++)
            {
                var candidate = _indexEntities[i];
                if (!Alive(candidate) || chosen.Contains(candidate.Pointer)) continue;

                var p = _indexOrigins[i];
                if (p.x < minX || p.x > maxX || p.y < minY || p.y > maxY || p.z < minZ || p.z > maxZ) continue;

                for (int b = 0; b < baseOrigins.Count; b++)
                {
                    var q = baseOrigins[b];
                    float dx = p.x - q.x, dy = p.y - q.y, dz = p.z - q.z;
                    if (dx * dx + dy * dy + dz * dz > radiusSq) continue;

                    result.Add(candidate);
                    chosen.Add(candidate.Pointer);
                    break;
                }

                if (result.Count >= MaxResult) { capped = true; break; }
            }

            return result;
        }

        // One native frame read per entity, once. Everything after this is managed maths.
        private static void EnsureIndex()
        {
            var sceneName = EntitySelector.CurrentSceneName;
            bool fresh = _indexScene == sceneName
                         && DateTime.UtcNow - _indexBuiltUtc < IndexMaxAge
                         && _indexEntities.Length > 0;
            if (fresh) return;

            var all = new List<GameEntity>();
            try { EntitySelector.CurrentScene.GetEntities(ref all); }
            catch (Exception ex) { Log.Warn("[SelectionGrow] could not enumerate the scene: " + ex.Message); return; }

            var entities = new List<GameEntity>(all.Count);
            var origins = new List<Vec3>(all.Count);
            foreach (var e in all)
            {
                if (!Alive(e)) continue;
                try
                {
                    origins.Add(e.GetGlobalFrame().origin);
                    entities.Add(e);
                }
                catch { }
            }

            _indexEntities = entities.ToArray();
            _indexOrigins = origins.ToArray();
            _indexScene = sceneName;
            _indexBuiltUtc = DateTime.UtcNow;
            Log.Info($"[SelectionGrow] indexed {_indexEntities.Length} entity origin(s) in '{sceneName}'.");
        }

        public static string Commit()
        {
            if (!IsActive) return "";
            var count = _resultCount;
            var radius = _radius;

            // RE-ASSERT THE RESULT ON THE WAY OUT (2026-08-23, "when I click to apply grow
            // selection it deselects"): the grown selection is already applied, but the CLICK
            // on the Apply button also reaches the native editor underneath (the DeferredSelection
            // hazard) and resets the selection to whatever the mouse is over - our panel, i.e.
            // nothing. The radius steps survive because each one queues through SetEditorSelection;
            // commit queued nothing, so the wipe was the last word. Queuing the final result here
            // lands it after the editor has finished with the press AND the release.
            LiveSceneChecks.SetEditorSelection(Compute(out _));

            End();
            Log.Info($"[SelectionGrow] committed: {count} selected at radius {radius:0.##}");
            return $"Selection grown to {count} entity(ies) within {radius:0.##} units.";
        }

        public static string Cancel()
        {
            if (!IsActive) return "";
            var restore = Base.Where(Alive).ToList();
            var count = restore.Count;
            LiveSceneChecks.SetEditorSelection(restore);
            End();
            Log.Info($"[SelectionGrow] cancelled, {count} entity(ies) restored");
            return $"Cancelled - back to the original {count} entity(ies).";
        }

        private static void End()
        {
            IsActive = false;
            Base.Clear();
            _typed = "";
            _resultCount = 0;
            _cappedLastRun = false;
        }

        // A scene switch invalidates every held reference AND the cached origins.
        public static void Clear(string reason)
        {
            _indexScene = null;
            _indexEntities = new GameEntity[0];
            _indexOrigins = new Vec3[0];
            _indexBuiltUtc = DateTime.MinValue;
            if (!IsActive) return;
            Log.Info($"[SelectionGrow] dropped {Base.Count} entity(ies): {reason}");
            End();
        }
    }
}
