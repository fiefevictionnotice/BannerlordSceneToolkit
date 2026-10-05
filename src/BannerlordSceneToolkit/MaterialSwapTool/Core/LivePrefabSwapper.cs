using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.Core
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
        public class SwapResult
        {
            public bool Success;
            public string Error;
            public GameEntity NewEntity;
            public string OldPrefabName;
            public string NewPrefabName;
            public MatrixFrame Frame;
            public bool ScaleWasNonDefault;
            // Secondaries placed via FamilyAutoPlacer, if newPrefabName (or a family it belongs
            // to) has any marked Auto-Place On New Instance in PrefabCreatorTool. Empty for the
            // common case where nothing's configured that way.
            public List<GameEntity> AutoPlacedSecondaries = new List<GameEntity>();
            public List<string> AutoPlaceFailed = new List<string>();
            // A secondary placed fine but its configured texture set failed to apply - see
            // FamilyAutoPlacer.AutoPlaceResult.TextureFailed.
            public List<string> AutoPlaceTextureFailed = new List<string>();
        }

        public static SwapResult SwapEntity(Scene scene, GameEntity oldEntity, string newPrefabName)
        {
            if (scene == null || oldEntity == null || string.IsNullOrEmpty(newPrefabName))
                return new SwapResult { Success = false, Error = "Missing scene, entity, or target prefab name." };

            var frame = oldEntity.GetGlobalFrame();
            var oldName = oldEntity.Name;
            var scale = oldEntity.GetLocalScale();
            bool nonDefaultScale = Math.Abs(scale.x - 1.0) > 0.001 || Math.Abs(scale.y - 1.0) > 0.001 || Math.Abs(scale.z - 1.0) > 0.001;

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

            NativeTrace.Around($"Scene.RemoveEntity('{oldName}')", () => scene.RemoveEntity(oldEntity, 0));

            var autoPlace = FamilyAutoPlacer.PlaceAutoSecondaries(scene, newPrefabName, frame);

            return new SwapResult
            {
                Success = true,
                NewEntity = newEntity,
                OldPrefabName = oldName,
                NewPrefabName = newPrefabName,
                Frame = frame,
                ScaleWasNonDefault = nonDefaultScale,
                AutoPlacedSecondaries = autoPlace.Placed,
                AutoPlaceFailed = autoPlace.Failed,
                AutoPlaceTextureFailed = autoPlace.TextureFailed,
            };
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
