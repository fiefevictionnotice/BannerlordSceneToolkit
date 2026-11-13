using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.Core
{
    // Moves a prefab's ORIGIN without moving the prefab.
    //
    // The problem it solves: a prefab whose origin sits somewhere unhelpful - out in space, at one
    // corner, wherever it happened to land when the thing was built - misbehaves in every tool
    // that reasons about position. Mirroring reflects the origin, distribution spaces from it,
    // rotation turns about it. A bad origin makes all of those produce results that look wrong
    // when the maths was right, which is exactly the confusion that produced this feature.
    //
    // HOW IT WORKS. The origin of a composite prefab is its ROOT entity's frame. So: move the root
    // to the anchor point, then put every child back where it was in world space. The prefab does
    // not visibly move; only the point everything else measures from does.
    //
    // Children are restored by GLOBAL frame, captured before the parent moves. Children are stored
    // as local offsets from the parent, so moving the parent drags them along - re-applying their
    // original global frames afterwards is what cancels that out. Child prefabs keep their own
    // origins untouched for the same reason: nothing writes to them except this restore, which
    // puts them back exactly where they were.
    public static class OriginToAnchor
    {
        public class Result
        {
            public bool Success;
            public string Message;
            public bool NeedsConfirmation;   // the root is not an empty - caller should ask first
            public string ConfirmationPrompt;
        }

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // An "empty" here means a pure organisational node: no mesh of its own and no physics
        // body. Those exist only to be a parent, so moving one is free. Anything with geometry or
        // collision is a real object in the scene and moving it changes what the player sees or
        // walks into - hence the confirmation.
        public static bool IsEmpty(GameEntity e, out string why)
        {
            why = null;
            if (!Alive(e)) { why = "entity is gone"; return false; }

            int meshes = CountRealMeshSlots(e);

            bool hasPhysics = false;
            try { hasPhysics = e.BodyFlag != BodyFlags.None; } catch { }

            if (meshes > 0 && hasPhysics) { why = $"has {meshes} mesh slot(s) and a physics body"; return false; }
            if (meshes > 0) { why = $"has {meshes} mesh slot(s)"; return false; }
            if (hasPhysics) { why = "has a physics body"; return false; }
            return true;
        }

        // EDITOR-ONLY MARKER MESHES DON'T COUNT (2026-11-12, "the F5 axis / side set origins
        // button no longer works": every click logged moved=0 skippedMeshed=1). The Fief scaffold
        // prefabs (and ~170 others in Native/SandBoxCore) put an editor_plane_low mesh with the
        // editor_gizmo material on their root - a pick handle the game never draws. It read as
        // "has 1 mesh slot", so those roots were refused although they are organisational empties.
        // A slot is ignored only when EVERY mesh in it uses editor_gizmo: editor_cube etc. are
        // also used with real materials (stone_a, rock_u, ... ~20k of them) and those ARE visible.
        // The marker moves with the origin, which is where a pick handle belongs anyway.
        public const string EditorOnlyMaterial = "editor_gizmo";

        public static int CountRealMeshSlots(GameEntity e)
        {
            int count = 0;
            int slots = 0;
            try { slots = e.MultiMeshComponentCount; } catch { return 0; }
            for (int m = 0; m < slots; m++)
            {
                bool editorOnly = false;
                try
                {
                    var meta = e.GetMetaMesh(m);
                    if (meta != null && meta.IsValid && meta.MeshCount > 0)
                    {
                        editorOnly = true;
                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mat = meta.GetMeshAtIndex(i)?.GetMaterial()?.Name;
                            if (!string.Equals(mat, EditorOnlyMaterial, StringComparison.OrdinalIgnoreCase)) { editorOnly = false; break; }
                        }
                    }
                }
                catch { editorOnly = false; }
                if (!editorOnly) count++;
            }
            return count;
        }

        // Pass force:true after the user has confirmed moving a non-empty root.
        public static Result MoveOriginTo(GameEntity root, Vec3 anchorPoint, bool force)
        {
            var result = new Result();

            if (!Alive(root)) { result.Message = "Nothing selected, or the entity is gone."; return result; }

            if (!IsEmpty(root, out var why) && !force)
            {
                result.NeedsConfirmation = true;
                result.ConfirmationPrompt =
                    $"'{root.Name}' is not an empty - it {why}. Moving it moves a real object in the scene, " +
                    "not just an organisational point. Its children will stay exactly where they are either " +
                    "way. Move it anyway?";
                result.Message = result.ConfirmationPrompt;
                return result;
            }

            List<GameEntity> children;
            try { children = new List<GameEntity>(root.GetChildren()); }
            catch (Exception ex) { result.Message = "Could not read children: " + ex.Message; return result; }

            // Captured BEFORE the parent moves. This is the whole trick.
            var childFrames = new List<KeyValuePair<GameEntity, MatrixFrame>>();
            foreach (var c in children)
            {
                if (!Alive(c)) continue;
                try { childFrames.Add(new KeyValuePair<GameEntity, MatrixFrame>(c, c.GetGlobalFrame())); }
                catch (Exception ex) { Log.Warn($"[OriginToAnchor] could not read child '{c.Name}': {ex.Message}"); }
            }

            MatrixFrame rootFrame;
            try { rootFrame = root.GetGlobalFrame(); }
            catch (Exception ex) { result.Message = "Could not read the entity's frame: " + ex.Message; return result; }

            PrefabSwapperTool.Core.ManipulationWatcher.SuppressSelfEdit(1.5f);

            var before = rootFrame.origin;

            // Origin only - the rotation is deliberately left alone. Re-orienting the root would
            // rotate the local axes every child offset is expressed in, and this is a "move the
            // measuring point" operation, not a re-orientation.
            rootFrame.origin = anchorPoint;

            try
            {
                root.SetGlobalFrame(ref rootFrame, true);
                EditorFrameSync.Sync(root);
            }
            catch (Exception ex) { result.Message = "Could not move the entity: " + ex.Message; return result; }

            int restored = 0, failed = 0;
            foreach (var kv in childFrames)
            {
                if (!Alive(kv.Key)) continue;
                var frame = kv.Value;
                try
                {
                    kv.Key.SetGlobalFrame(ref frame, true);
                    EditorFrameSync.Sync(kv.Key);
                    restored++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Warn($"[OriginToAnchor] could not restore child '{kv.Key.Name}': {ex.Message}");
                }
            }

            result.Success = true;
            result.Message =
                $"Origin of '{root.Name}' moved from ({before.x:0.##}, {before.y:0.##}, {before.z:0.##}) " +
                $"to ({anchorPoint.x:0.##}, {anchorPoint.y:0.##}, {anchorPoint.z:0.##}). " +
                $"{restored} child(ren) held in place." +
                (failed > 0 ? $" {failed} could not be restored - see tool.log." : "");

            Log.Info($"[OriginToAnchor] '{root.Name}' origin {before} -> {anchorPoint}, children restored={restored} failed={failed}");
            return result;
        }
    }
}
