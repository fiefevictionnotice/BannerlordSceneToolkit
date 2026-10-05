using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabSwapperTool.Core
{
    // Duplicates an entity FAITHFULLY - the prefab plus whatever was changed on that particular
    // instance afterwards.
    //
    // WHY THIS EXISTS. The only clone route the engine offers is
    // GameEntity.Instantiate(scene, prefabName, frame, true), which rebuilds the prefab from its
    // definition. Anything done to the instance since - a material swapped on one mesh slot, a
    // colour tint, the work this whole toolkit exists to do - is not in the prefab and does not
    // come along. A "duplicate" that silently reverted your overrides would be worse than no
    // duplicate at all, because the copy looks plausible and is wrong.
    //
    // So after instantiating, the source and the clone are walked in lockstep and every per-mesh
    // material name and Mesh.Color is copied across.
    //
    // WHAT IT STILL CANNOT DO, and says so rather than pretending:
    //   - an entity with no prefab name cannot be cloned at all (nothing to instantiate from)
    //   - if the two trees do not line up mesh-for-mesh, the mismatch is reported instead of
    //     copying materials onto whatever happened to sit at that index
    public static class EntityCloner
    {
        public class CloneResult
        {
            public GameEntity Instance;
            public string Error;
            public int SlotsCopied;
            public int Mismatches;
            public bool Success => Instance != null;
        }

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        public static CloneResult Clone(Scene scene, GameEntity source, MatrixFrame targetFrame)
        {
            var result = new CloneResult();

            if (scene == null || !Alive(source)) { result.Error = "No scene or source entity."; return result; }

            string prefabName = null;
            try { prefabName = source.GetPrefabName(); } catch { }
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                result.Error = $"'{source.Name}' has no prefab name - there is nothing to instantiate a copy from.";
                return result;
            }

            GameEntity instance;
            try { instance = GameEntity.Instantiate(scene, prefabName.Trim(), targetFrame, true); }
            catch (Exception ex) { result.Error = $"'{prefabName}': {ex.Message}"; return result; }
            if (instance == null) { result.Error = $"'{prefabName}': instantiate returned null (unknown prefab?)"; return result; }

            result.Instance = instance;
            CopyMeshOverrides(source, instance, result);

            try { EditorFrameSync.Sync(instance); } catch { }
            return result;
        }

        // Walks both trees in the same order and copies per-mesh material and colour.
        private static void CopyMeshOverrides(GameEntity source, GameEntity clone, CloneResult result)
        {
            var from = Flatten(source);
            var to = Flatten(clone);

            if (from.Count != to.Count)
            {
                // Not fatal - the copy still exists and is prefab-correct - but the overrides
                // cannot be trusted to land on the right meshes, so nothing is copied at all
                // rather than copying them onto the wrong ones.
                result.Mismatches++;
                MaterialSwapTool.Log.Warn(
                    $"[Clone] '{source.Name}' has {from.Count} entities but its copy has {to.Count} - " +
                    "skipping override copy rather than risk applying them to the wrong meshes.");
                return;
            }

            for (int e = 0; e < from.Count; e++)
            {
                var a = from[e];
                var b = to[e];
                if (!Alive(a) || !Alive(b)) continue;

                int slots;
                try { slots = Math.Min(a.MultiMeshComponentCount, b.MultiMeshComponentCount); }
                catch { continue; }

                for (int m = 0; m < slots; m++)
                {
                    MetaMesh ma = null, mb = null;
                    try { ma = a.GetMetaMesh(m); mb = b.GetMetaMesh(m); } catch { }
                    if (ma == null || mb == null || !ma.IsValid || !mb.IsValid) continue;

                    int count;
                    try { count = Math.Min(ma.MeshCount, mb.MeshCount); }
                    catch { continue; }

                    if (ma.MeshCount != mb.MeshCount) result.Mismatches++;

                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            var sourceMesh = ma.GetMeshAtIndex(i);
                            var cloneMesh = mb.GetMeshAtIndex(i);
                            if (sourceMesh == null || cloneMesh == null) continue;

                            var materialName = sourceMesh.GetMaterial()?.Name;
                            var cloneMaterialName = cloneMesh.GetMaterial()?.Name;

                            // Only write when it actually differs - SetMaterial is a native call and
                            // a prefab-fresh clone already carries the prefab's own materials.
                            if (!string.IsNullOrEmpty(materialName) &&
                                !string.Equals(materialName, cloneMaterialName, StringComparison.OrdinalIgnoreCase))
                            {
                                cloneMesh.SetMaterial(materialName);
                                result.SlotsCopied++;
                            }

                            var color = sourceMesh.Color;
                            if (cloneMesh.Color != color)
                            {
                                cloneMesh.Color = color;
                                result.SlotsCopied++;
                            }
                        }
                        catch (Exception ex)
                        {
                            MaterialSwapTool.Log.Warn($"[Clone] slot {m}/{i} copy failed: {ex.Message}");
                        }
                    }
                }
            }
        }

        // Depth-first, parents before children - the same order for both trees is what makes
        // index-for-index pairing meaningful.
        private static List<GameEntity> Flatten(GameEntity root)
        {
            var list = new List<GameEntity>();
            void Walk(GameEntity e, int depth)
            {
                if (!Alive(e) || depth > 64) return;
                list.Add(e);
                List<GameEntity> children = null;
                try { children = new List<GameEntity>(e.GetChildren()); } catch { }
                if (children == null) return;
                foreach (var c in children) Walk(c, depth + 1);
            }
            Walk(root, 0);
            return list;
        }
    }
}
