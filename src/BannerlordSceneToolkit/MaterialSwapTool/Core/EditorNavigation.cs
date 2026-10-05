using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MaterialSwapTool.Core
{
    // "Go to this position" for any report line that prints world coordinates.
    //
    // MBEditor.ZoomToPosition(Vec3) is a real public static (verified by reflecting
    // TaleWorlds.MountAndBlade.dll: MBEditor.ZoomToPosition + IMBEditor.ZoomToPosition). It is the
    // camera half of what double-clicking an entry in the editor's own scene list does; the other
    // half is selecting the thing. GoTo does both, so a Go button behaves the way the editor
    // already taught you to expect.
    public static class EditorNavigation
    {
        // How close an entity has to be to the printed coordinates to count as "the one that line
        // is about". The coordinates in our reports are printed to 2 decimals, so anything under
        // half a centimetre would be defeated by the rounding itself.
        private const float MatchTolerance = 0.05f;

        public static string GoTo(float x, float y, float z)
        {
            var pos = new Vec3(x, y, z);

            // Camera first, and inline: unlike selection, nothing else is competing to reset it
            // on the same click (see DeferredSelection for why selection cannot be done here).
            try { MBEditor.ZoomToPosition(pos); }
            catch (Exception ex)
            {
                Log.Warn("ZoomToPosition failed: " + ex.Message);
                return "Camera jump failed: " + ex.Message;
            }

            var entity = FindEntityAt(pos, out var distance);
            if (entity == null)
            {
                Log.Info($"[GoTo] moved camera to ({x:F2}, {y:F2}, {z:F2}); no entity within {MatchTolerance}m to select.");
                return $"Jumped to ({x:F2}, {y:F2}, {z:F2}). No entity exactly there to select.";
            }

            LiveSceneChecks.SetEditorSelection(new List<GameEntity> { entity });
            Log.Info($"[GoTo] ({x:F2}, {y:F2}, {z:F2}) -> '{entity.Name}' at {distance:F3}m; camera moved, selection queued.");
            return $"Jumped to '{entity.Name}' at ({x:F2}, {y:F2}, {z:F2}).";
        }

        // Nearest entity within tolerance, not merely the first one found - several entities can
        // share a position closely enough to pass the tolerance test, and the closest is the one
        // whose coordinates got printed.
        private static GameEntity FindEntityAt(Vec3 pos, out float distance)
        {
            distance = float.MaxValue;
            GameEntity best = null;
            try
            {
                if (!EntitySelector.HasOpenScene) return null;
                var all = new List<GameEntity>();
                EntitySelector.CurrentScene.GetEntities(ref all);
                foreach (var e in all)
                {
                    if (!EntitySelector.IsValidEntity(e)) continue;
                    float d;
                    try { d = (e.GetGlobalFrame().origin - pos).Length; } catch { continue; }
                    if (d < distance) { distance = d; best = e; }
                }
            }
            catch (Exception ex) { Log.Warn("FindEntityAt failed: " + ex.Message); return null; }

            return distance <= MatchTolerance ? best : null;
        }
    }
}
