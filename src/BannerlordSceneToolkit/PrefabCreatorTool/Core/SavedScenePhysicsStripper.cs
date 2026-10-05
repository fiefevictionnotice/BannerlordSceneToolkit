using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BannerlordSceneToolkit;

namespace PrefabCreatorTool.Core
{
    // Removes <physics .../> nodes from pile_nophys-tagged entities in the SAVED scene.xscene.
    //
    // WHY FILE SURGERY (2026-08-23, "they still have physics after i used physics: delete",
    // fourth report): every runtime lever was tried and confirmed executed - RemovePhysics,
    // SetBodyFlags(None), EntityFlags.PhysicsDisabled, SetPhysicsState(false) - and the saved
    // file STILL emitted <physics shape="bo_..."/> per piece (verified by reading the xscene).
    // The serializer writes the physics SHAPE REFERENCE from entity metadata no reachable API
    // clears, so the loader rebuilds the body on every load regardless. The pieces are tagged
    // pile_nophys at generation exactly so this pass can find them; stripping the node from
    // the saved file is the one edit that actually persists.
    //
    // Run AFTER saving; takes a timestamped backup of the file first; the live session is not
    // changed (reload to see the result). Saving again from the editor re-emits the nodes -
    // re-run after the final save.
    public static class SavedScenePhysicsStripper
    {
        public const string Tag = "pile_nophys";

        // Returns a human-readable status line.
        public static string StripCurrentScene()
        {
            var path = MaterialSwapTool.Backup.BackupManager.TryFindSceneFile("scene.xscene");
            if (path == null || !File.Exists(path))
                return "Couldn't find the saved scene.xscene for the open scene.";

            XDocument doc;
            try { doc = XDocument.Load(path, LoadOptions.PreserveWhitespace); }
            catch (Exception ex) { return "Couldn't parse the saved scene: " + ex.Message; }

            int stripped = 0;
            foreach (var entity in doc.Descendants("game_entity"))
            {
                var tags = entity.Element("tags");
                if (tags == null) continue;
                bool marked = tags.Elements("tag").Any(t => string.Equals((string)t.Attribute("name"), Tag, StringComparison.OrdinalIgnoreCase));
                if (!marked) continue;

                var physicsNodes = entity.Elements("physics").ToList();
                foreach (var p in physicsNodes) { p.Remove(); stripped++; }
            }

            if (stripped == 0)
                return $"No <physics> nodes found on {Tag}-tagged entities in the saved file - " +
                       "either already stripped, or the scene hasn't been saved since generating.";

            try
            {
                var backup = path + $".prestrip_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                File.Copy(path, backup, overwrite: false);

                // Auto re-strip runs after every save, so prune to the newest 3 .bak copies.
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    var stale = Directory.EnumerateFiles(dir, "scene.xscene.prestrip_*.bak")
                        .OrderByDescending(f => f)
                        .Skip(3).ToList();
                    foreach (var s in stale) File.Delete(s);
                }
                catch { }

                doc.Save(path, SaveOptions.DisableFormatting);
                Log.Info($"[Pile] SavedScenePhysicsStripper: removed {stripped} <physics> node(s) from '{path}' (backup: {Path.GetFileName(backup)}).");
                return $"Removed {stripped} <physics> node(s) from the SAVED scene (backup taken). " +
                       "RELOAD the scene to get the collision-free pieces - and note the editor re-emits " +
                       "the nodes on its next save of the stale session, so re-run this after your final save.";
            }
            catch (Exception ex)
            {
                return "Failed to write the stripped scene: " + ex.Message;
            }
        }
    }
}
