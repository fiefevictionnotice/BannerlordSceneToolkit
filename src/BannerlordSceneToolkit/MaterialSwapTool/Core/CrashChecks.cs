using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Core
{
    // Two load-crash checks found by bisecting real maps on a vanilla dedicated server, plus a
    // fix for each.
    //
    // 1. DESTRUCTIBLE REFERENCE TAG WITHOUT A TARGET (CC_93_siege, 2026-11-10).
    //    DestructableComponent.OnInit does
    //        _referenceEntity = GetFirstChildEntityWithTag(ReferenceEntityTag)   (when the tag is set)
    //    and then Debug.Assert(_referenceEntity != null, "Reference entity must be assigned...").
    //    GetFirstChildEntityWithTag only looks at DIRECT children (decompiled, 1.4.x). If nothing
    //    carries the tag, the dedicated server aborts during scene load ("Invalid Program:
    //    attempted to call a UnmanagedCallersOnly method from managed code"). CC_93 had 8 merlons
    //    with ReferenceEntityTag="reference" whose debris_holder child had been deleted. The editor
    //    only shows this as an entity warning in OnCheckForProblems, which is easy to miss. The
    //    same rule applies to ANY entity with a DestructableComponent (walls, gates, barricades,
    //    custom destructibles), not only merlons.
    //    Fix: add an empty "debris_holder" child carrying the tag at the entity's own origin
    //    (server-verified on CC_93: the map loads and runs). Debris spawns there; move it if needed.
    //
    // 2. LIVE merchant_stand_market PREFAB (CC_84, 2026-06-14/15).
    //    Bisected to the single entity on CC_battle_84_test, then confirmed on test maps: every map
    //    saved with <game_entity prefab="merchant_stand_market"> (a live link) crashed MP clients on
    //    load; the copy saved with old_prefab_name + inline children (link broken) loaded. The prefab
    //    lives in SandBox\Prefabs\merchant_sets.xml and SandBox prefabs are not loaded in MP. The
    //    dedicated server loads such scenes fine, so a server-only test does not catch it.
    //    Meshless <cloth_simulator/> is NOT the cause (the working CC_84_battle has 50 of them).
    //    The four sibling stands in merchant_sets.xml (mix, weapon, pottery, armor) are untested, so
    //    they only warn.
    //    Fix: BreakPrefab() on the stand, so the save writes the children inline like the copy that
    //    loaded. Everything stays where it is.
    public static class CrashChecks
    {
        public const string CursedPrefab = "merchant_stand_market";

        public static readonly string[] UntestedSiblingPrefabs =
        {
            "merchant_stand_mix", "merchant_stand_weapon", "merchant_stand_pottery", "merchant_stand_armor",
        };

        private static Finding Err(string msg) => new Finding { Severity = "ERROR", Message = msg };
        private static Finding Warn(string msg) => new Finding { Severity = "WARNING", Message = msg };
        private static Finding Info(string msg) => new Finding { Severity = "INFO", Message = msg };

        private static string Pos(GameEntity e)
        {
            var p = e.GlobalPosition;
            return $"{p.x:F3}, {p.y:F3}, {p.z:F3}";
        }

        private static string Path(GameEntity e)
        {
            var parts = new List<string>();
            for (var cur = e; cur != null && cur.Pointer != UIntPtr.Zero; cur = cur.Parent) parts.Insert(0, cur.Name ?? "?");
            return string.Join(" > ", parts);
        }

        // ============================================================
        // 1. DESTRUCTIBLE REFERENCE TAGS
        // ============================================================
        public class MissingReference
        {
            public GameEntity Entity;
            public string Tag;
        }

        public static List<MissingReference> FindMissingReferences(List<GameEntity> all)
        {
            var result = new List<MissingReference>();
            foreach (var e in all)
            {
                DestructableComponent dc;
                try { dc = e.GetFirstScriptOfType<DestructableComponent>(); } catch { continue; }
                if (dc == null) continue;
                var tag = dc.ReferenceEntityTag;
                if (string.IsNullOrWhiteSpace(tag)) continue;              // empty = the entity itself is the reference
                bool found = false;
                foreach (var c in e.GetChildren())                          // DIRECT children only, same as the game
                    if (c.HasTag(tag)) { found = true; break; }
                if (!found) result.Add(new MissingReference { Entity = e, Tag = tag });
            }
            return result;
        }

        public static List<Finding> CheckDestructibleReferences(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var missing = FindMissingReferences(all);
            if (missing.Count == 0)
            {
                findings.Add(Info("Every DestructableComponent with a ReferenceEntityTag has a direct child carrying that tag. OK."));
                return findings;
            }
            findings.Add(Err($"{missing.Count} destructible(s) have a ReferenceEntityTag but no direct child with that tag. " +
                             "The dedicated server ABORTS while loading the scene (CC_93_siege merlon bug). " +
                             "Use 'Fix Destructible Refs' to add a tagged debris_holder child to each, or clear the tag."));
            foreach (var m in missing)
                findings.Add(Err($"  --- {Path(m.Entity)}  tag '{m.Tag}'  at {Pos(m.Entity)}"));
            return findings;
        }

        public static int FixDestructibleReferences(Scene scene, List<GameEntity> all)
        {
            int fixedCount = 0;
            foreach (var m in FindMissingReferences(all))
            {
                GameEntity holder;
                try { holder = GameEntity.CreateEmpty(scene, true, false, true); }
                catch (Exception ex) { Log.Error($"FixDestructibleReferences: CreateEmpty failed for '{m.Entity.Name}': {ex.Message}"); continue; }
                if (holder == null) continue;
                holder.Name = "debris_holder";
                holder.AddTag(m.Tag);
                var frame = m.Entity.GetGlobalFrame();
                holder.SetGlobalFrame(ref frame, true);
                m.Entity.AddChild(holder, true);                            // keep world transform
                BannerlordSceneToolkit.EditorFrameSync.Sync(holder);
                fixedCount++;
            }
            return fixedCount;
        }

        // ============================================================
        // 2. LIVE merchant_stand_market PREFAB
        // ============================================================
        // GetPrefabName() is the live prefab link (the prefab= attribute in the saved file). A broken /
        // baked copy only has GetOldPrefabName(), and that is the version that loaded, so it is ignored.
        private static string LivePrefab(GameEntity e)
        {
            try { return e.GetPrefabName() ?? ""; } catch { return ""; }
        }

        private static bool IsLiveCursed(GameEntity e) =>
            string.Equals(LivePrefab(e), CursedPrefab, StringComparison.OrdinalIgnoreCase);

        public static List<Finding> CheckCursedPrefabs(List<GameEntity> all)
        {
            var findings = new List<Finding>();

            var cursed = all.Where(IsLiveCursed).ToList();
            if (cursed.Count > 0)
            {
                findings.Add(Err($"{cursed.Count} live '{CursedPrefab}' prefab(s) (SandBox). Saved as a live prefab it crashes MP clients on " +
                                 "map load (CC_84 bisect); the server loads it fine, so a server test won't catch it. " +
                                 "Use 'Unparent Stands' to break the link (children are kept in place), or delete it."));
                foreach (var e in cursed) findings.Add(Err($"  --- {Path(e)}  at {Pos(e)}"));
            }

            var siblings = all.Where(e => UntestedSiblingPrefabs.Any(s => string.Equals(LivePrefab(e), s, StringComparison.OrdinalIgnoreCase))).ToList();
            if (siblings.Count > 0)
            {
                findings.Add(Warn($"{siblings.Count} live reference(s) to other SandBox merchant stands from the same prefab file. Untested in MP; " +
                                  "SandBox prefabs are not loaded in multiplayer. Break the link or avoid them to be safe."));
                foreach (var e in siblings) findings.Add(Warn($"  --- {LivePrefab(e)}  {Path(e)}  at {Pos(e)}"));
            }

            if (findings.Count == 0) findings.Add(Info("No live merchant_stand_market or other SandBox merchant stand prefabs. OK."));
            return findings;
        }

        // Breaks the prefab link on every live merchant_stand_market, so the next save writes its
        // children inline (old_prefab_name), which is the version that loaded on CC_84. Nothing moves.
        // Returns the number of stands whose link is gone afterwards.
        public static int UnparentCursedPrefabs(Scene scene, List<GameEntity> all)
        {
            int count = 0;
            foreach (var stand in all.Where(IsLiveCursed).ToList())
            {
                try { stand.BreakPrefab(); }
                catch (Exception ex) { Log.Error($"Unparent Stands: BreakPrefab failed on '{stand.Name}': {ex.Message}"); continue; }
                if (!IsLiveCursed(stand)) count++;
                else Log.Warn($"Unparent Stands: '{stand.Name}' still reports a live prefab link after BreakPrefab.");
            }
            return count;
        }
    }
}
