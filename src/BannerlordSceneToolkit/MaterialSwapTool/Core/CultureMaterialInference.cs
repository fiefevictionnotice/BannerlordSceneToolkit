using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    public class CultureDefinition
    {
        // Substring PATTERNS, matched only when a material carries this culture's own signature
        // strongly enough that seeing it means "this preset is probably that culture" - kept
        // deliberately strict (unlike Common below) so a shared/generic material doesn't
        // mislabel an unrelated preset. Used by InferCultureTags/InferPrimaryCulture.
        public List<string> Unique { get; set; } = new List<string>();

        // EXACT material names (not patterns) empirically seen on that culture's architecture
        // prefabs - including ones also used by other cultures. Not for inference (that's what
        // Unique is for) - for generating a starter preset for a culture: "these are the
        // materials real Vlandia buildings actually use, go fill in what they become."
        public List<string> Common { get; set; } = new List<string>();
    }

    // Culture definitions were a hardcoded Dictionary of hand-guessed patterns before this -
    // "vlandia" had exactly 3 patterns (vlandia_, vlandian_, timber_frame) because Vlandia's own
    // architecture materials mostly have NO culture branding in their names at all (stone_wall_11,
    // planks_5, roof_normal - the same generic names other cultures' buildings use). Confirmed
    // empirically by cross-referencing mesh_slot_map.csv's Source column (which DOES carry
    // per-prefab culture labels like "european_castle_a", "vlandia_village_houses",
    // "sturgia_castle") against its DefaultMat column, rather than guessing from material names
    // alone. That produced two very different kinds of list per culture, hence the Unique/Common
    // split above - see CultureDefinition.
    //
    // Same editable-at-runtime convention as MaterialCategoryInference: ReferenceData\
    // culture_definitions.json (source-controlled, deployed with the module) is the seeded
    // default; Documents\...\MaterialSwapTool\culture_definitions.json, once saved from the
    // Culture Editor, always wins and is never overwritten except by an explicit Save. New
    // cultures can be added there too, not just edits to the existing 6.
    public static class CultureMaterialInference
    {
        private static Dictionary<string, CultureDefinition> _cultures;

        private static string ModuleDir => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ShippedDefaultPath => Path.Combine(ModuleDir, "ReferenceData", "culture_definitions.json");

        private static string UserOverridePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "culture_definitions.json");

        private static Dictionary<string, CultureDefinition> Cultures
        {
            get
            {
                if (_cultures == null) Load();
                return _cultures;
            }
        }

        private static void Load()
        {
            var path = File.Exists(UserOverridePath) ? UserOverridePath : ShippedDefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    _cultures = JsonConvert.DeserializeObject<Dictionary<string, CultureDefinition>>(json)
                                ?? new Dictionary<string, CultureDefinition>(StringComparer.OrdinalIgnoreCase);
                    return;
                }
                Log.Warn($"CultureMaterialInference: no culture file found at '{path}' - no cultures loaded.");
            }
            catch (Exception ex)
            {
                Log.Error("CultureMaterialInference: failed to load culture definitions: " + ex);
            }
            _cultures = new Dictionary<string, CultureDefinition>(StringComparer.OrdinalIgnoreCase);
        }

        public static void Reload() => Load();

        public static string[] AllCultures => Cultures.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();

        // The culture names in the SHIPPED ReferenceData default, regardless of any user
        // override - "built-in" in the UI sense: the six factory cultures plus anything a
        // future module update ships. Read once and cached; the shipped file never changes at
        // runtime. A user-DELETED built-in still counts as built-in here, which only matters
        // for row ordering/coloring in the generator - AllCultures stays the live authority on
        // what exists.
        private static HashSet<string> _builtInNames;

        public static bool IsBuiltInCulture(string culture)
        {
            if (_builtInNames == null)
            {
                _builtInNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(ShippedDefaultPath))
                    {
                        var shipped = JsonConvert.DeserializeObject<Dictionary<string, CultureDefinition>>(
                            File.ReadAllText(ShippedDefaultPath));
                        if (shipped != null)
                            foreach (var key in shipped.Keys) _builtInNames.Add(key);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("CultureMaterialInference: could not read shipped defaults for built-in check: " + ex.Message);
                }
            }
            return _builtInNames.Contains(culture ?? "");
        }

        // AllCultures with the built-ins pinned to the top (each half alphabetical) - the order
        // the generator's lists want, so a growing set of custom cultures never buries the six
        // standard ones in the middle of an alphabetical mix.
        public static string[] AllCulturesBuiltInFirst =>
            AllCultures.OrderBy(c => IsBuiltInCulture(c) ? 0 : 1)
                       .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                       .ToArray();

        public static Dictionary<string, CultureDefinition> GetAllForEditing() =>
            Cultures.ToDictionary(kvp => kvp.Key, kvp => new CultureDefinition
            {
                Unique = new List<string>(kvp.Value.Unique),
                Common = new List<string>(kvp.Value.Common),
            }, StringComparer.OrdinalIgnoreCase);

        public static void Save(Dictionary<string, CultureDefinition> cultures)
        {
            _cultures = cultures;
            try
            {
                var dir = Path.GetDirectoryName(UserOverridePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(UserOverridePath, JsonConvert.SerializeObject(cultures, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error("CultureMaterialInference: failed to save culture definitions: " + ex);
            }
        }

        // EXPORT / IMPORT.
        //
        // Shaped differently from presets, palettes, swap sets and pile recipes, because cultures
        // are not one-file-per-thing: they all live in a single culture_definitions.json keyed by
        // name. So export writes ONE file per culture anyway - otherwise sharing "my Vlandian set"
        // would mean shipping someone your whole file and clobbering their other five - and import
        // MERGES by name rather than replacing the file.
        public static string ExportDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Exports", "Cultures");

        public static void OpenExportFolder()
        {
            Directory.CreateDirectory(ExportDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExportDir) { UseShellExecute = true });
        }

        // The on-disk export format: the culture's name travels WITH it, since the dictionary key
        // that normally carries the name does not survive being split into per-culture files.
        private class ExportedCulture
        {
            public string Name { get; set; }
            public CultureDefinition Definition { get; set; }
        }

        public static (int exported, string dir) Export(string name = null)
        {
            Directory.CreateDirectory(ExportDir);
            var all = GetAllForEditing();
            var wanted = string.IsNullOrWhiteSpace(name)
                ? all.Keys.ToList()
                : all.Keys.Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToList();

            int exported = 0;
            foreach (var key in wanted)
            {
                try
                {
                    var payload = new ExportedCulture { Name = key, Definition = all[key] };
                    File.WriteAllText(Path.Combine(ExportDir, SafeFileName(key) + ".json"),
                                      JsonConvert.SerializeObject(payload, Formatting.Indented));
                    exported++;
                }
                catch (Exception ex) { Log.Warn($"Culture export failed for '{key}': {ex.Message}"); }
            }
            Log.Info($"[CultureIO] exported={exported} to {ExportDir}");
            return (exported, ExportDir);
        }

        // Merges into the existing set. A name clash keeps YOURS unless overwriteExisting - the
        // same rule as every other importer here, and it matters more for cultures because a
        // careless overwrite would silently redefine what every preset tagged with that culture
        // resolves to.
        public static (int imported, int skipped, List<string> problems) Import(bool overwriteExisting)
        {
            var problems = new List<string>();
            int imported = 0, skipped = 0;

            if (!Directory.Exists(ExportDir))
                return (0, 0, new List<string> { "No Exports\\Cultures folder yet - export something first, or drop .json files in there." });

            var current = GetAllForEditing();

            foreach (var file in Directory.GetFiles(ExportDir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var text = File.ReadAllText(file);
                    var payload = JsonConvert.DeserializeObject<ExportedCulture>(text);

                    // Accept a bare CultureDefinition too, named from the filename: someone
                    // hand-editing one of these is more likely to write the definition alone.
                    if (payload == null || payload.Definition == null)
                    {
                        var bare = JsonConvert.DeserializeObject<CultureDefinition>(text);
                        if (bare != null && (bare.Unique?.Count > 0 || bare.Common?.Count > 0))
                            payload = new ExportedCulture { Name = Path.GetFileNameWithoutExtension(file), Definition = bare };
                    }

                    if (payload?.Definition == null || string.IsNullOrWhiteSpace(payload.Name))
                    { problems.Add($"{fileName}: not a culture (no name or definition)"); skipped++; continue; }

                    int materials = (payload.Definition.Unique?.Count ?? 0) + (payload.Definition.Common?.Count ?? 0);
                    if (materials == 0)
                    { problems.Add($"{fileName}: culture '{payload.Name}' lists no materials"); skipped++; continue; }

                    if (current.ContainsKey(payload.Name) && !overwriteExisting)
                    { problems.Add($"{fileName}: '{payload.Name}' already exists (kept yours)"); skipped++; continue; }

                    current[payload.Name] = payload.Definition;
                    imported++;
                }
                catch (Exception ex) { problems.Add($"{fileName}: {ex.Message}"); skipped++; }
            }

            // One write at the end rather than per file - a half-applied merge on an exception
            // partway through would be worse than importing nothing.
            if (imported > 0) Save(current);

            Log.Info($"[CultureIO] imported={imported} skipped={skipped}");
            return (imported, skipped, problems);
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        public static List<string> GetCommonMaterials(string culture) =>
            Cultures.TryGetValue(culture, out var def) ? def.Common : new List<string>();

        // Checks every rule's FROM material against every culture's UNIQUE patterns and returns
        // the cultures with at least one hit, ordered by hit count (most-matched first).
        public static List<string> InferCultureTags(IEnumerable<string> fromMaterials)
        {
            var materials = fromMaterials.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (materials.Count == 0) return new List<string>();

            var hitCounts = new Dictionary<string, int>();
            foreach (var culture in Cultures.Keys) hitCounts[culture] = 0;

            foreach (var material in materials)
            {
                var lower = material.ToLowerInvariant();
                foreach (var kvp in Cultures)
                {
                    if (kvp.Value.Unique.Any(pattern => lower.Contains(pattern.ToLowerInvariant())))
                        hitCounts[kvp.Key]++;
                }
            }

            return hitCounts
                .Where(kvp => kvp.Value > 0)
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        // Single best-guess culture for one side of a rule set (FROM or TO materials) - null if
        // nothing matched. Used to build a "SourceCulture -> TargetCulture" display label, e.g.
        // running this over FromMaterial and ToMaterial separately for the same preset.
        public static string InferPrimaryCulture(IEnumerable<string> materials) =>
            InferCultureTags(materials).FirstOrDefault();
    }
}
