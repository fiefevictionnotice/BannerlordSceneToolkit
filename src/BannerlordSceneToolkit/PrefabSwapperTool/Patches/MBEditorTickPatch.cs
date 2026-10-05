using HarmonyLib;
using PrefabSwapperTool.Backup;
using PrefabSwapperTool.GUI;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;

namespace PrefabSwapperTool.Patches
{
    // Own independent Harmony patch on MBEditor.TickSceneEditorPresentation - split out of
    // MaterialSwapTool (which patches the same native method) specifically to isolate the
    // Prefab Swapper/Distribution tool as its own mod for crash A/B diagnosis. Harmony
    // explicitly supports multiple patches on one method via its own patch-chaining, so this
    // poses no contention risk running alongside MaterialSwapTool's own patch.
    [HarmonyPatch(typeof(MBEditor), nameof(MBEditor.TickSceneEditorPresentation))]
    public static class MBEditorTickScenePresentationPatch
    {
        private const InputKey ToggleKey = InputKey.F6;

        // Skipping the native camera-tick call for one frame while a text field in our own
        // panel is focused - same WASD-vs-typing fix as MaterialSwapTool's own patch.
        public static bool Prefix()
        {
            return !(PrefabSwapperLayer.IsFocusedOnInput || PrefabHistoryLayer.IsFocusedOnInput || SwapSetBrowserLayer.IsFocusedOnInput || TextureSetBrowserLayer.IsFocusedOnInput);
        }

        public static void Postfix(float dt)
        {
            if (!MBEditor.IsEditModeOn) return;

            if (Input.IsKeyPressed(ToggleKey))
                PrefabSwapperLayer.Toggle();

            PrefabSwapperLayer.Tick(dt);
            PrefabHistoryLayer.Tick(dt);
            SwapSetBrowserLayer.Tick(dt);
            TextureSetBrowserLayer.Tick(dt);
            BackupManager.Tick(dt);
        }
    }
}
