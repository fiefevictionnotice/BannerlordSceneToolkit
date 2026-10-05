using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MaterialSwapTool.Core
{
    // Ported from BannerlordSceneAnalyzer's Analyze-BannerlordScene.ps1 (local reference project,
    // 6500+ lines, community-vetted over time) - reusing its verified detection logic and curated
    // reference data (Bugged_Physics_Shapes.txt, LOD_Substitutions.txt) rather than re-deriving
    // checks from scratch. Checks 21-24 (Skirmish) and 26 (Editor Playtest Spawn) are themselves
    // ported from Gotha's BL_AddTestScene scene editor tester mod (see that script's own
    // attribution comments) - this is a third-generation port of Gotha's original C#.
    //
    // Default thresholds match the source script's own defaults: 0.02 units / 3 degrees for
    // duplicate DETECTION (a little slack, catches near-stacks worth a human look), 0.0 units for
    // duplicate DELETION (exact position match only - deletion is destructive, so it only ever
    // acts on true stacked duplicates, never near-matches that might be intentional).
    public static class BsaSceneChecks
    {
        public const double DefaultDuplicateThreshold = 0.02;
        public const double DefaultDuplicateAngleThresholdDeg = 3.0;
        public const double DefaultDeleteDuplicateThreshold = 0.0;

        private static readonly HashSet<string> DupExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reverb_wood_interior",
        };

        // ============================================================
        // REFERENCE DATA (editable override convention, same as everything else in this tool)
        // ============================================================

        public class BuggedPhysicsInfo
        {
            public string Description;
            public string Severity; // "ERROR" or "WARNING"
        }

        private static Dictionary<string, BuggedPhysicsInfo> _buggedPhysics;
        private static Dictionary<string, string> _lodSubstitutions;

        private static string ModuleDir => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ResolvePath(string fileName)
        {
            var overridePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord", "MaterialSwapTool", fileName);
            if (File.Exists(overridePath)) return overridePath;
            return Path.Combine(ModuleDir, "ReferenceData", fileName);
        }

        // Format: entity_name | description | severity (ERROR/WARNING). '#' comments and blanks
        // ignored - same convention the source .txt already uses.
        public static Dictionary<string, BuggedPhysicsInfo> BuggedPhysicsDict
        {
            get
            {
                if (_buggedPhysics != null) return _buggedPhysics;
                _buggedPhysics = new Dictionary<string, BuggedPhysicsInfo>(StringComparer.OrdinalIgnoreCase);
                var path = ResolvePath("Bugged_Physics_Shapes.txt");
                if (!File.Exists(path)) { Log.Warn($"BsaSceneChecks: '{path}' not found."); return _buggedPhysics; }
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var parts = line.Split(new[] { '|' }, 3);
                    if (parts.Length < 1) continue;
                    var name = parts[0].Trim();
                    if (name.Length == 0) continue;
                    var desc = parts.Length > 1 ? parts[1].Trim() : "";
                    var sev = parts.Length > 2 ? parts[2].Trim().ToUpperInvariant() : "WARNING";
                    _buggedPhysics[name] = new BuggedPhysicsInfo { Description = desc, Severity = sev == "ERROR" ? "ERROR" : "WARNING" };
                }
                return _buggedPhysics;
            }
        }

        // Format: bad_entity -> good_entity. Same convention as BSA templates elsewhere.
        public static Dictionary<string, string> LodSubstitutions
        {
            get
            {
                if (_lodSubstitutions != null) return _lodSubstitutions;
                _lodSubstitutions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var path = ResolvePath("LOD_Substitutions.txt");
                if (!File.Exists(path)) { Log.Warn($"BsaSceneChecks: '{path}' not found."); return _lodSubstitutions; }
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var arrowParts = line.Split(new[] { "->" }, StringSplitOptions.None);
                    if (arrowParts.Length != 2) continue;
                    var from = arrowParts[0].Trim();
                    var to = arrowParts[1].Trim();
                    if (from.Length > 0 && to.Length > 0) _lodSubstitutions[from] = to;
                }
                return _lodSubstitutions;
            }
        }

        private static Finding Err(string msg) => new Finding { Severity = "ERROR", Message = msg };
        private static Finding Warn(string msg) => new Finding { Severity = "WARNING", Message = msg };
        private static Finding Info(string msg) => new Finding { Severity = "INFO", Message = msg };

        // ============================================================
        // CHECK: BUGGED PHYSICS SHAPES
        // ============================================================
        public static List<Finding> CheckBuggedPhysics(XDocument doc)
        {
            var findings = new List<Finding>();
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in all)
            {
                var label = e.Prefab ?? e.Name;
                if (label != null && BuggedPhysicsDict.ContainsKey(label))
                    found[label] = found.TryGetValue(label, out var c) ? c + 1 : 1;
            }

            foreach (var kv in found.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var info = BuggedPhysicsDict[kv.Key];
                var msg = $"Bugged physics entity '{kv.Key}' x{kv.Value}: {info.Description}";
                findings.Add(info.Severity == "ERROR" ? Err(msg) : Warn(msg));
            }

            if (findings.Count == 0) findings.Add(Info("No bugged physics shape entities found."));
            return findings;
        }

        public static int TagBuggedPhysics(XDocument doc)
        {
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);
            int tagged = 0;
            foreach (var e in all)
            {
                var label = e.Prefab ?? e.Name;
                if (label != null && BuggedPhysicsDict.ContainsKey(label) && SceneXmlHelpers.AddTag(e.Node, "BSA_BUGGED_PHYSICS_SHAPE"))
                    tagged++;
            }
            return tagged;
        }

        // ============================================================
        // CHECK: DUPLICATE / OVERLAPPING ENTITIES (top-level only - see SceneXmlHelpers)
        // ============================================================
        public class DuplicatePair
        {
            public string Type;
            public SceneXmlEntity Keep;
            public SceneXmlEntity Remove;
            public double Distance;
            public bool RotationMatches;
        }

        public static List<DuplicatePair> FindDuplicates(XDocument doc, double distThreshold, double angleThresholdDeg)
        {
            var topLevel = SceneXmlHelpers.CollectTopLevelEntities(doc).Where(e => e.Pos != null && !DupExclusions.Contains(e.Label)).ToList();
            var byType = topLevel.GroupBy(e => e.Label, StringComparer.OrdinalIgnoreCase);
            var pairs = new List<DuplicatePair>();
            var angleThresholdRad = angleThresholdDeg * Math.PI / 180.0;

            foreach (var group in byType)
            {
                var list = group.ToList();
                if (list.Count < 2) continue;
                var reported = new HashSet<int>();

                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        if (reported.Contains(i) || reported.Contains(j)) continue;
                        var dist = SceneXmlHelpers.Vec3Distance(list[i].Pos, list[j].Pos);
                        if (dist > distThreshold) continue;

                        bool rotSimilar = true;
                        if (list[i].Rot != null && list[j].Rot != null)
                        {
                            for (int ax = 0; ax < 3; ax++)
                            {
                                if (SceneXmlHelpers.AngleDiff(list[i].Rot[ax], list[j].Rot[ax]) > angleThresholdRad)
                                {
                                    rotSimilar = false;
                                    break;
                                }
                            }
                        }

                        pairs.Add(new DuplicatePair { Type = group.Key, Keep = list[i], Remove = list[j], Distance = dist, RotationMatches = rotSimilar });
                        reported.Add(i);
                        reported.Add(j);
                    }
                }
            }

            return pairs;
        }

        public static List<Finding> CheckDuplicates(XDocument doc, double distThreshold = DefaultDuplicateThreshold, double angleThresholdDeg = DefaultDuplicateAngleThresholdDeg)
        {
            var pairs = FindDuplicates(doc, distThreshold, angleThresholdDeg);
            var findings = new List<Finding>();
            foreach (var p in pairs.Where(p => p.RotationMatches))
                findings.Add(Warn($"EXACT DUP '{p.Type}' at [{p.Remove.PosStr}] (dist={p.Distance:F5}, same rotation)"));
            foreach (var p in pairs.Where(p => !p.RotationMatches))
                findings.Add(Warn($"NEAR-DUP '{p.Type}' at [{p.Remove.PosStr}] (dist={p.Distance:F5}, rotation differs - may be intentional)"));
            if (findings.Count == 0) findings.Add(Info($"No likely duplicate entities found (within {distThreshold} units)."));
            return findings;
        }

        // Deletes exact duplicates (default threshold 0.0 = exact position match only - see class
        // comment). Caller is responsible for backing up first. Returns how many were removed.
        public static int DeleteDuplicates(XDocument doc, double distThreshold = DefaultDeleteDuplicateThreshold, bool tagBeforeDelete = false)
        {
            var pairs = FindDuplicates(doc, distThreshold, DefaultDuplicateAngleThresholdDeg).Where(p => p.RotationMatches).ToList();
            foreach (var p in pairs)
            {
                if (tagBeforeDelete) SceneXmlHelpers.AddTag(p.Remove.Node, "BSA_LIKELY_DUPLICATE");
                p.Remove.Node.Remove();
            }
            return pairs.Count;
        }

        public static int TagDuplicates(XDocument doc, double distThreshold = DefaultDeleteDuplicateThreshold)
        {
            var pairs = FindDuplicates(doc, distThreshold, DefaultDuplicateAngleThresholdDeg).Where(p => p.RotationMatches).ToList();
            int tagged = 0;
            foreach (var p in pairs)
                if (SceneXmlHelpers.AddTag(p.Remove.Node, "BSA_LIKELY_DUPLICATE")) tagged++;
            return tagged;
        }

        // ============================================================
        // CHECK: LOD SUBSTITUTION REMINDERS + REPLACE ACTION
        // ============================================================
        public static List<Finding> CheckLodSubstitutions(XDocument doc)
        {
            var findings = new List<Finding>();
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all)
            {
                var label = e.Prefab ?? e.Name;
                if (label != null && LodSubstitutions.ContainsKey(label))
                    counts[label] = counts.TryGetValue(label, out var c) ? c + 1 : 1;
            }
            foreach (var kv in counts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                findings.Add(Info($"Replace {kv.Value}x '{kv.Key}' -> '{LodSubstitutions[kv.Key]}'"));
            if (findings.Count == 0) findings.Add(Info("No known LOD substitutions apply to this scene."));
            return findings;
        }

        public class LodReplaceResult
        {
            public int AttributesChanged;
            public Dictionary<string, int> PerSubstitution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        // Works on the RAW XML TEXT, not the parsed DOM - deliberately, matching the source tool.
        // Bad LOD meshes are frequently baked as mesh/metamesh-level "name" attributes deep inside
        // a prefab's saved data (e.g. name="wooden_platform_plank_a.3"), not just top-level
        // <game_entity prefab="..."> references - a DOM-attribute-only approach (like
        // BrokenPrefabFixer's) would silently miss the majority of real occurrences. Four
        // attribute forms are covered, exactly matching the source: prefab="X", old_prefab_name=
        // "X", name="X" (exact), and name="X." (prefix, for embedded mesh asset refs like
        // name="prefab.0").
        public static LodReplaceResult ReplaceLods(string scenePath)
        {
            var raw = File.ReadAllText(scenePath, System.Text.Encoding.UTF8);
            var result = new LodReplaceResult();

            foreach (var kv in LodSubstitutions)
            {
                var from = kv.Key;
                var to = kv.Value;
                int hits = 0;

                (string oldPat, string newPat)[] patterns =
                {
                    ($" prefab=\"{from}\"", $" prefab=\"{to}\""),
                    ($" old_prefab_name=\"{from}\"", $" old_prefab_name=\"{to}\""),
                    ($" name=\"{from}\"", $" name=\"{to}\""),
                    ($" name=\"{from}.", $" name=\"{to}."),
                };

                foreach (var (oldPat, newPat) in patterns)
                {
                    var count = Regex.Matches(raw, Regex.Escape(oldPat)).Count;
                    if (count > 0)
                    {
                        raw = raw.Replace(oldPat, newPat);
                        hits += count;
                    }
                }

                if (hits > 0)
                {
                    result.PerSubstitution[$"{from} -> {to}"] = hits;
                    result.AttributesChanged += hits;
                }
            }

            if (result.AttributesChanged > 0)
                File.WriteAllText(scenePath, raw, new System.Text.UTF8Encoding(false));

            return result;
        }

        // ============================================================
        // CHECKS: GENERAL MP (spawn_visual, mp_camera_start_pos, envmap, flee_line)
        // Always relevant regardless of game mode.
        // ============================================================
        public static List<Finding> CheckGeneralMp(XDocument doc)
        {
            var findings = new List<Finding>();
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);

            // spawn_visual - missing crashes ALL players on team pick, every MP mode.
            var spawnVisuals = all.Where(e => e.Name == "spawn_visual" || e.Prefab == "spawn_visual").ToList();
            if (spawnVisuals.Count == 0)
                findings.Add(Err("No 'spawn_visual' entity found! This WILL crash the game for all players when picking a team. Add a 'spawn_visual' entity to the scene."));
            else if (spawnVisuals.Count > 1)
                findings.Add(Warn($"Multiple spawn_visual entities found ({spawnVisuals.Count}). Only one is typically needed."));
            else
                findings.Add(Info("spawn_visual: 1 found. OK."));

            // mp_camera_start_pos - pre-team-select lobby camera.
            var mpCams = all.Where(e => e.Name == "mp_camera_start_pos" || e.Prefab == "mp_camera_start_pos").ToList();
            if (mpCams.Count == 0)
                findings.Add(Warn("No 'mp_camera_start_pos' found. Players joining the server will have no lobby camera view before selecting a team."));
            else if (mpCams.Count > 1)
                findings.Add(Warn($"Multiple mp_camera_start_pos entities found ({mpCams.Count}). Exactly 1 is expected."));
            else
                findings.Add(Info("mp_camera_start_pos: 1 found. OK."));

            // envmap_prop/envmap_probe with IsGlobal=true reflection capturer.
            var envmapEntities = all.Where(e =>
                (e.Name?.IndexOf("envmap_prop", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Prefab?.IndexOf("envmap_prop", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Name?.IndexOf("envmap_probe", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Prefab?.IndexOf("envmap_probe", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            bool anyGlobal = envmapEntities.Any(e =>
            {
                var varNode = e.Node.Descendants("script").Where(s => (string)s.Attribute("name") == "ReflectionCapturer")
                    .SelectMany(s => s.Descendants("variable")).FirstOrDefault(v => (string)v.Attribute("name") == "IsGlobal");
                var val = (string)varNode?.Attribute("value");
                if (val == "true" || val == "1") return true;
                return e.Node.Descendants("script").Any(s => { var v = (string)s.Attribute("IsGlobal"); return v == "true" || v == "1"; });
            });
            if (envmapEntities.Count == 0)
                findings.Add(Warn("No envmap_prop entities found. Scene lighting and reflections may appear incorrect. Add at least one 'envmap_prop' with IsGlobal=true."));
            else if (!anyGlobal)
                findings.Add(Warn($"{envmapEntities.Count} envmap entity/entities found, but none has IsGlobal=true. Ensure one envmap_prop has IsGlobal set to true."));
            else
                findings.Add(Info($"envmap_prop: {envmapEntities.Count} total, at least one global. OK."));

            // flee_line - horses use it to exit the map. BROKEN since War Sails 1.3 (Mar 2026) -
            // still checked for legacy/pre-1.3 scenes, but the source script is explicit that this
            // should not be relied on for current builds, and neither should this port.
            var fleeLines = all.Where(e => e.Name == "flee_line" || e.Prefab == "flee_line" || SceneXmlHelpers.HasTag(e.Node, "flee_line")).ToList();
            if (fleeLines.Count == 0)
                findings.Add(Warn("No flee_line entity found. NOTE: flee_line has been non-functional since War Sails patch 1.3 (March 2026) regardless - this is a legacy check only."));
            else
                findings.Add(Info($"flee_line: {fleeLines.Count} found. (Reminder: non-functional post War Sails 1.3 - do not rely on this behavior.)"));

            return findings;
        }

        // ============================================================
        // CHECK: CLIMBABLE CIVILIAN LADDERS IN NON-SIEGE MP SCENES
        // ============================================================
        public static List<Finding> CheckClimbableCivilianLadders(XDocument doc)
        {
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);
            var found = new List<string>();

            foreach (var e in all)
            {
                var label = e.Name ?? e.Prefab;
                if (label == null) continue;
                var ll = label.ToLowerInvariant();
                bool isCivilLadder = ll.StartsWith("civil_ladder") || ll.StartsWith("civilian_ladder") ||
                                     (ll.Contains("civil") && ll.Contains("ladder"));
                if (!isCivilLadder) continue;

                bool hasSkeleton = e.Node.Element("skeleton") != null;
                bool hasLadderFlag = SceneXmlHelpers.HasDescendantElementWithAttr(e.Node, "body_flag", "name", "ladder");
                if (!hasSkeleton && !hasLadderFlag) continue;

                var reasons = new List<string>();
                if (hasSkeleton) reasons.Add("has <skeleton> (climbable animated setup)");
                if (hasLadderFlag) reasons.Add("has <body_flag name='ladder'>");
                found.Add($"{label} -- {string.Join("; ", reasons)}");
            }

            var findings = new List<Finding>();
            if (found.Count == 0)
            {
                findings.Add(Info("No climbable civilian ladders detected. OK."));
            }
            else
            {
                findings.Add(Warn($"{found.Count} climbable civilian ladder(s) detected. These CRASH the game when touched in Battle/Skirmish/TDM. Convert to static props by removing the <skeleton> and ladder_body child entity, keeping only mesh + physics. (Siege mode is exempt.)"));
                foreach (var item in found) findings.Add(Warn("  --- " + item));
            }
            return findings;
        }

        // ============================================================
        // CHECK: EDITOR PLAYTEST SPAWN ENTITIES
        // ============================================================
        public static List<Finding> CheckEditorSpawns(XDocument doc)
        {
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);
            var spPlay = all.Where(e => SceneXmlHelpers.HasTag(e.Node, "sp_play")).ToList();
            var spPlayer = all.Where(e => SceneXmlHelpers.HasTag(e.Node, "spawnpoint_player")).ToList();

            var findings = new List<Finding>();
            if (spPlay.Count == 0 && spPlayer.Count == 0)
            {
                findings.Add(Err("No editor spawn entities found! Add at least one entity tagged 'sp_play' or 'spawnpoint_player' for playtest spawning (U = Team 1, Ctrl+Left+U = Team 2)."));
            }
            else
            {
                if (spPlay.Count > 0) findings.Add(Info($"sp_play entities: {spPlay.Count}"));
                if (spPlayer.Count > 0) findings.Add(Info($"spawnpoint_player entities: {spPlayer.Count}"));
                if (spPlay.Count >= 2)
                {
                    bool anyTeamTagged = spPlay.Any(e =>
                    {
                        var tags = SceneXmlHelpers.GetTagNames(e.Node);
                        return tags.Contains("defender") || tags.Contains("attacker") || tags.Contains("team_1") || tags.Contains("team_2");
                    });
                    if (!anyTeamTagged)
                        findings.Add(Info("Multiple sp_play entities with no team tags. Consider tagging them 'defender'/'attacker' for Team 2 spawn."));
                }
            }
            return findings;
        }

        // ============================================================
        // CHECK: BATTLE MODE VALIDATION
        // ============================================================
        public static List<Finding> CheckBattleMode(XDocument doc)
        {
            var findings = new List<Finding>();
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);

            bool IsSergeantSpawn(SceneXmlEntity e) =>
                (e.Name != null && e.Name.IndexOf("sergeant_spawn", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Prefab != null && e.Prefab.IndexOf("sergeant_spawn", StringComparison.OrdinalIgnoreCase) >= 0) ||
                e.Prefab == "skirmish_start_spawn" || e.Name == "skirmish_start_spawn" ||
                e.Prefab == "mp_spawnpoint_attacker" || e.Name == "mp_spawnpoint_attacker" ||
                e.Prefab == "mp_spawnpoint_defender" || e.Name == "mp_spawnpoint_defender";

            var sgSpawns = all.Where(IsSergeantSpawn).ToList();
            var sgAttacker = sgSpawns.Where(e => SceneXmlHelpers.HasDescendantTag(e.Node, "attacker") || e.Prefab == "mp_spawnpoint_attacker" || e.Name == "mp_spawnpoint_attacker").ToList();
            var sgDefender = sgSpawns.Where(e => SceneXmlHelpers.HasDescendantTag(e.Node, "defender") || e.Prefab == "mp_spawnpoint_defender" || e.Name == "mp_spawnpoint_defender").ToList();

            if (sgSpawns.Count == 0)
            {
                findings.Add(Err("No spawn entities found! Battle mode requires sergeant_spawn, skirmish_start_spawn zone containers, or mp_spawnpoint_attacker/defender entities."));
            }
            else
            {
                findings.Add(sgAttacker.Count == 0 ? Err("No attacker spawn found. Attacker team cannot spawn.") : Info($"Attacker spawns: {sgAttacker.Count} found. OK."));
                findings.Add(sgDefender.Count == 0 ? Err("No defender spawn found. Defender team cannot spawn.") : Info($"Defender spawns: {sgDefender.Count} found. OK."));
            }

            // skirmish_start_spawn zone containers - the Battle spawn controller searches
            // specifically by this name; a differently-named container is silently ignored after
            // warmup, which is confirmed to break the map.
            var sssContainers = all.Where(e => e.Prefab == "skirmish_start_spawn" || e.Name == "skirmish_start_spawn").ToList();
            var standaloneAD = all.Where(e => e.Name == "mp_spawnpoint_attacker" || e.Prefab == "mp_spawnpoint_attacker" ||
                                               e.Name == "mp_spawnpoint_defender" || e.Prefab == "mp_spawnpoint_defender").ToList();
            var wrongNamed = all.Where(e =>
            {
                bool isCorrect = e.Prefab == "skirmish_start_spawn" || e.Name == "skirmish_start_spawn";
                bool isSgt = (e.Name?.IndexOf("sergeant_spawn", StringComparison.OrdinalIgnoreCase) >= 0) || (e.Prefab?.IndexOf("sergeant_spawn", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isStandalone = e.Name == "mp_spawnpoint_attacker" || e.Name == "mp_spawnpoint_defender" || e.Prefab == "mp_spawnpoint_attacker" || e.Prefab == "mp_spawnpoint_defender";
                if (isCorrect || isSgt || isStandalone) return false;
                return e.Node.Descendants("game_entity").Any(c =>
                    SceneXmlHelpers.HasTag(c, "spawnpoint") && (SceneXmlHelpers.HasTag(c, "attacker") || SceneXmlHelpers.HasTag(c, "defender")));
            }).ToList();

            if (wrongNamed.Count > 0)
            {
                var names = string.Join(", ", wrongNamed.Select(e => e.Label));
                findings.Add(Err($"{wrongNamed.Count} spawn zone container(s) with a non-standard name: {names}. The Battle round spawn controller searches specifically for 'skirmish_start_spawn' by name - any other name is ignored after warmup and players cannot respawn."));
            }
            else if (sssContainers.Count > 0)
            {
                findings.Add(Info($"skirmish_start_spawn containers: {sssContainers.Count} found. OK."));
                bool hasAtk = sssContainers.Any(e => e.Node.Descendants("game_entity").Any(c => SceneXmlHelpers.HasTag(c, "attacker")));
                bool hasDef = sssContainers.Any(e => e.Node.Descendants("game_entity").Any(c => SceneXmlHelpers.HasTag(c, "defender")));
                if (!hasAtk) findings.Add(Err("skirmish_start_spawn container(s) found but none contain attacker-tagged children. Attacker team will not spawn after warmup."));
                if (!hasDef) findings.Add(Err("skirmish_start_spawn container(s) found but none contain defender-tagged children. Defender team will not spawn after warmup."));
            }
            else if (standaloneAD.Count > 0)
            {
                findings.Add(Warn($"{standaloneAD.Count} standalone mp_spawnpoint_attacker/defender entity(ies) found with no skirmish_start_spawn container. TW documentation specifies skirmish_start_spawn as the required prefab - standalone entities work in practice but rely on undocumented behavior."));
            }

            // Capture flags A/B/C
            foreach (var fn in new[] { "flag_pole_big_sergeant_A", "flag_pole_big_sergeant_B", "flag_pole_big_sergeant_C" })
            {
                var found = all.Where(e => e.Name == fn || e.Prefab == fn).ToList();
                if (found.Count == 0) findings.Add(Err($"Missing Battle capture flag '{fn}'. All 3 sergeant flags (A, B, C) are required."));
                else if (found.Count > 1) findings.Add(Warn($"Multiple '{fn}' entities found ({found.Count}). Only 1 expected."));
                else findings.Add(Info($"{fn}: found. OK."));
            }

            var borders = all.Where(e => e.Name == "border_soft" || e.Prefab == "border_soft").ToList();
            findings.Add(borders.Count < 4
                ? Warn($"Only {borders.Count} border_soft entit(ies) found. Battle maps typically use 4 to define the play area boundary.")
                : Info($"border_soft: {borders.Count} found. OK."));

            return findings;
        }

        // ============================================================
        // CHECKS: SKIRMISH MODE VALIDATION (ported from Gotha's BL_AddTestScene)
        // ============================================================
        public static List<Finding> CheckSkirmishMode(XDocument doc)
        {
            var findings = new List<Finding>();
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);

            // Spawn zone tree: leaf entities tagged "spawnpoint" -> their XML PARENT is the zone
            // container. Gotha's actual model (MPDominationPlayerMissionController.cs): the leaf
            // carries "spawnpoint" + "attacker"/"defender"; the parent carries "starting" (initial
            // spawn) and should also carry "spawn_zone" per TW docs.
            var leaves = all.Where(e => SceneXmlHelpers.HasTag(e.Node, "spawnpoint")).ToList();
            var defenderZones = new List<XElement>();
            var attackerZones = new List<XElement>();
            var seenDef = new HashSet<XElement>();
            var seenAtk = new HashSet<XElement>();

            foreach (var leaf in leaves)
            {
                var zoneNode = leaf.Node.Parent;
                if (zoneNode == null || zoneNode.Name.LocalName != "game_entity") continue;
                var leafTags = SceneXmlHelpers.GetTagNames(leaf.Node);
                bool isDefender = leafTags.Contains("defender");
                bool isAttacker = leafTags.Contains("attacker");
                if (!isDefender && !isAttacker) continue;
                if (isDefender && seenDef.Add(zoneNode)) defenderZones.Add(zoneNode);
                if (isAttacker && seenAtk.Add(zoneNode)) attackerZones.Add(zoneNode);
            }

            int ValidateZones(List<XElement> zones, string team, List<Finding> outFindings)
            {
                int startingCount = 0;
                int missingSpawnZone = 0;
                foreach (var zone in zones)
                {
                    var zoneTags = new HashSet<string>(zone.Element("tags")?.Elements("tag").Select(t => (string)t.Attribute("name")) ?? Enumerable.Empty<string>());
                    bool hasStarting = zoneTags.Contains("starting");
                    bool hasSpawnZone = zoneTags.Contains("spawn_zone");
                    var zoneName = (string)zone.Attribute("name") ?? "(unnamed)";
                    var children = zone.Elements("game_entity").ToList();

                    if (children.Count == 0)
                    {
                        if (hasStarting) outFindings.Add(Warn($"Skirmish: {team} zone '{zoneName}' is tagged 'starting' but has NO child spawnpoint entities. Empty starting zones are skipped by the game."));
                        continue;
                    }

                    if (!hasSpawnZone) { missingSpawnZone++; }

                    if (hasStarting)
                    {
                        var firstChildTags = new HashSet<string>(children[0].Element("tags")?.Elements("tag").Select(t => (string)t.Attribute("name")) ?? Enumerable.Empty<string>());
                        if (firstChildTags.Contains(team.ToLowerInvariant()))
                            startingCount++;
                        else
                            outFindings.Add(Warn($"Skirmish: {team} zone '{zoneName}' has 'starting' tag but its first child spawnpoint does NOT carry a '{team.ToLowerInvariant()}' tag - won't count as a valid starting spawn."));
                    }
                }
                if (missingSpawnZone > 0)
                    outFindings.Add(Warn($"Skirmish: {missingSpawnZone} {team} zone(s) lack the 'spawn_zone' tag required by official documentation."));
                return startingCount;
            }

            int defStart = ValidateZones(defenderZones, "Defender", findings);
            int atkStart = ValidateZones(attackerZones, "Attacker", findings);

            findings.Add(defStart == 0 ? Err("Skirmish: No valid DEFENDER starting spawn zones found! Defender team will have nowhere to spawn at round start.") : Info($"Defender starting zones: {defStart}. OK."));
            findings.Add(atkStart == 0 ? Err("Skirmish: No valid ATTACKER starting spawn zones found! Attacker team will have nowhere to spawn at round start.") : Info($"Attacker starting zones: {atkStart}. OK."));

            int defRespawn = defenderZones.Count - defStart;
            int atkRespawn = attackerZones.Count - atkStart;
            if (defRespawn > 3) findings.Add(Warn($"Skirmish: {defRespawn} defender respawn zones detected. Docs state 'up to 3 per side' - extras may be ignored."));
            if (atkRespawn > 3) findings.Add(Warn($"Skirmish: {atkRespawn} attacker respawn zones detected. Docs state 'up to 3 per side' - extras may be ignored."));

            // Capture flags A/B/C
            foreach (var fn in new[] { "flag_pole_big_sergeant_A", "flag_pole_big_sergeant_B", "flag_pole_big_sergeant_C" })
            {
                var found = all.Where(e => e.Name == fn || e.Prefab == fn).ToList();
                if (found.Count == 0) findings.Add(Err($"Skirmish: Missing capture flag '{fn}'. All 3 (A, B, C) are required."));
                else if (found.Count > 1) findings.Add(Warn($"Skirmish: Multiple '{fn}' entities found ({found.Count})."));
                else findings.Add(Info($"{fn}: found. OK."));
            }

            // border_soft
            var skirmBorders = all.Where(e => e.Name == "border_soft" || e.Prefab == "border_soft").ToList();
            if (skirmBorders.Count == 0) findings.Add(Err("Skirmish: No border_soft entities found. Players will have no out-of-bounds warning."));
            else if (skirmBorders.Count < 3) findings.Add(Warn($"Skirmish: Only {skirmBorders.Count} border_soft entit(ies). A full boundary usually needs >=4."));
            else findings.Add(Info($"border_soft: {skirmBorders.Count} found. OK."));

            // StonePile/destructibles that crash outside Siege
            var crashDestructibles = new[] { "rock_pile", "pot_pile", "arrow_barrel", "boulder_destructible" };
            var destructibleHits = new List<string>();
            foreach (var e in all)
            {
                var label = (e.Name ?? "") + "/" + (e.Prefab ?? "");
                var ll = label.ToLowerInvariant();
                if (crashDestructibles.Any(bad => ll.Contains(bad))) { destructibleHits.Add(label.Trim('/')); continue; }
                if (e.Node.Descendants("script").Any(s => { var sn = (string)s.Attribute("name"); return sn != null && (sn.Contains("StonePile") || sn.Contains("DestructibleComponent")); }))
                    destructibleHits.Add(label.Trim('/') + " (destructible script)");
            }
            destructibleHits = destructibleHits.Distinct().ToList();
            if (destructibleHits.Count > 0)
                findings.Add(Err($"Skirmish/non-Siege: {destructibleHits.Count} SIEGE-ONLY destructible entity/entities that WILL CRASH here: {string.Join("; ", destructibleHits)}. Remove these or restrict to the Siege scene level."));
            else
                findings.Add(Info("No crash-prone Siege-only destructibles detected."));

            // enforce_troop_spawn debug tag
            var enforceSpawns = all.Where(e => SceneXmlHelpers.HasTag(e.Node, "enforce_troop_spawn")).ToList();
            if (enforceSpawns.Count > 0)
                findings.Add(Info($"{enforceSpawns.Count} entity/entities tagged 'enforce_troop_spawn' (editor-only debug spawn). Verify these are intentional, not leftover."));

            return findings;
        }

        // ============================================================
        // CHECK: SIEGE MODE VALIDATION
        // ============================================================
        public static List<Finding> CheckSiegeMode(XDocument doc)
        {
            var findings = new List<Finding>();
            var all = SceneXmlHelpers.CollectAllEntitiesRecursive(doc);

            // sp_zone_0..6 tag inventory - each must appear on AT MOST one parent entity.
            var spZoneTagMap = new Dictionary<string, List<string>>();
            foreach (var e in all)
            {
                for (int zx = 0; zx <= 6; zx++)
                {
                    var tagName = $"sp_zone_{zx}";
                    if (SceneXmlHelpers.HasTag(e.Node, tagName))
                    {
                        if (!spZoneTagMap.TryGetValue(tagName, out var list)) spZoneTagMap[tagName] = list = new List<string>();
                        list.Add(e.Label);
                    }
                }
            }

            if (spZoneTagMap.Count == 0)
            {
                findings.Add(Err("Siege: No sp_zone_x tags found at all. Requires spawn zones tagged sp_zone_0 through sp_zone_6 (minimum sp_zone_0 and sp_zone_6). Players will not be able to spawn."));
            }
            else
            {
                foreach (var required in new[] { "sp_zone_0", "sp_zone_6" })
                    if (!spZoneTagMap.ContainsKey(required))
                        findings.Add(Err($"Siege: Missing '{required}' tag. Server will crash without it."));

                foreach (var kv in spZoneTagMap)
                {
                    if (kv.Value.Count > 1)
                        findings.Add(Err($"Siege: Tag '{kv.Key}' is present on {kv.Value.Count} different entities: {string.Join(", ", kv.Value)}. Multiple spawn parents with the SAME sp_zone_x tag breaks siege spawning (silent fail)."));
                    else
                        findings.Add(Info($"{kv.Key}: 1 zone entity. OK."));
                }
            }

            // Siege flags A-F + main (7 total)
            var siegeFlagNames = new[] { "flag_pole_big_sergeant_A", "flag_pole_big_sergeant_B", "flag_pole_big_sergeant_C", "flag_pole_big_sergeant_D", "flag_pole_big_sergeant_E", "flag_pole_big_sergeant_F", "flag_pole_big_sergeant_main" };
            foreach (var fn in siegeFlagNames)
            {
                var found = all.Where(e => e.Name == fn || e.Prefab == fn).ToList();
                if (found.Count == 0) findings.Add(Err($"Siege: Missing flag '{fn}'. All 6 lettered flags (A-F) plus main are required."));
                else if (found.Count > 1) findings.Add(Warn($"Siege: Multiple '{fn}' found ({found.Count})."));
                else findings.Add(Info($"{fn}: found. OK."));
            }

            var siegeSpawnpoints = all.Count(e => SceneXmlHelpers.HasTag(e.Node, "mp_spawnpoint"));
            findings.Add(siegeSpawnpoints == 0
                ? Err("Siege: No mp_spawnpoint entities found as children of siege zones. Players cannot spawn.")
                : Info($"mp_spawnpoint total: {siegeSpawnpoints} found."));

            var wrongStarting = all.Count(e =>
            {
                var tags = SceneXmlHelpers.GetTagNames(e.Node);
                return tags.Contains("starting") && Enumerable.Range(0, 7).Any(zx => tags.Contains($"sp_zone_{zx}"));
            });
            if (wrongStarting > 0)
                findings.Add(Warn($"Siege: {wrongStarting} entity/entities have BOTH 'starting' AND an 'sp_zone_x' tag. 'starting' is for Skirmish/Battle and should be removed from Siege spawn zones."));

            // Wall segments
            var wallSegments = all.Where(e => e.Node.Descendants("script").Any(s => (string)s.Attribute("name") == "WallSegment")).ToList();
            if (wallSegments.Count == 0)
            {
                findings.Add(Err("Siege: No entities with WallSegment script found. Siegeable walls require a WallSegment script with broken_child/solid_child children and nav mesh IDs configured."));
            }
            else
            {
                findings.Add(Info($"WallSegment entities: {wallSegments.Count} found."));
                foreach (var ws in wallSegments)
                {
                    bool hasBroken = SceneXmlHelpers.HasDescendantTag(ws.Node, "broken_child");
                    bool hasSolid = SceneXmlHelpers.HasDescendantTag(ws.Node, "solid_child");
                    if (!hasBroken) findings.Add(Warn($"Siege: WallSegment on '{ws.Label}' has no child tagged 'broken_child'. Breached wall state will not work correctly."));
                    if (!hasSolid) findings.Add(Warn($"Siege: WallSegment on '{ws.Label}' has no child tagged 'solid_child'. Intact wall state will not work correctly."));
                }
            }

            // Castle gates
            var outerGates = all.Where(e => SceneXmlHelpers.HasTag(e.Node, "outer_gate")).ToList();
            var innerGates = all.Where(e => SceneXmlHelpers.HasTag(e.Node, "inner_gate")).ToList();
            if (outerGates.Count == 0)
            {
                findings.Add(Err("Siege: No entity tagged 'outer_gate' found. The battering ram targets the outer gate - without this tag it cannot function."));
            }
            else
            {
                findings.Add(Info($"outer_gate: {outerGates.Count} found."));
                foreach (var og in outerGates)
                    if (!og.Node.Descendants("script").Any(s => (string)s.Attribute("name") == "CastleGate"))
                        findings.Add(Warn($"Siege: Entity tagged 'outer_gate' ('{og.Label}') has no CastleGate script. Battering ram interaction requires it."));
            }
            findings.Add(innerGates.Count == 0
                ? Warn("Siege: No entity tagged 'inner_gate' found. Most layouts need one for troop AI pathfinding into the keep.")
                : Info($"inner_gate: {innerGates.Count} found."));

            // Siege machine spawners
            bool HasScript(SceneXmlEntity e, params string[] names) =>
                e.Node.Descendants("script").Any(s => names.Contains((string)s.Attribute("name")));

            var ramSpawners = all.Where(e => HasScript(e, "MultiplayerBatteringRamSpawner", "BatteringRamSpawner")).ToList();
            var towerSpawners = all.Where(e => HasScript(e, "MultiplayerSiegeTowerSpawner")).ToList();
            var ladderSpawners = all.Where(e => HasScript(e, "SiegeLadderSpawner")).ToList();
            var mangSpawners = all.Where(e => HasScript(e, "MultiplayerMangonelSpawner", "MultiplayerFireMangonelSpawner", "MultiplayerTrebuchetSpawner", "MultiplayerFireTrebuchetSpawner")).ToList();

            findings.Add(ramSpawners.Count == 0
                ? Err("Siege: No battering ram spawner found. Every siege map needs one - without it the middle lane cannot be attacked.")
                : Info($"Battering ram spawner(s): {ramSpawners.Count} found."));
            if (towerSpawners.Count == 0 && ladderSpawners.Count == 0)
                findings.Add(Err("Siege: No siege tower or ladder spawner found. Attackers need at least one wall-access machine per lane."));
            else
            {
                if (towerSpawners.Count > 0) findings.Add(Info($"Siege tower spawner(s): {towerSpawners.Count} found."));
                if (ladderSpawners.Count > 0) findings.Add(Info($"Siege ladder spawner(s): {ladderSpawners.Count} found."));
            }
            if (mangSpawners.Count > 0) findings.Add(Info($"Ranged siege machine spawner(s): {mangSpawners.Count} found."));

            // MP siege objective tags
            var objTags = new Dictionary<string, string>
            {
                ["mp_siege_objective_battering_ram"] = "Battering ram objective",
                ["mp_siege_objective_castle_gate"] = "Castle gate objective",
                ["mp_siege_objective_side_objective"] = "Side objective (tower/ladder lane)",
                ["mp_siege_objective_siege_tower"] = "Siege tower objective",
            };
            foreach (var kv in objTags)
            {
                var found = all.Count(e => SceneXmlHelpers.HasTag(e.Node, kv.Key));
                findings.Add(found == 0
                    ? Warn($"Siege: No entity tagged '{kv.Key}' found ({kv.Value}). Drives objective UI and AI target selection.")
                    : Info($"{kv.Key}: {found} found. OK."));
            }

            // level_visibility_mask on deployers (0/unset causes crashes per community reports)
            var deployers = all.Where(e => HasScript(e, "MultiplayerBatteringRamSpawner", "MultiplayerSiegeTowerSpawner", "SiegeLadderSpawner")).ToList();
            foreach (var dep in deployers)
            {
                var mask = (string)dep.Node.Attribute("level_visibility_mask");
                if (mask == "0" || string.IsNullOrEmpty(mask))
                    findings.Add(Warn($"Siege: Siege machine spawner '{dep.Label}' has level_visibility_mask='{mask}' (all levels hidden/unset). Community reports this causes crashes. Set Lvl 1-3 ON, Siege ON, Civilian OFF, Sally ON."));
            }

            return findings;
        }
    }
}
