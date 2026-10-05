using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.Core
{
    // Derives ColorPresets straight from a convention the scene already uses by hand: entities
    // tagged "<family>_<colorword>" (e.g. "fief_wine_shelf_redbrown" on the little book/greeble
    // pieces that make up one colored wine shelf assembly). Confirmed against a real exported
    // scene (fief_prefab_creation_9_2_greebles_only/scene.xscene) - three such tags exist there
    // (fief_wine_shelf_brown/lightbrown/redbrown), each shared by several loose sibling entities
    // (no parent/anchor - a flat cluster identified purely by tag + proximity, same as
    // VariantDetector already assumes elsewhere in this tool).
    //
    // One ColorPreset gets saved per (family, colorword) pair, BasePrefabName set to the family
    // tag itself (a loose identifier, not necessarily a literal saved prefab name - matches how
    // PrefabCombo's family concept also keys off this same string). Overrides are keyed by mesh
    // NAME (not entity name) so one preset can carry colors for every distinct mesh used across
    // the whole tagged group, and picks the single most-common Factor1 per mesh name if members
    // disagree (hand-placed greebles are rarely perfectly consistent) rather than failing outright.
    public static class ColorFamilyImporter
    {
        // Deliberately conservative and editable via the UI text box (comma-separated) rather than
        // hardcoded exhaustive - these are just sensible defaults covering the naming style already
        // seen in-scene (redbrown, lightbrown, brown) plus common furniture finish words.
        public static readonly string[] DefaultColorWords =
        {
            "lightbrown", "darkbrown", "redbrown", "brown",
            "black", "white", "grey", "gray", "tan", "beige",
            "red", "green", "blue", "gold", "silver",
        };

        public class ImportResult
        {
            public int PresetsSaved;
            public List<string> SavedNames = new List<string>();
            public List<string> Skipped = new List<string>();
        }

        public static ImportResult Import(List<GameEntity> entities, IEnumerable<string> colorWords)
        {
            var result = new ImportResult();
            // Longest word first so "lightbrown"/"darkbrown" match before the bare "brown" suffix
            // they'd otherwise also satisfy.
            var words = (colorWords ?? DefaultColorWords)
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim().ToLowerInvariant())
                .Distinct()
                .OrderByDescending(w => w.Length)
                .ToList();
            if (words.Count == 0) { result.Skipped.Add("No color words configured."); return result; }

            // (family, colorWord) -> meshName -> (color hex -> count)
            var groups = new Dictionary<(string family, string color), Dictionary<string, Dictionary<string, int>>>();

            foreach (var entity in entities)
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;

                string family = null, colorWord = null;
                foreach (var tag in entity.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    var lower = tag.ToLowerInvariant();
                    var match = words.FirstOrDefault(w => lower.EndsWith("_" + w) || lower == w);
                    if (match == null) continue;
                    family = lower.EndsWith("_" + match) ? tag.Substring(0, tag.Length - match.Length - 1) : tag;
                    colorWord = match;
                    break;
                }
                if (family == null) continue;

                var key = (family, colorWord);
                if (!groups.TryGetValue(key, out var meshColors))
                {
                    meshColors = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                    groups[key] = meshColors;
                }

                for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                {
                    var meta = entity.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var mesh = meta.GetMeshAtIndex(i);
                        if (mesh == null) continue;
                        var meshName = mesh.Name;
                        var colorHex = ColorHex.ToHex(mesh.Color);
                        if (string.IsNullOrEmpty(meshName) || colorHex == null) continue;

                        if (!meshColors.TryGetValue(meshName, out var tally))
                        {
                            tally = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            meshColors[meshName] = tally;
                        }
                        tally[colorHex] = tally.TryGetValue(colorHex, out var c) ? c + 1 : 1;
                    }
                }
            }

            if (groups.Count == 0)
            {
                result.Skipped.Add("No entities tagged '<family>_<colorword>' were found for the configured color words.");
                return result;
            }

            foreach (var groupEntry in groups)
            {
                var family = groupEntry.Key.family;
                var colorWord = groupEntry.Key.color;
                var meshColors = groupEntry.Value;
                if (meshColors.Count == 0) { result.Skipped.Add($"{family} - {colorWord}: no readable mesh colors."); continue; }

                var preset = new ColorPreset
                {
                    Name = $"{family} - {ToTitleCase(colorWord)}",
                    BasePrefabName = family,
                };
                foreach (var meshEntry in meshColors)
                {
                    var dominant = meshEntry.Value.OrderByDescending(kv => kv.Value).First().Key;
                    preset.Overrides[meshEntry.Key] = new ColorPresetOverride { Color = dominant };
                }

                ColorPresetStore.Save(preset);
                result.PresetsSaved++;
                result.SavedNames.Add(preset.Name);
            }

            return result;
        }

        private static string ToTitleCase(string word) =>
            word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);
    }
}
