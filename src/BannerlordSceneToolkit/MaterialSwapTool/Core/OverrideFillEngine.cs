using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.Core
{
    // Two related "learn from an existing entity instead of typing rules by hand" features, both
    // requested together:
    //
    // 1. Fill In Overrides (entity A -> entity B): pick a reference entity A (already has the
    //    colors you want), then a target entity B (same/similar materials, wrong colors). For
    //    every mesh slot on B whose CURRENT MATERIAL matches a material found somewhere on A,
    //    set that slot's own Mesh.Color to A's color for that material - matches by material
    //    NAME only, B's material itself is never touched, only its color.
    //
    //    HISTORY, 2026-08-19: this briefly got "fixed" to read/write MetaMesh.Factor1 instead,
    //    based on a real observation (tool.log showed SetFactor1 firing, not Mesh.Color) - but
    //    that observation was itself downstream of MaterialSwapEngine.Apply using the WRONG
    //    mechanism for a rule's own ColorFactor at the time. Once THAT was corrected back to
    //    Mesh.Color (see MaterialSwapEngine.cs's own history note - LodMismatchChecker.
    //    FixColorMismatches already proved Mesh.Color is the right per-part granularity, using
    //    Factor1 meant every rule sharing a building's one MetaMesh fought over a single color
    //    value), this needed to follow suit back to Mesh.Color too, to keep reading/writing the
    //    SAME property the Rules workflow actually uses.
    //
    // 2. Fill Rules From Entity: an alternate mode of "Get Input Rules from Selection" (the
    //    existing FromMaterial-only "blank rule" seeder). Only meaningful once you already have
    //    blank rules queued (FromMaterial set, ToMaterial empty) - it does NOT add new rows.
    //    Select a prefab (or several); this walks that selection's own hierarchy (self + every
    //    descendant - "the prefab/child of the prefab/direct entity you select"), groups entities
    //    by (Name, Tags) - duplicate copies of the same named part - and for any group that
    //    contains both a blank rule's FromMaterial AND a different material, fills that rule's
    //    ToMaterial with the other material. This is for prefabs where some copies of a repeated
    //    part were already manually swapped and others weren't yet - the swapped copies become
    //    the answer key for the rest.
    public static class OverrideFillEngine
    {
        public class ColorFillChange
        {
            public GameEntity Entity;
            public string EntityName;
            public int MetaMeshIndex;
            public int MeshIndex;
            public string SlotName;
            public string Material;
            public uint OldColor;
            public uint NewColor;
        }

        // First-seen wins if the reference entity has the same material on multiple slots with
        // different colors - same convention as LivePrefabSwapper.CaptureOwnMeshOverrides.
        //
        // Reads the EFFECTIVE color, not just the raw Mesh.Color: a reference entity colored via
        // the OLD (pre-2026-08-19) rules path, or via the always-on entity-wide "Clear" default,
        // can still be carrying its real visible color on MetaMesh.Factor1 instead of on the mesh
        // itself, leaving Mesh.Color sitting at white. Reading Mesh.Color alone off a reference
        // like that silently captures "white" for every material - the fill then "succeeds" but
        // visibly does nothing, which is indistinguishable from Fill In Overrides being broken.
        // Multiplying in the MetaMesh's own Factor1 (ColorHex.Multiply is a no-op against white,
        // the normal case going forward) captures what the reference actually LOOKS like instead
        // of trusting one specific property to hold the whole story.
        private static Dictionary<string, uint> BuildMaterialColorMap(GameEntity reference)
        {
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(reference))
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;
                for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                {
                    var meta = entity.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    var factor1 = meta.GetFactor1();
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var mesh = meta.GetMeshAtIndex(i);
                        var material = mesh?.GetMaterial()?.Name;
                        if (string.IsNullOrEmpty(material)) continue;
                        if (!map.ContainsKey(material))
                        {
                            var effective = ColorHex.Multiply(mesh.Color, factor1);
                            map[material] = effective;
                            if (effective != mesh.Color)
                                Log.Info($"[NativeTrace] FillReadEffectiveColor entity='{entity.Name}' material='{material}' meshColor='{ColorHex.ToHex(mesh.Color)}' factor1='{ColorHex.ToHex(factor1)}' effective='{ColorHex.ToHex(effective)}'");
                        }
                    }
                }
            }
            return map;
        }

        // Recurses into target's own children too - composite prefabs (parent anchor + mesh data
        // on children) are the norm in this asset library, not the exception, and every prior
        // "only checks the entity's own slots" bug this session traces back to skipping that.
        public static List<ColorFillChange> PreviewColorFill(GameEntity reference, GameEntity target)
        {
            var colorMap = BuildMaterialColorMap(reference);
            var changes = new List<ColorFillChange>();
            if (colorMap.Count == 0) return changes;

            foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(target))
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;
                for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                {
                    var meta = entity.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var mesh = meta.GetMeshAtIndex(i);
                        var material = mesh?.GetMaterial()?.Name;
                        if (string.IsNullOrEmpty(material)) continue;
                        if (!colorMap.TryGetValue(material, out var newColor)) continue;
                        if (mesh.Color == newColor) continue;

                        changes.Add(new ColorFillChange
                        {
                            Entity = entity,
                            EntityName = entity.Name,
                            MetaMeshIndex = m,
                            MeshIndex = i,
                            SlotName = mesh.Name,
                            Material = material,
                            OldColor = mesh.Color,
                            NewColor = newColor,
                        });
                    }
                }
            }
            return changes;
        }

        public static int ApplyColorFill(List<ColorFillChange> changes, string batchId, List<ChangeLogEntry> logEntries, string sceneName)
        {
            int applied = 0;
            Log.Info($"[NativeTrace] ENTER OverrideFillEngine.ApplyColorFill changes={changes?.Count ?? 0} scene='{sceneName}'");
            foreach (var c in changes)
            {
                if (!EntitySelector.IsValidEntity(c.Entity)) continue;
                var meta = c.Entity.GetMetaMesh(c.MetaMeshIndex);
                if (meta == null || !meta.IsValid) continue;
                var mesh = meta.GetMeshAtIndex(c.MeshIndex);
                if (mesh == null) continue;

                if (logEntries != null)
                {
                    var frame = c.Entity.GetGlobalFrame();
                    logEntries.Add(new ChangeLogEntry
                    {
                        BatchId = batchId,
                        TimestampUtc = DateTime.UtcNow,
                        SceneName = sceneName,
                        EntityName = c.EntityName,
                        MetaMeshIndex = c.MetaMeshIndex,
                        MeshIndex = c.MeshIndex,
                        OldMeshColor = ColorHex.ToHex(c.OldColor),
                        NewMeshColor = ColorHex.ToHex(c.NewColor),
                        PosX = frame.origin.x,
                        PosY = frame.origin.y,
                        PosZ = frame.origin.z,
                        RotForwardX = frame.rotation.f.x,
                        RotForwardY = frame.rotation.f.y,
                        RotForwardZ = frame.rotation.f.z,
                    });
                }

                Log.Info($"[NativeTrace] SetMeshColor entity='{c.EntityName}' slot={c.MetaMeshIndex} mesh={c.MeshIndex} '{ColorHex.ToHex(c.OldColor)}'->'{ColorHex.ToHex(c.NewColor)}'");
                mesh.Color = c.NewColor;
                applied++;
            }
            Log.Info($"[NativeTrace] EXIT OverrideFillEngine.ApplyColorFill applied={applied}");
            return applied;
        }

        // GENERATE-RULES mode for Reference (A), the alternative to applying colors directly.
        //
        // PreviewColorFill/ApplyColorFill write Mesh.Color onto one specific target entity - a
        // one-shot, instance-bound edit. That does not scale: recolouring 200 buildings that way
        // means 200 A->B pairings. Emitting the SAME information as rules instead makes it data -
        // it saves as a preset, applies to a WholeScene/Filtered selection in one Apply, survives
        // Revert to Normal, carries to other scenes, and can be handed to someone else.
        //
        // Each rule is (material -> SAME material) with a ColorFactor - that's how you express
        // "tint this material, don't swap it". Verified against MaterialSwapEngine's apply loop:
        // there is no early-out for "target material == current material", so the rule still
        // reaches the ColorFactor block and tints normally. It does re-issue a redundant
        // SetMaterial to the identical material first (harmless, but it does emit a SetMaterial
        // line into tool.log per mesh) - worth knowing when reading a trace from one of these.
        //
        // Reads through BuildMaterialColorMap, so it captures the reference's EFFECTIVE colour
        // (Mesh.Color x MetaMesh Factor1). That also launders a reference coloured by the old
        // pre-2026-08-19 Factor1 path into clean per-mesh rules.
        //
        // White is skipped deliberately: 0xFFFFFFFF is this engine's identity/no-tint value (see
        // ColorHex.Multiply), so emitting it would add rules that either do nothing or actively
        // wipe a colour the target already had.
        public static List<MaterialSwapRule> GenerateColorRules(GameEntity reference)
        {
            var rules = new List<MaterialSwapRule>();
            foreach (var kvp in BuildMaterialColorMap(reference).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (kvp.Value == WhiteColor) continue;
                rules.Add(new MaterialSwapRule(kvp.Key, kvp.Key, ColorHex.ToHex(kvp.Value)));
            }
            Log.Info($"[NativeTrace] GenerateColorRules reference='{reference?.Name}' rules={rules.Count}");
            return rules;
        }

        private const uint WhiteColor = 0xFFFFFFFF;

        public class MaterialInferResult
        {
            public List<MaterialSwapRule> Rules = new List<MaterialSwapRule>();
            // A From material that mapped to more than one different To across the two prefabs.
            // Emitted as a single weighted rule rather than several rows, because ruleMap in
            // MaterialSwapEngine is keyed by FromMaterial and keeps only the first match - N plain
            // rows for one material would silently lose all but one.
            public List<string> Weighted = new List<string>();
            public int SlotsCompared;
            public int EntitiesPaired;
            public bool StructureMismatch;
            public string MismatchNote = "";
        }

        // INFER MATERIAL RULES: A -> B across two structurally identical prefabs.
        //
        // This is the "as if I ran Get Input Materials on prefab 1, then pasted prefab 2's
        // materials in as the targets" operation. Walks BOTH hierarchies in parallel and pairs
        // them slot-for-slot, emitting FromMaterial = A's material, ToMaterial = B's.
        //
        // Alignment is POSITIONAL (same index in the enumeration, same metamesh/mesh index), not
        // by material name. Name-based matching is wrong here by construction: one material can
        // sit on several slots that translate differently, and the whole point is to capture that
        // per-slot difference. Positional alignment is only valid because the two prefabs are
        // meant to be copies - so entity names are cross-checked as a guard, and a mismatch is
        // reported rather than silently producing garbage rules.
        public static MaterialInferResult InferMaterialRules(GameEntity a, GameEntity b)
        {
            var result = new MaterialInferResult();
            var listA = EntitySelector.EnumerateSelfAndDescendants(a).Where(EntitySelector.IsValidEntity).ToList();
            var listB = EntitySelector.EnumerateSelfAndDescendants(b).Where(EntitySelector.IsValidEntity).ToList();

            if (listA.Count != listB.Count)
            {
                result.StructureMismatch = true;
                result.MismatchNote = $"A has {listA.Count} entity(ies), B has {listB.Count} - pairing the first {Math.Min(listA.Count, listB.Count)}.";
            }

            // from -> (to -> how many slots voted for it)
            var votes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            int pairCount = Math.Min(listA.Count, listB.Count);

            for (int e = 0; e < pairCount; e++)
            {
                var ea = listA[e];
                var eb = listB[e];
                if (!string.Equals(ea.Name, eb.Name, StringComparison.OrdinalIgnoreCase) && !result.StructureMismatch)
                {
                    result.StructureMismatch = true;
                    result.MismatchNote = $"Entity {e} differs: A='{ea.Name}' vs B='{eb.Name}'. Rules may be misaligned.";
                }
                result.EntitiesPaired++;

                int metaCount = Math.Min(ea.MultiMeshComponentCount, eb.MultiMeshComponentCount);
                for (int m = 0; m < metaCount; m++)
                {
                    var metaA = ea.GetMetaMesh(m);
                    var metaB = eb.GetMetaMesh(m);
                    if (metaA == null || !metaA.IsValid || metaB == null || !metaB.IsValid) continue;

                    int meshCount = Math.Min(metaA.MeshCount, metaB.MeshCount);
                    for (int i = 0; i < meshCount; i++)
                    {
                        // Normalized through StripCopySuffix (2026-08-23, "empire_wall_brick(copy):5
                        // ... this is not a valid rule"): a mesh carrying runtime overrides hands
                        // back a CLONED material named 'whatever(copy)' - not a real material
                        // resource, so a rule targeting it can never apply. Worse, the clone and
                        // its base voted as two DIFFERENT targets, splitting the count and
                        // manufacturing weighted specs out of what is really one material.
                        // Stripping before the vote yields real names and merged weights.
                        var matA = StripCopySuffix(metaA.GetMeshAtIndex(i)?.GetMaterial()?.Name);
                        var matB = StripCopySuffix(metaB.GetMeshAtIndex(i)?.GetMaterial()?.Name);
                        if (string.IsNullOrEmpty(matA) || string.IsNullOrEmpty(matB)) continue;

                        result.SlotsCompared++;
                        if (string.Equals(matA, matB, StringComparison.OrdinalIgnoreCase)) continue;

                        if (!votes.TryGetValue(matA, out var targets))
                        {
                            targets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            votes[matA] = targets;
                        }
                        targets.TryGetValue(matB, out int n);
                        targets[matB] = n + 1;
                    }
                }
            }

            foreach (var kvp in votes.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var targets = kvp.Value.OrderByDescending(t => t.Value).ToList();
                if (targets.Count == 1)
                {
                    result.Rules.Add(new MaterialSwapRule(kvp.Key, targets[0].Key));
                }
                else
                {
                    var spec = string.Join(", ", targets.Select(t => $"{t.Key}:{t.Value}"));
                    result.Rules.Add(new MaterialSwapRule(kvp.Key, spec));
                    result.Weighted.Add(kvp.Key);
                }
            }

            Log.Info($"[NativeTrace] InferMaterialRules A='{a?.Name}' B='{b?.Name}' paired={result.EntitiesPaired} slots={result.SlotsCompared} rules={result.Rules.Count} weighted={result.Weighted.Count} mismatch={result.StructureMismatch}");
            return result;
        }

        public class MaterialLayoutResult
        {
            public int EntitiesPaired;
            public int SlotsCompared;
            public int SlotsChanged;
            public bool StructureMismatch;
            public string MismatchNote = "";
            public List<ChangeLogEntry> Entries = new List<ChangeLogEntry>();
        }

        // Slot-exact sibling of InferMaterialRules (2026-08-23, "shouldn't it be overriding
        // stuff more selectively?"): when one of A's materials maps to SEVERAL of B's
        // depending on the slot (the retaining-wall case - one desert material vs four empire
        // ones), a name-based rule can only express a weighted dice roll, even though the
        // pairing walk knows exactly which slot should get which material. This uses that
        // knowledge directly: same A/B hierarchy walk, but instead of voting it SETS each of
        // A's mesh slots to the material B carries in the same slot. Names are normalized
        // through StripCopySuffix on both sides - reverts re-apply OldMaterial by name, so a
        // '(copy)' name there would be unrestorable.
        public static MaterialLayoutResult ApplyMaterialLayout(GameEntity a, GameEntity b,
            string batchId, string sceneName, bool dryRun)
        {
            var result = new MaterialLayoutResult();
            var listA = EntitySelector.EnumerateSelfAndDescendants(a).Where(EntitySelector.IsValidEntity).ToList();
            var listB = EntitySelector.EnumerateSelfAndDescendants(b).Where(EntitySelector.IsValidEntity).ToList();

            if (listA.Count != listB.Count)
            {
                result.StructureMismatch = true;
                result.MismatchNote = $"A has {listA.Count} entity(ies), B has {listB.Count} - pairing the first {Math.Min(listA.Count, listB.Count)}.";
            }

            int pairCount = Math.Min(listA.Count, listB.Count);
            for (int e = 0; e < pairCount; e++)
            {
                var ea = listA[e];
                var eb = listB[e];
                if (!string.Equals(ea.Name, eb.Name, StringComparison.OrdinalIgnoreCase) && !result.StructureMismatch)
                {
                    result.StructureMismatch = true;
                    result.MismatchNote = $"Entity {e} differs: A='{ea.Name}' vs B='{eb.Name}'. Slots may be misaligned.";
                }
                result.EntitiesPaired++;

                int metaCount = Math.Min(ea.MultiMeshComponentCount, eb.MultiMeshComponentCount);
                for (int m = 0; m < metaCount; m++)
                {
                    var metaA = ea.GetMetaMesh(m);
                    var metaB = eb.GetMetaMesh(m);
                    if (metaA == null || !metaA.IsValid || metaB == null || !metaB.IsValid) continue;

                    // B's slots indexed by LOD-stripped part name (2026-08-23, "is it ONLY usable
                    // when ALL entities match exactly?"): pairing by raw mesh index required the
                    // two variants to have identical mesh order AND identical LOD splits - a
                    // variant with merged materials often has neither. Name pairing survives
                    // reordering and differing LOD tiers; index pairing stays as the fallback for
                    // unnamed meshes.
                    var bByPart = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int j = 0; j < metaB.MeshCount; j++)
                    {
                        var meshB = metaB.GetMeshAtIndex(j);
                        if (meshB == null) continue;
                        var partName = LodMismatchChecker.StripLodSegment(meshB.Name, out _);
                        var matName = StripCopySuffix(meshB.GetMaterial()?.Name);
                        if (!string.IsNullOrEmpty(partName) && !string.IsNullOrEmpty(matName) && !bByPart.ContainsKey(partName))
                            bByPart[partName] = matName;
                    }

                    for (int i = 0; i < metaA.MeshCount; i++)
                    {
                        var meshA = metaA.GetMeshAtIndex(i);
                        if (meshA == null) continue;
                        var matA = StripCopySuffix(meshA.GetMaterial()?.Name);

                        var partA = LodMismatchChecker.StripLodSegment(meshA.Name, out _);
                        string matB = null;
                        if (!string.IsNullOrEmpty(partA) && bByPart.TryGetValue(partA, out var byName))
                            matB = byName;
                        else if (i < metaB.MeshCount)
                            matB = StripCopySuffix(metaB.GetMeshAtIndex(i)?.GetMaterial()?.Name);

                        if (string.IsNullOrEmpty(matA) || string.IsNullOrEmpty(matB)) continue;

                        result.SlotsCompared++;
                        if (string.Equals(matA, matB, StringComparison.OrdinalIgnoreCase)) continue;

                        var frame = ea.GetGlobalFrame();
                        result.Entries.Add(new ChangeLogEntry
                        {
                            BatchId = batchId,
                            TimestampUtc = DateTime.UtcNow,
                            SceneName = sceneName,
                            EntityName = ea.Name,
                            // UID-stamped like every MaterialSwapEngine entry (2026-08-23, "I
                            // can't undo apply B to A?"): RevertManager NEVER trusts a
                            // name+position match without one, so an un-UID'd batch undid as
                            // hundreds of "low-confidence, not applied" confirmations.
                            Uid = dryRun ? null : MaterialSwapEngine.EnsureTrackingUid(ea),
                            MetaMeshIndex = m,
                            MeshIndex = i,
                            OldMaterial = matA,
                            NewMaterial = matB,
                            PosX = frame.origin.x,
                            PosY = frame.origin.y,
                            PosZ = frame.origin.z,
                            RotForwardX = frame.rotation.f.x,
                            RotForwardY = frame.rotation.f.y,
                            RotForwardZ = frame.rotation.f.z,
                        });

                        if (!dryRun)
                        {
                            Log.Info($"[NativeTrace] LayoutSetMaterial entity='{ea.Name}' slot={m} mesh={i} '{matA}'->'{matB}'");
                            meshA.SetMaterial(matB);
                        }
                        result.SlotsChanged++;
                    }
                }
            }

            Log.Info($"[NativeTrace] ApplyMaterialLayout A='{a?.Name}' B='{b?.Name}' paired={result.EntitiesPaired} slots={result.SlotsCompared} changed={result.SlotsChanged} dryRun={dryRun} mismatch={result.StructureMismatch}");
            return result;
        }

        // The engine names a runtime-cloned material '<base>(copy)' - and clones of clones
        // '<base>(copy)(copy)'. Looped rather than a single TrimEnd because the suffix stacks.
        // Public because MaterialSwapEngine matches rules through it too: a mesh carrying an
        // override-clone 'x(copy)' must match a rule written against 'x' (2026-08-23, "only
        // half the textures got changed" - the plain-named half matched, the cloned half never
        // could).
        public static string StripCopySuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            const string suffix = "(copy)";
            while (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - suffix.Length).TrimEnd();
            return name;
        }

        public class RuleFillResult
        {
            public Dictionary<string, string> Fills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<string> Ambiguous = new List<string>();
        }

        // Materials on ONE entity's own slots only - no recursion, since the caller has already
        // flattened the hierarchy and each pooled item here should be counted as itself, not as
        // "itself plus its own children again."
        private static List<string> GetOwnMaterials(GameEntity entity)
        {
            var result = new List<string>();
            for (int m = 0; m < entity.MultiMeshComponentCount; m++)
            {
                var meta = entity.GetMetaMesh(m);
                if (meta == null || !meta.IsValid) continue;
                for (int i = 0; i < meta.MeshCount; i++)
                {
                    var material = meta.GetMeshAtIndex(i)?.GetMaterial()?.Name;
                    if (!string.IsNullOrEmpty(material)) result.Add(material);
                }
            }
            return result;
        }

        private static string TagsKey(GameEntity entity)
        {
            var tags = entity.Tags?.Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t.ToLowerInvariant())
                .OrderBy(t => t, StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            return string.Join("|", tags);
        }

        // blankFromMaterials: the FromMaterial of every currently-blank rule (ToMaterial empty),
        // deduped. For each one, looks for a (Name, Tags) group - within the selection's own
        // hierarchy - that contains both that material AND exactly one other distinct material;
        // that other material becomes the fill-in answer. A group with more than one OTHER
        // distinct material is ambiguous and reported, not guessed at.
        public static RuleFillResult PreviewRuleFill(List<GameEntity> selected, IEnumerable<string> blankFromMaterials)
        {
            var result = new RuleFillResult();
            var wanted = new HashSet<string>(blankFromMaterials, StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return result;

            var pool = new List<GameEntity>();
            foreach (var s in selected)
            {
                if (!EntitySelector.IsValidEntity(s)) continue;
                pool.AddRange(EntitySelector.EnumerateSelfAndDescendants(s));
            }

            var groups = pool.Where(EntitySelector.IsValidEntity)
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .GroupBy(e => (Name: e.Name, Tags: TagsKey(e)));

            foreach (var group in groups)
            {
                var materialsInGroup = group.SelectMany(GetOwnMaterials)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (materialsInGroup.Count < 2) continue;

                foreach (var from in materialsInGroup)
                {
                    if (!wanted.Contains(from)) continue;
                    if (result.Fills.ContainsKey(from) || result.Ambiguous.Contains(from, StringComparer.OrdinalIgnoreCase)) continue;

                    var others = materialsInGroup.Where(m => !string.Equals(m, from, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (others.Count == 1)
                        result.Fills[from] = others[0];
                    else
                        result.Ambiguous.Add(from);
                }
            }

            return result;
        }
    }
}
