using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.Engine;

namespace MaterialSwapTool.Core
{
    public class RequirementItem
    {
        public string Label { get; set; }
        // "tag": entity.HasTag(Value). "prefab": GetPrefabName() contains Value, falling back to
        // entity.Name - many placed entities have no name at all, so the prefab name is the
        // identity that actually survives for them.
        // "note": not checkable from live entities at all - always surfaced, never marked found/
        // missing (e.g. NavMesh depth/ID, which has no entity or tag representation).
        public string Kind { get; set; }
        public string Value { get; set; }
        public int MinCount { get; set; } = 1;
    }

    public class SceneRequirementSet
    {
        public string DisplayName { get; set; }
        public List<RequirementItem> Requirements { get; set; } = new List<RequirementItem>();
    }

    // Scans the LIVE scene's entities/tags against a named checklist (e.g. what a War Sails
    // seaborne raid scene needs) - grounded in moddocs.bannerlord.com's own documented
    // requirements, not guessed. Same editable-JSON-with-override convention as everything else
    // in this tool, so more scene types can be added by editing ReferenceData\
    // scene_requirements.json (or the Documents override) without a redeploy.
    public static class SceneRequirementChecker
    {
        private static Dictionary<string, SceneRequirementSet> _sets;

        private static string ModuleDir => System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ShippedDefaultPath => System.IO.Path.Combine(ModuleDir, "ReferenceData", "scene_requirements.json");

        private static string UserOverridePath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "scene_requirements.json");

        private static Dictionary<string, SceneRequirementSet> Sets
        {
            get
            {
                if (_sets != null) return _sets;
                var path = File.Exists(UserOverridePath) ? UserOverridePath : ShippedDefaultPath;
                try
                {
                    if (File.Exists(path))
                    {
                        _sets = JsonConvert.DeserializeObject<Dictionary<string, SceneRequirementSet>>(File.ReadAllText(path))
                                ?? new Dictionary<string, SceneRequirementSet>(StringComparer.OrdinalIgnoreCase);
                        return _sets;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("SceneRequirementChecker: failed to load requirement sets: " + ex);
                }
                _sets = new Dictionary<string, SceneRequirementSet>(StringComparer.OrdinalIgnoreCase);
                return _sets;
            }
        }

        public static string[] AllSetKeys => Sets.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

        public static string GetDisplayName(string key) =>
            Sets.TryGetValue(key, out var set) ? set.DisplayName : key;

        public class CheckItem
        {
            public string Label;
            public string Kind;
            public bool Found;
            public int Count;
        }

        public static List<CheckItem> Check(string setKey)
        {
            var result = new List<CheckItem>();
            if (!Sets.TryGetValue(setKey, out var set)) return result;

            if (!EntitySelector.HasOpenScene)
            {
                foreach (var req in set.Requirements)
                    result.Add(new CheckItem { Label = req.Label, Kind = req.Kind, Found = false, Count = 0 });
                return result;
            }

            var scene = EntitySelector.CurrentScene;
            var entities = new List<GameEntity>();
            scene.GetEntities(ref entities);
            entities = entities.Where(EntitySelector.IsValidEntity).ToList();

            foreach (var req in set.Requirements)
            {
                if (req.Kind == "note")
                {
                    result.Add(new CheckItem { Label = req.Label, Kind = "note", Found = false, Count = 0 });
                    continue;
                }

                int count = 0;
                foreach (var e in entities)
                {
                    // CORRECTED 2026-08-20: this matched entity.Name ONLY, on the assumption that
                    // live entities can't report their source prefab and that an instance is named
                    // after its prefab anyway. Both halves are wrong. GameEntity.GetPrefabName()
                    // exists (verified by reflecting TaleWorlds.Engine), and plenty of placed
                    // entities carry NO name at all - fief_material_test_4 alone has 50 stored as
                    // <game_entity prefab="..."> with no name attribute. Those are invisible to a
                    // name-only check, so a scene could fail a requirement it actually satisfies.
                    // Checks the real prefab name first, then falls back to the name.
                    bool hit;
                    if (req.Kind == "tag")
                    {
                        hit = e.HasTag(req.Value);
                    }
                    else if (req.Kind == "prefab")
                    {
                        string prefabName = null;
                        try { prefabName = e.GetPrefabName(); } catch { }
                        hit = (!string.IsNullOrEmpty(prefabName) &&
                               prefabName.IndexOf(req.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (!string.IsNullOrEmpty(e.Name) &&
                               e.Name.IndexOf(req.Value, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    else hit = false;
                    if (hit) count++;
                }

                result.Add(new CheckItem { Label = req.Label, Kind = req.Kind, Found = count >= req.MinCount, Count = count });
            }

            return result;
        }
    }
}
