using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.Core
{
    public class NewPrefabResult
    {
        public bool Success;
        public string Error;
        public GameEntity AnchorEntity;
        public int TaggedCount;
    }

    // Mode 1 (New Prefab helper) and Mode 3 (combine existing prefabs at a shared origin) both
    // boil down to "create/position an entity at a computed pivot, tag whatever's involved so it
    // can be re-selected." Deliberately does NOT attempt to script the editor's own "save selection
    // as a reusable prefab resource" workflow - that's Qt-editor-only territory with no live API
    // equivalent found. This gets you to "the pieces are positioned and tagged correctly, select
    // the tag and hit the editor's own Create Prefab command" and stops there.
    public static class PrefabCreatorEngine
    {
        public const string ReselectTagPrefix = "pct_pending_";

        // Mode 1: create a blank anchor entity at the selection's bottom-center pivot, named per
        // the Fief_ convention, and tag every originally-selected entity so it can be found again
        // (interacting with this tool's own panel can clear the native editor selection).
        public static NewPrefabResult CreateNewPrefabAnchor(Scene scene, List<GameEntity> selection, string baseName)
        {
            var result = new NewPrefabResult();
            if (scene == null || selection == null || selection.Count == 0)
            {
                result.Error = "Nothing selected.";
                return result;
            }

            var pivot = PivotMath.ComputeBottomCenterPivot(selection);
            var anchorName = PrefabNaming.AnchorName(baseName);
            var reselectTag = ReselectTagPrefix + PrefabNaming.SanitizeBaseName(baseName);

            GameEntity anchor;
            try
            {
                anchor = GameEntity.CreateEmpty(scene, true, false, true);
            }
            catch (Exception ex)
            {
                result.Error = "CreateEmpty failed: " + ex.Message;
                return result;
            }

            if (anchor == null)
            {
                result.Error = "CreateEmpty returned null.";
                return result;
            }

            anchor.Name = anchorName;
            var frame = MatrixFrame.Identity;
            frame.origin = pivot;
            anchor.SetGlobalFrame(ref frame, true);
            EditorFrameSync.Sync(anchor);
            anchor.AddTag(reselectTag);

            int tagged = 0;
            foreach (var entity in selection)
            {
                if (!EntitySelector.IsValidEntity(entity)) continue;
                if (!entity.HasTag(reselectTag))
                {
                    entity.AddTag(reselectTag);
                    tagged++;
                }
            }

            result.Success = true;
            result.AnchorEntity = anchor;
            result.TaggedCount = tagged;
            return result;
        }

        // Mode 3: instantiate several EXISTING named prefabs all at the same shared origin (a
        // provided pivot, e.g. the current selection's bottom-center, or an anchor entity's own
        // position), so a "clutter" piece (a pile of books) lands perfectly aligned with a matching
        // base piece (a bookshelf) authored to share the same local origin - composing variants
        // from reusable parts instead of needing a fully separate prefab per combination.
        //
        // Matches Mode 1/Mode 2's own pattern: a renamed blank anchor is created at the pivot and
        // every instantiated prefab is parented under it via AddChild (autoLocalizeFrame keeps
        // them visually in place) - NOT left as independent siblings that merely happen to overlap.
        // Each member can optionally carry a ColorPreset name, applied right after instantiation so
        // a "paired" piece can come in pre-recolored to match (e.g. a stained pile of books next to
        // a dark-stained bookshelf).
        public class CombineMemberRequest
        {
            public string PrefabName;
            public string PresetName;
            // null/HasOffset=false = old shared-origin behavior (instantiate exactly at the
            // anchor's pivot). Otherwise the member lands at the anchor's frame transformed by
            // this relative offset - see CaptureOffset/ApplyOffset below.
            public ComboMember Offset;
        }

        public class CombineResult
        {
            public bool Success;
            public string Error;
            public GameEntity AnchorEntity;
            public List<GameEntity> Instantiated = new List<GameEntity>();
            public List<string> Failed = new List<string>();
        }

        // referenceFrame is the FULL frame (position AND rotation) of whatever you selected as the
        // shared pivot - using its rotation (not forcing world-identity) matters for offset-aware
        // members: ApplyOffset transforms a captured local offset through this frame's own basis
        // vectors, so a rotated base entity needs a correspondingly-rotated anchor for the
        // reproduced relative placement to come out right.
        public static CombineResult CombineAtSharedOrigin(Scene scene, MatrixFrame referenceFrame, List<CombineMemberRequest> members, string baseName)
        {
            var result = new CombineResult();
            if (scene == null || members == null || members.Count == 0)
            {
                result.Error = "Nothing to combine.";
                return result;
            }

            var anchorName = PrefabNaming.AnchorName(baseName);
            var reselectTag = ReselectTagPrefix + PrefabNaming.SanitizeBaseName(baseName);

            GameEntity anchor;
            try
            {
                anchor = GameEntity.CreateEmpty(scene, true, false, true);
            }
            catch (Exception ex)
            {
                result.Error = "CreateEmpty failed: " + ex.Message;
                return result;
            }
            if (anchor == null)
            {
                result.Error = "CreateEmpty returned null.";
                return result;
            }

            anchor.Name = anchorName;
            var anchorFrame = referenceFrame;
            anchor.SetGlobalFrame(ref anchorFrame, true);
            EditorFrameSync.Sync(anchor);
            anchor.AddTag(reselectTag);
            result.AnchorEntity = anchor;

            foreach (var member in members.Where(m => m != null && !string.IsNullOrWhiteSpace(m.PrefabName)))
            {
                var placeFrame = (member.Offset != null && member.Offset.HasOffset)
                    ? ApplyOffset(anchorFrame, member.Offset)
                    : anchorFrame;

                GameEntity instance;
                try
                {
                    instance = GameEntity.Instantiate(scene, member.PrefabName.Trim(), placeFrame, true);
                }
                catch (Exception ex)
                {
                    result.Failed.Add($"{member.PrefabName}: {ex.Message}");
                    continue;
                }

                if (instance == null)
                {
                    result.Failed.Add($"{member.PrefabName}: instantiate returned null (unknown prefab name?)");
                    continue;
                }

                try { anchor.AddChild(instance, true); }
                catch (Exception ex) { result.Failed.Add($"{member.PrefabName}: failed to parent under anchor: {ex.Message}"); }

                if (!string.IsNullOrWhiteSpace(member.PresetName))
                {
                    try
                    {
                        var preset = ColorPresetStore.Load(member.PresetName);
                        ColorPresetApplier.Apply(instance, preset);
                    }
                    catch (Exception ex)
                    {
                        result.Failed.Add($"{member.PrefabName}: preset '{member.PresetName}' failed to apply: {ex.Message}");
                    }
                }

                instance.AddTag(reselectTag);
                result.Instantiated.Add(instance);
            }

            result.Success = true;
            return result;
        }

        // Captures pairedEntity's current placement RELATIVE to baseEntity's own frame - position
        // and full rotation basis, not just a position delta - so ApplyOffset can correctly
        // reproduce it against a differently-positioned/rotated anchor later. Both entities must
        // already be placed where they look right together; this just records that relationship.
        public static ComboMember CaptureOffset(GameEntity baseEntity, GameEntity pairedEntity)
        {
            var b = baseEntity.GetGlobalFrame();
            var p = pairedEntity.GetGlobalFrame();
            var delta = p.origin - b.origin;

            return new ComboMember
            {
                HasOffset = true,
                OffsetX = Vec3.DotProduct(delta, b.rotation.s),
                OffsetY = Vec3.DotProduct(delta, b.rotation.f),
                OffsetZ = Vec3.DotProduct(delta, b.rotation.u),
                RotSX = Vec3.DotProduct(p.rotation.s, b.rotation.s),
                RotSY = Vec3.DotProduct(p.rotation.s, b.rotation.f),
                RotSZ = Vec3.DotProduct(p.rotation.s, b.rotation.u),
                RotFX = Vec3.DotProduct(p.rotation.f, b.rotation.s),
                RotFY = Vec3.DotProduct(p.rotation.f, b.rotation.f),
                RotFZ = Vec3.DotProduct(p.rotation.f, b.rotation.u),
                RotUX = Vec3.DotProduct(p.rotation.u, b.rotation.s),
                RotUY = Vec3.DotProduct(p.rotation.u, b.rotation.f),
                RotUZ = Vec3.DotProduct(p.rotation.u, b.rotation.u),
            };
        }

        // Reproduces a captured offset against a (possibly different) anchor frame - the inverse of
        // CaptureOffset. Standard parent-child local-to-world transform: rebuild the offset's
        // position and rotation basis vectors in the anchor's own coordinate system.
        private static MatrixFrame ApplyOffset(MatrixFrame anchor, ComboMember offset)
        {
            var origin = anchor.origin
                + anchor.rotation.s * offset.OffsetX
                + anchor.rotation.f * offset.OffsetY
                + anchor.rotation.u * offset.OffsetZ;

            var s = anchor.rotation.s * offset.RotSX + anchor.rotation.f * offset.RotSY + anchor.rotation.u * offset.RotSZ;
            var f = anchor.rotation.s * offset.RotFX + anchor.rotation.f * offset.RotFY + anchor.rotation.u * offset.RotFZ;
            var u = anchor.rotation.s * offset.RotUX + anchor.rotation.f * offset.RotUY + anchor.rotation.u * offset.RotUZ;

            return new MatrixFrame(new Mat3(s, f, u), origin);
        }

        public class AutoCombineResult
        {
            public bool Success;
            public string Error;
            public GameEntity Host;
            public List<GameEntity> Parented = new List<GameEntity>();
            public List<string> Warnings = new List<string>();
        }

        // "Select a jumble of loose dressing plus exactly one already-tagged/prefixed host entity"
        // -> auto-detect which one is the host, parent everything else onto it LIVE right now
        // (AddChild, visually unchanged), and save each child as a ComboMember of the host's own
        // combo (keyed by the host's own live Name - no typing, no separate naming step) so the
        // same grouping can be reproduced automatically via Combine at Shared Origin later. hostTag
        // is matched as a case-insensitive prefix against each entity's Name (defaults to whatever
        // PrefabNaming.Prefix is currently set to, e.g. "fief_").
        public static AutoCombineResult AutoDetectHostAndCombine(List<GameEntity> selection, string hostPrefix)
        {
            var result = new AutoCombineResult();
            if (selection == null || selection.Count < 2)
            {
                result.Error = "Select at least 2 entities: one host (matching the prefix) and one or more pieces to attach to it.";
                return result;
            }

            var prefix = string.IsNullOrWhiteSpace(hostPrefix) ? PrefabNaming.Prefix : hostPrefix;
            var candidates = selection.Where(e => EntitySelector.IsValidEntity(e) &&
                !string.IsNullOrEmpty(e.Name) && e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            if (candidates.Count == 0)
            {
                result.Error = $"No selected entity's name starts with '{prefix}' - can't tell which one is the host.";
                return result;
            }
            if (candidates.Count > 1)
            {
                candidates = DisambiguateHostCandidates(candidates);
                if (candidates.Count > 1)
                {
                    result.Error = $"{candidates.Count} selected entities start with '{prefix}' and couldn't be told apart automatically (no detail-piece name marker, no clear size difference) - need exactly ONE to identify the host unambiguously ({string.Join(", ", candidates.Select(c => c.Name))}).";
                    return result;
                }
            }

            var host = candidates[0];
            result.Host = host;
            var children = selection.Where(e => EntitySelector.IsValidEntity(e) && e != host).ToList();
            if (children.Count == 0)
            {
                result.Error = "Only the host was selected - select the pieces to attach to it too.";
                return result;
            }

            foreach (var child in children)
            {
                if (string.IsNullOrWhiteSpace(child.Name))
                {
                    result.Warnings.Add("an entity with no name (can't save as a combo member) was skipped");
                    continue;
                }

                var offset = CaptureOffset(host, child);
                try { host.AddChild(child, true); }
                catch (Exception ex) { result.Warnings.Add($"'{child.Name}': failed to parent under host: {ex.Message}"); continue; }

                PrefabComboStore.AddMember(host.Name, child.Name, null, offset);
                result.Parented.Add(child);
            }

            result.Success = result.Parented.Count > 0;
            if (!result.Success) result.Error = "Nothing was parented.";
            return result;
        }

        // Two tie-breakers for "more than one selected entity matches the host prefix," tried in
        // order, instead of just failing outright:
        //   1. Name markers - a detail/attachment piece conventionally carries a marker word in its
        //      own name (seen in real asset names, e.g. "..._cupboard_details_l" paired with a
        //      "..._wine_cupboard" host) - if exactly one candidate lacks any such marker, it wins.
        //   2. Bounding-box volume - a host is near-always physically bigger than whatever's paired
        //      with it, so the largest candidate wins, but only if it's CLEARLY the largest (50%+
        //      bigger than the runner-up) - two close-in-size candidates is exactly the case a human
        //      should look at instead of a heuristic guessing wrong silently.
        // Returns the original list unchanged if neither tie-breaker narrows it to exactly one.
        private static List<GameEntity> DisambiguateHostCandidates(List<GameEntity> candidates)
        {
            var detailMarkers = new[] { "detail", "details", "trim", "hardware", "handle", "ornament", "accent" };
            var withoutDetailMarkers = candidates
                .Where(c => !detailMarkers.Any(m => c.Name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            if (withoutDetailMarkers.Count == 1) return withoutDetailMarkers;

            var pool = withoutDetailMarkers.Count > 0 ? withoutDetailMarkers : candidates;
            var bySize = pool
                .Select(c => (Entity: c, Volume: TryGetBoundingVolume(c)))
                .OrderByDescending(x => x.Volume)
                .ToList();
            if (bySize.Count >= 2 && bySize[0].Volume > bySize[1].Volume * 1.5f)
                return new List<GameEntity> { bySize[0].Entity };

            return candidates;
        }

        private static float TryGetBoundingVolume(GameEntity entity)
        {
            try
            {
                var box = entity.GetGlobalBoundingBox();
                var size = box.max - box.min;
                return Math.Max(0f, size.x) * Math.Max(0f, size.y) * Math.Max(0f, size.z);
            }
            catch { return 0f; }
        }

        // Mode 2's apply step: given a VariantGroup the detector confirmed, create one anchor empty
        // per cluster (anchor cluster included) at that cluster's own bottom-center pivot, parent
        // every member entity under its respective anchor (AddChild's autoLocalizeFrame keeps them
        // visually in place), and name each anchor per the Fief_ convention - the base variant gets
        // Fief_<BaseName>, every other variant gets Fief_<BaseName>_<Tag> where Tag comes from
        // whichever part's material changed (first differing part found; ties aren't expected to
        // matter for naming purposes).
        public class ApplyVariantGroupResult
        {
            public GameEntity AnchorEntity;
            public List<GameEntity> VariantAnchorEntities = new List<GameEntity>();
            public List<string> Warnings = new List<string>();
        }

        public static ApplyVariantGroupResult ApplyVariantGroup(Scene scene, VariantGroup group, string baseName)
        {
            var result = new ApplyVariantGroupResult();
            if (scene == null || group == null) return result;

            result.AnchorEntity = CreateGroupAnchor(scene, group.Anchor, PrefabNaming.AnchorName(baseName), result.Warnings);

            foreach (var (cluster, parts) in group.Variants)
            {
                var differing = parts.FirstOrDefault(p => p.MaterialDiffers);
                var tag = differing != null ? PrefabNaming.TryExtractMaterialTag(differing.VariantMaterial) : null;
                var variantName = tag != null
                    ? PrefabNaming.VariantName(baseName, tag)
                    : PrefabNaming.VariantName(baseName, $"Variant{result.VariantAnchorEntities.Count + 2}");

                var anchor = CreateGroupAnchor(scene, cluster, variantName, result.Warnings);
                if (anchor != null) result.VariantAnchorEntities.Add(anchor);
            }

            return result;
        }

        private static GameEntity CreateGroupAnchor(Scene scene, EntityCluster cluster, string name, List<string> warnings)
        {
            var pivot = PivotMath.ComputeBottomCenterPivot(cluster.Members);
            GameEntity anchor;
            try
            {
                anchor = GameEntity.CreateEmpty(scene, true, false, true);
            }
            catch (Exception ex)
            {
                warnings.Add($"'{name}': CreateEmpty failed: {ex.Message}");
                return null;
            }
            if (anchor == null)
            {
                warnings.Add($"'{name}': CreateEmpty returned null.");
                return null;
            }

            anchor.Name = name;
            var frame = MatrixFrame.Identity;
            frame.origin = pivot;
            anchor.SetGlobalFrame(ref frame, true);
            EditorFrameSync.Sync(anchor);

            foreach (var member in cluster.Members)
            {
                if (!EntitySelector.IsValidEntity(member)) continue;
                try { anchor.AddChild(member, true); }
                catch (Exception ex) { warnings.Add($"'{name}': failed to parent '{member.Name}': {ex.Message}"); }
            }

            return anchor;
        }
    }
}
