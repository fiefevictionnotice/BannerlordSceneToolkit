using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace BannerlordSceneToolkit
{
    // Single consolidated entry point for the v0.5 merge of MaterialSwapTool + PrefabSwapperTool
    // + PrefabCreatorTool. Deliberately ONE Harmony instance, not three - each original mod's own
    // SubModule.cs called harmony.PatchAll(typeof(SubModule).Assembly), which scans the WHOLE
    // assembly for [HarmonyPatch] classes, not just its own namespace. Three separate SubModule
    // entries in one merged assembly each doing that would each independently find and patch ALL
    // THREE tools' patch classes, tripling every Tick/hotkey/panel-tick call.
    //
    // Patches each tool's own single Harmony-patched class individually (via CreateClassProcessor,
    // not a blanket PatchAll) - each tool has exactly one such class, its MBEditor tick patch - so
    // ToolToggles can skip a specific tool's patch entirely instead of always patching everything.
    // Built for a direct A/B test of the standing "MaterialSwapTool alongside the others" crash
    // suspicion (see the crash investigation memory) - merging into one assembly didn't actually
    // test this on its own, since all three tools were already enabled together beforehand.
    public class SubModule : MBSubModuleBase
    {
        public const string HarmonyId = "BannerlordSceneToolkit.Patches";

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                var harmony = new Harmony(HarmonyId);
                var toggles = ToolToggles.Load();

                if (toggles.MaterialSwapTool)
                    harmony.CreateClassProcessor(typeof(MaterialSwapTool.Patches.MBEditorTickScenePresentationPatch)).Patch();
                if (toggles.PrefabSwapperTool)
                    harmony.CreateClassProcessor(typeof(PrefabSwapperTool.Patches.MBEditorTickScenePresentationPatch)).Patch();
                if (toggles.PrefabCreatorTool)
                    harmony.CreateClassProcessor(typeof(PrefabCreatorTool.Patches.MBEditorTickScenePresentationPatch)).Patch();

                // Registered regardless of the per-tool toggles: this is not a feature, it is the
                // toolkit letting go of a scene as the editor closes it. Whichever tools are on,
                // whatever they cached has to be dropped at the same moment.
                SceneTeardownPatch.TryApply(harmony);

                // Keeps the editor's Windows cursor while toolkit panels are open - see
                // Core/ToolkitCursor. Toggle-independent for the same reason: it is about how
                // every panel behaves, not about any one tool.
                ToolkitCursor.TryApplyPatch(harmony);

                // Preload loose sprite-sheet PNGs so "Unable to find texture" popups from
                // modules whose UI textures the editor can't resolve never fire - see
                // Core/TextureRescue. Also toggle-independent: it fixes the LAUNCH experience.
                TextureRescue.Run();

                Log.Info("BannerlordSceneToolkit loaded. Tools active this session: " +
                    $"MaterialSwapTool={toggles.MaterialSwapTool}, PrefabSwapperTool={toggles.PrefabSwapperTool}, PrefabCreatorTool={toggles.PrefabCreatorTool}. " +
                    "Edit Documents\\Mount and Blade II Bannerlord\\BannerlordSceneToolkit\\tool_toggles.txt and restart to change.");
            }
            catch (Exception ex)
            {
                // Editor-only tool: if a patch target doesn't match this game version, fail
                // loudly to the log rather than silently doing nothing.
                Log.Error("BannerlordSceneToolkit failed to apply Harmony patches: " + ex);
            }
        }
    }

    // Top-level Log shim so SubModule.cs (namespace BannerlordSceneToolkit, no per-tool Log.cs of
    // its own) has somewhere to write startup/failure messages. Writes into the same merged
    // tool.log convention as MaterialSwapTool.Log/PrefabSwapperTool.Log/PrefabCreatorTool.Log
    // (each tool's own namespace still uses its own Log class, unchanged) - this one covers the
    // merged entry point's own messages plus the v0.7 shared Core classes (ColorHex has nothing
    // to say, but EditorFrameSync/FamilyAutoPlacer warn here now instead of into whichever
    // tool's log their old per-namespace copies used). Actual writing and the 10 MB rotation
    // live in LogFile.
    internal static class Log
    {
        private static readonly string LogPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "BannerlordSceneToolkit", "tool.log");

        public static void Info(string message) => LogFile.Write(LogPath, "INFO", message);
        public static void Warn(string message) => LogFile.Write(LogPath, "WARN", message);
        public static void Error(string message) => LogFile.Write(LogPath, "ERROR", message);
    }
}
