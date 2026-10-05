using HarmonyLib;
using PrefabCreatorTool.Backup;
using PrefabCreatorTool.GUI;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;

namespace PrefabCreatorTool.Patches
{
    // Own independent Harmony patch on MBEditor.TickSceneEditorPresentation - a second mod
    // patching the same method as MaterialSwapTool poses no contention risk (Harmony explicitly
    // supports multiple patches on one method via its own patch-chaining), this is just kept
    // fully separate per the decision to isolate this more experimental tool as its own module.
    [HarmonyPatch(typeof(MBEditor), nameof(MBEditor.TickSceneEditorPresentation))]
    public static class MBEditorTickScenePresentationPatch
    {
        private const InputKey ToggleKey = InputKey.F5;

        // TickSceneEditorPresentation is the single opaque native call that drives the scene
        // editor's WASD camera movement - there's no managed hook to intercept individual keys
        // inside it. Skipping the call entirely while a text field in our own panel is focused
        // is the only available way to stop typing "wasd" into a color/tag/name box from also
        // dragging the camera around. This only pauses camera/viewport presentation for that one
        // frame; our own GauntletLayer's keystroke handling runs through a separate managed UI
        // input pipeline and isn't affected.
        public static bool Prefix()
        {
            return !(PrefabCreatorLayer.IsFocusedOnInput || TextureSetBrowserLayer.IsFocusedOnInput || PairingBrowserLayer.IsFocusedOnInput || PileGeneratorLayer.IsFocusedOnInput || FamilyBrowserLayer.IsFocusedOnInput);
        }

        public static void Postfix(float dt)
        {
            if (!MBEditor.IsEditModeOn) return;

            if (Input.IsKeyPressed(ToggleKey))
                PrefabCreatorLayer.Toggle();

            PrefabCreatorLayer.Tick(dt);
            TextureSetBrowserLayer.Tick(dt);
            PairingBrowserLayer.Tick(dt);
            PileGeneratorLayer.Tick(dt);
            FamilyBrowserLayer.Tick(dt);
            BackupManager.Tick(dt);
        }
    }
}
