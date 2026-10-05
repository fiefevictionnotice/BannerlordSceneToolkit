using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PrefabCreatorTool.Core
{
    // <Prefix><BaseName> for an anchor/first variant, <Prefix><BaseName>_<Tag> for a detected
    // variant - grounded in the user's own existing prefab (Fief_Aserai_Ruinous_Triple_Arch_A,
    // found in SandBoxCore\Prefabs\Fief_Aserai_Misc.xml - confirmed as their own addition, not
    // vanilla, by its file timestamps diverging from an untouched sibling file in the same
    // folder). Lowercase "fief_" is TaleWorlds' OWN convention for this kind of scene-prop asset
    // (seen throughout Native\Prefabs); "fief_" (settable, see Prefix) is the default here too,
    // distinguishing this tool's output from base-game names while still reading as the same
    // family of thing. Prefix is user-configurable (persisted) rather than hardcoded, and applying
    // it is idempotent - if the base name already starts with the configured prefix (any case),
    // it's left alone rather than doubled, since a base name is very often itself already the name
    // of an existing placed entity that was already prefixed by an earlier pass of this tool.
    public static class PrefabNaming
    {
        public const string DefaultPrefix = "fief_";
        private static string _prefix;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "naming_prefix.txt");

        public static string Prefix
        {
            get
            {
                if (_prefix != null) return _prefix;
                try
                {
                    _prefix = File.Exists(SettingsPath) ? NormalizePrefix(File.ReadAllText(SettingsPath)) : DefaultPrefix;
                }
                catch { _prefix = DefaultPrefix; }
                return _prefix;
            }
            set
            {
                _prefix = NormalizePrefix(value);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? "");
                    File.WriteAllText(SettingsPath, _prefix);
                }
                catch (Exception ex) { Log.Warn("PrefabNaming: failed to persist prefix: " + ex.Message); }
            }
        }

        private static string NormalizePrefix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DefaultPrefix;
            var trimmed = raw.Trim();
            return trimmed.EndsWith("_") ? trimmed : trimmed + "_";
        }

        public static string SanitizeBaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Untitled";
            var trimmed = name.Trim();
            // Collapse whitespace/invalid-for-entity-name characters to underscores rather than
            // stripping them, so "wood shelf" becomes "wood_shelf" instead of "woodshelf".
            var cleaned = Regex.Replace(trimmed, @"[^A-Za-z0-9_]+", "_").Trim('_');
            return cleaned.Length == 0 ? "Untitled" : cleaned;
        }

        // Never duplicates: if the sanitized base name already starts with the configured prefix
        // (case-insensitive - "fief_", "Fief_", "FIEF_" all count as "already has it"), it's
        // returned as-is rather than prefixed again.
        private static string ApplyPrefix(string sanitizedBaseName)
        {
            var prefix = Prefix;
            if (sanitizedBaseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return sanitizedBaseName;
            return prefix + sanitizedBaseName;
        }

        public static string AnchorName(string baseName) => ApplyPrefix(SanitizeBaseName(baseName));

        public static string VariantName(string baseName, string tag) =>
            $"{ApplyPrefix(SanitizeBaseName(baseName))}_{SanitizeBaseName(tag)}";

        // Pulls a readable word out of a material name to use as a variant tag - e.g.
        // "wood_dark_a" -> "Dark", "stone_wall_new_b" -> "New". Deliberately NOT an RGB-to-color-
        // name guesser (rejected per user feedback: game asset colors don't map cleanly onto web
        // color names and it produced wrong/silly results) - the material's own name is a much more
        // reliable signal, when it has one. Strips known-generic tokens (a/b/c suffix letters,
        // pure numbers, and a short list of near-meaningless words that show up in nearly every
        // material name) and keeps the first remaining descriptive word, capitalized.
        private static readonly string[] GenericTokens =
        {
            "a", "b", "c", "d", "e", "f", "new", "old", "detail", "np", "nrm", "alpha", "mat", "material",
        };

        public static string TryExtractMaterialTag(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return null;
            var parts = materialName.Split('_')
                .Where(p => p.Length > 0 && !Regex.IsMatch(p, @"^\d+$") && !GenericTokens.Contains(p.ToLowerInvariant()))
                .ToList();
            if (parts.Count == 0) return null;
            var word = parts[0];
            return char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word.Substring(1).ToLowerInvariant() : "");
        }

        // Which tag a detected variant (2nd, 3rd, ... - the 1st is the anchor/base itself, never
        // tagged) gets. ColorOrMaterial attempts TryExtractMaterialTag first and only falls back to
        // a letter if it can't infer one - matching the original ask ("attempt color or material
        // based naming... if it can't find a logical color-sorting algorithm, defaults back to
        // _a _b _c _d") rather than falling back to a number, which is what this silently did
        // before this mode existed as an explicit choice.
        public enum VariantNamingMode { Letters, Numbers, ColorOrMaterial }

        public static VariantNamingMode NamingMode
        {
            get
            {
                if (_namingMode != null) return _namingMode.Value;
                try
                {
                    _namingMode = File.Exists(ModeSettingsPath) &&
                                  Enum.TryParse(File.ReadAllText(ModeSettingsPath), out VariantNamingMode parsed)
                        ? parsed : VariantNamingMode.ColorOrMaterial;
                }
                catch { _namingMode = VariantNamingMode.ColorOrMaterial; }
                return _namingMode.Value;
            }
            set
            {
                _namingMode = value;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ModeSettingsPath) ?? "");
                    File.WriteAllText(ModeSettingsPath, value.ToString());
                }
                catch (Exception ex) { Log.Warn("PrefabNaming: failed to persist naming mode: " + ex.Message); }
            }
        }

        private static VariantNamingMode? _namingMode;

        private static string ModeSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "PrefabCreatorTool", "naming_mode.txt");

        // variantNumber: 2 for the first detected variant beyond the anchor, 3 for the next, etc. -
        // matches the existing "Variant{saved + 2}" convention this replaces.
        public static string NextVariantTag(int variantNumber, string materialNameForColorAttempt)
        {
            switch (NamingMode)
            {
                case VariantNamingMode.Numbers:
                    return "Variant" + variantNumber;
                case VariantNamingMode.Letters:
                    return LetterTag(variantNumber);
                case VariantNamingMode.ColorOrMaterial:
                default:
                    return TryExtractMaterialTag(materialNameForColorAttempt) ?? LetterTag(variantNumber);
            }
        }

        private static string LetterTag(int variantNumber)
        {
            var index = Math.Max(0, variantNumber - 2); // n=2 -> 'B' (anchor itself is the implicit "A")
            return index <= 24 ? ((char)('B' + index)).ToString() : "Variant" + variantNumber;
        }
    }
}
