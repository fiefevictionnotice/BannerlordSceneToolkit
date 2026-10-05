using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace PrefabCreatorTool.Core
{
    public class DetectorOptions
    {
        // How close two top-level entities need to be to count as part of the same physical
        // cluster ("one shelf assembly") - deliberately generous since a hand-built assembly of
        // several props can span a few units.
        public double ClusterRadius = 8.0;

        // Position/rotation are inherently the least reliable signal per the user's own read of
        // their workflow (a hand-placed duplicate is never bit-identical), so fuzzy matching is
        // the default, not an edge case - exact/strict mode is the opt-in.
        public bool FuzzyMatching = true;
        public double PositionToleranceFuzzy = 2.0;
        public double RotationToleranceDegreesFuzzy = 5.0;
        public double PositionToleranceStrict = 0.05;
        public double RotationToleranceDegreesStrict = 0.5;

        // Was hardcoded at 0.05 with no way to change it, regardless of FuzzyMatching or the
        // position/rotation tolerances - a real bug, not just a strict default: two hand-placed
        // "identical" assemblies with any single piece scaled even slightly differently (very
        // plausible from hand-placement) were silently rejected with no way to loosen it.
        public double ScaleToleranceFuzzy = 0.15;
        public double ScaleToleranceStrict = 0.02;
        public double ScaleTolerance => FuzzyMatching ? ScaleToleranceFuzzy : ScaleToleranceStrict;

        // Was: two clusters needed a byte-identical sorted name+count list to be considered AT
        // ALL, with zero tolerance - one extra/missing/differently-named piece anywhere in a
        // 20-30 piece assembly disqualified the whole pair before any geometry was even compared.
        // Now: clusters are candidates if at least this fraction of the SMALLER cluster's pieces
        // (by name, allowing count mismatches per name) also appear in the larger one. 1.0 =
        // old exact-match behavior; lower values tolerate real-world hand-placement variance.
        public double MinCompositionSimilarity = 0.75;

        public double PositionTolerance => FuzzyMatching ? PositionToleranceFuzzy : PositionToleranceStrict;
        public double RotationToleranceDegrees => FuzzyMatching ? RotationToleranceDegreesFuzzy : RotationToleranceDegreesStrict;
    }

    public class EntityCluster
    {
        public List<GameEntity> Members;
        public Vec3 Centroid;
        // Sorted, ".NNN"-suffix-stripped member name multiset, joined - human-readable summary only
        // now (shown in the UI), NOT used for matching anymore - see Composition/MinCompositionSimilarity.
        public string Signature;
        // name -> count, same suffix-stripping as Signature. The actual matching key: two clusters
        // are candidates if enough of this overlaps (CompositionSimilarity), not if it's identical.
        public Dictionary<string, int> Composition;
    }

    public class PartMatch
    {
        public GameEntity AnchorMember;
        public GameEntity VariantMember;
        public string AnchorMaterial;
        public string VariantMaterial;
        public bool MaterialDiffers;
    }

    public class VariantGroup
    {
        public EntityCluster Anchor;
        public List<(EntityCluster Cluster, List<PartMatch> Parts)> Variants = new List<(EntityCluster, List<PartMatch>)>();
    }

    // Detects repeated "same shape, different color" assemblies scattered through a scene (the
    // user's own example: several hand-built wooden shelf clusters, each a handful of props placed
    // together, differing only in which material/color override is applied to one or two pieces).
    //
    // Deliberately structured cheapest-check-first: (1) cluster by spatial proximity, (2) group
    // clusters by a categorical name/count signature - free compared to geometry, and rules out
    // almost every non-match immediately, (3) ONLY within a same-signature group, verify structural
    // correspondence via scale and rotation (more consistent across hand-placed duplicates than
    // absolute position, per the user) with a fuzzy tolerance, (4) finally diff material/color per
    // matched part pair to confirm it's a genuine variant and identify which part(s) changed -
    // explicitly NOT requiring every part to differ, since a rare variant might only recolor one
    // piece out of several.
    public static class VariantDetector
    {
        public static List<EntityCluster> BuildClusters(List<GameEntity> topLevelEntities, double radius)
        {
            var n = topLevelEntities.Count;
            var parentIndex = Enumerable.Range(0, n).ToArray();

            int Find(int i) { while (parentIndex[i] != i) { parentIndex[i] = parentIndex[parentIndex[i]]; i = parentIndex[i]; } return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parentIndex[a] = b; }

            var positions = topLevelEntities.Select(e => e.GetGlobalFrame().origin).ToList();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var d = positions[i] - positions[j];
                    if (d.Length <= radius) Union(i, j);
                }
            }

            var groups = new Dictionary<int, List<GameEntity>>();
            for (int i = 0; i < n; i++)
            {
                var root = Find(i);
                if (!groups.TryGetValue(root, out var list)) groups[root] = list = new List<GameEntity>();
                list.Add(topLevelEntities[i]);
            }

            return groups.Values
                .Where(members => members.Count >= 2) // a lone entity can't be a multi-part assembly
                .Select(members => BuildCluster(members))
                .ToList();
        }

        private static EntityCluster BuildCluster(List<GameEntity> members)
        {
            var centroid = Vec3.Zero;
            foreach (var m in members) centroid += m.GetGlobalFrame().origin;
            centroid /= members.Count;

            var composition = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in members)
            {
                var name = StripDuplicateSuffix(m.Name ?? "");
                composition[name] = composition.TryGetValue(name, out var c) ? c + 1 : 1;
            }
            var signature = string.Join("|", composition.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            return new EntityCluster { Members = members, Centroid = centroid, Signature = signature, Composition = composition };
        }

        // "wall_plank_a.001" -> "wall_plank_a" - the same editor-injected duplicate-suffix pattern
        // flagged in Scene Analyzer's spawn-naming-hygiene check, here used as a FEATURE: two
        // clusters built by copy-pasting the same source group will have members whose base names
        // match exactly even though the live suffixed names differ.
        private static string StripDuplicateSuffix(string name) => Regex.Replace(name, @"\.\d+$", "");

        // Fraction of the SMALLER cluster's pieces (by name, min-count per shared name) that also
        // appear in the larger cluster. 1.0 = every piece in the smaller cluster has a same-named
        // counterpart in the larger one; lower values tolerate extra/missing/renamed pieces.
        private static double CompositionSimilarity(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            int totalA = a.Values.Sum();
            int totalB = b.Values.Sum();
            int smaller = Math.Min(totalA, totalB);
            if (smaller == 0) return 0;

            int shared = 0;
            foreach (var kv in a)
                if (b.TryGetValue(kv.Key, out var bCount)) shared += Math.Min(kv.Value, bCount);

            return (double)shared / smaller;
        }

        public static List<VariantGroup> DetectVariantGroups(List<GameEntity> allEntities, DetectorOptions options)
        {
            var topLevel = allEntities.Where(e => EntitySelector.IsValidEntity(e) && e.Parent == null).ToList();
            var clusters = BuildClusters(topLevel, options.ClusterRadius)
                .Where(c => c.Composition.Count > 0)
                .ToList();

            // Greedy grouping by composition similarity (NOT exact signature match anymore) - for
            // each not-yet-claimed cluster, gather every other not-yet-claimed cluster similar
            // enough to be worth a full geometric check, verify each one via TryMatchClusters, and
            // group whatever passes. Cluster counts in a real scene are small enough that the O(n^2)
            // pairwise comparison here is cheap.
            var claimed = new bool[clusters.Count];
            var results = new List<VariantGroup>();

            for (int i = 0; i < clusters.Count; i++)
            {
                if (claimed[i]) continue;
                var anchor = clusters[i];
                var variantGroup = new VariantGroup { Anchor = anchor };

                for (int j = 0; j < clusters.Count; j++)
                {
                    if (j == i || claimed[j]) continue;
                    var candidate = clusters[j];
                    if (CompositionSimilarity(anchor.Composition, candidate.Composition) < options.MinCompositionSimilarity) continue;

                    var parts = TryMatchClusters(anchor, candidate, options);
                    if (parts != null)
                    {
                        variantGroup.Variants.Add((candidate, parts));
                        claimed[j] = true;
                    }
                }

                if (variantGroup.Variants.Count > 0)
                {
                    claimed[i] = true;
                    results.Add(variantGroup);
                }
            }

            return results;
        }

        // Pairs members between two composition-similar clusters by matching base name - shared
        // names are paired up to min(countA, countB) via NEAREST relative-position (not just list
        // order, which isn't guaranteed to correspond meaningfully between two independently
        // hand-placed clusters); any extra pieces beyond that on either side are simply left
        // unmatched rather than disqualifying the whole pair. Confirms structural correspondence via
        // scale + rotation (fuzzy-toleranced) on the matched pairs only, NOT absolute position -
        // only each part's offset relative to its own cluster's centroid is compared, so the whole
        // assembly can sit anywhere in the world and still match.
        private static List<PartMatch> TryMatchClusters(EntityCluster anchor, EntityCluster other, DetectorOptions options)
        {
            var anchorByName = anchor.Members.GroupBy(m => StripDuplicateSuffix(m.Name ?? ""), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var otherByName = other.Members.GroupBy(m => StripDuplicateSuffix(m.Name ?? ""), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var pairs = new List<(GameEntity a, GameEntity b)>();

            foreach (var kv in anchorByName)
            {
                if (!otherByName.TryGetValue(kv.Key, out var otherList)) continue; // this piece has no counterpart at all - skip it, don't disqualify

                var remaining = new List<GameEntity>(otherList);
                foreach (var a in kv.Value)
                {
                    if (remaining.Count == 0) break; // more of this piece in the anchor than the candidate has - leftover anchor pieces just go unmatched
                    var relA = a.GetGlobalFrame().origin - anchor.Centroid;

                    GameEntity best = null; double bestDist = double.MaxValue;
                    foreach (var b in remaining)
                    {
                        var relB = b.GetGlobalFrame().origin - other.Centroid;
                        var d = (relA - relB).Length;
                        if (d < bestDist) { bestDist = d; best = b; }
                    }
                    if (best != null) { pairs.Add((a, best)); remaining.Remove(best); }
                }
            }

            if (pairs.Count == 0) return null; // nothing in common at all despite passing the composition-similarity pre-filter (shouldn't normally happen)

            var rotToleranceRad = options.RotationToleranceDegrees * Math.PI / 180.0;
            var partMatches = new List<PartMatch>();
            foreach (var (a, b) in pairs)
            {
                var frameA = a.GetGlobalFrame();
                var frameB = b.GetGlobalFrame();
                var relA = frameA.origin - anchor.Centroid;
                var relB = frameB.origin - other.Centroid;
                if ((relA - relB).Length > options.PositionTolerance) continue; // this specific pair doesn't line up - skip the pair, not the whole cluster

                var scaleA = a.GetLocalScale();
                var scaleB = b.GetLocalScale();
                if ((scaleA - scaleB).Length > options.ScaleTolerance) continue;

                var dot = Math.Max(-1.0, Math.Min(1.0,
                    frameA.rotation.f.x * frameB.rotation.f.x + frameA.rotation.f.y * frameB.rotation.f.y + frameA.rotation.f.z * frameB.rotation.f.z));
                if (Math.Acos(dot) > rotToleranceRad) continue;

                var matA = GetFirstMaterialName(a);
                var matB = GetFirstMaterialName(b);
                partMatches.Add(new PartMatch
                {
                    AnchorMember = a,
                    VariantMember = b,
                    AnchorMaterial = matA,
                    VariantMaterial = matB,
                    MaterialDiffers = !string.Equals(matA, matB, StringComparison.OrdinalIgnoreCase),
                });
            }

            // Require most of the candidate pairing to actually check out geometrically (not just
            // one lucky part), and at least one part's material/color differing - otherwise it's
            // either not really the same assembly, or it's an exact duplicate (Scene Analyzer's
            // territory, not a variant).
            if (partMatches.Count < pairs.Count * 0.5) return null;
            return partMatches.Any(p => p.MaterialDiffers) ? partMatches : null;
        }

        private static string GetFirstMaterialName(GameEntity entity)
        {
            for (int m = 0; m < entity.MultiMeshComponentCount; m++)
            {
                var meta = entity.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var name = meta.GetMeshAtIndex(i)?.GetMaterial()?.Name;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            return null;
        }
    }
}
