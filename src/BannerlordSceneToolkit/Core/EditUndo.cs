using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace BannerlordSceneToolkit
{
    // ONE undo stack shared by every tool in this assembly.
    //
    // Deliberately shared rather than one per tool: the operations undo each other's mess in
    // practice (generate a pile with the Prefab Creator, rotate it with the Prefab Swapper) and
    // three separate stacks would make "undo the last thing I did" depend on which panel you had
    // open. It lives in the root namespace next to ToolToggles for the same reason.
    //
    // WHAT CAN AND CANNOT BE ON THIS STACK.
    //
    // Only operations whose inverse is exactly recoverable from what we captured:
    //   Frames  - the operation moved existing entities; undo restores their frames.
    //   Created - the operation instantiated entities; undo deletes exactly those.
    //   Tags    - the operation added a tag; undo removes that tag from those entities.
    //
    // DELETIONS ARE NOT UNDOABLE HERE and never will be. Delete Interior Entities and Break
    // Prefab Links destroy data that only the scene backup can bring back; putting them on this
    // stack would give a button that appears to work and silently does not. Remove Physics is the
    // same trap for a subtler reason: BodyFlag is only a flag, while RemovePhysics discards the
    // body itself, so restoring the flag would not restore collision.
    //
    // Entities are held as LIVE REFERENCES, not name+position. For a frame restore that is not a
    // preference but a requirement: position is the thing that changed, so re-finding by position
    // is exactly what would land on the wrong entity. References that die (deletion, scene switch)
    // are skipped and counted rather than dereferenced.
    public static class EditUndo
    {
        // Logs to MaterialSwapTool's tool.log rather than the root one: that is the file that
        // already carries [SelectInEditor], [GoTo], [Boundary] and the backup lines, so undo
        // history sits with the rest of the diagnostics instead of in a near-empty second file.

        private enum StepKind { Frames, Created, Tags }

        private class Step
        {
            public StepKind Kind;
            public string Label;
            public DateTime TakenAt;
            public List<KeyValuePair<GameEntity, MatrixFrame>> Frames;
            public List<GameEntity> Entities;
            public string Tag;
        }

        // Ten covers "I did that four times, walk it back" without holding dead references
        // indefinitely.
        private const int MaxDepth = 10;

        private static readonly List<Step> Stack = new List<Step>();

        public static int Depth => Stack.Count;

        // Drives the button label, so it says what it will actually undo instead of "Undo".
        public static string NextLabel => Stack.Count == 0 ? null : Stack[Stack.Count - 1].Label;

        private static void Push(Step step)
        {
            Stack.Add(step);
            while (Stack.Count > MaxDepth) Stack.RemoveAt(0);
            MaterialSwapTool.Log.Info($"[EditUndo] captured '{step.Label}' ({step.Kind}), depth={Stack.Count}");
        }

        private static bool Alive(GameEntity e) => e != null && e.Pointer != UIntPtr.Zero;

        // Call BEFORE an operation that MOVES existing entities.
        public static void CaptureFrames(string label, IEnumerable<GameEntity> entities)
        {
            if (entities == null) return;

            var frames = new List<KeyValuePair<GameEntity, MatrixFrame>>();
            foreach (var e in entities)
            {
                if (!Alive(e)) continue;
                try { frames.Add(new KeyValuePair<GameEntity, MatrixFrame>(e, e.GetGlobalFrame())); }
                catch (Exception ex) { MaterialSwapTool.Log.Warn($"[EditUndo] could not read '{e.Name}': {ex.Message}"); }
            }
            if (frames.Count == 0) return;

            Push(new Step { Kind = StepKind.Frames, Label = label, TakenAt = DateTime.Now, Frames = frames });
        }

        // Call AFTER an operation that CREATED entities, with everything it made. The anchor
        // belongs in here too, or undo leaves an empty anchor behind looking like a real object.
        public static void CaptureCreated(string label, IEnumerable<GameEntity> created)
        {
            if (created == null) return;
            var list = created.Where(Alive).Distinct().ToList();
            if (list.Count == 0) return;

            Push(new Step { Kind = StepKind.Created, Label = label, TakenAt = DateTime.Now, Entities = list });
        }

        // Call AFTER a tagging operation, with the tag and the entities that received it.
        public static void CaptureTags(string label, string tag, IEnumerable<GameEntity> tagged)
        {
            if (string.IsNullOrWhiteSpace(tag) || tagged == null) return;
            var list = tagged.Where(Alive).Distinct().ToList();
            if (list.Count == 0) return;

            Push(new Step { Kind = StepKind.Tags, Label = label, TakenAt = DateTime.Now, Entities = list, Tag = tag });
        }

        public static (bool ok, string message) UndoLast()
        {
            if (Stack.Count == 0)
                return (false, "Nothing to undo - no undoable operation has run this session.");

            // Undo rewrites frames or removes entities - the watcher must not read that back as
            // something the user just did.
            PrefabSwapperTool.Core.ManipulationWatcher.SuppressSelfEdit(1.5f);

            var step = Stack[Stack.Count - 1];
            Stack.RemoveAt(Stack.Count - 1);

            string message;
            switch (step.Kind)
            {
                case StepKind.Frames:  message = UndoFrames(step);  break;
                case StepKind.Created: message = UndoCreated(step); break;
                case StepKind.Tags:    message = UndoTags(step);    break;
                default:               message = "Unknown undo step."; break;
            }

            if (Stack.Count > 0) message += $" ({Stack.Count} more step(s) available.)";
            return (true, message);
        }

        private static string UndoFrames(Step step)
        {
            int restored = 0, gone = 0, failed = 0;
            foreach (var kvp in step.Frames)
            {
                var entity = kvp.Key;
                if (!Alive(entity)) { gone++; continue; }

                var frame = kvp.Value;
                try
                {
                    entity.SetGlobalFrame(ref frame, true);
                    SyncEditorFrame(entity);   // or the triad stays where the entity used to be
                    restored++;
                }
                catch (Exception ex) { failed++; MaterialSwapTool.Log.Warn($"[EditUndo] restore failed on '{entity.Name}': {ex.Message}"); }
            }

            var msg = $"Undid '{step.Label}' - moved {restored} entity(ies) back.";
            if (gone > 0) msg += $" {gone} no longer exist.";
            if (failed > 0) msg += $" {failed} failed (see tool.log).";
            MaterialSwapTool.Log.Info($"[EditUndo] frames '{step.Label}': restored={restored} gone={gone} failed={failed}");
            return msg;
        }

        private static string UndoCreated(Step step)
        {
            int removed = 0, gone = 0, failed = 0;

            // Children before parents: removing an anchor first would take its children with it
            // and leave the remaining entries pointing at freed memory.
            var ordered = step.Entities.OrderByDescending(DepthOf).ToList();

            foreach (var entity in ordered)
            {
                if (!Alive(entity)) { gone++; continue; }
                try { entity.Remove(0); removed++; }
                catch (Exception ex) { failed++; MaterialSwapTool.Log.Warn($"[EditUndo] remove failed on '{entity.Name}': {ex.Message}"); }
            }

            var msg = $"Undid '{step.Label}' - removed {removed} entity(ies).";
            if (gone > 0) msg += $" {gone} were already gone.";
            if (failed > 0) msg += $" {failed} failed (see tool.log).";
            MaterialSwapTool.Log.Info($"[EditUndo] created '{step.Label}': removed={removed} gone={gone} failed={failed}");
            return msg;
        }

        private static string UndoTags(Step step)
        {
            int cleared = 0, gone = 0, failed = 0;
            foreach (var entity in step.Entities)
            {
                if (!Alive(entity)) { gone++; continue; }
                try
                {
                    if (entity.HasTag(step.Tag)) { entity.RemoveTag(step.Tag); cleared++; }
                }
                catch (Exception ex) { failed++; MaterialSwapTool.Log.Warn($"[EditUndo] untag failed on '{entity.Name}': {ex.Message}"); }
            }

            var msg = $"Undid '{step.Label}' - removed tag '{step.Tag}' from {cleared} entity(ies).";
            if (gone > 0) msg += $" {gone} no longer exist.";
            if (failed > 0) msg += $" {failed} failed (see tool.log).";
            MaterialSwapTool.Log.Info($"[EditUndo] tags '{step.Label}': cleared={cleared} gone={gone} failed={failed}");
            return msg;
        }

        private static int DepthOf(GameEntity e)
        {
            int depth = 0;
            try
            {
                var p = e?.Parent;
                while (p != null && p.Pointer != UIntPtr.Zero && depth < 64) { depth++; p = p.Parent; }
            }
            catch { }
            return depth;
        }

        // Same call every mover in this toolkit makes after SetGlobalFrame - without it the entity
        // renders in its new place while the editor's transform gizmo stays behind.
        private static void SyncEditorFrame(GameEntity entity)
        {
            try
            {
                entity.UpdateTriadFrameForEditor();
                entity.UpdateTriadFrameForEditorForAllChildren();
            }
            catch { }
        }

        // A scene switch invalidates every reference held here. Restoring a frame onto - or
        // calling Remove on - a freed native pointer is the class of bug that takes the whole
        // editor down, so the stack is dropped wholesale rather than filtered.
        // Drops the most recent step without applying it - for a preview that was cancelled, where
        // the step was captured up front but nothing ended up changing. Leaving it would put a
        // no-op on the stack that silently eats the user's next Undo press.
        public static void DiscardLast()
        {
            if (Stack.Count == 0) return;
            var dropped = Stack[Stack.Count - 1];
            Stack.RemoveAt(Stack.Count - 1);
            MaterialSwapTool.Log.Info($"[EditUndo] discarded '{dropped.Label}' (cancelled), depth={Stack.Count}");
        }

        public static void Clear(string reason)
        {
            if (Stack.Count == 0) return;
            MaterialSwapTool.Log.Info($"[EditUndo] cleared {Stack.Count} step(s): {reason}");
            Stack.Clear();
        }
    }
}
