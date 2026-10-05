using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialSwapTool.Core
{
    // Composes a new "CultureA -> CultureB" preset out of two EXISTING built-in presets, without
    // needing any new reference data. Every built-in culture preset is already shaped as
    // "Empire material -> CultureX material" (that's how the BSA templates were authored) - so if
    // both the from-culture and to-culture presets have a rule keyed off the SAME Empire source
    // material, chaining through that shared key gives "CultureA's material -> CultureB's
    // material" directly.
    public static class CulturePresetGenerator
    {
        // Matches typed text against built-in presets by tag or name substring - "khuzait" will
        // match both Khuzait built-ins, which the caller needs to handle (ambiguous, not an
        // error).
        // FIXED 2026-08-19: this used to require p.IsBuiltIn, so a custom or generated preset could
        // never be picked as either side of a bridge - the Culture Generator could scaffold a stub
        // for a custom culture and then refuse to use it. Personal presets are now matchable too;
        // an exact name match wins outright so a personal preset sharing a word with a built-in
        // doesn't register as "ambiguous".
        public static List<PresetSummary> FindCandidates(string cultureText)
        {
            if (string.IsNullOrWhiteSpace(cultureText)) return new List<PresetSummary>();
            var term = cultureText.Trim();
            var all = MaterialSwapPreset.ListPresetSummaries();

            var exact = all.Where(p => string.Equals(p.Name, term, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) return exact;

            return all
                .Where(p => p.Tags.Any(t => t.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            p.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        // Role categories outrank material-type categories when scoring a role-based match: two
        // walls of different materials are a better bridge pair than two unrelated stone things.
        private static readonly HashSet<string> RoleCategories = new HashSet<string>(
            new[] { "wall", "roof", "floor", "column", "doorswindows", "ground" }, StringComparer.OrdinalIgnoreCase);

        // The materials that actually REPRESENT a culture in a preset, which depends on how the
        // preset was authored:
        //   - Built-in convention "EmpireMaterial -> CultureMaterial": the culture's own materials
        //     are the TO side.
        //   - Culture Generator stub "CultureMaterial -> (blank)": they're the FROM side.
        // Handling both is what lets a generated stub take part in a bridge at all.
        private static List<string> ExtractCultureMaterials(PresetSummary preset)
        {
            var mats = new List<string>();
            foreach (var r in preset.Rules)
            {
                if (!string.IsNullOrWhiteSpace(r.ToMaterial) && !WeightedTarget.IsWeighted(r.ToMaterial))
                    mats.Add(r.ToMaterial);
                else if (!string.IsNullOrWhiteSpace(r.FromMaterial))
                    mats.Add(r.FromMaterial);
            }
            return mats.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static int OverlapScore(List<string> a, List<string> b)
        {
            int score = 0;
            foreach (var cat in a)
                if (b.Contains(cat, StringComparer.OrdinalIgnoreCase))
                    score += RoleCategories.Contains(cat) ? 2 : 1;
            return score;
        }

        // ROLE-BASED BRIDGE - the fallback when the two presets share no Empire source material.
        //
        // GenerateBridge only works because every built-in was authored against the same Empire
        // list, making the Empire material an implicit role label ("what replaces empire_wall_a?").
        // Custom cultures and generated stubs have no such shared pivot, so nothing joins. This
        // instead classifies both sides through MaterialCategoryInference and pairs them on
        // category overlap - wall-to-wall, roof-to-roof - which needs no shared vocabulary at all.
        //
        // Ambiguity is expected here (a culture usually has several walls). Rather than picking one
        // arbitrarily, tied candidates become a single WEIGHTED rule - which is also the only safe
        // shape, since MaterialSwapEngine keys ruleMap by FromMaterial and keeps just the first
        // match, so emitting several rows for one material would silently discard all but one.
        // CULTURE -> CULTURE bridging. This is what the generator is actually for: a culture is
        // ONE distinctive material set, and the output is a conversion between two of them.
        //
        // Takes cultures rather than presets on purpose. Bridging presets was the original design
        // and it kept causing trouble: a bridge preset is itself a conversion (two sets mixed), so
        // feeding one back in as a "culture" is a category error, and no tag-based filter can
        // cleanly separate "preset that represents one palette" from "preset that represents a
        // conversion". Cultures have no such ambiguity - culture_definitions.json Common lists ARE
        // the sets - and a custom culture becomes usable the moment it is added there, with no
        // preset to author first.
        public static MaterialSwapPreset GenerateBridgeBetweenCultures(string fromCulture, string toCulture, string outputName)
        {
            var preset = new MaterialSwapPreset { Name = outputName };
            var fromMats = CultureMaterialInference.GetCommonMaterials(fromCulture)
                .Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var toMats = CultureMaterialInference.GetCommonMaterials(toCulture)
                .Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fromMats.Count == 0 || toMats.Count == 0) return preset;

            preset.Rules.AddRange(MatchByRole(fromMats, toMats));
            preset.Tags = new List<string> { "culture-bridge", "role-matched", fromCulture, toCulture };
            return preset;
        }

        // Shared role-overlap matcher - used by both the culture path above and the preset path
        // below, so the two can't drift into scoring matches differently.
        private static List<MaterialSwapRule> MatchByRole(List<string> fromMats, List<string> toMats)
        {
            var rules = new List<MaterialSwapRule>();
            var toClassified = toMats
                .Select(m => new { Material = m, Cats = MaterialCategoryInference.Classify(m) })
                .Where(x => x.Cats.Count > 0)
                .ToList();
            if (toClassified.Count == 0) return rules;

            // Dropped materials are NAMED, not silently omitted (2026-08-23, "why did
            // timber_frame_c receive no override?" - it classified only as 'timberframe', a
            // self-referential category no empire/khuzait material carries, so it fell out of
            // the preset without a word; the category fix that came with this made timber_frame
            // count as a wall). Whatever still can't match gets logged so the next mystery gap
            // diagnoses itself from tool.log.
            var dropped = new List<string>();
            foreach (var from in fromMats)
            {
                var fromCats = MaterialCategoryInference.Classify(from);
                if (fromCats.Count == 0) { dropped.Add($"{from} (matches no category)"); continue; }

                var scored = toClassified
                    .Select(x => new { x.Material, Score = OverlapScore(fromCats, x.Cats) })
                    .Where(x => x.Score > 0 && !string.Equals(x.Material, from, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (scored.Count == 0) { dropped.Add($"{from} (no target carries [{string.Join(", ", fromCats)}])"); continue; }

                int best = scored.Max(x => x.Score);
                var winners = scored.Where(x => x.Score == best).Select(x => x.Material).ToList();
                rules.Add(winners.Count == 1
                    ? new MaterialSwapRule(from, winners[0])
                    : new MaterialSwapRule(from, string.Join(", ", winners.Take(4).Select(w => $"{w}:1"))));
            }
            if (dropped.Count > 0)
                Log.Warn($"[CultureGen] {dropped.Count} material(s) had no role match and were LEFT OUT of the preset: {string.Join("; ", dropped)}");
            return rules;
        }

        // Pulls the LEFT side (FromMaterial) of a preset's rules - "the materials this preset
        // operates on". For a Culture Generator stub (CultureMaterial -> blank) that IS the
        // culture's own set, which is what makes this useful for seeding a culture from a preset.
        // TakeRightSide flips it to ToMaterial, which is what you want from a built-in authored as
        // Empire -> Culture, where the culture's palette is the target side.
        public static List<string> ExtractPresetSide(string presetName, bool takeRightSide)
        {
            var result = new List<string>();
            try
            {
                var preset = MaterialSwapPreset.Load(presetName);
                foreach (var r in preset.Rules)
                {
                    var v = takeRightSide ? r.ToMaterial : r.FromMaterial;
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    if (WeightedTarget.IsWeighted(v)) continue;   // a weighted spec isn't one material
                    result.Add(v.Trim());
                }
            }
            catch (Exception ex) { Log.Warn($"ExtractPresetSide('{presetName}') failed: {ex.Message}"); }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static MaterialSwapPreset GenerateBridgeByRole(PresetSummary fromPreset, PresetSummary toPreset, string outputName)
        {
            var preset = new MaterialSwapPreset { Name = outputName };
            var fromMats = ExtractCultureMaterials(fromPreset);
            var toMats = ExtractCultureMaterials(toPreset);
            if (fromMats.Count == 0 || toMats.Count == 0) return preset;

            var toClassified = toMats
                .Select(m => new { Material = m, Cats = MaterialCategoryInference.Classify(m) })
                .Where(x => x.Cats.Count > 0)
                .ToList();
            if (toClassified.Count == 0) return preset;

            foreach (var from in fromMats)
            {
                var fromCats = MaterialCategoryInference.Classify(from);
                if (fromCats.Count == 0) continue;

                var scored = toClassified
                    .Select(x => new { x.Material, Score = OverlapScore(fromCats, x.Cats) })
                    .Where(x => x.Score > 0 && !string.Equals(x.Material, from, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (scored.Count == 0) continue;

                int best = scored.Max(x => x.Score);
                var winners = scored.Where(x => x.Score == best).Select(x => x.Material).ToList();

                preset.Rules.Add(winners.Count == 1
                    ? new MaterialSwapRule(from, winners[0])
                    : new MaterialSwapRule(from, string.Join(", ", winners.Take(4).Select(w => $"{w}:1"))));
            }

            preset.Tags = new[] { "culture-bridge", "role-matched" }
                .Concat(fromPreset.Tags.Where(t => !string.Equals(t, "culture-swap", StringComparison.OrdinalIgnoreCase)))
                .Concat(toPreset.Tags.Where(t => !string.Equals(t, "culture-swap", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return preset;
        }

        public static MaterialSwapPreset GenerateBridge(PresetSummary fromCulturePreset, PresetSummary toCulturePreset, string outputName)
        {
            var preset = new MaterialSwapPreset { Name = outputName };

            var toByEmpireSource = toCulturePreset.Rules
                .Where(r => !string.IsNullOrWhiteSpace(r.FromMaterial) && !string.IsNullOrWhiteSpace(r.ToMaterial))
                .GroupBy(r => r.FromMaterial, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().ToMaterial, StringComparer.OrdinalIgnoreCase);

            var seen = new HashSet<(string, string)>();
            foreach (var fromRule in fromCulturePreset.Rules)
            {
                if (string.IsNullOrWhiteSpace(fromRule.FromMaterial) || string.IsNullOrWhiteSpace(fromRule.ToMaterial))
                    continue;
                // fromRule: EmpireMaterial -> FromCulture's material. Only usable if the
                // to-culture preset has a rule for that SAME Empire source.
                if (!toByEmpireSource.TryGetValue(fromRule.FromMaterial, out var toCultureMaterial))
                    continue;

                var bridgeFrom = fromRule.ToMaterial;
                var bridgeTo = toCultureMaterial;
                if (string.Equals(bridgeFrom, bridgeTo, StringComparison.OrdinalIgnoreCase))
                    continue; // both cultures already agree on this one - not a swap
                if (seen.Add((bridgeFrom, bridgeTo)))
                    preset.Rules.Add(new MaterialSwapRule(bridgeFrom, bridgeTo));
            }

            preset.Tags = new[] { "culture-bridge" }
                .Concat(fromCulturePreset.Tags.Where(t => !string.Equals(t, "culture-swap", StringComparison.OrdinalIgnoreCase)))
                .Concat(toCulturePreset.Tags.Where(t => !string.Equals(t, "culture-swap", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return preset;
        }
    }
}
