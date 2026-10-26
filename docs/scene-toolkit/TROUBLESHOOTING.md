# Troubleshooting notes

Things worth knowing when something misbehaves - especially the ones that are easy to
misdiagnose. Reference for future debugging sessions as much as for the user.

## The tool toggles do not fully isolate the tools

`tool_toggles.txt` unregisters a tool's Harmony patch, which kills its hotkey and its
own Tick loop - but it does not stop OTHER tools from calling into its code, since all
three live in one assembly. Concretely, MaterialSwapTool's tick drives
PrefabSwapperTool's ManipulationWatcher / NumericTransform / RepeatLastTransform every
frame regardless of the PrefabSwapperTool toggle, and (since the v0.7 backup
consolidation) every tool's tick drives the shared backup system in
MaterialSwapTool.Backup.

Accepted as-is (2026-08-22): the crash question the toggles were built to A/B turned
out to be deploys-while-the-editor-was-open, not tool interaction, so strict isolation
stopped mattering. If a toggle-based experiment is ever needed again, remember that
"PrefabSwapperTool=false" means "F6 and its panels are dead", not "no PST code runs".

## Reading a crash

Check, in order:
1. Was a build deployed while the editor was open? This was the actual cause of the
   2026-08 crash streak - module files overwritten under a running game. The engine's
   own crash logs show the module file being read seconds before death. Prevention:
   `-p:SkipDeploy=true` for compile checks, full builds only with the game closed.
2. `Documents\...\MaterialSwapTool\tool.log` - most subsystems log there. A
   `[NativeTrace]` line with nothing after it names the native mutation in flight when
   the process died. `[EditUndo]`, `[Manipulation]`, `[SceneGuard]`, `[TakeoverDiag]`
   are the other prefixes worth grepping.
3. The two historical crash mechanisms are unrelated to each other: the old native
   Qt5Core.dll instability, and a since-fixed GauntletUI binding exception
   (ComboMemberRowVM.AutoPlace notifying a string property with a bool). Don't
   conflate them when pattern-matching a new crash onto an old one.

## Backups landed somewhere unexpected / stopped appearing per tool

Since v0.7 every backup goes to `Documents\...\MaterialSwapTool\Backups`, whichever
tool triggered it. `PrefabSwapperTool\Backups` and `PrefabCreatorTool\Backups` are
legacy - valid history, no longer written. If backups seem to skip entirely: that is
the unchanged-on-disk check working (nothing to copy), visible in tool.log as
"Backup skipped". A manual F9 Backup Now always copies.

## The editor stutters when a hotkey is pressed

Fixed 2026-08-22, but worth knowing the shape of it, because the same trap is easy to
re-introduce. Reading the editor's selection has two paths: `Utilities.GetSelectedEntities`
(one native call, cheap) and a full scene enumeration with `MBEditor.IsEntitySelected`
per entity (thousands of interop calls - 7-11k on a real scene). Anything that runs
per-frame or per-keypress must never touch the second path.

The specific bug: an empty result from the fast path was treated as suspicious and fell
through to the scan. Since the editor clears the selection on a keypress *before* our
Postfix runs, every hotkey hit that path by definition, and so did the drag watcher's
0.06s poll whenever nothing happened to be selected.

If a hotkey feels heavy again, check tool.log for `[SelectionDiag] fallback scan used` -
that line only appears when the fast path actually threw, which should be never.

## Crash when closing or switching a scene

The editor tears a scene down over several frames - the engine log shows
`HandleDeactivate`, `Scene_view::clear_all(<scene>)`, `HandleFinalize`, `PopScreen` in
sequence, and a crash here usually ends with `QCoreApplication::postEvent: Unexpected
null receiver`. Anything of ours still holding that scene, or still adding widgets to
the screen being destroyed, is a candidate.

Two rules this codebase learned the hard way:

1. **A GauntletLayer must be removed from the screen it was ADDED to**, not from
   `ScreenManager.TopScreen`. At teardown those are different screens, so removing from
   TopScreen silently does nothing and leaves the dying screen holding the layer.
   `LastOperationHudLayer` had this bug until 2026-08-22.
2. **Nothing may create a layer while no scene is open.** Only `LastOperationHudLayer`
   re-opens itself every tick, which makes it the one most exposed to that window; it
   now refuses when `EntitySelector.HasOpenScene` is false.

All scene invalidation lives in one place - `BackupManager.InvalidateSceneState` - and
runs both on a scene SWITCH and on a scene CLOSE. The close case had no handling at all
before 2026-08-22: the old code only reacted to a transition to a different *named*
scene, so closing a file back to no scene left every cache, undo step and open layer
pointing into a scene being destroyed. Look for `[SceneGuard]` in tool.log to see it
fire.

## A panel is open but stuck / not updating

Panels only tick while their owning tool's patch is registered. If a panel misbehaves
after editing tool_toggles.txt, check which tool owns it: F7/F8/F9 belong to
MaterialSwapTool, F5 to PrefabCreatorTool, F6 to PrefabSwapperTool.

## Undo did nothing / says entities are gone

The shared undo stack (EditUndo) holds live entity references and is deliberately
cleared on every scene switch - stale pointers from a closed scene are a crash, not a
history. "N no longer exist" in the message means exactly that. Deletions were never
on this stack (Delete Interior Entities, Break Prefab Links, Remove Physics); only the
scene backup brings those back.

## Mirrored/cloned copy looks wrong

EntityCloner refuses two things rather than guessing: an entity with no prefab name
(nothing to instantiate from), and a source/copy pair whose mesh trees don't line up
(overrides skipped entirely rather than applied to the wrong meshes). Both are
reported in the result message and tool.log, not silent.
