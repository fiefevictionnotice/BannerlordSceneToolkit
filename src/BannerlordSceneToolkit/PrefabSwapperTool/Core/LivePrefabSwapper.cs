using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.Core
{
    // Shared "swap this entity for a different prefab, keep it where it was" engine - the same
    // core operation underlies the physics-unfucker-style curated fix (BrokenPrefabFixer), the LOD
    // Substitution replace action, and the general-purpose F6 Prefab Swapper. One live-API
    // implementation instead of three separate mechanisms (raw-text regex, XDocument attribute
    // editing, and whatever F6 would otherwise reinvent).
    //
    // GameEntity.Instantiate(scene, prefabName, frame, callScriptCallbacks) is confirmed live via
    // reflection against the shipped TaleWorlds.Engine.dll - it places the new entity directly at a
    // given MatrixFrame rather than the origin, so no separate "move it into place" step is needed.
    // Removal goes through Scene.RemoveEntity(entity, reason) with reason 0 (no named
    // EntityRemoveReason enum exists in this build - other calls in this codebase that remove
    // entities use the same raw 0).
    //
    // KNOWN LIMITATION: GameEntity exposes GetLocalScale()/GetGlobalScale() but no public setter -
    // scale is baked in from the prefab's own saved XML at instantiation time and can't be poked
    // afterward through this API. A swap onto an entity with non-default (not 1,1,1) scale will
    // silently lose that scale on the new instance. SwapEntity reports this via
    // SwapResult.ScaleWasNonDefault so callers can warn the user instead of the change happening
    // invisibly.
    public static class LivePrefabSwapper
    {
        // ADD MODE (2026-08-22, direct request): place the NEW prefab at each target's exact
        // frame and leave the original COMPLETELY untouched - no removal, no child re-parenting,
        // no override transplant. Born from a real failure: swapping pieces that are CHILDREN of
        // placed prefab instances made the removal step trip the editor's "break prefab?" dialog
        // once per piece, removals half-failed, and old+new ended up stacked with z-fighting.
        // Add mode makes the overlap DELIBERATE and safe: originals stay pristine, and you delete
        // them in one manual sweep once the new pieces look right. SWAP (replace) is the default
        // - revised by direct request the same evening Add mode was added; Add is the opt-in for
        // prefab-child targets. Shared by Swap Selected, Swap All Matching, swap to live
        // reference, and swap-set Apply.
        public static bool AddModeKeepOriginals = false;

        // SCALE (2026-08-22): the class comment above says scale can't carry over ("no public
        // setter") - HALF WRONG, confirmed live: this engine stores scale as the frame's rotation
        // basis LENGTHS, so Instantiate/SetGlobalFrame at the old entity's frame silently
        // INHERITS its scale after all. Made explicit and toggleable: false (default, "1x
        // Scale") normalizes the basis so the new entity gets the prefab's own authored scale;
        // true ("Inherit Scale") keeps the old entity's scale in the frame - what the code
        // always actually did. Applies to swap, add, and live-reference paths alike.
        public static bool InheritSourceScale = false;

        // Optional extra transform applied to every swapped-in entity (2026-08-23, "i need an
        // option to apply a z-axis rotation during the swapper mode ... scale overall"):
        // degrees of additional rotation about world Z, and a uniform scale multiplier ON TOP
        // of whatever the scale mode produced. Statics like the toggles above, so every path -
        // name swap, add mode, live reference, swap-set Apply - honours them; undo/redo zero
        // them out for the duration (see PrefabSwapHistory).
        public static float SwapZRotationDegrees = 0f;
        public static float SwapScaleMultiplier = 1f;

        // Unit-length basis when 1x Scale is selected; untouched when inheriting. Extra Z
        // rotation and scale multiplier are layered on afterwards.
        private static MatrixFrame ApplyScaleMode(MatrixFrame frame)
        {
            // Diagnostic (2026-08-23, "live reference doesn't appear to work with the Z Rot"):
            // says whether the extras actually ARRIVED for this swap - a silent 0 here means
            // the input binding never delivered, not that the math failed.
            if (Math.Abs(SwapZRotationDegrees) > 0.001f || Math.Abs(SwapScaleMultiplier - 1f) > 0.001f)
                Log.Info($"[Swap] extras active: zRot={SwapZRotationDegrees:0.##} deg, scaleMult={SwapScaleMultiplier:0.###}");
            Vec3 Unit(Vec3 v)
            {
                var len = (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
                return len < 0.0001f ? v : v / len;
            }
            if (!InheritSourceScale)
                frame.rotation = new Mat3(Unit(frame.rotation.s), Unit(frame.rotation.f), Unit(frame.rotation.u));

            if (Math.Abs(SwapZRotationDegrees) > 0.001f)
            {
                var rad = SwapZRotationDegrees * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                Vec3 RotZ(Vec3 v) => new Vec3(v.x * cos - v.y * sin, v.x * sin + v.y * cos, v.z, 0f);
                frame.rotation = new Mat3(RotZ(frame.rotation.s), RotZ(frame.rotation.f), RotZ(frame.rotation.u));
            }

            if (Math.Abs(SwapScaleMultiplier - 1f) > 0.001f && SwapScaleMultiplier > 0.001f)
                frame.rotation = new Mat3(frame.rotation.s * SwapScaleMultiplier,
                                          frame.rotation.f * SwapScaleMultiplier,
                                          frame.rotation.u * SwapScaleMultiplier);

            return frame;
        }

        public class SwapResult
        {
            public bool Success;
            public string Error;
            public GameEntity NewEntity;
            public string OldPrefabName;
            public string NewPrefabName;
            public MatrixFrame Frame;
            // The old entity's frame BEFORE ApplyScaleMode touched it (2026-08-23, "if I apply
            // a Z rotation or scale with swap selected and then undo, the undo doesn't undo the
            // Z or scale"): Frame above already carries the extras, and undo swapping back at
            // the CURRENT frame kept them baked in. Undo restores at this one instead.
            public MatrixFrame OriginalFrame;
            public bool ScaleWasNonDefault;
            // Secondaries placed via FamilyAutoPlacer, if newPrefabName (or a family it belongs
            // to) has any marked Auto-Place On New Instance in PrefabCreatorTool. Empty for the
            // common case where nothing's configured that way.
            public List<GameEntity> AutoPlacedSecondaries = new List<GameEntity>();
            public List<string> AutoPlaceFailed = new List<string>();
            // A secondary placed fine but its configured texture set failed to apply - see
            // FamilyAutoPlacer.AutoPlaceResult.TextureFailed.
            public List<string> AutoPlaceTextureFailed = new List<string>();
            // How many of the old entity's own children got re-parented onto the new instance -
            // see the CONFIRMED LIVE DATA LOSS note below.
            public int ChildrenPreserved;
            // The OLD entity survived RemoveEntity (prefab-instance child, or a live-copy
            // ghost) and now overlaps the new one - surfaced so callers can warn instead of
            // reporting a clean swap. Always false in Add mode (nothing is removed there).
            public bool OriginalNotRemoved;
            // How many of the old entity's children were DROPPED (left on oldEntity, cascade-
            // deleted with it) because the newly-instantiated prefab already brought its own
            // same-named child - see the "obliterates half the object" note below.
            public int ChildrenSuperseded;
            public int MeshOverridesRestored;
        }

        // CONFIRMED LIVE DATA LOSS, now fixed: Scene.RemoveEntity cascades to an entity's whole
        // child subtree. A "giant prefab with nested child columns" swapped through here used to
        // lose every one of those children the instant the old entity was removed - true on the
        // very first forward swap, not just something Undo (which calls this same method to swap
        // back) exposed. Same root cause, same fix, for a second real loss: any per-mesh material/
        // color override manually applied to the OLD entity (e.g. via Material Swap Tool) lived
        // only on that live instance, not in any saved prefab data, so a freshly-instantiated new
        // entity had no way to inherit it - "undo deletes the materials that were overridden."
        // Both are captured from the old entity BEFORE it's removed and carried onto the new one.
        public static SwapResult SwapEntity(Scene scene, GameEntity oldEntity, string newPrefabName)
        {
            if (scene == null || oldEntity == null || string.IsNullOrEmpty(newPrefabName))
                return new SwapResult { Success = false, Error = "Missing scene, entity, or target prefab name." };

            var oldName = oldEntity.Name;

            // Add mode branches BEFORE the reinstantiability check on purpose: that check exists
            // solely so a REMOVAL can be undone by re-instantiating the old name, and add mode
            // removes nothing - so it works on hand-built originals a swap would refuse.
            if (AddModeKeepOriginals)
            {
                var addFrame = ApplyScaleMode(oldEntity.GetGlobalFrame());
                GameEntity added;
                try
                {
                    added = NativeTrace.Around($"GameEntity.Instantiate('{newPrefabName}') [add-mode]",
                        () => GameEntity.Instantiate(scene, newPrefabName, addFrame, true));
                }
                catch (Exception ex)
                {
                    return new SwapResult { Success = false, Error = $"Instantiate threw: {ex.Message}" };
                }
                if (added == null)
                    return new SwapResult { Success = false, Error = $"Prefab '{newPrefabName}' failed to instantiate (unknown prefab name?)." };

                EditorFrameSync.Sync(added);
                var addScale = oldEntity.GetLocalScale();
                return new SwapResult
                {
                    Success = true,
                    NewEntity = added,
                    OldPrefabName = oldName,
                    NewPrefabName = newPrefabName,
                    Frame = addFrame,
                    ScaleWasNonDefault = Math.Abs(addScale.x - 1.0) > 0.001 || Math.Abs(addScale.y - 1.0) > 0.001 || Math.Abs(addScale.z - 1.0) > 0.001,
                };
            }

            // CONFIRMED LIVE DATA LOSS #3 (2026-08-18), "the undo didn't work... mostly it didn't
            // delete the new thing I placed down": a Whole Scene swap doesn't discriminate - it
            // hits every entity, including ones that were never created from a real saved prefab
            // in the first place (a bare "empty"/anchor entity placed directly in the editor, a
            // spawn point, etc.). Its .Name is just whatever arbitrary string it has, not a
            // resolvable prefab name. The forward swap replaced it fine (Instantiate only needs
            // NEW prefabName to be valid), but Undo tries to Instantiate the OLD name back and
            // fails - and used to bail out at that point WITHOUT removing the thing it was trying
            // to undo, leaving the replacement in place forever with no way back. Refusing the
            // swap up front, before anything destructive happens, is the only way to guarantee
            // this is actually undoable. Checked via a real throwaway Instantiate/Remove probe,
            // not a "no mesh components" heuristic - a legitimate composite prefab's own top-level
            // anchor is COMMONLY meshless by design too (all visible geometry lives on its
            // children), so mesh count alone would false-positive on real, valid prefabs.
            if (!IsReinstantiable(scene, oldName, out var oldDefinitionalChildren))
                return new SwapResult
                {
                    Success = false,
                    Error = $"'{oldName}' doesn't look like a real saved prefab (Instantiate can't resolve that name back - " +
                            "probably a raw scene-placed empty/anchor/spawn point, not something a swap could ever be undone " +
                            "for). Refusing to touch it - nothing was changed.",
                };

            var originalFrame = oldEntity.GetGlobalFrame();
            var frame = ApplyScaleMode(originalFrame);
            var scale = oldEntity.GetLocalScale();
            bool nonDefaultScale = Math.Abs(scale.x - 1.0) > 0.001 || Math.Abs(scale.y - 1.0) > 0.001 || Math.Abs(scale.z - 1.0) > 0.001;

            List<GameEntity> existingChildren;
            try { existingChildren = oldEntity.GetChildren().Where(EntitySelector.IsValidEntity).ToList(); }
            catch { existingChildren = new List<GameEntity>(); }

            // DEFINITIONAL CHILDREN DIE WITH THE OLD PREFAB (2026-08-23, "empire to aserai
            // castle tower ... kept the old empire merlons on top of placing new aserai ones",
            // via a swap set): the preserve-extras pass compared old children against the NEW
            // prefab's children by name - on a cross-culture swap the names never match
            // (empire_merlon_* vs aserai_merlon_*), so the OLD prefab's own baked-in parts
            // read as "hand-placed extras" and were carried over. The reinstantiability probe
            // above already builds a pristine instance of the OLD prefab; its child names ARE
            // the definition. Anything on the old entity matching them (unique-number suffix
            // ignored) belongs to the old prefab and cascade-deletes with it; only genuinely
            // hand-added children survive as extras.
            if (oldDefinitionalChildren.Count > 0)
            {
                int before = existingChildren.Count;
                existingChildren = existingChildren
                    .Where(c => !oldDefinitionalChildren.Contains(StripUniqueNameSuffix(c.Name)))
                    .ToList();
                if (before != existingChildren.Count)
                    Log.Info($"[NativeTrace] SwapEntity: {before - existingChildren.Count} of {before} old child(ren) " +
                             "are the old prefab's own definition - removed with it, not carried over.");
            }

            var meshOverrides = CaptureOwnMeshOverrides(oldEntity);
            var metaMeshColors = CaptureOwnMetaMeshColors(oldEntity);

            GameEntity newEntity;
            try
            {
                newEntity = NativeTrace.Around($"GameEntity.Instantiate('{newPrefabName}')", () => GameEntity.Instantiate(scene, newPrefabName, frame, true));
            }
            catch (Exception ex)
            {
                return new SwapResult { Success = false, Error = $"Instantiate threw: {ex.Message}" };
            }

            if (newEntity == null)
                return new SwapResult { Success = false, Error = $"Prefab '{newPrefabName}' failed to instantiate (unknown prefab name?)." };

            var swapResult = FinishSwap(scene, oldEntity, oldName, newEntity, newPrefabName, frame, existingChildren, meshOverrides, metaMeshColors, nonDefaultScale, runAutoPlace: true);
            swapResult.OriginalFrame = originalFrame;
            return swapResult;
        }

        // NEW (2026-08-19): swap directly onto a LIVE, UNSAVED entity's current structure - no
        // saved prefab resource required at all. "Can we skip that requirement to save" - yes:
        // GameEntity.CopyFrom(Scene, GameEntity, bool createPhysics, bool callScriptCallbacks) is
        // a genuine static "clone this exact live instance" factory, confirmed live via reflection
        // against the shipped TaleWorlds.Engine.dll. The earlier "no clone API exists" finding
        // (see the Mirror/Undo "why do I have to save it first" explanations) only checked the
        // Instantiate-by-name overloads and missed this one. Untested in-game as of writing - the
        // first real use of CopyFrom anywhere in this codebase, so treat the first few uses as a
        // live test, same as every other fix tonight.
        //
        // referenceEntity is copied AS-IS, wherever it currently sits - the copy lands at the
        // reference's own transform, not oldEntity's, so it's explicitly re-positioned to
        // oldEntity's frame afterward (Instantiate takes a target frame directly; CopyFrom
        // doesn't, so this is the one real behavioral difference callers need to know about).
        // Everything else - child re-parenting/collision-avoidance, mesh override restore, auto-
        // place, logging - is identical to the Instantiate path via the same FinishSwap helper.
        public static SwapResult SwapEntityToLiveReference(Scene scene, GameEntity oldEntity, GameEntity referenceEntity)
        {
            if (scene == null || oldEntity == null || referenceEntity == null)
                return new SwapResult { Success = false, Error = "Missing scene, entity, or reference entity." };
            if (!EntitySelector.IsValidEntity(referenceEntity))
                return new SwapResult { Success = false, Error = "Reference entity is no longer valid." };

            var oldName = oldEntity.Name;
            var refName = referenceEntity.Name;

            if (!IsReinstantiable(scene, oldName))
                return new SwapResult
                {
                    Success = false,
                    Error = $"'{oldName}' doesn't look like a real saved prefab (Instantiate can't resolve that name back), " +
                            "so a swap onto it could never be undone. Refusing to touch it - nothing was changed.",
                };

            var originalFrame = oldEntity.GetGlobalFrame();
            var frame = ApplyScaleMode(originalFrame);
            var scale = oldEntity.GetLocalScale();
            bool nonDefaultScale = Math.Abs(scale.x - 1.0) > 0.001 || Math.Abs(scale.y - 1.0) > 0.001 || Math.Abs(scale.z - 1.0) > 0.001;

            List<GameEntity> existingChildren;
            try { existingChildren = oldEntity.GetChildren().Where(EntitySelector.IsValidEntity).ToList(); }
            catch { existingChildren = new List<GameEntity>(); }

            var meshOverrides = CaptureOwnMeshOverrides(oldEntity);
            var metaMeshColors = CaptureOwnMetaMeshColors(oldEntity);

            GameEntity newEntity;
            try
            {
                newEntity = NativeTrace.Around($"GameEntity.CopyFrom('{refName}')", () => GameEntity.CopyFrom(scene, referenceEntity, true, true));
            }
            catch (Exception ex)
            {
                return new SwapResult { Success = false, Error = $"CopyFrom threw: {ex.Message}" };
            }
            if (newEntity == null)
                return new SwapResult { Success = false, Error = $"CopyFrom('{refName}') returned null." };

            // COMPOSITE REFERENCES COPY THEIR WHOLE TREE (2026-08-23, "when i did swap selected
            // with live reference, it didn't work" - the trace showed newEntityOwnChildren=0):
            // CopyFrom copies the ROOT but not children that were ATTACHED to it at runtime
            // (re-parented survivors of an earlier swap, hand-added decorations - on a worked-on
            // composite that is ALL the visible geometry), so the copy arrived empty and
            // invisible. Any reference child missing from the copy is cloned and attached
            // explicitly, recursively, BEFORE the copy is moved into place - each clone lands
            // where the reference's child sits, so the assembly then travels with the root.
            try { CopyMissingChildren(scene, referenceEntity, newEntity, 0); }
            catch (Exception ex) { Log.Warn($"LivePrefabSwapper: child copy from '{refName}' incomplete: {ex.Message}"); }

            // Same flag clear as PrefabDistributor.CloneSourceAt, for the same confirmed reason:
            // CopyFrom marks its product "DontSaveToScene, NonModifiableFromEditor" (a runtime
            // entity) - the first makes the swap vanish from the saved scene, the second leaves
            // it unselectable in the editor. RECURSIVE (2026-08-23): children carry their own
            // flags, and viewport picking lands on child meshes.
            try { PrefabDistributor.ClearRuntimeFlagsInTree(newEntity, 0); }
            catch (Exception flagEx) { Log.Warn($"LivePrefabSwapper: flag adjust failed for copy of '{refName}': {flagEx.Message}"); }

            // (AttachEntity removed in the 2026-08-23 revert - see CloneSourceAt.) Tree refresh
            // and ready-state are the benign remainder.
            try { newEntity.SetReadyToRender(true); } catch { }
            try { TaleWorlds.MountAndBlade.MBEditor.UpdateSceneTree(true); } catch { }

            try { newEntity.SetGlobalFrame(ref frame, true); EditorFrameSync.Sync(newEntity); }
            catch (Exception ex)
            {
                Log.Warn($"LivePrefabSwapper: failed to reposition live-copied entity from '{refName}' onto '{oldName}''s spot: {ex.Message}");
            }

            // Add mode: the copy is placed and positioned - stop here, originals untouched.
            if (AddModeKeepOriginals)
                return new SwapResult
                {
                    Success = true,
                    NewEntity = newEntity,
                    OldPrefabName = oldName,
                    NewPrefabName = refName,
                    Frame = frame,
                };

            // No real prefab name behind a live copy - the reference entity's own current Name is
            // stored for logging/History display only. Undo within THIS session still works fully
            // (the fast path uses the live NewEntity reference, swapping back to OldPrefabName via
            // the normal Instantiate path regardless of how NewEntity was created) - only the
            // cross-session log-based fallback (position+name re-matching) is best-effort here,
            // same limitation the rest of that fallback already has.
            //
            // NOTHING FROM THE OLD ENTITY RIDES ALONG (2026-08-23, "instead of swapping things
            // out ... it like duplicated stuff"): a live-reference swap means "an exact copy of
            // the reference, here" - the copy's tree is complete by construction now. Re-parenting
            // the old entity's children on top (name-swap's preserve-extras heuristic) DOUBLED
            // every part on a like-for-like variant swap: the clones carry auto-suffixed unique
            // names, so the supersede-by-name check never matched, and preserved=18 stacked onto
            // the reference's own cloned 18. Old children are removed WITH the old entity, and
            // the old entity's mesh/color overrides are not re-applied either - the reference's
            // own look is the entire point of this mode.
            var liveResult = FinishSwap(scene, oldEntity, oldName, newEntity, refName, frame,
                existingChildren: new List<GameEntity>(),
                meshOverrides: new List<MeshOverride>(),
                metaMeshColors: new List<MetaMeshColorOverride>(),
                nonDefaultScale, runAutoPlace: false);
            liveResult.OriginalFrame = originalFrame;
            return liveResult;
        }

        // Clones every reference child the copy is missing (matched by name) and attaches it at
        // the reference child's own world spot - AddChild(child, true) localizes the frame, so a
        // later root reposition carries the whole assembly. Recursive with a depth guard; see
        // the call site in SwapEntityToLiveReference for why this exists.
        //
        // RECURSES INTO MATCHED PAIRS TOO (2026-08-23, second round: "overriding the broken
        // prefabs ... was broken" with the trace showing cloned=0 and only the copy's 6
        // definitional children): the first version only descended into children it had CLONED
        // - a child the copy already carried by name was skipped wholesale, so any
        // GRANDCHILDREN under it that CopyFrom left behind stayed missing. Matched
        // source/copy child pairs are now compared level by level all the way down.
        private static int CopyMissingChildren(Scene scene, GameEntity source, GameEntity copy, int depth)
        {
            if (depth > 8) return 0;

            List<GameEntity> srcChildren;
            try { srcChildren = source.GetChildren().Where(EntitySelector.IsValidEntity).ToList(); }
            catch (Exception ex)
            {
                Log.Warn($"LivePrefabSwapper: could not enumerate reference children of '{source?.Name}': {ex.Message}");
                return 0;
            }
            if (srcChildren.Count == 0) return 0;

            var copyChildByName = new Dictionary<string, GameEntity>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var c in copy.GetChildren().Where(EntitySelector.IsValidEntity))
                    if (!string.IsNullOrEmpty(c.Name) && !copyChildByName.ContainsKey(c.Name))
                        copyChildByName[c.Name] = c;
            }
            catch (Exception ex)
            {
                Log.Warn($"LivePrefabSwapper: could not enumerate copy children of '{copy?.Name}': {ex.Message}");
            }

            int cloned = 0;
            foreach (var child in srcChildren)
            {
                if (!string.IsNullOrEmpty(child.Name) && copyChildByName.TryGetValue(child.Name, out var matched))
                {
                    // The copy already has this child - but ITS subtree may still be incomplete.
                    cloned += CopyMissingChildren(scene, child, matched, depth + 1);
                    continue;
                }

                GameEntity childCopy = null;
                try { childCopy = GameEntity.CopyFrom(scene, child, true, true); }
                catch (Exception ex) { Log.Warn($"LivePrefabSwapper: could not copy child '{child.Name}': {ex.Message}"); }
                if (childCopy == null) continue;

                try
                {
                    var gf = child.GetGlobalFrame();
                    childCopy.SetGlobalFrame(ref gf, true);
                    copy.AddChild(childCopy, true);
                    cloned++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"LivePrefabSwapper: could not attach copied child '{child.Name}': {ex.Message}");
                    continue;
                }

                cloned += CopyMissingChildren(scene, child, childCopy, depth + 1);
            }

            if (depth == 0)
                Log.Info($"[NativeTrace] live-ref copy: source has {srcChildren.Count} top-level child(ren), " +
                         $"cloned {cloned} entit(y/ies) across the tree that CopyFrom left behind.");
            return cloned;
        }

        private static SwapResult FinishSwap(Scene scene, GameEntity oldEntity, string oldName, GameEntity newEntity,
            string newPrefabLabel, MatrixFrame frame, List<GameEntity> existingChildren, List<MeshOverride> meshOverrides,
            List<MetaMeshColorOverride> metaMeshColors, bool nonDefaultScale, bool runAutoPlace)
        {
            // CONFIRMED LIVE DATA LOSS #2 (2026-08-18), "obliterates half the object" on a big
            // composite prefab: Instantiate() doesn't just create a bare entity - if newPrefabName
            // is ITSELF a composite prefab (parent anchor + its own baked-in child entities, the
            // norm for real assets here, not the exception), the freshly-created newEntity already
            // arrives with its own full set of children matching the NEW prefab's saved definition.
            // The old code below re-parented EVERY one of oldEntity's children onto newEntity
            // unconditionally - for any child whose name happened to match one newEntity already
            // had (a structural part both prefab variants share, e.g. "column_a"), this collided
            // with newEntity's own freshly-instantiated child of the same name. Only children with
            // NO counterpart among newEntity's own children - hand-placed decorations, Family
            // Auto-Place secondaries, anything added on top of the original instance after it was
            // first placed - are genuinely "extra" and need carrying over. A child that DOES match
            // one of newEntity's own is already covered by the new prefab's own version; leaving it
            // on oldEntity (where it gets cascade-deleted along with oldEntity below, same as
            // before this whole fix existed) is the correct outcome, not a bug.
            HashSet<string> newEntityOwnChildNames;
            try
            {
                newEntityOwnChildNames = new HashSet<string>(
                    newEntity.GetChildren().Where(EntitySelector.IsValidEntity).Select(c => c.Name ?? ""),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch { newEntityOwnChildNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

            // Re-parent BEFORE removing the old entity - AddChild detaches each child from its
            // current parent first, so by the time RemoveEntity runs below, oldEntity has none of
            // these left to cascade-delete. autoLocalizeFrame keeps every child visually in place
            // regardless of any frame difference between the old and new prefab's own origins.
            int childrenPreserved = 0;
            int childrenSuperseded = 0;
            foreach (var child in existingChildren)
            {
                if (!string.IsNullOrEmpty(child.Name) && newEntityOwnChildNames.Contains(child.Name))
                {
                    childrenSuperseded++;
                    continue;
                }
                try
                {
                    NativeTrace.Around($"AddChild('{child.Name}' -> '{newPrefabLabel}')", () => newEntity.AddChild(child, true));
                    childrenPreserved++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"LivePrefabSwapper: failed to re-parent child '{child.Name}' onto swapped-in '{newPrefabLabel}': {ex.Message}");
                }
            }

            Log.Info($"[NativeTrace] SwapEntity children: oldChildren={existingChildren.Count} newEntityOwnChildren={newEntityOwnChildNames.Count} preserved={childrenPreserved} superseded={childrenSuperseded}");

            NativeTrace.Around($"Scene.RemoveEntity('{oldName}')", () => scene.RemoveEntity(oldEntity, 0));

            // VERIFIED, not assumed (2026-08-23, "swap sometimes doesn't remove the original"):
            // RemoveEntity can silently fail - known cases are pieces living INSIDE placed
            // prefab instances (the editor guards their removal) and unregistered live-copy
            // ghosts - and the swap then reported success while old and new overlapped with
            // z-fighting. Now the survivor is detected, logged, and carried on the result so
            // the status line can say so.
            bool originalNotRemoved = false;
            try
            {
                originalNotRemoved = oldEntity != null && oldEntity.Pointer != UIntPtr.Zero && oldEntity.IsInEditorScene();
            }
            catch { }
            if (originalNotRemoved)
                Log.Warn($"[Swap] '{oldName}' was NOT removed (still in the scene after RemoveEntity) - it overlaps " +
                         $"the swapped-in '{newPrefabLabel}'. Known causes: pieces inside placed prefab instances, or " +
                         "unregistered live-copy ghosts. ADD mode avoids removal entirely; delete the survivor by hand.");

            int meshOverridesRestored = ApplyMeshOverrides(newEntity, meshOverrides) + ApplyMetaMeshColors(newEntity, metaMeshColors);

            // Auto-Place is keyed by a real prefab/family name (PrefabCreatorTool's own family
            // data) - doesn't apply to a live-copied entity with no saved prefab identity behind
            // it, so the CopyFrom path skips this entirely (runAutoPlace: false) rather than
            // looking up "family membership" for a name that was never really a prefab.
            var autoPlace = runAutoPlace
                ? FamilyAutoPlacer.PlaceAutoSecondaries(scene, newPrefabLabel, frame)
                : new FamilyAutoPlacer.AutoPlaceResult();

            return new SwapResult
            {
                Success = true,
                OriginalNotRemoved = originalNotRemoved,
                NewEntity = newEntity,
                OldPrefabName = oldName,
                NewPrefabName = newPrefabLabel,
                Frame = frame,
                ScaleWasNonDefault = nonDefaultScale,
                AutoPlacedSecondaries = autoPlace.Placed,
                AutoPlaceFailed = autoPlace.Failed,
                AutoPlaceTextureFailed = autoPlace.TextureFailed,
                ChildrenPreserved = childrenPreserved,
                ChildrenSuperseded = childrenSuperseded,
                MeshOverridesRestored = meshOverridesRestored,
            };
        }

        // Real probe, not a guess: tries to Instantiate the name and immediately removes the
        // throwaway result. GameEntity.Instantiate already reliably returns null for an unknown
        // prefab name (same check the normal newPrefabName path relies on below) - this just
        // applies that exact same resolution logic to the OLD entity's name up front.
        private static bool IsReinstantiable(Scene scene, string name) => IsReinstantiable(scene, name, out _);

        // The probe doubles as the source of the prefab's DEFINITIONAL child names (unique
        // suffix stripped) - what SwapEntity uses to tell the old prefab's own parts from
        // hand-added extras (see the merlon note at its call site).
        private static bool IsReinstantiable(Scene scene, string name, out HashSet<string> definitionalChildNames)
        {
            definitionalChildNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(name)) return false;
            GameEntity probe;
            try
            {
                probe = GameEntity.Instantiate(scene, name, MatrixFrame.Identity, false);
            }
            catch
            {
                return false;
            }
            if (probe == null) return false;
            try
            {
                foreach (var child in probe.GetChildren())
                    if (child != null && !string.IsNullOrEmpty(child.Name))
                        definitionalChildNames.Add(StripUniqueNameSuffix(child.Name));
            }
            catch { }
            try { scene.RemoveEntity(probe, 0); }
            catch (Exception ex) { Log.Warn($"LivePrefabSwapper: failed to clean up reinstantiability probe for '{name}': {ex.Message}"); }
            return true;
        }

        // The editor keeps entity names unique by appending _NN ("empire_merlon_a_02") -
        // stripped for definition-vs-instance name comparisons.
        private static string StripUniqueNameSuffix(string name) =>
            System.Text.RegularExpressions.Regex.Replace(name ?? "", @"_\d+$", "");

        private class MeshOverride
        {
            public string SlotName;
            public string Material;
            public uint Color;
        }

        private class MetaMeshColorOverride
        {
            public string MetaMeshName;
            public uint Factor1;
        }

        private const uint DefaultWhite = 0xFFFFFFFFu;

        // CONFIRMED BUG, fixed 2026-08-19 (same class of mistake as OverrideFillEngine's own fix
        // the same day): this used to only capture/restore Mesh.Color (a third, rarely-used per-
        // individual-mesh tint), never MetaMesh.Factor1 - the actual per-LOD-slot color mechanism
        // MaterialSwapEngine.Apply's own rule ColorFactor uses ("SetFactor1" in tool.log). Material
        // NAME preservation (mesh.SetMaterial, below) was always correct; only the color side of
        // "undo deletes the materials that were overridden" was silently incomplete - a real
        // Factor1-based color override would reset to the new prefab's default on swap/undo
        // without this. Matched by MetaMesh name (meta.GetName()), same "best-effort, no match =
        // nothing restored" convention as the mesh-slot version. Skips slots already at default
        // white - nothing to preserve there.
        private static List<MetaMeshColorOverride> CaptureOwnMetaMeshColors(GameEntity entity)
        {
            var result = new List<MetaMeshColorOverride>();
            try
            {
                for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                {
                    var meta = entity.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    var name = meta.GetName();
                    if (string.IsNullOrEmpty(name)) continue;
                    var factor1 = meta.GetFactor1();
                    if (factor1 == DefaultWhite) continue;
                    result.Add(new MetaMeshColorOverride { MetaMeshName = name, Factor1 = factor1 });
                }
            }
            catch (Exception ex)
            {
                Log.Warn("LivePrefabSwapper: failed to capture MetaMesh colors from old entity: " + ex.Message);
            }
            return result;
        }

        private static int ApplyMetaMeshColors(GameEntity target, List<MetaMeshColorOverride> overrides)
        {
            if (overrides.Count == 0) return 0;
            var byName = overrides
                .GroupBy(o => o.MetaMeshName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int restored = 0;
            try
            {
                for (int m = 0; m < target.MultiMeshComponentCount; m++)
                {
                    var meta = target.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    var name = meta.GetName();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!byName.TryGetValue(name, out var ov)) continue;

                    meta.SetFactor1(ov.Factor1);
                    restored++;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("LivePrefabSwapper: failed to restore MetaMesh colors onto new entity: " + ex.Message);
            }
            return restored;
        }

        // Reads the OLD entity's own mesh slots (not its children - those are re-parented whole,
        // keeping whatever materials they already had) so a manually-applied material/color
        // override can be carried onto the matching-named slot on the new instance. Best-effort by
        // design: a genuinely different prefab variant may not share slot names at all, in which
        // case nothing matches and nothing is restored - no worse than before this fix existed.
        private static List<MeshOverride> CaptureOwnMeshOverrides(GameEntity entity)
        {
            var result = new List<MeshOverride>();
            try
            {
                for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                {
                    var meta = entity.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var mesh = meta.GetMeshAtIndex(i);
                        if (mesh == null || string.IsNullOrEmpty(mesh.Name)) continue;
                        var material = mesh.GetMaterial()?.Name;
                        result.Add(new MeshOverride { SlotName = mesh.Name, Material = material, Color = mesh.Color });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("LivePrefabSwapper: failed to capture mesh overrides from old entity: " + ex.Message);
            }
            return result;
        }

        private static int ApplyMeshOverrides(GameEntity target, List<MeshOverride> overrides)
        {
            if (overrides.Count == 0) return 0;
            var bySlot = overrides
                .Where(o => !string.IsNullOrEmpty(o.SlotName))
                .GroupBy(o => o.SlotName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int restored = 0;
            try
            {
                for (int m = 0; m < target.MultiMeshComponentCount; m++)
                {
                    var meta = target.GetMetaMesh(m);
                    if (meta == null || !meta.IsValid) continue;
                    for (int i = 0; i < meta.MeshCount; i++)
                    {
                        var mesh = meta.GetMeshAtIndex(i);
                        if (mesh == null || string.IsNullOrEmpty(mesh.Name)) continue;
                        if (!bySlot.TryGetValue(mesh.Name, out var ov)) continue;

                        if (!string.IsNullOrWhiteSpace(ov.Material)) mesh.SetMaterial(ov.Material);
                        mesh.Color = ov.Color;
                        restored++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("LivePrefabSwapper: failed to restore mesh overrides onto new entity: " + ex.Message);
            }
            return restored;
        }

        // Batch form used by BrokenPrefabFixer/LOD Substitution/F6 - swaps every (entity, newPrefab)
        // pair, logging each as a ChangeLogEntry via the Old/NewMeshColor-adjacent free-text fields
        // aren't a fit for a prefab swap, so batches are logged by the caller instead; this just
        // returns the per-entity results so the caller can build whatever log/undo entry fits.
        public static List<SwapResult> SwapMany(Scene scene, List<(GameEntity entity, string newPrefab)> pairs)
        {
            var results = new List<SwapResult>();
            foreach (var (entity, newPrefab) in pairs)
                results.Add(SwapEntity(scene, entity, newPrefab));
            return results;
        }
    }
}
