Bannerlord Scene Toolkit
========================

Editor tooling for Mount & Blade II: Bannerlord scene work - material swapping,
prefab swapping and distribution, prefab creation, scene analysis, and backups.

All three tools are ENABLED in this package (see tool_toggles.txt). Earlier
builds shipped with Prefab Swapper and Prefab Creator switched off; they are on
now, so every hotkey below is live.


HOTKEYS (in the scene editor)
  F5   Prefab Creator
  F6   Prefab Swapper and Distribution
  F7   Scene Analyzer
  F8   Material Swap
  F9   Backups


PANEL SIZE (if the panels are too big or too small for your screen)
One size applies to ALL panels. With any panel focused (click it first):
  Ctrl + -    make the panels smaller (10% per press)
  Ctrl + =    make the panels bigger  (the unshifted '+' key)
  Ctrl + 0    reset to the default size (80%)
Number-row keys only (the numpad belongs to Grow Selection, Ctrl+Numpad+).
Range is 50%-150%. Panels open at 80% by default;
whatever you set applies to every panel you open afterward and persists across
sessions (saved in
  Documents\Mount and Blade II Bannerlord\MaterialSwapTool\PanelScales.json).

PANEL POSITION
  Drag any panel by its title row. Ctrl + Alt + 0 (or Ctrl + Shift + Numpad 0) pops every
  OPEN panel back to the centre of the screen - for when one has wandered off the edge.
  (Ctrl + Shift + 0 on the number row also works, unless Windows has that combo reserved
  for input-language switching, which it does by default.)


INSTALLATION
1. Copy the BannerlordSceneToolkit folder into
       ...\Mount & Blade II Bannerlord\Modules\
2. Copy tool_toggles.txt into
       Documents\Mount and Blade II Bannerlord\BannerlordSceneToolkit\
   (create that folder if it doesn't exist) BEFORE first launching with the mod
   enabled. If you launch first, the game writes its own default file and you
   can edit that instead - either works, this just saves a step.
3. Enable "Bannerlord Scene Toolkit" in the launcher's mod list, start the
   editor build, and confirm the hotkeys above open their panels.
4. Check Documents\Mount and Blade II Bannerlord\BannerlordSceneToolkit\tool.log
   for a line like:
       "Tools active this session: MaterialSwapTool=True,
        PrefabSwapperTool=True, PrefabCreatorTool=True."
   to confirm the toggles took effect.


BACKUPS - READ THIS BEFORE USING THE DESTRUCTIVE TOOLS
Several operations cannot be undone from inside the toolkit: Delete Interior
Entities, Break Prefab Links, Remove Physics Outside Border, and Simplify for
Export. Backups are enabled by default and run before each of those, on scene
change, and on a timer.

Backups copy the SAVED files on disk (scene.xscene, terrain.bin,
terrain_ed.bin) for the open scene. They do NOT save the scene for you, and they
do NOT cover navmesh or flora. Anything you have changed but not saved is not in
the backup - F9 warns when the scene has gone 15 minutes without a save.

F9 shows how old the newest backup is, and lets you change the interval,
retention and location.


HOW TO ASSEMBLE THIS PACKAGE (for the author, not the recipient)
Run Tools\Package-SceneToolkit.ps1 from the repo. It stages the deployed module
into this folder (excluding SceneObj\, SceneEditData\ and *.bak - working data,
not part of the mod), warns if the deployed DLL is older than the source, and
zips it together with this README, LICENSE.txt and tool_toggles.txt. It runs
by hand only - no build step invokes it. Older zips are in ..\Archive\.

See LICENSE.txt for usage terms.
