using System;

namespace PrefabSwapperTool.Core
{
    // One swapped entity, logged by PrefabSwapLogger. Deliberately its own record shape rather than
    // reusing ChangeLogEntry - that one is built around editing a still-alive entity's material/
    // color in place, but a prefab swap deletes the old entity and a new one takes over its spot,
    // so undo has to work by re-finding an entity (by name + position), not by holding a reference
    // that's guaranteed to still resolve.
    public class PrefabSwapLogEntry
    {
        public string BatchId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string SceneName { get; set; }
        public string OldPrefabName { get; set; }
        public string NewPrefabName { get; set; }
        public bool Undone { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float RotForwardX { get; set; }
        public float RotForwardY { get; set; }
        public float RotForwardZ { get; set; }
    }
}
