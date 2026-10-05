using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PrefabSwapperTool.Core
{
    // One old-prefab -> new-prefab -> texture-override rule within a set. No offset/auto-place
    // fields like PrefabCreatorTool's ComboMember - a Swap Set is scoped to "change what these
    // things ARE (and how they look)," not "place something new alongside them," which is what
    // Pairing/Auto-Place already covers. Every entity a set touches still keeps whatever
    // auto-placed secondaries its normal single-swap path would apply, for free, since
    // Apply-to-Selection runs each match through the exact same LivePrefabSwapper.
    public class SwapSetPair
    {
        public string OldPrefabName { get; set; } = "";
        public string NewPrefabName { get; set; } = "";
        // Optional saved ColorPreset name (see Core/ColorPreset.cs) applied to the NEW entity right
        // after the swap - shares the same data files as PrefabCreatorTool's own Texture Sets, so a
        // texture authored in either mod's browser is usable here.
        public string PresetName { get; set; }
    }

    // A named group of prefab-identity swaps applied together - "change this whole modular house
    // at once" instead of one prefab swap at a time. Apply-to-Selection matches each SELECTED
    // entity's current prefab name against OldPrefabName (case-insensitive); entities that don't
    // match anything in the set are left alone, so a partial selection (missing a piece, an extra
    // duplicate window) doesn't break the rest.
    public class SwapSet
    {
        public string Name { get; set; } = "Untitled Set";
        public List<SwapSetPair> Pairs { get; set; } = new List<SwapSetPair>();
    }

    public static class SwapSetStore
    {
        private static string SetsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabSwapperTool", "SwapSets");

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        public static List<string> ListNames()
        {
            if (!Directory.Exists(SetsDir)) return new List<string>();
            return Directory.EnumerateFiles(SetsDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Save(SwapSet set)
        {
            if (set == null || string.IsNullOrWhiteSpace(set.Name)) return;
            Directory.CreateDirectory(SetsDir);
            var path = Path.Combine(SetsDir, SafeFileName(set.Name) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(set, Formatting.Indented));
            Log.Info($"Saved swap set '{set.Name}' ({set.Pairs.Count} pair(s)) to {path}");
        }

        public static SwapSet Load(string name)
        {
            var path = Path.Combine(SetsDir, SafeFileName(name) + ".json");
            if (!File.Exists(path)) throw new FileNotFoundException($"No swap set named '{name}' found in {SetsDir}");
            return JsonConvert.DeserializeObject<SwapSet>(File.ReadAllText(path))
                   ?? throw new InvalidDataException($"Set '{name}' failed to parse.");
        }

        // EXPORT / IMPORT. Same convention as the Material Swap tool's presets and the Prefab
        // Creator's pile recipes: a folder you can zip and send, its own subfolder so a scan for
        // one kind of file cannot swallow another, and an import that keeps YOUR copy on a name
        // clash rather than silently overwriting work.
        public static string ExportDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabSwapperTool", "Exports", "SwapSets");

        public static void OpenExportFolder()
        {
            Directory.CreateDirectory(ExportDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExportDir) { UseShellExecute = true });
        }

        public static (int exported, string dir) Export(string name = null)
        {
            Directory.CreateDirectory(ExportDir);
            var wanted = string.IsNullOrWhiteSpace(name) ? ListNames() : new List<string> { name };
            int exported = 0;
            foreach (var n in wanted)
            {
                try
                {
                    var set = Load(n);
                    File.WriteAllText(Path.Combine(ExportDir, SafeFileName(set.Name) + ".json"),
                                      JsonConvert.SerializeObject(set, Formatting.Indented));
                    exported++;
                }
                catch (Exception ex) { Log.Warn($"Swap set export failed for '{n}': {ex.Message}"); }
            }
            Log.Info($"[SwapSetIO] exported={exported} to {ExportDir}");
            return (exported, ExportDir);
        }

        // Validated, not trusted: a set with no pairs would import cleanly and then swap nothing,
        // which is harder to diagnose than a refusal.
        public static (int imported, int skipped, List<string> problems) Import(bool overwriteExisting)
        {
            var problems = new List<string>();
            int imported = 0, skipped = 0;

            if (!Directory.Exists(ExportDir))
                return (0, 0, new List<string> { "No Exports\\SwapSets folder yet - export something first, or drop .json files in there." });

            Directory.CreateDirectory(SetsDir);
            foreach (var file in Directory.GetFiles(ExportDir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var set = JsonConvert.DeserializeObject<SwapSet>(File.ReadAllText(file));
                    if (set == null || string.IsNullOrWhiteSpace(set.Name))
                    { problems.Add($"{fileName}: not a swap set (no Name)"); skipped++; continue; }

                    var usable = set.Pairs?.Count(p => !string.IsNullOrWhiteSpace(p.OldPrefabName)
                                                    && !string.IsNullOrWhiteSpace(p.NewPrefabName)) ?? 0;
                    if (usable == 0)
                    { problems.Add($"{fileName}: set '{set.Name}' has no usable old->new pairs"); skipped++; continue; }

                    var target = Path.Combine(SetsDir, SafeFileName(set.Name) + ".json");
                    if (File.Exists(target) && !overwriteExisting)
                    { problems.Add($"{fileName}: '{set.Name}' already exists (kept yours)"); skipped++; continue; }

                    Save(set);
                    imported++;
                }
                catch (Exception ex) { problems.Add($"{fileName}: {ex.Message}"); skipped++; }
            }

            Log.Info($"[SwapSetIO] imported={imported} skipped={skipped}");
            return (imported, skipped, problems);
        }

        public static void Delete(string name)
        {
            var path = Path.Combine(SetsDir, SafeFileName(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
