using System;

namespace MaterialSwapTool.Core
{
    // One row per material change. Position/rotation are always recorded (cheap, non-invasive)
    // even when Uid is null, so revert/solidify always has a fallback identity signal to cross-check
    // against - a UID tag alone is not trustworthy on its own, since cloning an entity duplicates
    // its tags and therefore its UID.
    public class ChangeLogEntry
    {
        public string BatchId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string SceneName { get; set; }
        public string EntityName { get; set; }
        public string Uid { get; set; }
        public int MetaMeshIndex { get; set; }
        public int MeshIndex { get; set; }
        public string OldMaterial { get; set; }
        public string NewMaterial { get; set; }
        public string OldColorFactor { get; set; } = "";
        public string NewColorFactor { get; set; } = "";
        // True for an entity-wide color factor entry (GameEntity.SetFactorColor) - distinct from
        // the per-LOD-slot color above (MetaMesh.SetFactor1). When true, MetaMeshIndex/MeshIndex/
        // OldMaterial/NewMaterial are unused; only the color fields and entity identity matter.
        public bool IsEntityWideColor { get; set; }
        // A THIRD, per-individual-mesh color tint (Mesh.Color/Color2) - confirmed live via a
        // user test scene (european_city_house_d3) where exactly one LOD5 submesh had a non-
        // white Color while its MetaMesh's Factor1 and the entity's FactorColor were both still
        // white. This overturns the earlier assumption that Mesh.Color wasn't a verified live
        // rendering property. Independent of OldColorFactor/NewColorFactor above (that's still
        // MetaMesh.SetFactor1) and of material - can be set on a slot whose material never changed.
        public string OldMeshColor { get; set; } = "";
        public string NewMeshColor { get; set; } = "";
        public string OldMeshColor2 { get; set; } = "";
        public string NewMeshColor2 { get; set; } = "";
        // Entity-wide entries only (2026-08-23, "when we undo ... a per-layer color factor
        // colors the ENTIRE entity"): SetFactorColor writes across every mesh's own color, so
        // undoing an entity-wide entry via SetFactorColor(OldColorFactor) repainted the whole
        // entity one flat color, destroying per-layer colors the batch never touched. This
        // snapshots EVERY mesh's color before the forward SetFactorColor, as
        // "meta:mesh:hex|meta:mesh:hex|...", and undo restores them individually instead.
        // Empty on entries from before this field existed - undo falls back to the old
        // single-color behaviour there.
        public string MeshColorSnapshot { get; set; } = "";
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float RotForwardX { get; set; }
        public float RotForwardY { get; set; }
        public float RotForwardZ { get; set; }
    }
}
