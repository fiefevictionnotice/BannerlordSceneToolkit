using System.Collections.Generic;
using System.Linq;

namespace MaterialSwapTool.Core
{
    // A rule's ToMaterial can optionally encode multiple weighted options instead of one plain
    // material name - "stone_wall_desert_a:4, stone_wall_desert_b:1" (weight optional, defaults
    // to 1) - the same syntax BSA's own category-fallback templates use. Detected purely by the
    // presence of ':' or ',', since no valid material name contains either character.
    //
    // Picking is deterministic per (entity, slot, rule), not truly random: hashed from stable
    // identity (entity name + position + slot indices + the rule itself), NOT
    // string.GetHashCode() - .NET randomizes string hashing per-process by default, which would
    // make "deterministic" picks reshuffle every time the game restarts, breaking the promise
    // that re-running Dry Run or reopening the scene gives the same result.
    public static class WeightedTarget
    {
        public static bool IsWeighted(string toMaterial) =>
            !string.IsNullOrEmpty(toMaterial) && (toMaterial.Contains(':') || toMaterial.Contains(','));

        public static List<(string Material, int Weight)> Parse(string toMaterial)
        {
            var result = new List<(string, int)>();
            foreach (var rawPart in toMaterial.Split(','))
            {
                var part = rawPart.Trim();
                if (part.Length == 0) continue;

                var colonIndex = part.LastIndexOf(':');
                if (colonIndex > 0 && int.TryParse(part.Substring(colonIndex + 1).Trim(), out var weight) && weight > 0)
                    result.Add((part.Substring(0, colonIndex).Trim(), weight));
                else
                    result.Add((part, 1));
            }
            return result;
        }

        // Returns toMaterial unchanged if it isn't a weighted spec. seed should uniquely identify
        // this (entity, slot, rule) combination - see the callers in MaterialSwapEngine.
        public static string Resolve(string toMaterial, string seed)
        {
            if (!IsWeighted(toMaterial)) return toMaterial;

            var options = Parse(toMaterial);
            if (options.Count == 0) return toMaterial;
            if (options.Count == 1) return options[0].Material;

            int totalWeight = options.Sum(o => o.Weight);
            if (totalWeight <= 0) return options[0].Material;

            uint hash = StableHash(seed);
            int pick = (int)(hash % (uint)totalWeight);
            int cumulative = 0;
            foreach (var option in options)
            {
                cumulative += option.Weight;
                if (pick < cumulative) return option.Material;
            }
            return options[options.Count - 1].Material;
        }

        // Uniform deterministic pick among N options - MaterialSwapEngine uses this to choose
        // between DUPLICATE rules sharing one FromMaterial (2026-08-23: repeated left-side
        // inputs are alternatives to roll between, not rows to silently discard).
        public static int PickIndex(int optionCount, string seed) =>
            optionCount <= 1 ? 0 : (int)(StableHash(seed) % (uint)optionCount);

        // FNV-1a - simple, well-known, and (unlike string.GetHashCode()) produces the same output
        // every run, which is the entire point here.
        private static uint StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return hash;
            }
        }
    }
}
