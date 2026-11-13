using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace MaterialSwapTool.Core
{
    // SCENE STATISTICS + LIGHT LIST (2026-11-12). Read-only: nothing here changes the scene.
    //
    // COST MODEL. Managed work is free next to native interop, so everything is organized
    // around how many engine calls one button press makes:
    //
    //   Collect        one pass, ~6 integer/string calls per entity (prefab name, script count,
    //                  four component counts). Lights are read in full only on the entities
    //                  that own one - a few dozen out of 12,000+.
    //   CollectMeshes  a second, heavier pass (one MetaMesh wrapper + name per mesh component),
    //                  which is why it runs only when the Heaviest tab is first opened. Face
    //                  counts are read once per DISTINCT meta mesh and multiplied out.
    //
    // Nothing is cached between presses and nothing runs on tick. Rows keep names and
    // positions, never GameEntity references: Select re-finds its entities by name at click
    // time, so a row left over from before a scene switch cannot hand the editor a dead pointer.
    public static class SceneStatistics
    {
        // A point light casting shadows renders the scene again for every face of its shadow
        // map, and how much of the scene falls inside grows with the radius. These are
        // judgement thresholds for "worth a look", not engine limits - torches in native scenes
        // sit around radius 6-10.
        public const float ShadowRadiusWarn = 15f;
        public const float HugeRadiusWarn = 40f;
        private const float ZeroThreshold = 0.001f;
        private const float SamePositionGrid = 0.05f;

        public sealed class PrefabRow
        {
            public string Name;
            public int Count;
            public int Linked;      // still a live prefab link (prefab= in the file); the rest are baked copies
            public bool HasPos;
            public float X, Y, Z;   // first instance, for Go
        }

        public sealed class LightRow
        {
            public string Owner;
            public string Root;     // top-level ancestor's name, when the owner is a child
            public float Radius, Intensity;
            public float R, G, B;
            public bool Shadow;
            public bool Hidden;
            public int ExtraLights; // further light components on the same entity (only the first is readable)
            public bool ReadFailed;
            public float X, Y, Z;
            public int SamePositionCount;
            public List<string> Warnings = new List<string>();

            public bool HasWarning => Warnings.Count > 0;
            public float ShadowCost => Shadow ? Radius * Radius : 0f;
        }

        public sealed class Snapshot
        {
            public string SceneName;
            public int Entities;
            public int TopLevel = -1;           // -1 = the engine count was unavailable
            public int WithPrefab, LinkedPrefabs, NoPrefab;
            public int MeshEntities, MeshComponents;
            public int LightEntities, LightComponents, ShadowLights, LightWarnings;
            public int ParticleEntities, ParticleComponents;
            public int DecalEntities, DecalComponents;
            public int ClothComponents;
            public int ScriptedEntities, ScriptComponents;
            public int Unreadable;
            public List<PrefabRow> Prefabs = new List<PrefabRow>();
            public List<LightRow> Lights = new List<LightRow>();
            public long ElapsedMs;
            // What could not be counted because the engine call behind it crashed the editor in
            // an earlier session (Core/NativeProbe). Shown in the panel so a 0 is not mistaken
            // for "none".
            public List<string> Skipped = new List<string>();
            public bool LightsAvailable = true;
        }

        public sealed class MeshRow
        {
            public string Name;
            public int Instances;
            public long TrisEach;
            public bool HasPos;
            public float X, Y, Z;
            public long Total => TrisEach * Instances;
        }

        public sealed class MeshSnapshot
        {
            public List<MeshRow> Rows = new List<MeshRow>();
            public long TotalTris;
            public int Components;
            public int ZeroFaceMeshes;   // distinct meta meshes whose LOD0 face count read back as 0
            public int Unreadable;
            public long ElapsedMs;
        }

        // The prefab an entity came from: the live link if there is one, else the name it was
        // baked from. Two native calls at most, one in the common (linked) case.
        private const string BakedProbeName = "GameEntity.Name + PrefabExists (baked copies)";

        private static readonly System.Text.RegularExpressions.Regex DriftSuffix =
            new System.Text.RegularExpressions.Regex(@"(_mst|_\d+)$",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Which prefab an UNLINKED entity is a copy of, judged by its display name: the name
        // itself if a prefab of that name exists, else the name with editor/toolkit suffixes
        // (_2, _3, _mst) stripped one at a time. null when nothing matches. One or a few engine
        // calls per DISTINCT name - the cache makes thousands of same-named copies free.
        private static string ResolveBakedPrefab(string displayName, Dictionary<string, string> cache)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            if (cache.TryGetValue(displayName, out var known)) return known;

            string found = null;
            var current = displayName.Trim();
            for (int step = 0; step < 4 && current.Length > 0; step++)
            {
                if (GameEntity.PrefabExists(current)) { found = current; break; }
                var stripped = DriftSuffix.Replace(current, "");
                if (stripped.Length == current.Length) break;
                current = stripped;
            }
            cache[displayName] = found;
            return found;
        }

        private static string PrefabKey(GameEntity e, Dictionary<string, string> bakedCache, out bool linked)
        {
            linked = false;
            string name = null;
            try { name = e.GetPrefabName(); } catch { }
            if (!string.IsNullOrEmpty(name)) { linked = true; return name; }
            // No fallback to GetOldPrefabName(): that call crashes the editor (see Collect).
            // Baked copies are matched by display name, as in the scan - unless that pass has
            // itself been switched off by the crash ledger.
            if (NativeProbe.IsSkipped("SceneStats: " + BakedProbeName)) return null;
            try { return ResolveBakedPrefab(e.Name, bakedCache); } catch { }
            return null;
        }

        // Runs one group of not-yet-proven engine calls under the crash ledger. Returns false
        // (and notes what is missing) when that group crashed the editor in an earlier session.
        private static bool Probe(Snapshot s, string ledgerName, string what, Action action)
        {
            bool ran;
            try { ran = NativeProbe.Run("SceneStats: " + ledgerName, action); }
            catch (Exception ex)
            {
                Log.Warn("[SceneStats] " + ledgerName + " threw: " + ex.Message);
                s.Skipped.Add(what + " (error: " + ex.Message + ")");
                return false;
            }
            if (!ran) s.Skipped.Add(what);
            return ran;
        }

        public static Snapshot Collect(Scene scene)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var s = new Snapshot();
            try { s.SceneName = scene.GetName(); } catch { }

            var all = LiveSceneChecks.CollectAll(scene);
            s.Entities = all.Count;
            int n = all.Count;

            // PASSES, NOT ONE LOOP (2026-11-12). The first live run of this scan crashed the
            // editor natively, with nothing logged and no way to tell which engine call did it.
            // Pass 1 uses only calls the rest of the toolkit has made for months. Every call
            // that had never been made before today then gets its OWN pass inside
            // NativeProbe.Run: if one of them takes the editor down, the ledger names it and it
            // is skipped from then on (see Core/NativeProbe.cs). The cost is a few more trips
            // over the entity list, which is managed and cheap.
            var linkedName = new string[n];
            var oldName = new string[n];
            var readable = new bool[n];

            // Pass 1 - proven calls: live prefab name, mesh component count.
            for (int i = 0; i < n; i++)
            {
                try
                {
                    var e = all[i];
                    var name = e.GetPrefabName();
                    linkedName[i] = string.IsNullOrEmpty(name) ? null : name;
                    int meshes = e.MultiMeshComponentCount;
                    if (meshes > 0) { s.MeshEntities++; s.MeshComponents += meshes; }
                    readable[i] = true;
                }
                catch { s.Unreadable++; }
            }

            Probe(s, "Scene.RootEntityCount", "top-level count", () =>
            {
                s.TopLevel = scene.RootEntityCount;
            });

            // GameEntity.GetOldPrefabName() IS NEVER CALLED (2026-11-12). It was the call that
            // crashed the editor on the first two live runs of this scan: the crash ledger
            // stopped on "trying|SceneStats: GameEntity.GetOldPrefabName" (18:48:18, a new map),
            // with Scene.RootEntityCount before it marked ok. So entities that are not live
            // prefab links (baked copies, hand-built ones) are counted but not named: oldName
            // stays empty and they land in "no prefab name".
            //
            // BAKED COPIES ARE NAMED FROM THEIR DISPLAY NAME INSTEAD (same day, after the first
            // working run: "most-used prefabs only shows what is in my heaviest entity, and
            // Select does not pick everything"). A broken-out copy has no live link, but it
            // keeps the prefab's name as its entity name - possibly with the editor's _2/_3
            // duplicate suffix or Material Swap's _mst. So: take the display name, ask the
            // engine whether a prefab of that name exists, and if not strip one such suffix and
            // ask again. Names that are not prefabs (sub-parts, hand-built things) stay unnamed.
            var bakedCache = new Dictionary<string, string>(StringComparer.Ordinal);
            Probe(s, BakedProbeName, "names of baked (unlinked) prefab copies", () =>
            {
                for (int i = 0; i < n; i++)
                {
                    if (!readable[i] || linkedName[i] != null) continue;
                    try { oldName[i] = ResolveBakedPrefab(all[i].Name, bakedCache); } catch { }
                }
            });

            Probe(s, "GetComponentCount(ClothSimulator)", "cloth simulators", () =>
            {
                for (int i = 0; i < n; i++)
                {
                    if (!readable[i]) continue;
                    try { int c = all[i].ClothSimulatorComponentCount; if (c > 0) s.ClothComponents += c; } catch { }
                }
            });

            Probe(s, "GetComponentCount(ParticleSystemInstanced)", "particle systems", () =>
            {
                for (int i = 0; i < n; i++)
                {
                    if (!readable[i]) continue;
                    try
                    {
                        int c = all[i].GetComponentCount(GameEntity.ComponentType.ParticleSystemInstanced);
                        if (c > 0) { s.ParticleEntities++; s.ParticleComponents += c; }
                    }
                    catch { }
                }
            });

            Probe(s, "GetComponentCount(Decal)", "decals", () =>
            {
                for (int i = 0; i < n; i++)
                {
                    if (!readable[i]) continue;
                    try
                    {
                        int c = all[i].GetComponentCount(GameEntity.ComponentType.Decal);
                        if (c > 0) { s.DecalEntities++; s.DecalComponents += c; }
                    }
                    catch { }
                }
            });

            // WeakGameEntity is a struct over the same pointer, and the only public route to
            // the script COUNT (GameEntity.ScriptCount is private).
            Probe(s, "WeakGameEntity.GetScriptCount", "script components", () =>
            {
                for (int i = 0; i < n; i++)
                {
                    if (!readable[i]) continue;
                    try
                    {
                        int c = all[i].WeakEntity.GetScriptCount();
                        if (c > 0) { s.ScriptedEntities++; s.ScriptComponents += c; }
                    }
                    catch { }
                }
            });

            var lightCounts = new int[n];
            bool lightsCounted = Probe(s, "GetComponentCount(Light)", "lights", () =>
            {
                for (int i = 0; i < n; i++)
                {
                    if (!readable[i]) continue;
                    try
                    {
                        int c = all[i].GetComponentCount(GameEntity.ComponentType.Light);
                        if (c > 0) { lightCounts[i] = c; s.LightEntities++; s.LightComponents += c; }
                    }
                    catch { }
                }
            });
            s.LightsAvailable = lightsCounted;

            if (lightsCounted && s.LightEntities > 0)
            {
                bool read = Probe(s, "GameEntity.GetLight + light getters", "light details (radius, color, shadow)", () =>
                {
                    for (int i = 0; i < n; i++)
                        if (lightCounts[i] > 0) s.Lights.Add(ReadLight(all[i], lightCounts[i]));
                });
                if (!read)
                {
                    // Still list the lights, just without their values.
                    for (int i = 0; i < n; i++)
                    {
                        if (lightCounts[i] <= 0) continue;
                        var row = new LightRow { ExtraLights = Math.Max(0, lightCounts[i] - 1), ReadFailed = true };
                        try { row.Owner = all[i].Name; } catch { }
                        if (string.IsNullOrEmpty(row.Owner)) row.Owner = "(unnamed)";
                        try { var o = all[i].GetGlobalFrame().origin; row.X = o.x; row.Y = o.y; row.Z = o.z; } catch { }
                        s.Lights.Add(row);
                    }
                }
            }

            // Prefab table, from the names gathered above.
            var prefabs = new Dictionary<string, PrefabRow>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                if (!readable[i]) continue;
                bool linked = linkedName[i] != null;
                var key = linked ? linkedName[i] : oldName[i];
                if (key == null) { s.NoPrefab++; continue; }
                s.WithPrefab++;
                if (linked) s.LinkedPrefabs++;
                if (!prefabs.TryGetValue(key, out var row))
                {
                    row = new PrefabRow { Name = key };
                    // Position of the FIRST instance only - one frame read per distinct prefab
                    // instead of one per entity.
                    try
                    {
                        var o = all[i].GetGlobalFrame().origin;
                        row.X = o.x; row.Y = o.y; row.Z = o.z; row.HasPos = true;
                    }
                    catch { }
                    prefabs[key] = row;
                }
                row.Count++;
                if (linked) row.Linked++;
            }

            s.Prefabs = prefabs.Values.OrderByDescending(p => p.Count).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            FlagLights(s);
            if (s.Skipped.Count > 0)
                Log.Warn("[SceneStats] not counted (crashed the editor in an earlier session, see " + NativeProbe.LedgerPath + "): " + string.Join("; ", s.Skipped));
            s.ElapsedMs = stopwatch.ElapsedMilliseconds;
            Log.Info($"[SceneStats] '{s.SceneName}': {s.Entities} entities, {s.Prefabs.Count} distinct prefabs, " +
                     $"{s.LightComponents} light(s), {s.Unreadable} unreadable, {s.ElapsedMs}ms.");
            return s;
        }

        // GameEntity.GetLight() hands back the entity's (first) light. The engine itself reads
        // lights this way on every torch (LightCycle.SetVisibility), so it is the trodden path.
        // Only getters are touched here.
        private static LightRow ReadLight(GameEntity e, int lightCount)
        {
            var row = new LightRow { ExtraLights = Math.Max(0, lightCount - 1) };
            try { row.Owner = e.Name; } catch { }
            if (string.IsNullOrEmpty(row.Owner)) row.Owner = "(unnamed)";

            try
            {
                var root = EntitySelector.TopMostParent(e);
                if (EntitySelector.IsValidEntity(root) && root.Pointer != e.Pointer)
                {
                    var rootName = root.Name;
                    if (!string.IsNullOrEmpty(rootName)) row.Root = rootName;
                }
            }
            catch { }

            try
            {
                var o = e.GetGlobalFrame().origin;
                row.X = o.x; row.Y = o.y; row.Z = o.z;
            }
            catch { }

            try { row.Hidden = !e.IsVisibleIncludeParents(); } catch { }

            try
            {
                var light = e.GetLight();
                if (light == null || !light.IsValid) { row.ReadFailed = true; return row; }
                row.Radius = light.Radius;
                row.Intensity = light.Intensity;
                var c = light.LightColor;
                row.R = c.x; row.G = c.y; row.B = c.z;
                row.Shadow = light.ShadowEnabled;
            }
            catch (Exception ex)
            {
                row.ReadFailed = true;
                Log.Warn("[SceneStats] reading the light on '" + row.Owner + "' failed: " + ex.Message);
            }
            return row;
        }

        private static void FlagLights(Snapshot s)
        {
            // Same-position groups, on a 5 cm grid: two lights that close together are a
            // stacked duplicate (a pasted torch) far more often than a design choice.
            var cells = new Dictionary<long, int>();
            Func<LightRow, long> cell = l =>
            {
                long x = (long)Math.Round(l.X / SamePositionGrid);
                long y = (long)Math.Round(l.Y / SamePositionGrid);
                long z = (long)Math.Round(l.Z / SamePositionGrid);
                return unchecked(x * 73856093L ^ y * 19349663L ^ z * 83492791L);
            };
            foreach (var l in s.Lights)
            {
                var k = cell(l);
                cells[k] = cells.TryGetValue(k, out var n) ? n + 1 : 1;
            }

            foreach (var l in s.Lights)
            {
                if (l.Shadow) s.ShadowLights++;
                l.SamePositionCount = cells[cell(l)];

                if (l.ReadFailed) { l.Warnings.Add("could not be read"); }
                else
                {
                    if (l.Radius <= ZeroThreshold) l.Warnings.Add("zero radius - lights nothing");
                    if (l.Intensity <= ZeroThreshold) l.Warnings.Add("zero intensity - lights nothing");
                    if (l.Shadow && l.Radius >= ShadowRadiusWarn)
                        l.Warnings.Add("shadow-casting with a large radius - likely expensive");
                    else if (l.Radius >= HugeRadiusWarn)
                        l.Warnings.Add("very large radius");
                }
                if (l.SamePositionCount > 1) l.Warnings.Add(l.SamePositionCount + " lights at this position");
                if (l.ExtraLights > 0) l.Warnings.Add("+" + l.ExtraLights + " more light component(s) on this entity, not listed");
                if (l.HasWarning) s.LightWarnings++;
            }
        }

        // ---- Heaviest meshes ----

        // "house_d.lod5.6" is the LOD5 copy of part 6; "house_d.6" is full detail. The lodN
        // segment in the mesh name is the tier marker LodMismatchChecker verified live, and it
        // is used here rather than GetLodMaskForMeshAtIndex, whose bit layout was never checked.
        private static bool IsLodMeshName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int i = name.IndexOf(".lod", StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                int d = i + 4;
                if (d < name.Length && char.IsDigit(name[d])) return true;
                i = name.IndexOf(".lod", i + 1, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static long Lod0Faces(MetaMesh meta)
        {
            long faces = 0;
            int count = meta.MeshCount;
            for (int i = 0; i < count; i++)
            {
                var mesh = meta.GetMeshAtIndex(i);
                if (mesh == null || !mesh.IsValid) continue;
                if (IsLodMeshName(mesh.Name)) continue;
                faces += mesh.GetFaceCount();
            }
            return faces;
        }

        // A meta mesh with no name of its own (a loose mesh component) is keyed by its first
        // mesh, so it still caches and still groups.
        private static string MeshKey(MetaMesh meta)
        {
            var name = meta.GetName();
            if (!string.IsNullOrEmpty(name)) return name;
            if (meta.MeshCount <= 0) return null;
            var first = meta.GetMeshAtIndex(0);
            var firstName = first != null && first.IsValid ? first.Name : null;
            return string.IsNullOrEmpty(firstName) ? null : "(unnamed) " + firstName;
        }

        public static MeshSnapshot CollectMeshes(Scene scene)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var s = new MeshSnapshot();
            var rows = new Dictionary<string, MeshRow>(StringComparer.Ordinal);

            // Whole pass under the crash ledger: MetaMesh.GetName and Mesh.GetFaceCount had
            // never been called by the toolkit before 2026-11-12.
            bool ran = NativeProbe.Run("SceneStats: mesh names + Mesh.GetFaceCount", () => CollectMeshRows(scene, s, rows));
            if (!ran)
            {
                Log.Warn("[SceneStats] triangle counts skipped: that scan crashed the editor in an earlier session (" + NativeProbe.LedgerPath + ").");
                return null;
            }

            s.Rows = rows.Values.OrderByDescending(r => r.Total).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var r in s.Rows) s.TotalTris += r.Total;
            s.ElapsedMs = stopwatch.ElapsedMilliseconds;
            Log.Info($"[SceneStats] meshes: {s.Components} component(s), {s.Rows.Count} distinct, {s.TotalTris} LOD0 tris, " +
                     $"{s.ZeroFaceMeshes} read as 0 faces, {s.Unreadable} unreadable, {s.ElapsedMs}ms.");
            return s;
        }

        private static void CollectMeshRows(Scene scene, MeshSnapshot s, Dictionary<string, MeshRow> rows)
        {
            foreach (var e in LiveSceneChecks.CollectAll(scene))
            {
                try
                {
                    int count = e.MultiMeshComponentCount;
                    for (int m = 0; m < count; m++)
                    {
                        var meta = e.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;
                        var key = MeshKey(meta);
                        if (key == null) continue;
                        s.Components++;

                        if (!rows.TryGetValue(key, out var row))
                        {
                            row = new MeshRow { Name = key, TrisEach = Lod0Faces(meta) };
                            if (row.TrisEach == 0) s.ZeroFaceMeshes++;
                            try
                            {
                                var o = e.GetGlobalFrame().origin;
                                row.X = o.x; row.Y = o.y; row.Z = o.z; row.HasPos = true;
                            }
                            catch { }
                            rows[key] = row;
                        }
                        row.Instances++;
                    }
                }
                catch { s.Unreadable++; }
            }
        }

        // ---- Select (re-resolved at click time, see the class comment) ----

        public static List<GameEntity> FindPrefabInstances(Scene scene, string prefabName)
        {
            var result = new List<GameEntity>();
            var bakedCache = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in LiveSceneChecks.CollectAll(scene))
            {
                try
                {
                    if (string.Equals(PrefabKey(e, bakedCache, out _), prefabName, StringComparison.Ordinal)) result.Add(e);
                }
                catch { }
            }
            return result;
        }

        public static List<GameEntity> FindMeshUsers(Scene scene, string meshKey)
        {
            var result = new List<GameEntity>();
            foreach (var e in LiveSceneChecks.CollectAll(scene))
            {
                try
                {
                    int count = e.MultiMeshComponentCount;
                    for (int m = 0; m < count; m++)
                    {
                        var meta = e.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;
                        if (string.Equals(MeshKey(meta), meshKey, StringComparison.Ordinal)) { result.Add(e); break; }
                    }
                }
                catch { }
            }
            return result;
        }
    }
}
