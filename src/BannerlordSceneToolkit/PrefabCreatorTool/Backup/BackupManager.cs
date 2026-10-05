namespace PrefabCreatorTool.Backup
{
    // Thin delegation shell since v0.7. This used to be a full copy of MaterialSwapTool's
    // backup system - its own 4-minute timer, its own Backups folder under
    // Documents\...\PrefabCreatorTool\ - which meant three timers copying the same three scene
    // files, and this copy never received the later improvements (F9 settings, unchanged-skip,
    // copy verification, notification gating). Everything now routes to the one shared
    // implementation in MaterialSwapTool.Backup.BackupManager; backups land in that tool's
    // Backups folder and honour the F9 panel's settings. The old
    // Documents\...\PrefabCreatorTool\Backups folder is legacy - nothing writes there anymore.
    //
    // Kept as a class (rather than rewriting 30+ call sites) so this tool's code still reads
    // Backup.BackupManager.BackupNow("before-apply") the way it always did. Tick stays wired
    // from this tool's own patch so the shared Tick - including the scene-switch state
    // invalidation that protects EditUndo and friends from stale native pointers - runs even
    // when MaterialSwapTool is disabled in tool_toggles.txt.
    public static class BackupManager
    {
        public static bool BackupsEnabled => MaterialSwapTool.Backup.BackupManager.BackupsEnabled;

        public static void Tick(float dt) => MaterialSwapTool.Backup.BackupManager.Tick(dt);

        public static string BackupNow(string reason) => MaterialSwapTool.Backup.BackupManager.BackupNow(reason);

        public static void ArmSaveReminder() => MaterialSwapTool.Backup.BackupManager.ArmSaveReminder();

        public static string TryFindSceneDir(string sceneName) => MaterialSwapTool.Backup.BackupManager.TryFindSceneDir(sceneName);
    }
}
