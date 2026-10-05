using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.Engine;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;
using StreamWriter = System.IO.StreamWriter;

namespace MaterialSwapTool.Core
{
    public class SolidifyReport
    {
        public int EntitiesSolidified { get; set; }
        public int EntitiesAmbiguousSkipped { get; set; }
        public int EntriesDroppedMissing { get; set; }
        public string NewLogPath { get; set; }
    }

    // Whole-scene-only cleanup: downgrades every UID-tracked entry to position-only tracking and
    // removes the tracking tags from the scene. Never touches the original log - always writes a
    // new file, same backup-before-destructive-op convention the rest of this stack follows.
    // Order matters: for each entity, the solidified record is written and flushed before its tag
    // is stripped, so an interrupted run leaves a few harmless leftover tags rather than a record
    // that can no longer be resolved to anything.
    public static class SolidifyManager
    {
        private const string UidTagPrefix = "mst_uid_";

        public static SolidifyReport Run()
        {
            var report = new SolidifyReport();
            if (!EntitySelector.HasOpenScene)
                throw new InvalidOperationException("No scene is currently open in the editor.");
            var scene = EntitySelector.CurrentScene;

            var all = ChangeLogger.ReadAll();
            if (all.Count == 0) return report;

            var liveEntities = new List<GameEntity>();
            scene.GetEntities(ref liveEntities);
            liveEntities = liveEntities.Where(EntitySelector.IsValidEntity).ToList();

            var newLogPath = IOPath.Combine(ChangeLogger.LogDir, $"solidified_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl");
            IODirectory.CreateDirectory(ChangeLogger.LogDir);
            report.NewLogPath = newLogPath;

            using (var writer = new StreamWriter(newLogPath, append: false))
            {
                // Untagged entries were already position-only - pass through unchanged.
                foreach (var entry in all.Where(e => string.IsNullOrEmpty(e.Uid)))
                    writer.WriteLine(JsonConvert.SerializeObject(entry));

                var byUid = all.Where(e => !string.IsNullOrEmpty(e.Uid)).GroupBy(e => e.Uid);

                foreach (var group in byUid)
                {
                    var tag = UidTagPrefix + group.Key;
                    var matches = liveEntities.Where(e => e.HasTag(tag)).ToList();

                    if (matches.Count == 0)
                    {
                        report.EntriesDroppedMissing += group.Count();
                        Log.Warn($"Solidify: UID {group.Key} no longer resolves to any entity - dropping {group.Count()} entry(ies).");
                        continue;
                    }

                    if (matches.Count > 1)
                    {
                        report.EntitiesAmbiguousSkipped++;
                        Log.Warn($"Solidify: UID {group.Key} resolves to {matches.Count} entities (unresolved clone) - leaving tag in place, not solidifying.");
                        continue;
                    }

                    var entity = matches[0];
                    var frame = entity.GetGlobalFrame();

                    foreach (var entry in group)
                    {
                        entry.Uid = null;
                        entry.PosX = frame.origin.x;
                        entry.PosY = frame.origin.y;
                        entry.PosZ = frame.origin.z;
                        entry.RotForwardX = frame.rotation.f.x;
                        entry.RotForwardY = frame.rotation.f.y;
                        entry.RotForwardZ = frame.rotation.f.z;
                        writer.WriteLine(JsonConvert.SerializeObject(entry));
                    }
                    writer.Flush();

                    entity.RemoveTag(tag);
                    report.EntitiesSolidified++;
                }
            }

            return report;
        }
    }
}
