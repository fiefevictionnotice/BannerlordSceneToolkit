using System;
using System.Reflection;
using HarmonyLib;

namespace BannerlordSceneToolkit
{
    // Lets go of a scene at the moment the editor screen starts shutting down.
    //
    // WHY THIS EXISTS SEPARATELY FROM THE TICK PATCHES. Every other hook in this toolkit hangs
    // off MBEditor.TickSceneEditorPresentation, which the scene editor screen drives. That is
    // fine for anything that happens while a scene is open and useless for anything that happens
    // as one closes: deactivating the screen stops the ticks, so a tick-based "is the scene gone?"
    // check never gets a frame in which to notice. Measured, not assumed - a tick-based close
    // check shipped on 2026-08-22 was confirmed loaded in the crashing session and never logged
    // once.
    //
    // The engine's own log names the sequence on close:
    //     SceneEditorScreen::HandlePause / HandleDeactivate
    //     Scene_view::clear_all(<scene>)
    //     SceneEditorScreen::HandleFinalize
    // HandleDeactivate is the first of those and still runs with the screen alive, which is what
    // this needs: our layers can be removed from a screen that still exists, and our caches
    // dropped before any native teardown touches the entities they point at.
    //
    // RESOLVED BY NAME AT RUNTIME, and allowed to fail. SceneEditorScreen lives in
    // TaleWorlds.MountAndBlade.View, which this project does not reference (it is a Native-module
    // assembly, not a bin/ one), and a hard reference for one patch is not worth it. If the type
    // or method ever moves, this logs and does nothing rather than throwing during startup and
    // taking every other patch down with it.
    internal static class SceneTeardownPatch
    {
        private const string ScreenTypeName = "TaleWorlds.MountAndBlade.View.Screens.SceneEditorScreen";

        // HandleDeactivate first; HandleFinalize as a fallback in case a build only has that one.
        private static readonly string[] CandidateMethods = { "HandleDeactivate", "HandleFinalize" };

        // The editor screen's Type, kept for the prefix's instance filter - see OnTearingDown.
        private static Type _editorScreenType;

        public static void TryApply(Harmony harmony)
        {
            try
            {
                var screenType = AccessTools.TypeByName(ScreenTypeName);
                if (screenType == null)
                {
                    Log.Warn($"[SceneGuard] '{ScreenTypeName}' not found - scene-close cleanup is not hooked. " +
                             "Panels and caches will still be dropped on a scene SWITCH, just not on a close.");
                    return;
                }
                _editorScreenType = screenType;

                var prefix = new HarmonyMethod(typeof(SceneTeardownPatch)
                    .GetMethod(nameof(OnTearingDown), BindingFlags.NonPublic | BindingFlags.Static));

                foreach (var name in CandidateMethods)
                {
                    var method = AccessTools.Method(screenType, name);
                    if (method == null) continue;

                    // CONFIRMED FAILURE, fixed 2026-08-22: SceneEditorScreen does not OVERRIDE
                    // HandleDeactivate in this game build (v1.4.8) - it inherits ScreenBase's -
                    // and Harmony refuses to patch an inherited slot ("You can only patch
                    // implemented methods... Patch the declared method"). The guard silently
                    // failed to install on every launch, which is exactly the state the CC_76
                    // scene-close crash happened in. So: walk to the method's DECLARING type and
                    // patch it there. The prefix then fires for EVERY screen's deactivate, so it
                    // filters by instance type - only the scene editor screen triggers cleanup.
                    if (method.DeclaringType != null && method.DeclaringType != screenType)
                    {
                        var declared = AccessTools.DeclaredMethod(method.DeclaringType, name);
                        if (declared == null) continue;
                        harmony.Patch(declared, prefix: prefix);
                        Log.Info($"[SceneGuard] hooked {method.DeclaringType.FullName}.{name} (declared base of " +
                                 $"{ScreenTypeName}, instance-filtered) for scene-close cleanup.");
                        return;
                    }

                    harmony.Patch(method, prefix: prefix);
                    Log.Info($"[SceneGuard] hooked {ScreenTypeName}.{name} for scene-close cleanup.");
                    return;
                }

                Log.Warn($"[SceneGuard] found {ScreenTypeName} but none of ({string.Join(", ", CandidateMethods)}) " +
                         "- scene-close cleanup is not hooked.");
            }
            catch (Exception ex)
            {
                // Never let a missing optional hook stop the toolkit from loading.
                Log.Error("[SceneGuard] failed to hook scene teardown: " + ex);
            }
        }

        // Deliberately swallows everything: this runs inside the engine's own shutdown path, and
        // an exception thrown from a Prefix propagates into it. Failing to tidy up is recoverable;
        // throwing here would not be.
        //
        // __instance filter: the patch usually lands on ScreenBase.HandleDeactivate (see
        // TryApply), so every screen in the game passes through here - menus, missions, all of
        // them. Only the scene editor screen (or a subclass) may trigger cleanup.
        private static void OnTearingDown(object __instance)
        {
            try
            {
                if (_editorScreenType == null || __instance == null) return;
                if (!_editorScreenType.IsAssignableFrom(__instance.GetType())) return;

                Log.Info("[SceneGuard] scene editor screen deactivating - running scene-close cleanup.");
                MaterialSwapTool.Backup.BackupManager.OnEditorScreenTearingDown();
            }
            catch (Exception ex) { Log.Error("[SceneGuard] cleanup threw during teardown: " + ex); }
        }
    }
}
