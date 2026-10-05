using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using BannerlordSceneToolkit;

namespace MaterialSwapTool.Core
{
    // Resets an entity's materials AND all three color-tint mechanisms to their vanilla defaults -
    // independent of this tool's own change log, so it works even on an entity this session never
    // touched (a building edited by hand, by another tool, or in someone else's scene).
    // Complementary to, not a replacement for, backups (whole-file rollback) and Undo/Redo (precise
    // replay of what this tool specifically did) - see the design discussion this was built from.
    //
    // "Normal" for color means white/#FFFFFFFF (no tint) - every vanilla entity dumped this session
    // (european_city_house_d, and every untouched slot on the deliberately-mismatched
    // european_city_house_d3) came back exactly white on all three mechanisms, so that's the
    // trustworthy baseline, same convention the entity-wide/rule-level "Clear" buttons already use.
    public static class RevertToNormalEngine
    {
        private const uint White = 0xFFFFFFFFu;

        public class Result
        {
            public string BatchId;
            public int EntitiesTouched;
            public int MaterialsReverted;
            public int ColorsReverted;
            public int SlotsNotInReference;
            public List<ChangeLogEntry> Entries = new List<ChangeLogEntry>();
        }

        public static Result Apply(List<GameEntity> targets, bool dryRun)
        {
            var result = new Result { BatchId = ChangeLogger.NewBatchId() };
            var sceneName = EntitySelector.CurrentSceneName;

            foreach (var target in targets)
            {
                if (!EntitySelector.IsValidEntity(target)) continue;

                // Composite prefabs (parent anchor + mesh data on children) need the full
                // hierarchy walked, not just the selected entity's own slots - see
                // EntitySelector.EnumerateSelfAndDescendants for why.
                foreach (var entity in EntitySelector.EnumerateSelfAndDescendants(target))
                {
                    if (!EntitySelector.IsValidEntity(entity)) continue;
                    bool touched = false;
                    var frame = entity.GetGlobalFrame();

                    ChangeLogEntry NewEntry(int metaIndex, int meshIndex) => new ChangeLogEntry
                    {
                        BatchId = result.BatchId,
                        TimestampUtc = DateTime.UtcNow,
                        SceneName = sceneName,
                        EntityName = entity.Name,
                        MetaMeshIndex = metaIndex,
                        MeshIndex = meshIndex,
                        PosX = frame.origin.x,
                        PosY = frame.origin.y,
                        PosZ = frame.origin.z,
                        RotForwardX = frame.rotation.f.x,
                        RotForwardY = frame.rotation.f.y,
                        RotForwardZ = frame.rotation.f.z,
                    };

                    // Entity-wide color factor (GameEntity.SetFactorColor).
                    var entityColor = entity.GetFactorColor();
                    if (entityColor != White)
                    {
                        var entry = NewEntry(0, 0);
                        entry.IsEntityWideColor = true;
                        entry.OldColorFactor = ColorHex.ToHex(entityColor);
                        entry.NewColorFactor = ColorHex.ToHex(White);
                        result.Entries.Add(entry);
                        if (!dryRun) entity.SetFactorColor(White);
                        result.ColorsReverted++;
                        touched = true;
                    }

                    for (int m = 0; m < entity.MultiMeshComponentCount; m++)
                    {
                        var meta = entity.GetMetaMesh(m);
                        if (meta == null || !meta.IsValid) continue;
                        var metaName = meta.GetName();

                        // MetaMesh-level color factor (MetaMesh.SetFactor1) - independent of any
                        // individual mesh slot, so logged once per MetaMesh rather than per mesh.
                        var metaColor = meta.GetFactor1();
                        if (metaColor != White && meta.MeshCount > 0)
                        {
                            var entry = NewEntry(m, 0);
                            entry.OldColorFactor = ColorHex.ToHex(metaColor);
                            entry.NewColorFactor = ColorHex.ToHex(White);
                            result.Entries.Add(entry);
                            if (!dryRun) meta.SetFactor1(White);
                            result.ColorsReverted++;
                            touched = true;
                        }

                        for (int i = 0; i < meta.MeshCount; i++)
                        {
                            var mesh = meta.GetMeshAtIndex(i);
                            if (mesh == null) continue;

                            var currentName = mesh.GetMaterial()?.Name;
                            bool hasMaterialChange = false;
                            string defaultMaterial = null;
                            if (currentName != null)
                            {
                                if (!MeshDefaults.TryGetDefault(metaName, mesh.Name, out defaultMaterial))
                                    result.SlotsNotInReference++;
                                else
                                    hasMaterialChange = !string.Equals(currentName, defaultMaterial, StringComparison.OrdinalIgnoreCase);
                            }

                            // A THIRD, per-individual-mesh color tint (Mesh.Color/Color2) - confirmed
                            // live via a user test scene where exactly one LOD5 submesh had a non-
                            // white Color while its MetaMesh's Factor1 and the entity's FactorColor
                            // were both still white. Independent of material.
                            var meshColor = mesh.Color;
                            var meshColor2 = mesh.Color2;
                            bool hasColorChange = meshColor != White;
                            bool hasColor2Change = meshColor2 != White;

                            if (!hasMaterialChange && !hasColorChange && !hasColor2Change) continue;

                            var slotEntry = NewEntry(m, i);
                            if (hasMaterialChange)
                            {
                                slotEntry.OldMaterial = currentName;
                                slotEntry.NewMaterial = defaultMaterial;
                                if (!dryRun) mesh.SetMaterial(defaultMaterial);
                                result.MaterialsReverted++;
                            }
                            if (hasColorChange)
                            {
                                slotEntry.OldMeshColor = ColorHex.ToHex(meshColor);
                                slotEntry.NewMeshColor = ColorHex.ToHex(White);
                                if (!dryRun) mesh.Color = White;
                                result.ColorsReverted++;
                            }
                            if (hasColor2Change)
                            {
                                slotEntry.OldMeshColor2 = ColorHex.ToHex(meshColor2);
                                slotEntry.NewMeshColor2 = ColorHex.ToHex(White);
                                if (!dryRun) mesh.Color2 = White;
                                result.ColorsReverted++;
                            }

                            result.Entries.Add(slotEntry);
                            touched = true;
                        }
                    }

                    if (touched) result.EntitiesTouched++;
                }
            }

            if (!dryRun && result.Entries.Count > 0)
                ChangeLogger.Append(result.Entries);

            return result;
        }
    }
}
