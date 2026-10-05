using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.Engine;

namespace MaterialSwapTool.Core
{
    // Finds and replaces known-broken prefab instances in the LIVE scene via LivePrefabSwapper -
    // same idea as the "bannerlord_physics_unf-cker" community tool this was originally modeled
    // on, but now applies instantly to the running editor scene instead of editing scene.xscene on
    // disk (see LivePrefabSwapper for why: it reflects unsaved edits and needs no scene reload to
    // see the result). A live GameEntity doesn't expose its source prefab name as a separate field
    // from its instance Name, but Bannerlord names an instance after its prefab by default, so
    // matching on entity.Name against the mapping's broken-prefab keys is the correct live-editor
    // equivalent of the old prefab= attribute match.
    //
    // Seeded with the one documented case (module_wall_plank_a/b, which ship with inverted
    // collision - "walk in, can't walk out") but the mapping is just a JSON file, same editable-
    // override convention as everything else, so more broken/fixed pairs can be added without a
    // redeploy.
    public static class BrokenPrefabFixer
    {
        private static Dictionary<string, string> _mappings;

        private static string ModuleDir => System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ShippedDefaultPath => System.IO.Path.Combine(ModuleDir, "ReferenceData", "broken_prefabs.json");

        private static string UserOverridePath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "broken_prefabs.json");

        private static Dictionary<string, string> Mappings
        {
            get
            {
                if (_mappings != null) return _mappings;
                var path = File.Exists(UserOverridePath) ? UserOverridePath : ShippedDefaultPath;
                try
                {
                    if (File.Exists(path))
                    {
                        _mappings = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))
                                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        return _mappings;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("BrokenPrefabFixer: failed to load mappings: " + ex);
                }
                _mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return _mappings;
            }
        }

        public class ScanResult
        {
            // broken prefab name -> how many live instances found
            public Dictionary<string, int> MatchCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public int TotalMatches => MatchCounts.Values.Sum();
        }

        public static ScanResult Scan()
        {
            if (!EntitySelector.HasOpenScene) return null;
            var all = LiveSceneChecks.CollectAll(EntitySelector.CurrentScene);
            var result = new ScanResult();

            foreach (var e in all)
            {
                if (e.Name != null && Mappings.ContainsKey(e.Name))
                    result.MatchCounts[e.Name] = result.MatchCounts.TryGetValue(e.Name, out var c) ? c + 1 : 1;
            }
            return result;
        }

        public class ApplyResult
        {
            public int EntitiesFixed;
            public int Failed;
            public List<string> Errors = new List<string>();
        }

        // Caller is responsible for backing up first (see SceneAnalyzerVM - reuses BackupManager,
        // same as every other destructive action in this tool).
        public static ApplyResult ApplyFix()
        {
            if (!EntitySelector.HasOpenScene)
                throw new InvalidOperationException("No scene is currently open.");

            var scene = EntitySelector.CurrentScene;
            var all = LiveSceneChecks.CollectAll(scene);
            var targets = all.Where(e => e.Name != null && Mappings.ContainsKey(e.Name)).ToList();

            var result = new ApplyResult();
            foreach (var entity in targets)
            {
                var newPrefab = Mappings[entity.Name];
                var swap = LivePrefabSwapper.SwapEntity(scene, entity, newPrefab);
                if (swap.Success) result.EntitiesFixed++;
                else { result.Failed++; result.Errors.Add($"{entity.Name}: {swap.Error}"); }
            }
            return result;
        }
    }
}
