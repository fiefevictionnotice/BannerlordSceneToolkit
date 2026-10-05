using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace PrefabCreatorTool.Core
{
    // One "paired prefab" entry a base prefab can be combined with - e.g. base "bd_bookshelf_a"
    // paired with "pile_of_books_a", optionally tagged with a specific ColorPreset name to apply
    // on combine. Position/rotation are stored RELATIVE to the base's own frame (captured by
    // reading both entities' actual placement at Add Pairing time, via PrefabCreatorEngine's
    // offset-capture math) rather than assuming they share one exact origin point - most
    // real pairs (a bookshelf + a pile of books sitting ON it, not centered inside it) were never
    // going to land correctly with a shared-origin assumption anyway. HasOffset guards entries
    // saved before this existed, which really were meant as shared-origin (zero offset).
    public class ComboMember
    {
        public string PrefabName { get; set; } = "";
        public string PresetName { get; set; } // optional - name of a saved ColorPreset, or null

        // Opt-in, off by default: when true, MaterialSwapTool's own placement tools (Swap Selected/
        // All Matching, Distribute) auto-place this secondary alongside any NEW instance of the
        // base prefab (or any member of its family) they place - cross-mod by reading this same
        // Combos JSON directly, not by referencing PrefabCreatorTool's assembly (the two mods stay
        // independent per the earlier decision to keep them separable for crash A/B diagnosis).
        // Distinct from the manual Combine flow (explicit select + stage + Combine), which still
        // works regardless of this flag and doesn't require it.
        public bool AutoPlaceOnNewInstance { get; set; }

        public bool HasOffset { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float OffsetZ { get; set; }

        // Relative rotation, stored as the paired entity's rotation basis vectors expressed in the
        // base's own local frame (exact, no gimbal-lock risk from an Euler-angle representation).
        // Defaults to identity so a legacy (HasOffset=false) entry still deserializes to a sane,
        // unrotated relative frame even though it's never actually read in that case.
        public float RotSX { get; set; } = 1f;
        public float RotSY { get; set; }
        public float RotSZ { get; set; }
        public float RotFX { get; set; }
        public float RotFY { get; set; } = 1f;
        public float RotFZ { get; set; }
        public float RotUX { get; set; }
        public float RotUY { get; set; }
        public float RotUZ { get; set; } = 1f;

        // A denormalized copy of PresetName's own Overrides dict, refreshed every time PresetName
        // is set via PrefabComboStore.AddMember. Exists so FamilyAutoPlacer (MaterialSwapTool/
        // PrefabSwapperTool, a different mod that only ever reads this JSON file, never
        // ColorPresets\*.json) can actually apply the texture when it auto-places a secondary,
        // instead of just placing it at the right offset in its default/native texture (which is
        // all it did before this field existed). Goes stale only if the named preset is edited or
        // deleted without this pairing being re-saved/re-cycled afterward.
        public Dictionary<string, ColorPresetOverride> ResolvedOverrides { get; set; }
    }

    // All the prefabs known to pair with one base prefab - what "Display Paired Prefabs" looks up
    // for whatever's currently selected, so Combine at Shared Origin never requires typing a
    // prefab name at use time. New pairings are still typed once, at definition time - there's no
    // way to discover an asset's name from nothing, so authoring a brand new pairing costs one
    // typed entry, but every use after that is select + click.
    public class PrefabCombo
    {
        public string BasePrefabName { get; set; } = "";
        public List<ComboMember> Members { get; set; } = new List<ComboMember>();
    }

    public static class PrefabComboStore
    {
        private static string CombosDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "Combos");

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        public static PrefabCombo LoadForBasePrefab(string basePrefabName)
        {
            var path = Path.Combine(CombosDir, SafeFileName(basePrefabName) + ".json");
            if (!File.Exists(path)) return new PrefabCombo { BasePrefabName = basePrefabName };
            try
            {
                return JsonConvert.DeserializeObject<PrefabCombo>(File.ReadAllText(path))
                       ?? new PrefabCombo { BasePrefabName = basePrefabName };
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to load combo for '{basePrefabName}': {ex.Message}");
                return new PrefabCombo { BasePrefabName = basePrefabName };
            }
        }

        public static void Save(PrefabCombo combo)
        {
            if (combo == null || string.IsNullOrWhiteSpace(combo.BasePrefabName)) return;
            Directory.CreateDirectory(CombosDir);
            var path = Path.Combine(CombosDir, SafeFileName(combo.BasePrefabName) + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(combo, Formatting.Indented));
            Log.Info($"Saved combo for '{combo.BasePrefabName}' ({combo.Members.Count} paired prefab(s)) to {path}");
        }

        // Adds (or updates, if the same PrefabName is already paired) one member and persists.
        // offset is null for the old "assume shared origin" behavior (HasOffset stays false).
        // autoPlace is null to leave the existing/default flag alone (most callers - e.g. cycling
        // the preset shouldn't silently change it); pass true/false to explicitly set it.
        public static PrefabCombo AddMember(string basePrefabName, string pairedPrefabName, string presetName, ComboMember offset = null, bool? autoPlace = null)
        {
            var combo = LoadForBasePrefab(basePrefabName);
            var existing = combo.Members.FirstOrDefault(m =>
                string.Equals(m.PrefabName, pairedPrefabName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new ComboMember { PrefabName = pairedPrefabName };
                combo.Members.Add(existing);
            }
            existing.PresetName = presetName;
            existing.ResolvedOverrides = ResolvePresetOverrides(presetName);
            if (offset != null)
            {
                existing.HasOffset = true;
                existing.OffsetX = offset.OffsetX; existing.OffsetY = offset.OffsetY; existing.OffsetZ = offset.OffsetZ;
                existing.RotSX = offset.RotSX; existing.RotSY = offset.RotSY; existing.RotSZ = offset.RotSZ;
                existing.RotFX = offset.RotFX; existing.RotFY = offset.RotFY; existing.RotFZ = offset.RotFZ;
                existing.RotUX = offset.RotUX; existing.RotUY = offset.RotUY; existing.RotUZ = offset.RotUZ;
            }
            if (autoPlace.HasValue) existing.AutoPlaceOnNewInstance = autoPlace.Value;

            Save(combo);
            return combo;
        }

        private static Dictionary<string, ColorPresetOverride> ResolvePresetOverrides(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return null;
            try { return ColorPresetStore.Load(presetName).Overrides; }
            catch (Exception ex)
            {
                Log.Warn($"Failed to resolve texture set '{presetName}' for a pairing: {ex.Message}");
                return null;
            }
        }

        public static void RemoveMember(string basePrefabName, string pairedPrefabName)
        {
            var combo = LoadForBasePrefab(basePrefabName);
            combo.Members.RemoveAll(m => string.Equals(m.PrefabName, pairedPrefabName, StringComparison.OrdinalIgnoreCase));
            Save(combo);
        }

        // Family-level pairings, stored in the SAME per-name JSON shape as a direct pairing, just
        // keyed by "family__<familyName>" instead of a literal base prefab name - a secondary
        // defined here is offered for EVERY base prefab that's a member of the family (see
        // PrefabFamilyStore below), instead of needing the same pairing re-defined once per color
        // variant. Kept as a second combo file rather than folding into PrefabCombo itself so the
        // existing per-base file format and all its callers don't need to change shape.
        private const string FamilyFilePrefix = "family__";

        public static PrefabCombo LoadForFamily(string familyName) => LoadForBasePrefab(FamilyFilePrefix + familyName);

        public static void AddFamilyMember(string familyName, string pairedPrefabName, string presetName, ComboMember offset = null) =>
            AddMember(FamilyFilePrefix + familyName, pairedPrefabName, presetName, offset);

        public static void RemoveFamilyMember(string familyName, string pairedPrefabName) =>
            RemoveMember(FamilyFilePrefix + familyName, pairedPrefabName);

        // What "Display Paired Prefabs" actually shows: this base's own direct pairings, PLUS every
        // pairing defined on any family this base is a member of (e.g. all 4 bookcase color
        // variants sharing the same "fief_bookcase" family both get the same pile-of-books
        // secondary without it being defined 4 separate times). Direct entries win if the same
        // PrefabName is paired both directly and via a family, since a direct entry is a more
        // specific, deliberate override. StorageKey on each result tells the caller which combo
        // file (this literal base, or "family__<name>") a later edit (cycle preset / remove) needs
        // to write back to.
        public static List<EffectiveComboMember> LoadEffectiveMembers(string basePrefabName)
        {
            var result = LoadForBasePrefab(basePrefabName).Members
                .Select(m => new EffectiveComboMember { Member = m, StorageKey = basePrefabName, SourceFamily = null })
                .ToList();
            var seen = new HashSet<string>(result.Select(m => m.Member.PrefabName), StringComparer.OrdinalIgnoreCase);

            foreach (var family in PrefabFamilyStore.GetFamiliesForBase(basePrefabName))
            {
                foreach (var member in LoadForFamily(family).Members)
                {
                    if (seen.Add(member.PrefabName))
                        result.Add(new EffectiveComboMember { Member = member, StorageKey = FamilyFilePrefix + family, SourceFamily = family });
                }
            }
            return result;
        }
    }

    public class EffectiveComboMember
    {
        public ComboMember Member { get; set; }
        public string StorageKey { get; set; } // literal base prefab name, or "family__<name>"
        public string SourceFamily { get; set; } // null if this came from a direct pairing
    }

    // Which family (if any) each base prefab name belongs to - many-to-many (a base can belong to
    // more than one family, though the common case is one, e.g. "fief_bookcase" for all 4 color
    // variants of the same bookcase). Deliberately plain metadata (a JSON file), not scene tags -
    // per the user's own lean, membership is a property of the PREFAB NAME itself (true regardless
    // of which scene it's placed in or how many copies exist), not of any one placed instance.
    public static class PrefabFamilyStore
    {
        private static string FamiliesPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "Combos", "_families.json");

        private static Dictionary<string, List<string>> Load()
        {
            if (!File.Exists(FamiliesPath)) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(FamiliesPath))
                       ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to load prefab families: {ex.Message}");
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveAll(Dictionary<string, List<string>> families)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FamiliesPath));
            File.WriteAllText(FamiliesPath, JsonConvert.SerializeObject(families, Formatting.Indented));
        }

        public static List<string> GetFamiliesForBase(string basePrefabName)
        {
            if (string.IsNullOrWhiteSpace(basePrefabName)) return new List<string>();
            return Load().Where(kv => kv.Value.Any(m => string.Equals(m, basePrefabName, StringComparison.OrdinalIgnoreCase)))
                .Select(kv => kv.Key).ToList();
        }

        public static List<string> GetMembers(string familyName)
        {
            var families = Load();
            return families.TryGetValue(familyName, out var members) ? members : new List<string>();
        }

        public static void AddMemberToFamily(string familyName, string basePrefabName)
        {
            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(basePrefabName)) return;
            var families = Load();
            if (!families.TryGetValue(familyName, out var members))
            {
                members = new List<string>();
                families[familyName] = members;
            }
            if (!members.Any(m => string.Equals(m, basePrefabName, StringComparison.OrdinalIgnoreCase)))
                members.Add(basePrefabName);
            SaveAll(families);
        }

        public static List<string> ListFamilyNames() => Load().Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        public static void RemoveMemberFromFamily(string familyName, string basePrefabName)
        {
            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(basePrefabName)) return;
            var families = Load();
            if (!families.TryGetValue(familyName, out var members)) return;
            members.RemoveAll(m => string.Equals(m, basePrefabName, StringComparison.OrdinalIgnoreCase));
            SaveAll(families);
        }

        // Does NOT touch any family-scoped pairings saved under "family__<name>" in
        // PrefabComboStore (see LoadForFamily) - those are left as orphaned data, matching how
        // deleting a direct pairing's base prefab doesn't retroactively clean up its Combos file
        // either. Harmless: LoadEffectiveMembers only ever reads a family's pairings for bases that
        // are still GetFamiliesForBase-listed as members, so an orphaned file just goes unread.
        public static void DeleteFamily(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return;
            var families = Load();
            if (families.Remove(familyName)) SaveAll(families);
        }
    }
}
