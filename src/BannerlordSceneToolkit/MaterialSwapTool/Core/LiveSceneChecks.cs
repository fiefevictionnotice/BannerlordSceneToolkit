using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Core
{
    // Live-API rewrite of the Scene Analyzer's checks - originally built against the offline
    // scene.xscene file (see BsaSceneChecks), migrated here because the live GameEntity API turns
    // out to cover everything that matters: HasTag/HasScriptComponent/GetChildren/Parent/
    // GetGlobalFrame/GetLocalScale/BodyFlag/Remove/Instantiate(scene, prefab, frame) are all real
    // (confirmed via decompile). Live checks reflect your CURRENT unsaved editor state instead of
    // only what's on disk, and live actions (delete, tag, swap) apply instantly and visibly
    // instead of needing "reload the scene." Logic and reference data still ported from
    // BannerlordSceneAnalyzer's Analyze-BannerlordScene.ps1 (Skirmish/Editor-Spawn originally from
    // Gotha's BL_AddTestScene) - only the DATA SOURCE changed, not the detection rules.
    //
    // A few source checks stayed out of scope here because they're not meaningfully live-API-able:
    // terrain layer count and flora texture layers live in the binary terrain data, not on any
    // GameEntity; "unresolved custom prefab references" is a question about the SAVED FILE's
    // baked-vs-live structure (an efficiency/build-hygiene concern, not a runtime one) that stops
    // meaning anything once the scene is already loaded and every reference has resolved.
    public static class LiveSceneChecks
    {
        // ============================================================
        // REFERENCE DATA
        // ============================================================
        private static Dictionary<string, string> _misleadingPhysics;
        private static Dictionary<string, bool> _knownBadLods;
        private static Dictionary<string, bool> _worstLods;
        private static Dictionary<string, string> _interiorEntities;
        private static List<string> _interiorGoodPatterns;
        private static List<string> _interiorUntestedPatterns;
        private static List<string> _sittablePrefabs;

        private static string ModuleDir => System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ResolvePath(string fileName)
        {
            var overridePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord", "MaterialSwapTool", fileName);
            if (File.Exists(overridePath)) return overridePath;
            return System.IO.Path.Combine(ModuleDir, "ReferenceData", fileName);
        }

        private static IEnumerable<string> LoadRefLines(string fileName)
        {
            var path = ResolvePath(fileName);
            if (!File.Exists(path)) yield break;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                yield return line;
            }
        }

        private static Dictionary<string, string> MisleadingPhysics
        {
            get
            {
                if (_misleadingPhysics != null) return _misleadingPhysics;
                _misleadingPhysics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in LoadRefLines("Misleading_Physics.txt"))
                {
                    var parts = line.Split(new[] { '|' }, 2);
                    _misleadingPhysics[parts[0].Trim()] = parts.Length > 1 ? parts[1].Trim() : "";
                }
                return _misleadingPhysics;
            }
        }

        private static void EnsureLodsLoaded()
        {
            if (_knownBadLods != null) return;
            _knownBadLods = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _worstLods = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in LoadRefLines("Known_Bad_LODs.txt"))
            {
                if (line.Contains(":WORST"))
                {
                    var name = line.Replace(":WORST", "").Trim();
                    _worstLods[name] = true;
                    _knownBadLods[name] = true;
                }
                else
                {
                    _knownBadLods[line.Trim()] = true;
                }
            }
        }

        private static void EnsureInteriorLoaded()
        {
            if (_interiorEntities != null) return;
            _interiorEntities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _interiorGoodPatterns = new List<string>();
            _interiorUntestedPatterns = new List<string>();
            foreach (var line in LoadRefLines("Interior_Entities.txt"))
            {
                var parts = line.Split(new[] { '|' }, 2);
                var name = parts[0].Trim();
                var cls = parts.Length > 1 ? parts[1].Trim() : "UNTESTED";
                if (name.StartsWith("PATTERN:"))
                {
                    var pat = name.Substring(8).Trim();
                    (cls == "KNOWN_GOOD" ? _interiorGoodPatterns : _interiorUntestedPatterns).Add(pat);
                }
                else
                {
                    _interiorEntities[name] = cls;
                }
            }
        }

        private static bool WildcardMatch(string text, string pattern) =>
            System.Text.RegularExpressions.Regex.IsMatch(text,
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static string GetInteriorClass(string name)
        {
            EnsureInteriorLoaded();
            if (_interiorEntities.TryGetValue(name, out var cls)) return cls;
            if (_interiorGoodPatterns.Any(p => WildcardMatch(name, p))) return "KNOWN_GOOD";
            if (_interiorUntestedPatterns.Any(p => WildcardMatch(name, p))) return "UNTESTED";
            return null;
        }

        private static List<string> SittablePrefabs
        {
            get
            {
                if (_sittablePrefabs != null) return _sittablePrefabs;
                _sittablePrefabs = LoadRefLines("Sittable_MP_Prefabs.txt").ToList();
                return _sittablePrefabs;
            }
        }

        private static readonly string[] UsableScripts =
        {
            "UsablePlace", "chair_sit__position_script", "sit_point", "sp_sit", "animation_point_sit", "interact_point_sit",
        };

        private static Finding Err(string msg) => new Finding { Severity = "ERROR", Message = msg };
        private static Finding Warn(string msg) => new Finding { Severity = "WARNING", Message = msg };
        private static Finding Info(string msg) => new Finding { Severity = "INFO", Message = msg };

        // ============================================================
        // ENTITY COLLECTION
        // ============================================================
        public static List<GameEntity> CollectAll(Scene scene)
        {
            var list = new List<GameEntity>();
            scene.GetEntities(ref list);
            return list.Where(EntitySelector.IsValidEntity).ToList();
        }

        private static string Label(GameEntity e) => e.Name;

        // ============================================================
        // CHECK: BUGGED PHYSICS SHAPES (reuses BsaSceneChecks' reference-data loader)
        // ============================================================
        public static List<Finding> CheckBuggedPhysics(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                var label = Label(e);
                if (label != null && BsaSceneChecks.BuggedPhysicsDict.ContainsKey(label))
                    found[label] = found.TryGetValue(label, out var c) ? c + 1 : 1;
            }
            foreach (var kv in found.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var info = BsaSceneChecks.BuggedPhysicsDict[kv.Key];
                var msg = $"Bugged physics entity '{kv.Key}' x{kv.Value}: {info.Description}";
                findings.Add(info.Severity == "ERROR" ? Err(msg) : Warn(msg));
            }
            if (findings.Count == 0) findings.Add(Info("No bugged physics shape entities found."));
            return findings;
        }

        public static int TagBuggedPhysics(List<GameEntity> all)
        {
            int tagged = 0;
            foreach (var e in all)
            {
                var label = Label(e);
                if (label != null && BsaSceneChecks.BuggedPhysicsDict.ContainsKey(label) && !e.HasTag("BSA_BUGGED_PHYSICS_SHAPE"))
                {
                    e.AddTag("BSA_BUGGED_PHYSICS_SHAPE");
                    tagged++;
                }
            }
            return tagged;
        }

        // ============================================================
        // CHECK: MISLEADING PHYSICS
        // ============================================================
        public static List<Finding> CheckMisleadingPhysics(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                var label = Label(e);
                if (label != null && MisleadingPhysics.ContainsKey(label))
                    found[label] = found.TryGetValue(label, out var c) ? c + 1 : 1;
            }
            foreach (var kv in found.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                findings.Add(Warn($"Misleading physics entity '{kv.Key}' x{kv.Value}: appears to have gaps/openings but collision is solid - players may get stuck."));
            if (findings.Count == 0) findings.Add(Info("No misleading-physics entities found."));
            return findings;
        }

        // ============================================================
        // CHECK: BAD/EARLY-POPPING LODS (name-list based, distinct from LodMismatchChecker's
        // same-entity full-vs-LOD5 color/material comparison)
        // ============================================================
        public static List<Finding> CheckKnownBadLods(List<GameEntity> all)
        {
            EnsureLodsLoaded();
            var findings = new List<Finding>();
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                var label = Label(e);
                if (label != null && _knownBadLods.ContainsKey(label))
                    found[label] = found.TryGetValue(label, out var c) ? c + 1 : 1;
            }
            foreach (var kv in found.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var worstNote = _worstLods.ContainsKey(kv.Key) ? " [WORST - pops to near-invisible within easy visual range]" : "";
                findings.Add(Warn($"Bad/early-popping LOD entity '{kv.Key}' x{kv.Value}{worstNote}"));
            }
            if (findings.Count == 0) findings.Add(Info("No known bad-LOD entities found."));
            return findings;
        }

        // ============================================================
        // CHECK: LOD SUBSTITUTION REMINDERS (reuses BsaSceneChecks' LodSubstitutions data;
        // the actual REPLACE action now goes through LivePrefabSwapper)
        // ============================================================
        public static List<Finding> CheckLodSubstitutions(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                var label = Label(e);
                if (label != null && BsaSceneChecks.LodSubstitutions.ContainsKey(label))
                    counts[label] = counts.TryGetValue(label, out var c) ? c + 1 : 1;
            }
            foreach (var kv in counts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                findings.Add(Info($"Replace {kv.Value}x '{kv.Key}' -> '{BsaSceneChecks.LodSubstitutions[kv.Key]}' (use Prefab Swapper)"));
            if (findings.Count == 0) findings.Add(Info("No known LOD substitutions apply to this scene."));
            return findings;
        }

        public static List<LivePrefabSwapper.SwapResult> ApplyLodSubstitutions(Scene scene, List<GameEntity> all)
        {
            var targets = all.Where(e => Label(e) != null && BsaSceneChecks.LodSubstitutions.ContainsKey(Label(e))).ToList();
            var pairs = targets.Select(e => (e, BsaSceneChecks.LodSubstitutions[Label(e)])).ToList();
            return LivePrefabSwapper.SwapMany(scene, pairs);
        }

        // ============================================================
        // CHECK: MAP_ PREFIX ENTITIES
        // ============================================================
        public static List<Finding> CheckMapPrefixEntities(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                var label = Label(e);
                if (label != null && label.StartsWith("map_", StringComparison.OrdinalIgnoreCase))
                    counts[label] = counts.TryGetValue(label, out var c) ? c + 1 : 1;
            }
            foreach (var kv in counts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                findings.Add(Info($"map_ prefixed entity '{kv.Key}' x{kv.Value} - world-map asset, usually fine but visually inappropriate in non-map scenes."));
            if (findings.Count == 0) findings.Add(Info("No map_ prefix entities found."));
            return findings;
        }

        // ============================================================
        // CHECK: INTERIOR ENTITIES
        // ============================================================
        public static List<Finding> CheckInteriorEntities(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var flagged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in all)
            {
                if (!IsInteriorEntity(e, out var label)) continue;
                var cls = GetInteriorClass(label);
                if (cls == "KNOWN_GOOD") known.Add(label);
                else flagged[label] = flagged.TryGetValue(label, out var c) ? c + 1 : 1;
            }

            foreach (var name in known.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                findings.Add(Info($"'{name}' - known-good interior entity."));
            foreach (var kv in flagged.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                findings.Add(Warn($"Interior entity (verify MP compatibility): '{kv.Key}' x{kv.Value}"));
            if (findings.Count == 0) findings.Add(Info("No interior entities found."));
            return findings;
        }

        // ============================================================
        // CHECK: NON-UNIFORM SCALE ON PHYSICS ENTITIES
        // "Has physics" is approximated as BodyFlag != None - there's no direct
        // HasPhysicsShape live query, but a non-default body flag is a reasonable proxy for
        // "this entity participates in collision."
        // ============================================================
        public static List<Finding> CheckNonUniformScale(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            const double eps = 0.001;
            foreach (var e in all)
            {
                // Most placed prefabs (confirmed while building the wall_plank fixed-prefab copy)
                // are a plain root container entity - name/transform only, no BodyFlag of its own -
                // whose CHILDREN carry the actual physics/mesh components. Gating on e.BodyFlag
                // alone missed almost every building-scale prefab (e.g. european_city_house_a),
                // since the flag lives one level down, not on the entity you'd select in the
                // editor. Checking one level of children as well catches that common shape without
                // a full recursive scan.
                bool hasPhysics = e.BodyFlag != BodyFlags.None || e.GetChildren().Any(c => c.BodyFlag != BodyFlags.None);
                if (!hasPhysics) continue;
                var s = e.GetLocalScale();
                if (Math.Abs(s.x - s.y) > eps || Math.Abs(s.x - s.z) > eps)
                    findings.Add(Warn($"Non-uniform scale on physics entity '{Label(e)}' - scale: [{s.x:F3}, {s.y:F3}, {s.z:F3}] - physics behaviour undefined."));
            }
            if (findings.Count == 0) findings.Add(Info("No non-uniform scale + physics entities found."));
            return findings;
        }

        // ============================================================
        // CHECK: SITTABLE / ANIMATION-POINT PREFABS
        // ============================================================
        public static List<Finding> CheckSittablePrefabs(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            foreach (var e in all)
            {
                var label = Label(e);
                if (label == null || label.IndexOf("_unsittable", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                string reason = null;
                if (UsableScripts.Any(e.HasScriptComponent))
                {
                    reason = "has a sit-triggering script";
                }
                else
                {
                    foreach (var child in e.GetChildren())
                    {
                        if (UsableScripts.Any(child.HasScriptComponent)) { reason = $"child entity '{child.Name}' has a sit-triggering script"; break; }
                    }
                }
                if (reason == null)
                {
                    var lower = label.ToLowerInvariant();
                    var frag = SittablePrefabs.FirstOrDefault(f => lower.Contains(f.ToLowerInvariant()));
                    if (frag != null) reason = $"name matches known-bad fragment '{frag}' (verify no sit scripts)";
                }

                if (reason != null) findings.Add(Warn($"Sittable/animation-interact entity '{label}': {reason}. Verify intentional in MP or strip the interact scripts."));
            }
            if (findings.Count == 0) findings.Add(Info("No sittable/animation-interact prefabs detected."));
            return findings;
        }

        // ============================================================
        // CHECK: WALK/BARRIER VOLUME PRESENCE
        // ============================================================
        public static List<Finding> CheckWalkBarrierVolumes(List<GameEntity> all)
        {
            int walk = all.Count(e => Label(e)?.IndexOf("walk_volume", StringComparison.OrdinalIgnoreCase) >= 0);
            int barrier = all.Count(e => Label(e)?.IndexOf("barrier_volume", StringComparison.OrdinalIgnoreCase) >= 0);
            var findings = new List<Finding> { Info($"walk_volume entities: {walk}"), Info($"barrier_volume entities: {barrier}") };
            if (walk == 0 && barrier == 0)
                findings.Add(Info("No walk_volume or barrier_volume entities. For Skirmish/Siege these define traversable AI areas - omitting them lets AI use the full terrain mesh (may degrade performance)."));
            return findings;
        }

        // ============================================================
        // CHECK: ENTITIES OUTSIDE BORDER_SOFT BOUNDARY (2D convex hull, XZ plane)
        // ============================================================
        private static readonly string[] OobExempt = { "border_soft", "spawn_visual", "mp_camera_start_pos", "envmap_prop", "envmap_probe", "flee_line" };

        public static List<Finding> CheckEntitiesOutsideBorder(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var (status, oobEntities) = GetEntitiesOutsideBorder(all);
            if (status != null) { findings.Add(status); return findings; }

            if (oobEntities.Count == 0)
            {
                findings.Add(Info("All entities are within the border_soft boundary (+10 unit margin). OK."));
            }
            else
            {
                findings.Add(Warn($"{oobEntities.Count} entity/entities lie outside the soft border (+10 units)."));
                foreach (var e in oobEntities)
                {
                    var p = e.GetGlobalFrame().origin;
                    findings.Add(Info($"  OOB: '{Label(e)}' at [{p.x:F2}, {p.y:F2}, {p.z:F2}]"));
                }
            }
            return findings;
        }

        // Shared by the check above and the two fix actions below - returns null status + the
        // actual entity list when the boundary could be computed, or a non-null Info status (and an
        // empty list) when there weren't enough border_soft points to build a hull from.
        private static (Finding status, List<GameEntity> oob) GetEntitiesOutsideBorder(List<GameEntity> all)
        {
            var borderPts = all.Where(e => Label(e) == "border_soft")
                // ROOT CAUSE of "tag outside soft border tagged EVERYTHING", fixed 2026-08-20:
                // this used (x, z) - but Bannerlord is Z-UP, so z is HEIGHT and the ground plane
                // is X-Y. Proven against fief_material_test_4's own scene.xscene, where all 8
                // border_soft markers sit at z=0.000 and vary only in x (73..251) and y (57..375),
                // while a building nearby sits at z=9.241. Sampling (x, z) therefore gave every
                // marker an identical second coordinate - perfectly collinear - so the hull
                // collapsed to 2 vertices, PointInPolygon2D's `n < 3` guard returned false, and
                // every entity in the scene read as outside the border.
                .Select(e => { var p = e.GetGlobalFrame().origin; return new[] { (double)p.x, (double)p.y }; })
                .ToList();

            if (borderPts.Count < 3)
            {
                Log.Info($"[Boundary] SKIP: only {borderPts.Count} border_soft marker(s) found (need >= 3)");
                return (Info(borderPts.Count == 0
                    ? "No border_soft entities with positions found - boundary check skipped."
                    : $"Only {borderPts.Count} border_soft position(s) found (need >= 3) - boundary check skipped."),
                    new List<GameEntity>());
            }

            // CONFIRMED BUG, fixed 2026-08-20: only the RAW POINT COUNT was validated, never the
            // resulting hull. ConvexHull2D legitimately returns fewer than 3 vertices when the
            // border_soft markers are coincident or collinear (the `cross <= 0` pop discards every
            // intermediate collinear point, leaving just the two endpoints). PointInPolygon2D then
            // hits its `if (n < 3) return false` guard and declares EVERY entity outside - so a
            // scene with a degenerate border ring silently tagged the entire scene OOB instead of
            // reporting that it couldn't build a boundary. Distinct-point and area checks below.
            var distinct = borderPts
                .GroupBy(p => $"{p[0]:F3}|{p[1]:F3}")
                .Select(g => g.First())
                .ToList();

            if (distinct.Count < 3)
            {
                Log.Info($"[Boundary] SKIP: {borderPts.Count} markers but only {distinct.Count} distinct positions");
                return (Info($"{borderPts.Count} border_soft marker(s) found but only {distinct.Count} distinct position(s) " +
                             "- they're stacked on the same spot, so no boundary can be built. Boundary check skipped."),
                        new List<GameEntity>());
            }

            var hull = ConvexHull2D(distinct);
            if (hull.Length < 3 || PolygonArea2D(hull) < 1.0)
            {
                Log.Info($"[Boundary] SKIP: hull={hull.Length} vertices area={PolygonArea2D(hull):F2} - degenerate");
                return (Info($"border_soft markers ({distinct.Count} distinct) are collinear or enclose no area " +
                             $"(hull vertices: {hull.Length}, area: {PolygonArea2D(hull):F2}) - no usable boundary. Boundary check skipped."),
                        new List<GameEntity>());
            }

            var expanded = ExpandHull2D(hull, 10.0);

            var considered = 0;
            var oob = new List<GameEntity>();
            foreach (var e in all)
            {
                var label = Label(e);
                if (label != null && OobExempt.Any(ex => label.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                considered++;
                var p = e.GetGlobalFrame().origin;
                if (!PointInPolygon2D(p.x, p.y, expanded)) oob.Add(e);   // X-Y ground plane, see above
            }

            // "Everything is outside" is never a real result - a scene's own contents sit inside
            // its border by definition. It means the hull is wrong (bad winding, markers not
            // actually forming the ring, wrong plane). Refuse rather than hand back a list that
            // would tag the whole scene.
            if (considered > 0 && oob.Count == considered)
            {
                return (Warn($"Boundary rejected: ALL {considered} entities computed as outside the border_soft hull " +
                             $"({hull.Length} vertices, area {PolygonArea2D(hull):F0}). That can't be right, so nothing was " +
                             "flagged. Check the border_soft markers actually ring the playable area."),
                        new List<GameEntity>());
            }

            Log.Info($"[Boundary] border_soft markers={borderPts.Count} distinct={distinct.Count} hull={hull.Length} " +
                     $"area={PolygonArea2D(hull):F0} considered={considered} outside={oob.Count}");
            return (null, oob);
        }

        // Shoelace formula, absolute value - used only to reject degenerate (zero-area) hulls.
        private static double PolygonArea2D(double[][] poly)
        {
            if (poly.Length < 3) return 0.0;
            double sum = 0.0;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                sum += (poly[j][0] * poly[i][1]) - (poly[i][0] * poly[j][1]);
            return Math.Abs(sum) * 0.5;
        }

        public const string InvisibleTag = "hidden_entity";

        // Tags entities that don't render, so they can be found by tag search in the editor -
        // hidden props are easy to lose track of and still cost load time / ship in the scene.
        //
        // IsVisibleIncludeParents() is the test that matters: an entity whose own flag is ON but
        // whose parent is hidden still doesn't render, and is just as easy to lose. Both API names
        // verified by reflecting TaleWorlds.Engine.GameEntity rather than assumed. The two causes
        // are counted separately so the status line can distinguish "you hid this" from "it
        // inherited hidden from a parent", which need different fixes.
        public static (int tagged, int ownFlag, int inherited) TagInvisibleEntities(List<GameEntity> all)
        {
            int tagged = 0, ownFlag = 0, inherited = 0;
            var undoBatch = new List<GameEntity>();
            foreach (var e in all)
            {
                if (!EntitySelector.IsValidEntity(e)) continue;

                bool visible;
                bool ownVisible;
                try
                {
                    visible = e.IsVisibleIncludeParents();
                    ownVisible = e.GetVisibilityExcludeParents();
                }
                catch (Exception ex)
                {
                    Log.Warn($"TagInvisibleEntities: visibility query failed for '{Label(e)}': {ex.Message}");
                    continue;
                }

                if (visible) continue;
                if (ownVisible) inherited++; else ownFlag++;
                if (!e.HasTag(InvisibleTag)) { e.AddTag(InvisibleTag); tagged++; undoBatch.Add(e); }
            }
            // Precise undo: removes the tag only from what THIS run tagged, unlike the
            // blanket Untag button which clears the tag scene-wide (see EditUndo).
            BannerlordSceneToolkit.EditUndo.CaptureTags("Tag invisible", InvisibleTag, undoBatch);

            Log.Info($"[Invisible] scanned={all.Count} hidden={ownFlag + inherited} (ownFlag={ownFlag} inheritedFromParent={inherited}) newlyTagged={tagged}");
            return (tagged, ownFlag, inherited);
        }

        public static int UntagInvisibleEntities(List<GameEntity> all)
        {
            int removed = 0;
            foreach (var e in all)
                if (e.HasTag(InvisibleTag)) { e.RemoveTag(InvisibleTag); removed++; }
            return removed;
        }

        // Interior entities (empire_house_b_interior and friends) are SP room dressing - in MP they
        // are usually invisible and just cost load time, which is why CheckInteriorEntities already
        // flags them. This is the bulk removal for that finding.
        //
        // Matching is the SAME rule the check uses - name contains "interior" - so what gets
        // deleted is exactly what the check reports, with no second definition to drift.
        public static List<(string Name, int Count, bool KnownGood)> PreviewInteriorEntities(List<GameEntity> all)
        {
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                if (!IsInteriorEntity(e, out var label)) continue;
                byName[label] = byName.TryGetValue(label, out var c) ? c + 1 : 1;
            }
            return byName
                .OrderByDescending(kv => kv.Value)
                .Select(kv => (kv.Key, kv.Value, GetInteriorClass(kv.Key) == "KNOWN_GOOD"))
                .ToList();
        }

        // SELECT IN EDITOR.
        //
        // 2026-08-20: this used to call GameEntity.SelectEntityOnEditor() per entity. That method
        // exists and throws nothing, but it only flips a per-entity native flag - the editor's own
        // selection (the one the triad gizmo and the entity list read) never learned about it, so
        // our popup count was right and the editor still showed nothing selected.
        //
        // The real API is TaleWorlds.Engine.Utilities.SelectEntities(List<GameEntity>) - a static
        // that maps to the native binding "select_entities_in_editor" (confirmed by reflecting
        // TaleWorlds.Engine.dll: IUtil.SelectEntities + enm_IMono_Util_select_entities_in_editor).
        // It sets the editor selection wholesale. Utilities.GetSelectedEntities(ref list) reads it
        // back, which is how we now verify, and how we clear whatever was selected before.
        public static int SelectInEditor(List<GameEntity> all, Func<GameEntity, bool> predicate)
        {
            var matches = new List<GameEntity>();
            foreach (var e in all)
            {
                if (!EntitySelector.IsValidEntity(e)) continue;
                bool match;
                try { match = predicate(e); } catch { continue; }
                if (match) matches.Add(e);
            }
            return SetEditorSelection(matches);
        }

        // Queues the selection instead of applying it inline - see DeferredSelection for the
        // proof that applying it inside the click handler is why this silently did nothing.
        // Returns how many MATCHED; the applied count is logged a couple of ticks later.
        public static int SetEditorSelection(List<GameEntity> entities)
        {
            var wanted = entities ?? new List<GameEntity>();
            DeferredSelection.Request(wanted);
            return wanted.Count;
        }

        // The actual work, run from the tick loop rather than from a button's click handler.
        public static int ApplyEditorSelectionNow(List<GameEntity> wanted)
        {
            if (wanted == null) wanted = new List<GameEntity>();

            // Drop anything the scene has since disposed - a queued selection can outlive its
            // entities if the scene changed in the two ticks we waited.
            wanted = wanted.Where(EntitySelector.IsValidEntity).ToList();

            // SELECT FIRST, THEN CLEAR WHATEVER IS LEFT OVER - not the other way round.
            //
            // CONFIRMED FREEZE, fixed 2026-08-22. This used to clear the old selection first, by
            // calling GameEntity.DeselectEntityOnEditor() once per previously-selected entity.
            // Measured from tool.log, that call costs roughly 12ms EACH - the editor evidently
            // does real work per deselect - so the cost scaled with whatever happened to be
            // selected BEFORE, not with what was being selected:
            //     10 selected  ->  101ms
            //     120 selected -> 1695ms
            //     137 selected -> 1728ms
            // Reported as "the editor freezes for a bit when I hit Ctrl+Shift+P" - that shortcut
            // promotes a whole selection to its roots, so it always has a large old selection to
            // throw away. Reflection over TaleWorlds.Engine confirms there is NO bulk deselect
            // (Utilities exposes SelectEntities / GetSelectedEntities / CreateSelectionInEditor;
            // deselect exists only as per-entity GameEntity.DeselectEntityOnEditor), so the loop
            // cannot just be swapped for a bulk call.
            //
            // What it CAN be is skipped. Utilities.SelectEntities is a single bulk native call
            // (IUtil.SelectEntities(UIntPtr[], Int32)) that sets the selection wholesale, so once
            // it has run there is normally nothing stale left to clear. In this order the
            // per-entity loop only touches entities the bulk call actually left behind - usually
            // none - while still preserving the original "anything selected outside our scope
            // gets cleared too" guarantee if the engine ever turns out to ADD rather than
            // replace. The log line reports how many stale entries it had to clear and how long
            // the whole apply took, so the engine's real semantics stay visible instead of
            // assumed.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            if (wanted.Count > 0)
            {
                try { Utilities.SelectEntities(wanted); }
                catch (Exception ex)
                {
                    Log.Warn($"[SelectInEditor] Utilities.SelectEntities threw: {ex.Message}. Falling back to per-entity select.");
                    foreach (var e in wanted) { try { e.SelectEntityOnEditor(); } catch { } }
                }
            }

            int deselected = 0;
            try
            {
                var keep = new HashSet<UIntPtr>();
                foreach (var e in wanted) keep.Add(e.Pointer);

                var current = new List<GameEntity>();
                Utilities.GetSelectedEntities(ref current);
                foreach (var e in current)
                {
                    if (e == null || e.Pointer == UIntPtr.Zero) continue;
                    if (keep.Contains(e.Pointer)) continue;   // we asked for this one; leave it
                    try { e.DeselectEntityOnEditor(); deselected++; } catch { }
                }
            }
            catch (Exception ex) { Log.Warn($"[SelectInEditor] clearing stale selection failed: {ex.Message}"); }

            if (wanted.Count == 0)
            {
                Log.Info($"[SelectInEditor] nothing to select; cleared {deselected} entity(ies) in {stopwatch.ElapsedMilliseconds}ms.");
                return 0;
            }

            // Verify against MBEditor.IsEntitySelected specifically - that is what EntitySelector's
            // per-tick cache and Manual mode read, and it is the reading that used to disagree
            // with our own success message one frame later.
            int viaEditor = 0;
            foreach (var e in wanted) { try { if (MBEditor.IsEntitySelected(e)) viaEditor++; } catch { } }

            Log.Info($"[SelectInEditor] applied: {viaEditor}/{wanted.Count} confirmed selected by MBEditor; " +
                     $"{deselected} stale deselected; {stopwatch.ElapsedMilliseconds}ms.");
            if (viaEditor == 0)
                Log.Warn("[SelectInEditor] the editor reports NOTHING selected right after the call - the deferral is not long enough, or SelectEntities is not the right entry point.");
            return viaEditor;
        }

        public static bool IsInvisible(GameEntity e)
        {
            try { return !e.IsVisibleIncludeParents(); } catch { return false; }
        }

        public static bool IsUnbrokenNonNativePrefab(GameEntity e) => IsUnbrokenNonNative(e, out _);

        private static HashSet<string> _nativePrefabs;
        private static HashSet<string> _customPrefabs;

        private static HashSet<string> NativePrefabs
        {
            get
            {
                if (_nativePrefabs != null) return _nativePrefabs;
                _nativePrefabs = new HashSet<string>(LoadRefLines("Known_Native_Prefabs.txt").Select(l => l.Trim()),
                                                     StringComparer.OrdinalIgnoreCase);
                return _nativePrefabs;
            }
        }

        private static HashSet<string> CustomPrefabs
        {
            get
            {
                if (_customPrefabs != null) return _customPrefabs;
                _customPrefabs = new HashSet<string>(LoadRefLines("Custom_Module_Prefabs.txt").Select(l => l.Split('|')[0].Trim()),
                                                     StringComparer.OrdinalIgnoreCase);
                return _customPrefabs;
            }
        }

        public const string UnbrokenPrefabTag = "unbroken_custom_prefab";

        // UNBROKEN PREFAB LINKS.
        //
        // An entity with a non-empty GetPrefabName() is still LINKED to a prefab resource - the
        // scene stores a reference, so loading it needs that prefab file present. For a custom
        // prefab that means anyone without your module gets a broken scene. BreakPrefab() bakes
        // the contents into the scene so the entity keeps working with no file dependency.
        // (GetPrefabName / BreakPrefab both verified present on GameEntity by reflection.)
        //
        // Classification is deliberately THREE buckets, not "native vs custom":
        // Known_Native_Prefabs.txt holds only 265 names auto-generated from 17 official maps, so it
        // is a partial native list. Treating "not in that list" as "custom" would mislabel every
        // native prefab those 17 maps happen not to use. Unknown stays its own bucket and is
        // reported as unknown.
        public enum PrefabOrigin { Native, Custom, Unknown }

        public static PrefabOrigin ClassifyPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return PrefabOrigin.Unknown;
            if (NativePrefabs.Contains(prefabName)) return PrefabOrigin.Native;
            if (CustomPrefabs.Contains(prefabName)) return PrefabOrigin.Custom;
            return PrefabOrigin.Unknown;
        }

        public static List<(string Prefab, int Count, PrefabOrigin Origin)> PreviewUnbrokenPrefabs(List<GameEntity> all)
        {
            // Routed through IsUnbrokenNonNative rather than re-deriving the test here, so the
            // preview, the tag, the select and the break can never disagree about what counts -
            // this originally had its own copy of the logic and silently ignored the prefix filter.
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                if (!IsUnbrokenNonNative(e, out var prefab)) continue;
                byName[prefab] = byName.TryGetValue(prefab, out var c) ? c + 1 : 1;
            }
            return byName
                .Select(kv => (kv.Key, kv.Value, ClassifyPrefab(kv.Key)))
                .OrderByDescending(x => x.Item2)
                .ToList();
        }

        // Comma-separated name prefixes that scope the unbroken-prefab operations, e.g.
        // "ff_,fief_". Empty means "everything not positively identified as native".
        //
        // This exists because the native list is not authoritative: Known_Native_Prefabs.txt
        // holds 265 names auto-generated from 17 official maps, so the overwhelming majority of
        // real native prefabs classify as Unknown and were being offered as candidates - which
        // made Tag/Select/Break match essentially the whole scene. Your own prefabs follow a
        // naming convention, so a prefix is a far more reliable filter than an incomplete
        // blocklist, and it is something you control rather than something I have to guess.
        public static string UnbrokenPrefixFilter = "";

        private static string[] PrefixList()
        {
            if (string.IsNullOrWhiteSpace(UnbrokenPrefixFilter)) return new string[0];
            return UnbrokenPrefixFilter.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        private static bool IsUnbrokenNonNative(GameEntity e, out string prefab)
        {
            prefab = null;
            if (!EntitySelector.IsValidEntity(e)) return false;
            try { prefab = e.GetPrefabName(); } catch { return false; }
            if (string.IsNullOrEmpty(prefab)) return false;
            if (ClassifyPrefab(prefab) == PrefabOrigin.Native) return false;

            var prefixes = PrefixList();
            if (prefixes.Length == 0) return true;
            foreach (var p in prefixes)
                if (prefab.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static (int tagged, int found) TagUnbrokenPrefabs(List<GameEntity> all)
        {
            int tagged = 0, found = 0;
            var undoBatch = new List<GameEntity>();
            foreach (var e in all)
            {
                if (!IsUnbrokenNonNative(e, out _)) continue;
                found++;
                if (!e.HasTag(UnbrokenPrefabTag)) { e.AddTag(UnbrokenPrefabTag); tagged++; undoBatch.Add(e); }
            }
            // Precise undo: removes the tag only from what THIS run tagged, unlike the
            // blanket Untag button which clears the tag scene-wide (see EditUndo).
            BannerlordSceneToolkit.EditUndo.CaptureTags("Tag unbroken prefabs", UnbrokenPrefabTag, undoBatch);

            Log.Info($"[UnbrokenPrefab] found={found} newlyTagged={tagged}");
            return (tagged, found);
        }

        public static int UntagUnbrokenPrefabs(List<GameEntity> all)
        {
            int removed = 0;
            foreach (var e in all)
                if (e.HasTag(UnbrokenPrefabTag)) { e.RemoveTag(UnbrokenPrefabTag); removed++; }
            return removed;
        }

        // Breaks the prefab link, keeping the entity and its contents in the scene. Re-checks
        // validity per entity: BreakPrefab restructures the entity's own children, so anything
        // captured earlier in the list can be stale by the time the loop reaches it.
        public static int BreakUnbrokenPrefabs(List<GameEntity> all)
        {
            int broken = 0;
            foreach (var e in all)
            {
                if (!IsUnbrokenNonNative(e, out var prefab)) continue;
                try
                {
                    Log.Info($"[NativeTrace] BreakPrefab '{prefab}'");
                    e.BreakPrefab();
                    broken++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"BreakUnbrokenPrefabs: failed on '{prefab}': {ex.Message}");
                }
            }
            Log.Info($"[UnbrokenPrefab] broken={broken}");
            return broken;
        }

        // ONE definition of "is this an interior entity", shared by the check, the tag and the
        // delete so they can never disagree about what they're operating on.
        //
        // Matching entity Name alone was not enough, on two counts proven against a real scene:
        //   1. Most placed entities have NO name attribute - fief_material_test_4 has 50 stored as
        //      <game_entity prefab="..."> with no name at all - so a name-only rule can silently
        //      skip them. GetPrefabName() (verified present on GameEntity by reflection) is the
        //      identity that actually survives for those.
        //   2. Interior_Entities.txt lists empire_screen_a as a KNOWN_GOOD interior entity, and it
        //      has no "interior" substring anywhere in it. The reference data itself documents
        //      exceptions the substring rule cannot see.
        public static bool IsInteriorEntity(GameEntity e, out string label)
        {
            label = null;
            if (!EntitySelector.IsValidEntity(e)) return false;

            string prefab = null;
            try { prefab = e.GetPrefabName(); } catch { }

            var name = Label(e);
            foreach (var candidate in new[] { name, prefab })
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                if (candidate.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    GetInteriorClass(candidate) != null)
                {
                    label = candidate;
                    return true;
                }
            }
            return false;
        }

        // ---- Interior whitelist editing ----
        //
        // Interior_Entities.txt decides which interiors Delete Interior Entities refuses to touch
        // (KNOWN_GOOD) and which it will remove (UNTESTED). Editing that by hand in a text file
        // meant the protection list was effectively frozen, so it is editable at runtime now -
        // same shipped-default-plus-Documents-override convention as categories and cultures.
        //
        // Entries keep the file's two shapes: an exact entity name, or "PATTERN: some_prefix*"
        // for a wildcard. Both round-trip through here unchanged.
        public class InteriorEntry
        {
            public string Name;          // exact name, or the pattern body when IsPattern
            public bool IsPattern;
            public bool KnownGood;
        }

        private static string InteriorOverridePath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Interior_Entities.txt");

        public static List<InteriorEntry> GetInteriorEntriesForEditing()
        {
            var result = new List<InteriorEntry>();
            foreach (var line in LoadRefLines("Interior_Entities.txt"))
            {
                var parts = line.Split(new[] { '|' }, 2);
                var name = parts[0].Trim();
                var cls = parts.Length > 1 ? parts[1].Trim() : "UNTESTED";
                bool isPattern = name.StartsWith("PATTERN:", StringComparison.OrdinalIgnoreCase);
                if (isPattern) name = name.Substring(8).Trim();
                if (name.Length == 0) continue;
                result.Add(new InteriorEntry { Name = name, IsPattern = isPattern, KnownGood = cls.Equals("KNOWN_GOOD", StringComparison.OrdinalIgnoreCase) });
            }
            return result.OrderByDescending(e => e.KnownGood).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Always writes the Documents override, never the shipped ReferenceData copy - deleting
        // the override stays a clean factory reset, same as the other editors.
        public static void SaveInteriorEntries(List<InteriorEntry> entries)
        {
            var path = InteriorOverridePath;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Interior entity classification - edited from the Interior Whitelist panel.");
            sb.AppendLine("# Format: entity_name | KNOWN_GOOD or UNTESTED");
            sb.AppendLine("# KNOWN_GOOD entities are PROTECTED from Delete Interior Entities.");
            sb.AppendLine("# PATTERN: prefix* | KNOWN_GOOD  matches by wildcard.");
            sb.AppendLine("#");
            foreach (var e in entries)
            {
                var name = (e.Name ?? "").Trim();
                if (name.Length == 0) continue;
                var label = e.IsPattern ? "PATTERN: " + name : name;
                sb.AppendLine($"{label} | {(e.KnownGood ? "KNOWN_GOOD" : "UNTESTED")}");
            }
            File.WriteAllText(path, sb.ToString());
            Log.Info($"Saved interior whitelist ({entries.Count} entries) to {path}");
            ReloadInterior();
        }

        // Drops the cached lists so an edit takes effect without restarting the game.
        public static void ReloadInterior()
        {
            _interiorEntities = null;
            _interiorGoodPatterns = null;
            _interiorUntestedPatterns = null;
        }

        public const string InteriorTag = "interior_entity";

        // Non-destructive counterpart to DeleteInteriorEntities - tag them, look at what you've
        // got in the editor, then decide. Same "name contains interior" rule as the check and the
        // delete, so all three always agree on what an interior entity is.
        public static (int tagged, int found, int knownGood) TagInteriorEntities(List<GameEntity> all)
        {
            int tagged = 0, found = 0, knownGood = 0;
            var undoBatch = new List<GameEntity>();
            foreach (var e in all)
            {
                if (!IsInteriorEntity(e, out var label)) continue;

                found++;
                if (GetInteriorClass(label) == "KNOWN_GOOD") knownGood++;
                if (!e.HasTag(InteriorTag)) { e.AddTag(InteriorTag); tagged++; undoBatch.Add(e); }
            }
            // Precise undo: removes the tag only from what THIS run tagged, unlike the
            // blanket Untag button which clears the tag scene-wide (see EditUndo).
            BannerlordSceneToolkit.EditUndo.CaptureTags("Tag interiors", InteriorTag, undoBatch);

            Log.Info($"[Interior] scanned={all.Count} found={found} knownGood={knownGood} newlyTagged={tagged}");
            return (tagged, found, knownGood);
        }

        public static int UntagInteriorEntities(List<GameEntity> all)
        {
            int removed = 0;
            foreach (var e in all)
                if (e.HasTag(InteriorTag)) { e.RemoveTag(InteriorTag); removed++; }
            return removed;
        }

        // KNOWN_GOOD interior entities are the ones Interior_Entities.txt records as serving a real
        // visual/functional purpose in MP - they are not the SP room dressing this delete exists to
        // clear out. They are now SKIPPED outright rather than deleted-with-a-warning: a bulk
        // delete that quietly removes the things you were told to keep is the wrong default, and
        // the warning was doing no work at the moment it mattered.
        //
        // Tagging and selecting still cover them, because finding them is useful and harmless.
        // Only deletion is protective.
        public static bool IsProtectedInterior(GameEntity e)
        {
            if (!IsInteriorEntity(e, out var label)) return false;
            return GetInteriorClass(label) == "KNOWN_GOOD";
        }

        public static int DeleteInteriorEntities(List<GameEntity> all)
        {
            int deleted = 0, protectedCount = 0;
            foreach (var e in all)
            {
                if (IsProtectedInterior(e)) { protectedCount++; continue; }
                // Re-checked every iteration on purpose: removing a parent takes its children with
                // it, so an entity captured in this list can already be gone by the time we reach
                // it. Calling Remove on a dead entity is exactly the sort of thing that takes the
                // editor down rather than throwing something catchable.
                if (!IsInteriorEntity(e, out var label)) continue;

                try
                {
                    Log.Info($"[NativeTrace] Remove interior entity '{label}'");
                    e.Remove(0);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"DeleteInteriorEntities: failed to remove '{label}': {ex.Message}");
                }
            }
            Log.Info($"[Interior] deleted={deleted} protectedKnownGood={protectedCount}");
            return deleted;
        }

        // How many interiors this scene has that deletion will refuse to touch. The confirm dialog
        // needs this to state the real total up front rather than promising N and removing fewer.
        public static int CountProtectedInteriors(List<GameEntity> all) => all.Count(IsProtectedInterior);

        public const string LockedTag = "locked_entity";

        // Editor lock is <edit_mode_data locked_for_selection="true"/> in scene.xscene - NOT an
        // EntityFlags bit. My first attempt used EntityFlags.NonModifiableFromEditor and detected
        // nothing, because that flag is unrelated. Confirmed by reflecting every TaleWorlds
        // assembly: the only managed reference to this state anywhere is a hotkey category name
        // (DebugHotKeyCategory.EditingManagerHotkeySwitchObjectsLockedForSelection), and MBEditor
        // exposes only IsEntitySelected. There is no managed getter - it's native editor state.
        //
        // So the lock has to be read from the SAVED scene file and matched back onto live
        // entities by position. Consequence worth stating plainly: this only sees locks that have
        // been SAVED. Lock something and don't save, and it won't be found.
        //
        // Position is the match key (0.05 tolerance) rather than name, because most entities here
        // are unnamed - the scene stores prefab="..." with no name attribute at all - and several
        // copies of one prefab are the normal case.
        public static (int tagged, int locked, string error) TagLockedEntities(List<GameEntity> all)
        {
            var sceneName = EntitySelector.CurrentSceneName;
            var dir = Backup.BackupManager.TryFindSceneDir(sceneName);
            if (dir == null)
                return (0, 0, $"Couldn't locate the scene folder for '{sceneName}' on disk, so saved lock state can't be read.");

            var xscene = System.IO.Path.Combine(dir, "scene.xscene");
            if (!File.Exists(xscene))
                return (0, 0, $"No scene.xscene found at '{dir}'.");

            var lockedPositions = new List<double[]>();
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xscene);
                foreach (var ent in doc.Descendants("game_entity"))
                {
                    var edit = ent.Element("edit_mode_data");
                    var lockedAttr = (string)edit?.Attribute("locked_for_selection");
                    if (!string.Equals(lockedAttr, "true", StringComparison.OrdinalIgnoreCase)) continue;

                    var pos = (string)ent.Element("transform")?.Attribute("position");
                    if (string.IsNullOrEmpty(pos)) continue;
                    var parts = pos.Split(',');
                    if (parts.Length < 3) continue;
                    if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                        double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                        double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                        lockedPositions.Add(new[] { x, y, z });
                }
            }
            catch (Exception ex)
            {
                return (0, 0, "Failed to read scene.xscene: " + ex.Message);
            }

            Log.Info($"[Locked] scene='{sceneName}' lockedInFile={lockedPositions.Count}");
            if (lockedPositions.Count == 0)
                return (0, 0, "No entities are marked locked in the saved scene file. (Locks only count once the scene is saved.)");

            int tagged = 0, matched = 0;
            const double tol = 0.05;
            var undoBatch = new List<GameEntity>();
            foreach (var e in all)
            {
                if (!EntitySelector.IsValidEntity(e)) continue;
                var p = e.GetGlobalFrame().origin;
                bool isLocked = lockedPositions.Any(lp =>
                    Math.Abs(lp[0] - p.x) <= tol && Math.Abs(lp[1] - p.y) <= tol && Math.Abs(lp[2] - p.z) <= tol);
                if (!isLocked) continue;

                matched++;
                if (!e.HasTag(LockedTag)) { e.AddTag(LockedTag); tagged++; undoBatch.Add(e); }
            }

            // Precise undo: removes the tag only from what THIS run tagged, unlike the
            // blanket Untag button which clears the tag scene-wide (see EditUndo).
            BannerlordSceneToolkit.EditUndo.CaptureTags("Tag locked", LockedTag, undoBatch);

            Log.Info($"[Locked] matchedLive={matched} newlyTagged={tagged}");
            var err = matched == 0
                ? $"{lockedPositions.Count} locked entit(y/ies) in the saved file, but none matched a live entity by position - the scene has moved since it was saved."
                : null;
            return (tagged, matched, err);
        }

        public static int UntagLockedEntities(List<GameEntity> all)
        {
            int removed = 0;
            foreach (var e in all)
                if (e.HasTag(LockedTag)) { e.RemoveTag(LockedTag); removed++; }
            return removed;
        }

        // Counterpart to TagEntitiesOutsideBorder - there was no way to remove the tag once
        // applied, so a bad run (see the degenerate-hull bug above) left every entity in the scene
        // permanently tagged with no cleanup path short of editing the scene by hand.
        public static int UntagEntitiesOutsideBorder(List<GameEntity> all)
        {
            int removed = 0;
            foreach (var e in all)
            {
                if (e.HasTag(OutsideBorderTag)) { e.RemoveTag(OutsideBorderTag); removed++; }
            }
            return removed;
        }

        public const string OutsideBorderTag = "OOB_soft_border";

        // Tags every entity currently outside the border_soft boundary (+10 unit margin) so they
        // can be re-selected later (search the tag in the editor) without recomputing the hull -
        // purely additive/non-destructive, safe to run and re-run.
        public static (int tagged, Finding status) TagEntitiesOutsideBorder(List<GameEntity> all)
        {
            var (status, oob) = GetEntitiesOutsideBorder(all);
            if (status != null) return (0, status);

            int tagged = 0;
            var undoBatch = new List<GameEntity>();
            foreach (var e in oob)
            {
                if (!e.HasTag(OutsideBorderTag)) { e.AddTag(OutsideBorderTag); tagged++; undoBatch.Add(e); }
            }
            // Precise undo: removes the tag only from what THIS run tagged, unlike the
            // blanket Untag button which clears the tag scene-wide (see EditUndo).
            BannerlordSceneToolkit.EditUndo.CaptureTags("Tag outside border", OutsideBorderTag, undoBatch);

            return (tagged, null);
        }

        // Strips collision (RemovePhysics - the same API TaleWorlds itself uses for e.g. dropped
        // loot props that stay visible but lose their collider) from every entity currently outside
        // the border_soft boundary. Destructive-ish (collision is gone until you undo/re-add it
        // some other way) but leaves the entity and its visible mesh completely intact - this is
        // for greeble/scatter dressing sitting outside the actual play area that doesn't need to be
        // solid, not for deleting anything.
        public static (int cleared, Finding status) RemovePhysicsOutsideBorder(List<GameEntity> all)
        {
            var (status, oob) = GetEntitiesOutsideBorder(all);
            if (status != null) return (0, status);

            int cleared = 0;
            foreach (var e in oob)
            {
                if (e.BodyFlag == BodyFlags.None) continue;
                try { e.RemovePhysics(false); cleared++; }
                catch (Exception ex) { Log.Warn($"RemovePhysicsOutsideBorder: failed on '{Label(e)}': {ex.Message}"); }
            }
            return (cleared, null);
        }

        private static double[][] ConvexHull2D(List<double[]> points)
        {
            if (points.Count < 3) return points.ToArray();
            int pivotIdx = 0;
            for (int i = 1; i < points.Count; i++)
                if (points[i][1] < points[pivotIdx][1] || (points[i][1] == points[pivotIdx][1] && points[i][0] < points[pivotIdx][0]))
                    pivotIdx = i;
            var pivot = points[pivotIdx];
            var sorted = points.Where((_, i) => i != pivotIdx)
                .OrderBy(p => Math.Atan2(p[1] - pivot[1], p[0] - pivot[0]))
                .ToList();

            var hull = new List<double[]> { pivot };
            foreach (var p in sorted)
            {
                while (hull.Count >= 2)
                {
                    var a = hull[hull.Count - 2];
                    var b = hull[hull.Count - 1];
                    var cross = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]);
                    if (cross <= 0) hull.RemoveAt(hull.Count - 1); else break;
                }
                hull.Add(p);
            }
            return hull.ToArray();
        }

        private static double[][] ExpandHull2D(double[][] hull, double margin)
        {
            var cx = hull.Average(p => p[0]);
            var cy = hull.Average(p => p[1]);
            return hull.Select(p =>
            {
                var dx = p[0] - cx; var dy = p[1] - cy;
                var len = Math.Sqrt(dx * dx + dy * dy);
                return len < 0.001 ? new[] { p[0], p[1] } : new[] { p[0] + dx / len * margin, p[1] + dy / len * margin };
            }).ToArray();
        }

        private static bool PointInPolygon2D(double px, double py, double[][] hull)
        {
            int n = hull.Length;
            if (n < 3) return false;
            bool inside = false;
            int j = n - 1;
            for (int i = 0; i < n; i++)
            {
                double xi = hull[i][0], yi = hull[i][1], xj = hull[j][0], yj = hull[j][1];
                if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                    inside = !inside;
                j = i;
            }
            return inside;
        }

        // ============================================================
        // CHECK: REFERENCES.TXT SANITY (auxiliary file, not entity-based, but simple)
        // ============================================================
        public static List<Finding> CheckReferencesFile()
        {
            var scenePath = SceneXmlHelpers.FindScenePath();
            if (scenePath == null) return new List<Finding> { Info("Could not locate the scene folder - skipped.") };

            var refsPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scenePath), "references.txt");
            if (!File.Exists(refsPath)) return new List<Finding> { Info("references.txt not found in scene folder - skipped.") };

            var lines = File.ReadAllLines(refsPath).Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0) return new List<Finding> { Warn("references.txt exists but is empty.") };

            bool parsedCount = int.TryParse(lines[0].Trim(), out var declaredCount);
            var dataLines = parsedCount ? lines.Skip(1).ToList() : lines;
            var actualCount = dataLines.Count;

            var findings = new List<Finding> { Info($"references.txt: {actualCount} entries.") };
            if (parsedCount && actualCount != declaredCount)
                findings.Add(Warn($"references.txt declared count ({declaredCount}) does not match actual entry count ({actualCount}). May be stale or hand-edited."));
            else
                findings.Add(Info("references.txt: count header OK."));
            return findings;
        }

        // ============================================================
        // CHECKS: GENERAL MP (spawn_visual, mp_camera_start_pos, envmap, flee_line)
        // ============================================================
        // Entities Bannerlord's spawn/objective systems look up by EXACT, case-sensitive name.
        // Duplicating one of these in the editor auto-appends a ".001"-style suffix, and manually
        // re-casing one is easy to do by accident - either way the lookup silently fails to find
        // it (it just reads as "spawn not found," with nothing pointing at why). This scans for
        // near-matches - same name once a duplicate suffix is stripped and case is ignored, but not
        // actually identical - and calls them out specifically, rather than letting them either
        // pass silently (if some other check's Contains/IndexOf happens to be forgiving) or show up
        // only as a generic "missing" error with no clue that a rename is the real fix.
        private static List<Finding> CheckSpawnNamingHygiene(List<GameEntity> all, string[] canonicalNames)
        {
            var findings = new List<Finding>();
            var exactSet = new HashSet<string>(canonicalNames, StringComparer.Ordinal);

            foreach (var e in all)
            {
                var name = e.Name;
                if (string.IsNullOrEmpty(name) || exactSet.Contains(name)) continue;

                var stripped = System.Text.RegularExpressions.Regex.Replace(name, @"\.\d+$", "");
                var canonicalMatch = canonicalNames.FirstOrDefault(c => string.Equals(c, stripped, StringComparison.OrdinalIgnoreCase));
                if (canonicalMatch == null) continue;

                var reasons = new List<string>();
                if (stripped.Length != name.Length) reasons.Add($"has a duplicate-entity suffix ('{name.Substring(stripped.Length)}')");
                if (!string.Equals(stripped, canonicalMatch, StringComparison.Ordinal)) reasons.Add("differs in capitalization");
                findings.Add(Warn($"Spawn-related entity '{name}' looks like it should be exactly '{canonicalMatch}' but {string.Join(" and ", reasons)} - Bannerlord's spawn lookups are exact-match/case-sensitive, so this will silently fail to register as a valid spawn."));
            }
            return findings;
        }

        public static List<Finding> CheckGeneralMp(List<GameEntity> all)
        {
            var findings = new List<Finding>();

            var spawnVisuals = all.Where(e => Label(e) == "spawn_visual").ToList();
            if (spawnVisuals.Count == 0) findings.Add(Err("No 'spawn_visual' entity found! This WILL crash the game for all players when picking a team."));
            else if (spawnVisuals.Count > 1) findings.Add(Warn($"Multiple spawn_visual entities found ({spawnVisuals.Count})."));
            else findings.Add(Info("spawn_visual: 1 found. OK."));

            findings.AddRange(CheckSpawnNamingHygiene(all, new[] { "spawn_visual" }));

            var mpCams = all.Where(e => Label(e) == "mp_camera_start_pos").ToList();
            if (mpCams.Count == 0) findings.Add(Warn("No 'mp_camera_start_pos' found. No lobby camera view before team select."));
            else if (mpCams.Count > 1) findings.Add(Warn($"Multiple mp_camera_start_pos entities found ({mpCams.Count})."));
            else findings.Add(Info("mp_camera_start_pos: 1 found. OK."));

            var envmapEntities = all.Where(e => { var l = Label(e); return l != null && (l.IndexOf("envmap_prop", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("envmap_probe", StringComparison.OrdinalIgnoreCase) >= 0); }).ToList();
            bool anyGlobal = envmapEntities.Any(e => e.HasScriptComponent("ReflectionCapturer"));
            if (envmapEntities.Count == 0) findings.Add(Warn("No envmap_prop entities found. Add at least one with IsGlobal=true."));
            else if (!anyGlobal) findings.Add(Warn($"{envmapEntities.Count} envmap entity/entities found - could not confirm a global reflection capturer script."));
            else findings.Add(Info($"envmap_prop: {envmapEntities.Count} found. OK."));

            var fleeLines = all.Where(e => Label(e) == "flee_line" || e.HasTag("flee_line")).ToList();
            findings.Add(fleeLines.Count == 0
                ? Warn("No flee_line entity found. NOTE: flee_line has been non-functional since War Sails patch 1.3 - legacy check only.")
                : Info($"flee_line: {fleeLines.Count} found. (Reminder: non-functional post War Sails 1.3.)"));

            return findings;
        }

        // ============================================================
        // CHECK: CLIMBABLE CIVILIAN LADDERS
        // ============================================================
        public static List<Finding> CheckClimbableCivilianLadders(List<GameEntity> all)
        {
            var found = new List<string>();
            foreach (var e in all)
            {
                var label = Label(e);
                if (label == null) continue;
                var ll = label.ToLowerInvariant();
                bool isCivilLadder = ll.StartsWith("civil_ladder") || ll.StartsWith("civilian_ladder") || (ll.Contains("civil") && ll.Contains("ladder"));
                if (!isCivilLadder) continue;

                bool hasSkeleton = e.Skeleton != null;
                bool hasLadderFlag = (e.BodyFlag & BodyFlags.Ladder) != 0;
                if (!hasSkeleton && !hasLadderFlag) continue;

                var reasons = new List<string>();
                if (hasSkeleton) reasons.Add("has a skeleton (climbable animated setup)");
                if (hasLadderFlag) reasons.Add("has the Ladder body flag");
                found.Add($"{label} -- {string.Join("; ", reasons)}");
            }

            var findings = new List<Finding>();
            if (found.Count == 0)
            {
                findings.Add(Info("No climbable civilian ladders detected. OK."));
            }
            else
            {
                findings.Add(Warn($"{found.Count} climbable civilian ladder(s) detected. These CRASH the game when touched in Battle/Skirmish/TDM. Convert to static props (remove skeleton + ladder body flag). Siege is exempt."));
                foreach (var item in found) findings.Add(Warn("  --- " + item));
            }
            return findings;
        }

        // ============================================================
        // CHECK: EDITOR PLAYTEST SPAWN ENTITIES
        // ============================================================
        public static List<Finding> CheckEditorSpawns(List<GameEntity> all)
        {
            var spPlay = all.Where(e => e.HasTag("sp_play")).ToList();
            var spPlayer = all.Where(e => e.HasTag("spawnpoint_player")).ToList();

            var findings = new List<Finding>();
            if (spPlay.Count == 0 && spPlayer.Count == 0)
            {
                findings.Add(Err("No editor spawn entities found! Add one tagged 'sp_play' or 'spawnpoint_player' (U = Team 1, Ctrl+Left+U = Team 2)."));
            }
            else
            {
                if (spPlay.Count > 0) findings.Add(Info($"sp_play entities: {spPlay.Count}"));
                if (spPlayer.Count > 0) findings.Add(Info($"spawnpoint_player entities: {spPlayer.Count}"));
                if (spPlay.Count >= 2 && !spPlay.Any(e => e.HasTag("defender") || e.HasTag("attacker") || e.HasTag("team_1") || e.HasTag("team_2")))
                    findings.Add(Info("Multiple sp_play entities with no team tags. Consider tagging 'defender'/'attacker' for Team 2 spawn."));
            }
            return findings;
        }

        // ============================================================
        // CHECK: BATTLE MODE VALIDATION
        // ============================================================
        public static List<Finding> CheckBattleMode(List<GameEntity> all)
        {
            var findings = new List<Finding>();

            bool IsSergeantSpawn(GameEntity e)
            {
                var n = e.Name;
                return (n != null && n.IndexOf("sergeant_spawn", StringComparison.OrdinalIgnoreCase) >= 0) ||
                       n == "skirmish_start_spawn" || n == "mp_spawnpoint_attacker" || n == "mp_spawnpoint_defender";
            }

            var sgSpawns = all.Where(IsSergeantSpawn).ToList();
            var sgAttacker = sgSpawns.Where(e => e.GetChildrenWithTagRecursiveList("attacker").Count > 0 || e.Name == "mp_spawnpoint_attacker").ToList();
            var sgDefender = sgSpawns.Where(e => e.GetChildrenWithTagRecursiveList("defender").Count > 0 || e.Name == "mp_spawnpoint_defender").ToList();

            if (sgSpawns.Count == 0)
                findings.Add(Err("No spawn entities found! Battle mode requires sergeant_spawn, skirmish_start_spawn zone containers, or mp_spawnpoint_attacker/defender."));
            else
            {
                findings.Add(sgAttacker.Count == 0 ? Err("No attacker spawn found. Attacker team cannot spawn.") : Info($"Attacker spawns: {sgAttacker.Count} found. OK."));
                findings.Add(sgDefender.Count == 0 ? Err("No defender spawn found. Defender team cannot spawn.") : Info($"Defender spawns: {sgDefender.Count} found. OK."));
            }

            var sssContainers = all.Where(e => e.Name == "skirmish_start_spawn").ToList();
            var standaloneAD = all.Where(e => e.Name == "mp_spawnpoint_attacker" || e.Name == "mp_spawnpoint_defender").ToList();
            var wrongNamed = all.Where(e =>
            {
                if (e.Name == "skirmish_start_spawn" || (e.Name?.IndexOf("sergeant_spawn", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    e.Name == "mp_spawnpoint_attacker" || e.Name == "mp_spawnpoint_defender") return false;
                return e.GetChildren().Any(c => c.HasTag("spawnpoint") && (c.HasTag("attacker") || c.HasTag("defender")));
            }).ToList();

            if (wrongNamed.Count > 0)
                findings.Add(Err($"{wrongNamed.Count} spawn zone container(s) with a non-standard name: {string.Join(", ", wrongNamed.Select(Label))}. The Battle spawn controller searches specifically for 'skirmish_start_spawn' - any other name is ignored after warmup."));
            else if (sssContainers.Count > 0)
            {
                findings.Add(Info($"skirmish_start_spawn containers: {sssContainers.Count} found. OK."));
                bool hasAtk = sssContainers.Any(e => e.GetChildrenWithTagRecursiveList("attacker").Count > 0);
                bool hasDef = sssContainers.Any(e => e.GetChildrenWithTagRecursiveList("defender").Count > 0);
                if (!hasAtk) findings.Add(Err("skirmish_start_spawn container(s) found but none contain attacker-tagged children."));
                if (!hasDef) findings.Add(Err("skirmish_start_spawn container(s) found but none contain defender-tagged children."));
            }
            else if (standaloneAD.Count > 0)
            {
                findings.Add(Warn($"{standaloneAD.Count} standalone mp_spawnpoint_attacker/defender found with no skirmish_start_spawn container. Works in practice but undocumented."));
            }

            foreach (var fn in new[] { "flag_pole_big_sergeant_A", "flag_pole_big_sergeant_B", "flag_pole_big_sergeant_C" })
            {
                var found = all.Where(e => e.Name == fn).ToList();
                if (found.Count == 0) findings.Add(Err($"Missing Battle capture flag '{fn}'."));
                else if (found.Count > 1) findings.Add(Warn($"Multiple '{fn}' entities found ({found.Count})."));
                else findings.Add(Info($"{fn}: found. OK."));
            }

            var borders = all.Where(e => e.Name == "border_soft").ToList();
            findings.Add(borders.Count < 4 ? Warn($"Only {borders.Count} border_soft entit(ies) found. Battle maps typically use 4.") : Info($"border_soft: {borders.Count} found. OK."));

            findings.AddRange(CheckSpawnNamingHygiene(all, new[] { "skirmish_start_spawn", "mp_spawnpoint_attacker", "mp_spawnpoint_defender" }));

            return findings;
        }

        // ============================================================
        // CHECKS: SKIRMISH MODE VALIDATION (ported from Gotha's BL_AddTestScene)
        // ============================================================
        public static List<Finding> CheckSkirmishMode(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var leaves = all.Where(e => e.HasTag("spawnpoint")).ToList();
            var defenderZones = new List<GameEntity>();
            var attackerZones = new List<GameEntity>();
            var seenDef = new HashSet<GameEntity>();
            var seenAtk = new HashSet<GameEntity>();

            foreach (var leaf in leaves)
            {
                var zone = leaf.Parent;
                if (zone == null) continue;
                bool isDefender = leaf.HasTag("defender");
                bool isAttacker = leaf.HasTag("attacker");
                if (!isDefender && !isAttacker) continue;
                if (isDefender && seenDef.Add(zone)) defenderZones.Add(zone);
                if (isAttacker && seenAtk.Add(zone)) attackerZones.Add(zone);
            }

            int ValidateZones(List<GameEntity> zones, string team, List<Finding> outFindings)
            {
                int startingCount = 0, missingSpawnZone = 0;
                foreach (var zone in zones)
                {
                    bool hasStarting = zone.HasTag("starting");
                    bool hasSpawnZone = zone.HasTag("spawn_zone");
                    var children = zone.GetChildren().ToList();

                    if (children.Count == 0)
                    {
                        if (hasStarting) outFindings.Add(Warn($"Skirmish: {team} zone '{zone.Name}' is tagged 'starting' but has NO child spawnpoint entities."));
                        continue;
                    }
                    if (!hasSpawnZone) missingSpawnZone++;
                    if (hasStarting)
                    {
                        if (children[0].HasTag(team.ToLowerInvariant())) startingCount++;
                        else outFindings.Add(Warn($"Skirmish: {team} zone '{zone.Name}' has 'starting' tag but its first child does NOT carry a '{team.ToLowerInvariant()}' tag."));
                    }
                }
                if (missingSpawnZone > 0) outFindings.Add(Warn($"Skirmish: {missingSpawnZone} {team} zone(s) lack the 'spawn_zone' tag required by official documentation."));
                return startingCount;
            }

            int defStart = ValidateZones(defenderZones, "Defender", findings);
            int atkStart = ValidateZones(attackerZones, "Attacker", findings);
            findings.Add(defStart == 0 ? Err("Skirmish: No valid DEFENDER starting spawn zones found!") : Info($"Defender starting zones: {defStart}. OK."));
            findings.Add(atkStart == 0 ? Err("Skirmish: No valid ATTACKER starting spawn zones found!") : Info($"Attacker starting zones: {atkStart}. OK."));

            int defRespawn = defenderZones.Count - defStart, atkRespawn = attackerZones.Count - atkStart;
            if (defRespawn > 3) findings.Add(Warn($"Skirmish: {defRespawn} defender respawn zones detected. Docs say 'up to 3 per side'."));
            if (atkRespawn > 3) findings.Add(Warn($"Skirmish: {atkRespawn} attacker respawn zones detected. Docs say 'up to 3 per side'."));

            foreach (var fn in new[] { "flag_pole_big_sergeant_A", "flag_pole_big_sergeant_B", "flag_pole_big_sergeant_C" })
            {
                var found = all.Where(e => e.Name == fn).ToList();
                if (found.Count == 0) findings.Add(Err($"Skirmish: Missing capture flag '{fn}'."));
                else if (found.Count > 1) findings.Add(Warn($"Skirmish: Multiple '{fn}' entities found ({found.Count})."));
                else findings.Add(Info($"{fn}: found. OK."));
            }

            var skirmBorders = all.Where(e => e.Name == "border_soft").ToList();
            if (skirmBorders.Count == 0) findings.Add(Err("Skirmish: No border_soft entities found."));
            else if (skirmBorders.Count < 3) findings.Add(Warn($"Skirmish: Only {skirmBorders.Count} border_soft entit(ies). Need >=4 for a full boundary."));
            else findings.Add(Info($"border_soft: {skirmBorders.Count} found. OK."));

            var crashDestructibles = new[] { "rock_pile", "pot_pile", "arrow_barrel", "boulder_destructible" };
            var destructibleHits = new List<string>();
            foreach (var e in all)
            {
                var label = Label(e);
                if (label == null) continue;
                var ll = label.ToLowerInvariant();
                if (crashDestructibles.Any(bad => ll.Contains(bad))) { destructibleHits.Add(label); continue; }
                if (e.HasScriptComponent("StonePile") || e.HasScriptComponent("DestructibleComponent"))
                    destructibleHits.Add(label + " (destructible script)");
            }
            destructibleHits = destructibleHits.Distinct().ToList();
            findings.Add(destructibleHits.Count > 0
                ? Err($"Skirmish/non-Siege: {destructibleHits.Count} SIEGE-ONLY destructible entity/entities that WILL CRASH here: {string.Join("; ", destructibleHits)}.")
                : Info("No crash-prone Siege-only destructibles detected."));

            var enforceSpawns = all.Where(e => e.HasTag("enforce_troop_spawn")).ToList();
            if (enforceSpawns.Count > 0) findings.Add(Info($"{enforceSpawns.Count} entity/entities tagged 'enforce_troop_spawn' (editor-only debug spawn)."));

            return findings;
        }

        // ============================================================
        // CHECK: SIEGE MODE VALIDATION
        // ============================================================
        public static List<Finding> CheckSiegeMode(List<GameEntity> all)
        {
            var findings = new List<Finding>();
            var spZoneTagMap = new Dictionary<string, List<string>>();
            foreach (var e in all)
                for (int zx = 0; zx <= 6; zx++)
                {
                    var tagName = $"sp_zone_{zx}";
                    if (e.HasTag(tagName))
                    {
                        if (!spZoneTagMap.TryGetValue(tagName, out var list)) spZoneTagMap[tagName] = list = new List<string>();
                        list.Add(Label(e));
                    }
                }

            if (spZoneTagMap.Count == 0)
            {
                findings.Add(Err("Siege: No sp_zone_x tags found. Requires sp_zone_0 through sp_zone_6 (minimum 0 and 6)."));
            }
            else
            {
                foreach (var required in new[] { "sp_zone_0", "sp_zone_6" })
                    if (!spZoneTagMap.ContainsKey(required)) findings.Add(Err($"Siege: Missing '{required}' tag. Server will crash without it."));
                foreach (var kv in spZoneTagMap)
                    findings.Add(kv.Value.Count > 1
                        ? Err($"Siege: Tag '{kv.Key}' is on {kv.Value.Count} different entities: {string.Join(", ", kv.Value)}. Breaks siege spawning.")
                        : Info($"{kv.Key}: 1 zone entity. OK."));
            }

            foreach (var fn in new[] { "flag_pole_big_sergeant_A", "flag_pole_big_sergeant_B", "flag_pole_big_sergeant_C", "flag_pole_big_sergeant_D", "flag_pole_big_sergeant_E", "flag_pole_big_sergeant_F", "flag_pole_big_sergeant_main" })
            {
                var found = all.Where(e => e.Name == fn).ToList();
                if (found.Count == 0) findings.Add(Err($"Siege: Missing flag '{fn}'."));
                else if (found.Count > 1) findings.Add(Warn($"Siege: Multiple '{fn}' found ({found.Count})."));
                else findings.Add(Info($"{fn}: found. OK."));
            }

            var siegeSpawnpoints = all.Count(e => e.HasTag("mp_spawnpoint"));
            findings.Add(siegeSpawnpoints == 0 ? Err("Siege: No mp_spawnpoint entities found.") : Info($"mp_spawnpoint total: {siegeSpawnpoints} found."));

            var wrongStarting = all.Count(e => e.HasTag("starting") && Enumerable.Range(0, 7).Any(zx => e.HasTag($"sp_zone_{zx}")));
            if (wrongStarting > 0) findings.Add(Warn($"Siege: {wrongStarting} entity/entities have BOTH 'starting' AND an 'sp_zone_x' tag - remove 'starting' from Siege zones."));

            var wallSegments = all.Where(e => e.HasScriptComponent("WallSegment")).ToList();
            if (wallSegments.Count == 0)
            {
                findings.Add(Err("Siege: No entities with WallSegment script found."));
            }
            else
            {
                findings.Add(Info($"WallSegment entities: {wallSegments.Count} found."));
                foreach (var ws in wallSegments)
                {
                    bool hasBroken = ws.GetChildrenWithTagRecursiveList("broken_child").Count > 0;
                    bool hasSolid = ws.GetChildrenWithTagRecursiveList("solid_child").Count > 0;
                    if (!hasBroken) findings.Add(Warn($"Siege: WallSegment on '{Label(ws)}' has no 'broken_child' - breached state won't work."));
                    if (!hasSolid) findings.Add(Warn($"Siege: WallSegment on '{Label(ws)}' has no 'solid_child' - intact state won't work."));
                }
            }

            var outerGates = all.Where(e => e.HasTag("outer_gate")).ToList();
            var innerGates = all.Where(e => e.HasTag("inner_gate")).ToList();
            if (outerGates.Count == 0)
            {
                findings.Add(Err("Siege: No entity tagged 'outer_gate' found. Battering ram cannot function."));
            }
            else
            {
                findings.Add(Info($"outer_gate: {outerGates.Count} found."));
                foreach (var og in outerGates)
                    if (!og.HasScriptComponent("CastleGate")) findings.Add(Warn($"Siege: 'outer_gate' entity '{Label(og)}' has no CastleGate script."));
            }
            findings.Add(innerGates.Count == 0 ? Warn("Siege: No entity tagged 'inner_gate' found.") : Info($"inner_gate: {innerGates.Count} found."));

            bool HasAny(GameEntity e, params string[] names) => names.Any(e.HasScriptComponent);
            var ramSpawners = all.Where(e => HasAny(e, "MultiplayerBatteringRamSpawner", "BatteringRamSpawner")).ToList();
            var towerSpawners = all.Where(e => HasAny(e, "MultiplayerSiegeTowerSpawner")).ToList();
            var ladderSpawners = all.Where(e => HasAny(e, "SiegeLadderSpawner")).ToList();
            var mangSpawners = all.Where(e => HasAny(e, "MultiplayerMangonelSpawner", "MultiplayerFireMangonelSpawner", "MultiplayerTrebuchetSpawner", "MultiplayerFireTrebuchetSpawner")).ToList();

            // Warning, not error: missing siege equipment spawners mean the map is incomplete for
            // Siege (no way to breach), but unlike the sp_zone_0/6 case above this doesn't crash the
            // server - a scene mid-editing that just hasn't had its ram/tower placed yet shouldn't
            // read as "broken."
            findings.Add(ramSpawners.Count == 0 ? Warn("Siege: No battering ram spawner found - attackers have no way to breach the gate.") : Info($"Battering ram spawner(s): {ramSpawners.Count} found."));
            if (towerSpawners.Count == 0 && ladderSpawners.Count == 0) findings.Add(Warn("Siege: No siege tower or ladder spawner found - attackers have no way to scale the walls."));
            else
            {
                if (towerSpawners.Count > 0) findings.Add(Info($"Siege tower spawner(s): {towerSpawners.Count} found."));
                if (ladderSpawners.Count > 0) findings.Add(Info($"Siege ladder spawner(s): {ladderSpawners.Count} found."));
            }
            if (mangSpawners.Count > 0) findings.Add(Info($"Ranged siege machine spawner(s): {mangSpawners.Count} found."));

            var objTags = new Dictionary<string, string>
            {
                ["mp_siege_objective_battering_ram"] = "Battering ram objective",
                ["mp_siege_objective_castle_gate"] = "Castle gate objective",
                ["mp_siege_objective_side_objective"] = "Side objective",
                ["mp_siege_objective_siege_tower"] = "Siege tower objective",
            };
            foreach (var kv in objTags)
            {
                var found = all.Count(e => e.HasTag(kv.Key));
                findings.Add(found == 0 ? Warn($"Siege: No entity tagged '{kv.Key}' found ({kv.Value}).") : Info($"{kv.Key}: {found} found. OK."));
            }

            return findings;
        }

        // ============================================================
        // DUPLICATE DETECTION - restricted to TOP-LEVEL entities only (Parent == null), same
        // restriction the offline version enforced deliberately (CollectTopLevelEntities, not
        // CollectAllEntitiesRecursive). An earlier version of this method dropped that restriction
        // on the theory that GetGlobalFrame() resolves fully to world space regardless of nesting
        // depth so it wouldn't matter - true as far as it goes, but it ignored a different, real
        // risk: nested children reuse generic internal part names across completely unrelated
        // top-level prefab instances (confirmed while building the wall_plank fixed-prefab copy:
        // every wall instance's children are literally both named "fief_wooden_platform_2_plank_f_
        // column"). Two such children from two different, unrelated wall placements can end up
        // with identical resolved positions purely by construction (grid-snapped, mirrored, or
        // otherwise structurally identical local offsets), which produced a live false-positive:
        // legitimate, distinct castle walls getting flagged and deleted as "duplicates" just for
        // sharing an internal part name. Comparing only entities with no parent removes that whole
        // failure class, matching the offline tool's proven-safe behavior.
        // Rotation compared via forward-vector angle (robust, no per-axis wraparound issues).
        // ============================================================
        public class DuplicatePair
        {
            public string Type;
            public GameEntity Keep;
            public GameEntity Remove;
            public double Distance;
            public bool RotationMatches;
        }

        public static List<DuplicatePair> FindDuplicates(List<GameEntity> all, double distThreshold, double angleThresholdDeg)
        {
            var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "reverb_wood_interior" };
            var byType = all.Where(e => e.Parent == null && Label(e) != null && !exclusions.Contains(Label(e))).GroupBy(Label, StringComparer.OrdinalIgnoreCase);
            var pairs = new List<DuplicatePair>();
            var angleThresholdRad = angleThresholdDeg * Math.PI / 180.0;

            foreach (var group in byType)
            {
                var list = group.ToList();
                if (list.Count < 2) continue;
                var reported = new HashSet<int>();

                for (int i = 0; i < list.Count; i++)
                {
                    var fi = list[i].GetGlobalFrame();
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        if (reported.Contains(i) || reported.Contains(j)) continue;
                        var fj = list[j].GetGlobalFrame();
                        double dx = fi.origin.x - fj.origin.x, dy = fi.origin.y - fj.origin.y, dz = fi.origin.z - fj.origin.z;
                        var dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (dist > distThreshold) continue;

                        double dot = fi.rotation.f.x * fj.rotation.f.x + fi.rotation.f.y * fj.rotation.f.y + fi.rotation.f.z * fj.rotation.f.z;
                        dot = Math.Max(-1.0, Math.Min(1.0, dot));
                        var angle = Math.Acos(dot);
                        bool rotSimilar = angle <= angleThresholdRad;

                        pairs.Add(new DuplicatePair { Type = group.Key, Keep = list[i], Remove = list[j], Distance = dist, RotationMatches = rotSimilar });
                        reported.Add(i);
                        reported.Add(j);
                    }
                }
            }
            return pairs;
        }

        public static List<Finding> CheckDuplicates(List<GameEntity> all, double distThreshold = 0.02, double angleThresholdDeg = 3.0)
        {
            var pairs = FindDuplicates(all, distThreshold, angleThresholdDeg);
            var findings = new List<Finding>();
            foreach (var p in pairs.Where(p => p.RotationMatches))
                findings.Add(Warn($"EXACT DUP '{p.Type}' (dist={p.Distance:F5}, same rotation)"));
            foreach (var p in pairs.Where(p => !p.RotationMatches))
                findings.Add(Warn($"NEAR-DUP '{p.Type}' (dist={p.Distance:F5}, rotation differs - may be intentional)"));
            if (findings.Count == 0) findings.Add(Info($"No likely duplicate entities found (within {distThreshold} units)."));
            return findings;
        }

        public static int DeleteDuplicates(List<GameEntity> all, double distThreshold = 0.0, bool tagBeforeDelete = false)
        {
            var pairs = FindDuplicates(all, distThreshold, 3.0).Where(p => p.RotationMatches).ToList();
            foreach (var p in pairs)
            {
                if (tagBeforeDelete && !p.Remove.HasTag("BSA_LIKELY_DUPLICATE")) p.Remove.AddTag("BSA_LIKELY_DUPLICATE");
                p.Remove.Remove(0);
            }
            return pairs.Count;
        }

        public static int TagDuplicates(List<GameEntity> all, double distThreshold = 0.0)
        {
            var pairs = FindDuplicates(all, distThreshold, 3.0).Where(p => p.RotationMatches).ToList();
            int tagged = 0;
            foreach (var p in pairs)
                if (!p.Remove.HasTag("BSA_LIKELY_DUPLICATE")) { p.Remove.AddTag("BSA_LIKELY_DUPLICATE"); tagged++; }
            return tagged;
        }
    }

    // Small extension helpers used above - GetChildrenWithTagRecursive takes a pre-allocated List
    // out-param rather than returning one, this just wraps that into an expression-friendly form.
    public static class GameEntityCheckExtensions
    {
        public static List<GameEntity> GetChildrenWithTagRecursiveList(this GameEntity entity, string tag)
        {
            var list = new List<GameEntity>();
            entity.GetChildrenWithTagRecursive(list, tag);
            return list;
        }
    }
}
