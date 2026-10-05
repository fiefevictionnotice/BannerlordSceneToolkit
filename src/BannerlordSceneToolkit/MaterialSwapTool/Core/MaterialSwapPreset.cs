using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace MaterialSwapTool.Core
{
    public class MaterialSwapPreset
    {
        public string Name { get; set; } = "Untitled Preset";
        public List<MaterialSwapRule> Rules { get; set; } = new List<MaterialSwapRule>();
        public List<string> Tags { get; set; } = new List<string>();

        private static string ModuleDir => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", ".."));

        // Ships with the module (git-tracked under src\...\Presets\BuiltIn, deployed alongside
        // the GUI prefabs and reference data). Read-only from the tool's perspective - Delete()
        // only ever touches PersonalPresetsDir, so a same-named built-in is never at risk.
        private static string BuiltInPresetsDir => Path.Combine(ModuleDir, "Presets", "BuiltIn");

        private static string PersonalPresetsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Presets");

        public static List<PresetSummary> ListPresetSummaries()
        {
            var result = new List<PresetSummary>();
            AddSummaries(result, BuiltInPresetsDir, isBuiltIn: true);
            AddSummaries(result, PersonalPresetsDir, isBuiltIn: false);
            return result;
        }

        private static void AddSummaries(List<PresetSummary> result, string dir, bool isBuiltIn)
        {
            if (!Directory.Exists(dir))
            {
                if (!isBuiltIn) Directory.CreateDirectory(dir);
                return;
            }

            foreach (var name in EnumeratePresetNames(dir))
            {
                try
                {
                    var preset = LoadFrom(dir, name);
                    if (preset == null) continue;
                    result.Add(new PresetSummary
                    {
                        Name = name,
                        ModifiedUtc = GetModifiedUtc(dir, name),
                        IsDeletable = !isBuiltIn && File.Exists(Path.Combine(dir, SafeFileName(name) + ".json")),
                        IsBuiltIn = isBuiltIn,
                        Rules = preset.Rules,
                        Tags = preset.Tags ?? new List<string>(),
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn($"Skipping preset '{name}' in {dir}: {ex.Message}");
                }
            }
        }

        private static IEnumerable<string> EnumeratePresetNames(string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                yield return Path.GetFileNameWithoutExtension(file);
            // BSA retexture templates share the same old|new shape - surface them as presets too.
            foreach (var file in Directory.EnumerateFiles(dir, "*.txt"))
                yield return Path.GetFileNameWithoutExtension(file);
        }

        private static DateTime GetModifiedUtc(string dir, string name)
        {
            var jsonPath = Path.Combine(dir, SafeFileName(name) + ".json");
            if (File.Exists(jsonPath)) return File.GetLastWriteTimeUtc(jsonPath);
            var txtPath = Path.Combine(dir, SafeFileName(name) + ".txt");
            if (File.Exists(txtPath)) return File.GetLastWriteTimeUtc(txtPath);
            return DateTime.MinValue;
        }

        // Only ever deletes from the personal folder - a built-in never has a matching file
        // there, so this naturally refuses to delete a built-in without needing a separate check.
        // IMPORT / EXPORT.
        //
        // Everything moves through one fixed folder rather than a file picker: GauntletUI has no
        // file-dialog widget, and a typed-path text box in a game overlay is worse than a known
        // location you can open in Explorer. OpenExportFolder() does exactly that.
        //
        // Presets are already plain JSON on disk, so export is a copy and import is a validated
        // copy back - no separate serialisation format to keep in step with Save/Load.
        public static string ExportDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "MaterialSwapTool", "Exports");

        public static void OpenExportFolder()
        {
            Directory.CreateDirectory(ExportDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExportDir) { UseShellExecute = true });
        }

        // Exports one preset by name, or every personal preset when name is null/empty.
        public static (int exported, string dir) Export(string name = null)
        {
            Directory.CreateDirectory(ExportDir);
            var summaries = ListPresetSummaries();
            var wanted = string.IsNullOrWhiteSpace(name)
                ? summaries.Where(s => !s.IsBuiltIn).ToList()
                : summaries.Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

            int exported = 0;
            foreach (var s in wanted)
            {
                try
                {
                    var preset = Load(s.Name);
                    var path = Path.Combine(ExportDir, SafeFileName(preset.Name) + ".json");
                    File.WriteAllText(path, JsonConvert.SerializeObject(preset, Formatting.Indented));
                    exported++;
                }
                catch (Exception ex) { Log.Warn($"Export failed for '{s.Name}': {ex.Message}"); }
            }
            return (exported, ExportDir);
        }

        // Imports every .json in the export folder into Personal presets. Each file is DESERIALISED
        // AND CHECKED before it is accepted - a file that parses as JSON but isn't a preset (no
        // rules) would otherwise land as an empty preset that silently does nothing on Apply.
        public static (int imported, int skipped, List<string> problems) Import(bool overwriteExisting)
        {
            var problems = new List<string>();
            int imported = 0, skipped = 0;
            if (!Directory.Exists(ExportDir)) return (0, 0, new List<string> { "No Exports folder yet - export something first, or drop .json files in there." });

            Directory.CreateDirectory(PersonalPresetsDir);
            foreach (var file in Directory.GetFiles(ExportDir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var preset = JsonConvert.DeserializeObject<MaterialSwapPreset>(File.ReadAllText(file));
                    if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                    {
                        problems.Add($"{fileName}: not a preset (no Name)"); skipped++; continue;
                    }
                    if (preset.Rules == null || preset.Rules.Count == 0)
                    {
                        problems.Add($"{fileName}: preset '{preset.Name}' has no rules"); skipped++; continue;
                    }

                    var target = Path.Combine(PersonalPresetsDir, SafeFileName(preset.Name) + ".json");
                    if (File.Exists(target) && !overwriteExisting)
                    {
                        problems.Add($"{fileName}: '{preset.Name}' already exists (kept yours)"); skipped++; continue;
                    }
                    preset.Save(archiveOldVersion: true);
                    imported++;
                }
                catch (Exception ex)
                {
                    problems.Add($"{fileName}: {ex.Message}"); skipped++;
                }
            }
            Log.Info($"[PresetIO] imported={imported} skipped={skipped}");
            return (imported, skipped, problems);
        }

        public static void Delete(string name)
        {
            var jsonPath = Path.Combine(PersonalPresetsDir, SafeFileName(name) + ".json");
            if (!File.Exists(jsonPath))
                throw new InvalidOperationException(
                    $"'{name}' isn't a deletable personal preset (it's either a built-in or a BSA .txt template) - " +
                    "delete it from disk directly if you really want it gone.");
            File.Delete(jsonPath);
            Log.Info($"Deleted preset '{name}'");
        }

        // archiveOldVersion=false is for History's own Restore action specifically - restoring an
        // old version shouldn't itself mint a brand new history entry every time you use it (that
        // was the original design, on the theory that a restore might be a mistake worth being
        // able to undo, but in practice it just means clicking through old versions floods the
        // history list with near-duplicates). A normal edit-and-save still archives as before.
        public void Save(bool archiveOldVersion = true)
        {
            Directory.CreateDirectory(PersonalPresetsDir);
            var path = Path.Combine(PersonalPresetsDir, SafeFileName(Name) + ".json");
            if (archiveOldVersion)
                PresetHistoryManager.ArchiveIfExists(Name, path); // snapshot whatever's there BEFORE overwriting it
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            Log.Info($"Saved preset '{Name}' ({Rules.Count} rule(s)) to {path}");
        }

        // Personal takes precedence over a same-named built-in, so a user can locally override
        // one without needing to delete/replace the shipped file.
        public static MaterialSwapPreset Load(string name)
        {
            var fromPersonal = LoadFrom(PersonalPresetsDir, name);
            if (fromPersonal != null) return fromPersonal;

            var fromBuiltIn = LoadFrom(BuiltInPresetsDir, name);
            if (fromBuiltIn != null) return fromBuiltIn;

            throw new FileNotFoundException($"No preset named '{name}' found in {PersonalPresetsDir} or {BuiltInPresetsDir}");
        }

        private static MaterialSwapPreset LoadFrom(string dir, string name)
        {
            var jsonPath = Path.Combine(dir, SafeFileName(name) + ".json");
            if (File.Exists(jsonPath))
            {
                var preset = JsonConvert.DeserializeObject<MaterialSwapPreset>(File.ReadAllText(jsonPath));
                if (preset != null) return preset;
            }

            var txtPath = Path.Combine(dir, SafeFileName(name) + ".txt");
            if (File.Exists(txtPath))
                return LoadBsaTemplate(name, txtPath);

            return null;
        }

        // BSA's -RtxtTemplate format: "old_material -> new_material" (optionally followed by
        // "| type:xxx") or the older plain "old_material|new_material" with no arrow. '#'
        // comments and blank lines are ignored, except a "# Tags:" header line, which seeds this
        // preset's Tags. Letting our presets read that format directly means a template built in
        // either tool works in both, with nothing to keep in sync.
        private static MaterialSwapPreset LoadBsaTemplate(string name, string path)
        {
            var preset = new MaterialSwapPreset { Name = name };
            var typeTags = new List<string>();
            const string tagsHeaderPrefix = "# Tags:";

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("#"))
                {
                    if (line.StartsWith(tagsHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var tags = line.Substring(tagsHeaderPrefix.Length).Split(',')
                            .Select(t => t.Trim()).Where(t => t.Length > 0);
                        preset.Tags.AddRange(tags);
                    }
                    continue;
                }

                if (line.StartsWith("~"))
                {
                    // Category fallback ("~stone -> target_a:4, target_b:1, ...") - wildcard
                    // source-prefix matching with weighted random target selection. This tool's
                    // rule model is exact FromMaterial -> ToMaterial only, no wildcards or
                    // weighting, so these aren't representable as a rule. Recognized and skipped
                    // deliberately rather than mis-parsed into a garbage literal rule.
                    Log.Info($"Skipping category-fallback line (unsupported by this tool's exact-match rules) in {path}: '{rawLine.Trim()}'");
                    continue;
                }

                if (line.Contains("->"))
                {
                    // A trailing "| type:xxx" annotation must be stripped BEFORE splitting on
                    // "->", or it ends up glued onto ToMaterial (e.g. "stone_wall_2a | type:stone"
                    // instead of "stone_wall_2a") - that was silently corrupting every import
                    // until this was caught.
                    var rulePart = line;
                    var pipeIndex = line.IndexOf('|');
                    if (pipeIndex >= 0)
                    {
                        var afterPipe = line.Substring(pipeIndex + 1).Trim();
                        if (afterPipe.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
                        {
                            var typeTag = afterPipe.Substring("type:".Length).Trim();
                            if (typeTag.Length > 0 && !typeTags.Contains(typeTag, StringComparer.OrdinalIgnoreCase))
                                typeTags.Add(typeTag);
                        }
                        rulePart = line.Substring(0, pipeIndex).Trim();
                    }

                    var arrowParts = rulePart.Split(new[] { "->" }, StringSplitOptions.None);
                    if (arrowParts.Length == 2)
                    {
                        preset.Rules.Add(new MaterialSwapRule(arrowParts[0].Trim(), arrowParts[1].Trim()));
                        continue;
                    }
                }
                else if (line.Contains("|"))
                {
                    var pipeParts = line.Split(new[] { "|" }, StringSplitOptions.None);
                    if (pipeParts.Length == 2)
                    {
                        preset.Rules.Add(new MaterialSwapRule(pipeParts[0].Trim(), pipeParts[1].Trim()));
                        continue;
                    }
                }

                Log.Warn($"Skipping unparseable template line in {path}: '{rawLine}'");
            }

            preset.Tags.AddRange(typeTags.Where(t => !preset.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
            return preset;
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
