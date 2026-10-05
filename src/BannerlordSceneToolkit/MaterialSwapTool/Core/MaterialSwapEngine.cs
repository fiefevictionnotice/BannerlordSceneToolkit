using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.Core
{
    public class ApplyOptions
    {
        public bool DryRun { get; set; }
        public bool TagChangedEntities { get; set; }
        public string ChangeTag { get; set; } = "MST_Changed";
        public bool RenameChangedEntities { get; set; }
        public string RenameSuffix { get; set; } = "mst";
        public bool AssignTrackingUid { get; set; }
        // Optional, independent of the material rules: applies GameEntity.SetFactorColor to
        // every targeted entity regardless of whether any rule matched it. Distinct from a
        // rule's own ColorFactor, which is per-LOD-slot (MetaMesh.SetFactor1).
        public string EntityColorFactor { get; set; } = "";
    }

    public class ApplyResult
    {
        public int EntitiesTouched { get; set; }
        public int MaterialsSwapped { get; set; }
        public int EntityColorFactorsApplied { get; set; }
        public string BatchId { get; set; }
        public List<ChangeLogEntry> Entries { get; set; } = new List<ChangeLogEntry>();

        // Color factor strings that were typed but do NOT parse as a color ("#ffwe193"),
        // collected so the UI can warn instead of silently applying no tint (2026-08-23:
        // "the per-material color factor is broken AGAIN" - it wasn't; a typo'd hex was
        // being skipped without a word, which is indistinguishable from broken).
        public List<string> InvalidColorFactors { get; } = new List<string>();
    }

    // The one place Mesh.SetMaterial(string) gets called. Name-based swap only, deliberately -
    // it repoints to a distinct cached resource instead of mutating a shared Material object,
    // which is what keeps two entities that happen to share a base material from bleeding into
    // each other. See Material.CreateCopy() in the decompiled engine source for what the
    // alternative (mutate-in-place) pattern looks like and why we're avoiding it here.
    public static class MaterialSwapEngine
    {
        public static ApplyResult Apply(List<GameEntity> targets, List<MaterialSwapRule> rules, ApplyOptions options)
        {
            var result = new ApplyResult { BatchId = ChangeLogger.NewBatchId() };
            if (targets == null || targets.Count == 0)
                return result;

            rules = rules ?? new List<MaterialSwapRule>();

            // GameEntity.SetFactorColor is a persistent property - it does NOT reset itself between
            // Apply calls. A blank per-rule color box sensibly means "skip this rule," but this
            // field has no such analog: it's a single always-visible box representing the current
            // intended entity-wide tint, not one of several independent rows. Treating blank as
            // "don't touch" (ColorHex.TryParse's normal behavior, correct for per-rule boxes) meant
            // a previously-applied saturated entity-wide tint kept silently multiplying into every
            // per-rule/per-LOD color applied afterward once the box was cleared - the per-rule color
            // WAS being applied correctly, it just looked broken under the leftover tint. Blank here
            // means "no entity-wide tint," same as explicitly clicking Clear.
            bool hasEntityColorFactor;
            uint entityColor;
            if (string.IsNullOrWhiteSpace(options.EntityColorFactor))
            {
                hasEntityColorFactor = true;
                ColorHex.TryParse("#FFFFFFFF", out entityColor);
            }
            else
            {
                hasEntityColorFactor = ColorHex.TryParse(options.EntityColorFactor, out entityColor);
                if (!hasEntityColorFactor)
                {
                    result.InvalidColorFactors.Add($"entity-wide '{options.EntityColorFactor}'");
                    Log.Warn($"[NativeTrace] entity-wide color factor '{options.EntityColorFactor}' does not parse - NO entity tint will be applied.");
                }
            }

            // No early-return short-circuit on "rules.Count == 0 && !hasEntityColorFactor" anymore -
            // hasEntityColorFactor is unconditionally true now (see above), so that check could
            // never fire. The per-entity "skip if already this color" check below keeps a genuinely
            // no-op call (no rules, blank box, entity already white) cheap without needing a
            // top-level fast path.
            // ALL rules sharing a FromMaterial are kept as alternatives (2026-08-23, "a bunch of
            // inputs that repeat themselves ... just applies stuff uniformly?"): this used to be
            // .First(), which silently discarded every duplicate row - repeated left-side inputs
            // in a preset are the author asking for a roll between those rows, each with its own
            // target AND its own color factor. One row is picked per top-level prefab (same
            // anchor as the weighted-target roll below), then that row's ToMaterial resolves as
            // usual - so a picked row can itself still be a weighted spec.
            var ruleMap = rules
                .Where(r => !string.IsNullOrWhiteSpace(r.FromMaterial) && !string.IsNullOrWhiteSpace(r.ToMaterial))
                .GroupBy(r => r.FromMaterial, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var sceneName = EntitySelector.CurrentSceneName;
            bool entityTouchedThisRun;

            // Batch-level bracket, not one per mesh/entity - "Swap All Matching in Scene" can touch
            // thousands of meshes, and NativeTrace's paired Enter/Exit (two flushed file writes each)
            // would materially slow that down and skew the very timing we're trying to observe. The
            // single "about to mutate" line before each SetMaterial/SetFactor1/SetFactorColor call
            // below is enough on its own: if a later line exists, the previous mutation completed:
            // if the log ends on one, that's the mutation in flight when it died.
            Log.Info($"[NativeTrace] ENTER MaterialSwapEngine.Apply targets={targets.Count} rules={rules.Count} dryRun={options.DryRun} scene='{sceneName}'");

            // Full rule dump with color factors, once per Apply (2026-08-23): "which rule had
            // which color when it ran" is the exact question every color-factor investigation
            // has needed answered, and it was never in the log. A typed-but-unparseable color
            // is flagged loudly here AND reported back to the panel - silently applying no
            // tint is indistinguishable from the feature being broken.
            foreach (var r in ruleMap.Values.SelectMany(list => list))
            {
                bool hasColorText = !string.IsNullOrWhiteSpace(r.ColorFactor);
                bool colorOk = hasColorText && ColorHex.TryParse(r.ColorFactor, out _);
                Log.Info($"[NativeTrace]   rule {r.FromMaterial} -> {r.ToMaterial}" +
                         (hasColorText ? $" color='{r.ColorFactor}'{(colorOk ? "" : " (UNPARSEABLE - tint will be SKIPPED)")}" : " (no color)"));
                if (hasColorText && !colorOk)
                {
                    result.InvalidColorFactors.Add($"'{r.ColorFactor}' on {r.FromMaterial} -> {r.ToMaterial}");
                    Log.Warn($"[NativeTrace] color factor '{r.ColorFactor}' on rule {r.FromMaterial} -> {r.ToMaterial} does not parse - materials will swap but NO tint will be applied.");
                }
            }

            // ONE weighted roll per TOP-LEVEL PREFAB per rule (2026-08-23, refined the same day
            // it landed per-target - "if I select 50 prefabs the top parent should define the
            // inheritance for those below"): per-mesh and per-part granularity kept finding ways
            // to disagree (LOD tiers, merged LOD meshes, duplicate sibling parts), and keying by
            // TARGET made the outcome depend on how the targets were gathered - hand-picked
            // roots rolled per prefab, but filter/scene mode hands children over as their own
            // targets, which put patchwork right back inside a prefab. The roll is now anchored
            // to each mesh-owning entity's TOPMOST parent, cached across the whole Apply:
            // uniform within a placed prefab no matter what was selected, varied across prefabs
            // by the weights - which is what weights were for.
            var rootPicks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Chosen row per (top-level prefab, FromMaterial) when duplicate rules exist.
            var rootRulePicks = new Dictionary<string, MaterialSwapRule>(StringComparer.OrdinalIgnoreCase);

            foreach (var target in targets)
            {
                if (!EntitySelector.IsValidEntity(target)) continue;

                // Composite prefabs (a parent anchor entity with the actual visible mesh living on
                // CHILD entities - columns, greebles, and a lot of real furniture in this asset
                // library) have empty or near-empty mesh slots on the entity actually selected.
                // Walking the full hierarchy (this entity plus every descendant, recursively)
                // instead of assuming the selection itself owns the mesh data is what makes a rule
                // actually match anything on this kind of prefab - confirmed live as "doesn't seem
                // to work when there's a prefab with children." Same two-pass idea already proven in
                // PrefabCreatorTool's ColorPresetApplier, generalized to arbitrary depth since a
                // material rule (unlike a preset's fixed part-key list) needs to match ANY mesh
                // slot anywhere under the selection, not just one known child name.
                foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(target))
                {
                    if (!EntitySelector.IsValidEntity(entity)) continue;
                    entityTouchedThisRun = false;

                    // Weighted-roll anchor for THIS entity's meshes - resolved lazily (the parent
                    // walk only runs when a weighted rule actually matches something here).
                    GameEntity rollRoot = null;

                    // CONFIRMED BUG, fixed 2026-08-19 (second time this file needed this exact class of
                    // fix in one day): this entity-wide reset used to run AFTER the per-mesh coloring
                    // loop below. GameEntity.GetFactorColor()/SetFactorColor() do NOT behave like an
                    // independent multiplicative tint layered on top of Mesh.Color the way the original
                    // "blank means clear" comment assumed - confirmed live via tool.log, where
                    // GetFactorColor() read back the EXACT color that had just been written to individual
                    // meshes a few lines earlier in the SAME Apply call, on every single call, regardless
                    // of what color was used. That means SetFactorColor writes across the entity's own
                    // mesh-color state directly, not a separate field - so running this AFTER the per-mesh
                    // loop was unconditionally stomping the correctly-applied colors back to white on
                    // every Apply, every time, which is exactly what "I applied #ffd580 but it's white"
                    // looks like. Moved to run FIRST: any genuinely stale tint gets cleared before the
                    // per-mesh loop runs, so the correct colors are the last thing written and they stick.
                    if (hasEntityColorFactor && entity.GetFactorColor() != entityColor)
                    {
                        var entityFrame = entity.GetGlobalFrame();
                        var factorEntry = new ChangeLogEntry
                        {
                            BatchId = result.BatchId,
                            TimestampUtc = DateTime.UtcNow,
                            SceneName = sceneName,
                            EntityName = entity.Name,
                            IsEntityWideColor = true,
                            OldColorFactor = ColorHex.ToHex(entity.GetFactorColor()),
                            NewColorFactor = ColorHex.ToHex(entityColor),
                            PosX = entityFrame.origin.x,
                            PosY = entityFrame.origin.y,
                            PosZ = entityFrame.origin.z,
                            RotForwardX = entityFrame.rotation.f.x,
                            RotForwardY = entityFrame.rotation.f.y,
                            RotForwardZ = entityFrame.rotation.f.z,
                        };

                        if (options.AssignTrackingUid && !options.DryRun)
                            factorEntry.Uid = EnsureTrackingUid(entity);

                        // Snapshot every mesh's own color BEFORE SetFactorColor stomps them -
                        // this is what undo restores (see ChangeLogEntry.MeshColorSnapshot).
                        try
                        {
                            var snap = new List<string>();
                            for (int sm = 0; sm < entity.MultiMeshComponentCount; sm++)
                            {
                                var smeta = entity.GetMetaMesh(sm);
                                if (smeta == null || !smeta.IsValid) continue;
                                for (int si = 0; si < smeta.MeshCount; si++)
                                {
                                    var sMesh = smeta.GetMeshAtIndex(si);
                                    if (sMesh == null) continue;
                                    snap.Add($"{sm}:{si}:{ColorHex.ToHex(sMesh.Color)}");
                                }
                            }
                            factorEntry.MeshColorSnapshot = string.Join("|", snap);
                        }
                        catch { }

                        if (!options.DryRun)
                        {
                            Log.Info($"[NativeTrace] SetFactorColor entity='{entity.Name}' '{factorEntry.OldColorFactor}'->'{factorEntry.NewColorFactor}'");
                            entity.SetFactorColor(entityColor);
                        }

                        result.Entries.Add(factorEntry);
                        result.EntityColorFactorsApplied++;
                        entityTouchedThisRun = true;
                    }

                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;

                        // CLEANUP, added 2026-08-19: the per-rule ColorFactor used to go through
                        // MetaMesh.SetFactor1 (see the history note below) before being fixed to use
                        // per-mesh Mesh.Color instead. Anything colored by the OLD code left a
                        // permanent, never-reset Factor1 tint behind - confirmed live: an entity
                        // Apply'd before this fix still had Factor1 stuck at a color from an hour
                        // earlier, silently multiplying into the render on top of the NEW, correctly-
                        // applied Mesh.Color and making it look like the fix hadn't worked at all when
                        // it actually had. Scoped to only entities THIS Apply is already touching (at
                        // least one mesh in this slot matches a rule) - not a blanket reset of every
                        // Factor1 in the scene, since that property is also used by unrelated native
                        // systems (e.g. TaleWorlds.MountAndBlade.MapAtmosphereProbe) this tool has no
                        // business touching on entities it was never asked to change.
                        bool metaHasMatch = false;
                        for (int probe = 0; probe < meta.MeshCount; probe++)
                        {
                            var probeName = OverrideFillEngine.StripCopySuffix(meta.GetMeshAtIndex(probe)?.GetMaterial()?.Name);
                            if (!string.IsNullOrEmpty(probeName) && ruleMap.ContainsKey(probeName)) { metaHasMatch = true; break; }
                        }
                        if (metaHasMatch && !options.DryRun)
                        {
                            const uint white = 0xFFFFFFFFu;
                            var staleFactor1 = meta.GetFactor1();
                            if (staleFactor1 != white)
                            {
                                Log.Info($"[NativeTrace] ClearStaleFactor1 entity='{entity.Name}' slot={m} '{ColorHex.ToHex(staleFactor1)}'->'{ColorHex.ToHex(white)}'");
                                meta.SetFactor1(white);
                            }
                        }

                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mesh = meta.GetMeshAtIndex(i);
                            if (mesh == null) continue;

                            var currentMaterial = mesh.GetMaterial();
                            // Matched through StripCopySuffix (2026-08-23, "only half the textures
                            // got changed at all?!"): a mesh carrying overrides reports its material
                            // as a runtime clone named 'x(copy)', which never matched a rule written
                            // against 'x' - so on a mixed entity the plain-named meshes swapped and
                            // the cloned ones silently didn't, which looks like a half-applied rule.
                            // The stripped name is also what lands in the change log as OldMaterial:
                            // a revert re-applies materials BY NAME, and 'x(copy)' is not a real
                            // resource it could restore.
                            var currentName = OverrideFillEngine.StripCopySuffix(currentMaterial?.Name);
                            if (string.IsNullOrEmpty(currentName) || !ruleMap.TryGetValue(currentName, out var candidates))
                                continue;

                            // Duplicate From rows are alternatives: pick one per top-level prefab
                            // (see the ruleMap comment above). Single-row groups skip the roll.
                            MaterialSwapRule rule;
                            if (candidates.Count == 1)
                                rule = candidates[0];
                            else
                            {
                                if (rollRoot == null)
                                    rollRoot = EntitySelector.TopMostParent(entity) ?? target;
                                var rowKey = $"{rollRoot.Pointer}|row|{currentName}";
                                if (!rootRulePicks.TryGetValue(rowKey, out rule))
                                {
                                    var rollFrame = rollRoot.GetGlobalFrame();
                                    var rowSeed = $"{rollRoot.Name}|{rollFrame.origin.x:F2}|{rollFrame.origin.y:F2}|{rollFrame.origin.z:F2}|row|{currentName}";
                                    rule = candidates[WeightedTarget.PickIndex(candidates.Count, rowSeed)];
                                    rootRulePicks[rowKey] = rule;
                                }
                            }

                            var frame = entity.GetGlobalFrame();

                            // Weighted rules ("matA:4, matB:1") resolve ONCE per top-level prefab
                            // per rule (see rootPicks above) - every mesh, LOD tier, and child
                            // entity under that prefab gets the same pick, cached by root POINTER
                            // for the run and seeded by the root's name+position so re-applies
                            // and reopens stay deterministic. Plain rules pass through unchanged.
                            string resolvedTarget;
                            if (!WeightedTarget.IsWeighted(rule.ToMaterial))
                                resolvedTarget = rule.ToMaterial;
                            else
                            {
                                if (rollRoot == null)
                                    rollRoot = EntitySelector.TopMostParent(entity) ?? target;
                                var pickKey = $"{rollRoot.Pointer}|{rule.FromMaterial}";
                                if (!rootPicks.TryGetValue(pickKey, out resolvedTarget))
                                {
                                    var rootFrame = rollRoot.GetGlobalFrame();
                                    resolvedTarget = WeightedTarget.Resolve(rule.ToMaterial,
                                        $"{rollRoot.Name}|{rootFrame.origin.x:F2}|{rootFrame.origin.y:F2}|{rootFrame.origin.z:F2}|{rule.FromMaterial}");
                                    rootPicks[pickKey] = resolvedTarget;
                                }
                            }

                            var entry = new ChangeLogEntry
                            {
                                BatchId = result.BatchId,
                                TimestampUtc = DateTime.UtcNow,
                                SceneName = sceneName,
                                EntityName = entity.Name,
                                MetaMeshIndex = m,
                                MeshIndex = i,
                                OldMaterial = currentName,
                                NewMaterial = resolvedTarget,
                                PosX = frame.origin.x,
                                PosY = frame.origin.y,
                                PosZ = frame.origin.z,
                                RotForwardX = frame.rotation.f.x,
                                RotForwardY = frame.rotation.f.y,
                                RotForwardZ = frame.rotation.f.z,
                            };

                            if (options.AssignTrackingUid && !options.DryRun)
                                entry.Uid = EnsureTrackingUid(entity);

                            if (!options.DryRun)
                            {
                                Log.Info($"[NativeTrace] SetMaterial entity='{entity.Name}' slot={m} mesh={i} '{currentName}'->'{resolvedTarget}'");
                                mesh.SetMaterial(resolvedTarget);
                            }

                            // CONFIRMED BUG, fixed 2026-08-19: a rule's own ColorFactor used to be a
                            // MetaMesh-level property (MetaMesh.SetFactor1) - but a normal building has
                            // exactly ONE MetaMesh covering every mesh across every LOD tier (confirmed
                            // by LodMismatchChecker's own header comment), so EVERY rule matching ANYTHING
                            // on that entity was fighting over the SAME single color value, "last one
                            // applied wins" silently discarding the rest - confirmed live via repeated
                            // "multiple different color factors requested" warnings on a single building
                            // with wall rules wanting one color and roof rules wanting another. Per-mesh
                            // Mesh.Color, not Factor1, is the mechanism this project ALREADY proves works
                            // for exactly this "different color per part" need - LodMismatchChecker.
                            // FixColorMismatches has used lodMesh.Color successfully the whole time. Now
                            // matches that: each mesh's color is set independently, immediately, no shared
                            // state to collide over - a wall rule and a roof rule can both fully apply on
                            // the same building at once.
                            if (!string.IsNullOrWhiteSpace(rule.ColorFactor) && ColorHex.TryParse(rule.ColorFactor, out var newColor))
                            {
                                entry.OldMeshColor = ColorHex.ToHex(mesh.Color);
                                entry.NewMeshColor = ColorHex.ToHex(newColor);
                                if (!options.DryRun)
                                {
                                    Log.Info($"[NativeTrace] SetMeshColor entity='{entity.Name}' slot={m} mesh={i} '{entry.OldMeshColor}'->'{entry.NewMeshColor}'");
                                    mesh.Color = newColor;
                                }
                            }

                            result.Entries.Add(entry);
                            result.MaterialsSwapped++;
                            entityTouchedThisRun = true;
                        }
                    }

                    if (entityTouchedThisRun)
                    {
                        result.EntitiesTouched++;
                        if (!options.DryRun)
                        {
                            if (options.TagChangedEntities && !entity.HasTag(options.ChangeTag))
                                entity.AddTag(options.ChangeTag);
                            // Same already-marked guard as the tag above - without it a second
                            // Apply on the same entity stacked suffixes (house_mst_mst_...).
                            if (options.RenameChangedEntities &&
                                !(entity.Name ?? "").EndsWith($"_{options.RenameSuffix}", StringComparison.OrdinalIgnoreCase))
                                entity.Name = $"{entity.Name}_{options.RenameSuffix}";
                        }
                    }
                }
            }

            if (!options.DryRun && result.Entries.Count > 0)
                ChangeLogger.Append(result.Entries);

            Log.Info($"[NativeTrace] EXIT MaterialSwapEngine.Apply touched={result.EntitiesTouched} materialsSwapped={result.MaterialsSwapped} colorFactorsApplied={result.EntityColorFactorsApplied}");

            return result;
        }

        private const string UidTagPrefix = "mst_uid_";

        // Reuses an existing tracking UID if one is already tagged on this entity (repeat edits
        // chain together), otherwise mints a new one. A clone carries the source's UID tag along
        // with it - that ambiguity is resolved at revert/solidify time by cross-checking position,
        // not here.
        // Internal (2026-08-23): OverrideFillEngine.ApplyMaterialLayout stamps UIDs through this
        // too - without one, RevertManager's name+position fallback NEVER trusts a match, so a
        // layout batch undid as "694 low-confidence matches, not applied" instead of undoing.
        internal static string EnsureTrackingUid(GameEntity entity)
        {
            foreach (var tag in entity.Tags)
            {
                if (tag != null && tag.StartsWith(UidTagPrefix, StringComparison.OrdinalIgnoreCase))
                    return tag.Substring(UidTagPrefix.Length);
            }

            var uid = Guid.NewGuid().ToString("N").Substring(0, 10);
            entity.AddTag(UidTagPrefix + uid);
            return uid;
        }
    }
}
