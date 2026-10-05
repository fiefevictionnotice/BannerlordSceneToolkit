using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MaterialSwapTool.Core
{
    // Static reference list of every material name in the game (from material_lookup.csv, 2812
    // names), shipped with the module as ReferenceData\known_materials.txt so validation works
    // even on someone else's machine that never had access to the original CSV. Loaded once,
    // lazily, from a path relative to the running assembly - not the hardcoded dev install path -
    // so it still resolves correctly wherever the module ends up deployed.
    public static class KnownMaterials
    {
        private static HashSet<string> _names;

        private static string ModuleDir => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", ".."));

        private static string ReferenceFilePath => Path.Combine(ModuleDir, "ReferenceData", "known_materials.txt");

        private static HashSet<string> Names
        {
            get
            {
                if (_names != null) return _names;
                _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var path = ReferenceFilePath;
                    if (File.Exists(path))
                    {
                        foreach (var line in File.ReadLines(path))
                        {
                            var name = line.Trim();
                            if (name.Length > 0) _names.Add(name);
                        }
                    }
                    else
                    {
                        Log.Warn($"KnownMaterials: reference file not found at {path} - material validation will treat everything as unknown.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("KnownMaterials: failed to load reference file: " + ex);
                }
                return _names;
            }
        }

        public static bool IsKnown(string materialName) =>
            !string.IsNullOrWhiteSpace(materialName) && Names.Contains(materialName.Trim());

        // Prefix-narrowing auto-correct, exactly as specified: try matching on the first 4
        // characters of what was typed, then 8, then 16... doubling each round, filtering the
        // surviving candidate set down each time. Stops and returns a correction only if exactly
        // one candidate remains at some point; 0 candidates (no material starts that way) or
        // still 2+ candidates once the prefix reaches the full typed length both count as "can't
        // auto-correct" - returns null rather than guessing. Only catches typos that preserve the
        // material's leading characters (missing/extra/wrong characters further in) - a typo in
        // the first few letters won't narrow to anything and is intentionally left unresolved.
        public static string TryAutoCorrect(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed)) return null;
            typed = typed.Trim();

            var candidates = Names.AsEnumerable();
            int prefixLen = 4;

            while (true)
            {
                var probe = typed.Length <= prefixLen ? typed : typed.Substring(0, prefixLen);
                var narrowed = candidates
                    .Where(n => n.StartsWith(probe, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (narrowed.Count == 1) return narrowed[0];
                if (narrowed.Count == 0) break;

                if (prefixLen >= typed.Length) break; // still ambiguous at full length
                candidates = narrowed;
                prefixLen = Math.Min(prefixLen * 2, typed.Length);
            }

            // Prefix-narrowing only ever catches a typo that comes AFTER the point where the
            // candidate set has already narrowed to one - a wrong character earlier in the name
            // (before that point) breaks it, same as one that never narrows at all. The common
            // real case that misses is simpler though: extra junk tacked onto the end of an
            // otherwise-correct name. Handle that separately by trimming from the end and looking
            // for an EXACT known name - not a real binary search (exact-match isn't monotonic in
            // trim length, so there's no sorted boundary to bisect on), just a plain scan, but at
            // material-name lengths (a few dozen characters) that's already only a few dozen O(1)
            // hash lookups, i.e. effectively instant either way.
            return TryAutoCorrectBySuffixTrim(typed);
        }

        private const int MinSuffixTrimLength = 4;

        // Tries every prefix of `typed` from full length down to MinSuffixTrimLength, longest
        // first, and returns the first one that's an exact known material name - "exact" rather
        // than "narrows to one candidate" because trimming already commits to keeping only the
        // leading characters, so there's nothing left to disambiguate against.
        private static string TryAutoCorrectBySuffixTrim(string typed)
        {
            for (int len = typed.Length - 1; len >= MinSuffixTrimLength; len--)
            {
                var candidate = typed.Substring(0, len);
                if (Names.Contains(candidate)) return candidate;
            }
            return null;
        }
    }
}
