# Known issues and outstanding work

Status as of 2026-08-22, end of the EVENING session (the daytime v0.7 list is below,
updated). Everything is deployed unless it says otherwise. See CHANGELOG.md.

## Known limitations (confirmed live, 2026-08-22 evening)

- **Runtime-created entities hold stale editor state until save/reload (2026-08-23).**
  Pile pieces and other runtime-created entities don't respond to editor visibility
  toggles, and runtime physics changes don't show, until a save + reload rebuilds the
  editor's caches - same registry disease as the CopyFrom ghost copies.
  UpdateSceneTree is nudged after pile generation; save/reload is the full heal.
- **Runtime physics stripping cannot survive reload on prefab-reference entities
  (confirmed 2026-08-23 via the saved xscene).** A plain Instantiate saves as
  <game_entity prefab="X"> and the loader rebuilds physics from the prefab
  definition every load - RemovePhysics/BodyFlags/PhysicsDisabled/SetPhysicsState
  all evaporate. Delete Physics pile pieces are therefore created as BROKEN-OUT
  CopyFrom entities (serialize with explicit components; no physics node once
  stripped), trading prefab identity for persistence.

- **CCModule_SP in the editor: DLL errors fixed, texture warnings unfixable.** The
  module ships only Client binaries - its wEditor bin was created and populated
  2026-08-23, which cures the "Cannot find ...dll" popups. The "Unable to find
  texture: ui_cc_scenes_*" (etc.) RGL warnings CANNOT be fixed from outside: the
  sheets exist in the module's pack0.tpac but the editor does not mount them, and
  the editor's texture resolver ignores the loose GUI\SpriteSheets PNG path the
  game client uses (extraction was tried - correct layout, correct names, still
  not consulted; the PNGs remain in the module harmlessly). ui_ccbanners_1 is
  additionally missing from the module's assets entirely (client-side too).
  Remedy: deselect CCModule_SP in the launcher for editing sessions that don't
  need it; otherwise click through the warnings once per launch - cosmetic only.
  FINAL RESOLUTION 2026-08-23: the popups fire during NATIVE editor startup,
  BEFORE any module DLL loads (proven by timeline - popups shown while the
  toolkit's load line was still absent), so no in-process fix can ever exist -
  the in-mod TextureRescue preloader runs too late by definition (kept anyway;
  harmless and may serve loose sheets for later lookups). The working fix is
  Tools\Dismiss-RglPopups.ps1: an EXTERNAL watchdog that auto-closes #32770
  dialogs whose title contains "RGL" - start it before (or during) an editor
  launch; it exits on its own 10 minutes after the last TaleWorlds process.

- **Scene-close teardown crash is vanilla and harmless.** Closing the scene editor
  screen (window X / scene close) dies in native Qt teardown AFTER the save and after
  Scene_view::clear_all - data is never lost, but the watchdog may write a ~770MB dump
  to C:\ProgramData\...\crashes, and sometimes the process survives to the dashboard.
  Confirmed with the toolkit's teardown guard installed and firing. Exit via the
  application's own quit path for a clean shutdown. Not fixable from a managed mod.
- **Prefab-less mirror + odd flips renders inside-out until save/reload.** A
  hand-built (no saved prefab) source mirrored an odd number of times goes through
  CopyFrom + a handedness-flipping SetGlobalFrame, and the engine's winding cache is
  not reachable from managed code. The SAVED data is correct - reload displays it
  right. Prefab-backed sources (and mirroring an anchor, which expands to children)
  are unaffected.
- **Swap mode vs prefab instances**: deleting pieces that live INSIDE a placed prefab
  instance trips the editor's "break prefab?" dialog per piece and half-fails. Use
  the ADD mode toggle (F5 swap section) for those - place new, keep originals,
  delete manually.
- **Shift+O isolate**: third binding in one day (bare-I places-on-ground with
  modifiers held; a native Ctrl+Shift+O hides the selection). Known cost: a capital
  O typed into a text box may trigger isolate. The once-per-session hint announces
  the exit key; F9 can silence it.
- **Scale never carries over any Instantiate-based operation** (swap, distribute,
  by-name mirror) - the API has no scale setter. Long-standing, restated here
  because Add mode makes it visible side-by-side with the scaled original.

## Needs live testing (updated 2026-10-04)

Validated live 2026-08-22 evening, no longer open: Distribute Onto Surface (multiple
rounds: upright fix, multi-target filter, fill-target-surfaces, scale
normalization), Mirror through EntityCloner + CopyFrom fallback + anchor expansion,
swap-set Apply, F5 swap selection reads, Max Instances cap. Grow Selection
(Ctrl+Numpad+, step sizes) confirmed tested by the user, 2026-10-04.

Still unexercised from August: the Ctrl+Shift+P timing log check. Closed 2026-10-04: Auto
Origin on several prefabs CONFIRMED by the user; category import/export round-trip
DECLINED (user does not use it); log rotation VERIFIED from evidence - tool.log.old of
2026-09-17 is 10,485,763 bytes, i.e. the roll-over three bytes past the 10 MB limit.

Added 2026-10-03, deployed, not yet seen in the editor (smoke steps in CHANGELOG.md):
- Native cursor in panels: CONFIRMED by the user 2026-10-04 with the SECOND version (the
  unconditional skip of ActivateMouseCursor in the editor). The first, visibility-gated
  version did nothing - the editor evidently reports its mouse as always visible.
- F9 "Backups folder on disk" line: CONFIRMED by the user 2026-10-04 (2.49 GB shown).
- Tag-based Isolate restore: CONFIRMED across reloads by the user 2026-10-04. Test-mode
  round trip deliberately not tested (user: not going to bother).
- Grow / scaling key split: CONFIRMED by the user 2026-10-04 (numpad = Grow, number row =
  panel size, both directions).
- Strip-physics button relabel: CONFIRMED fits, 2026-10-04.

## Original daytime list (kept for the still-open items)

Almost everything built on 2026-08-22 compiles and is deployed, but most of it has
never been exercised in the editor. Untested code is not finished code, and the two
freeze bugs found this session were both invisible to the compiler. In rough order of
risk:

1. **Distribute Onto Surface** (F6) - the newest and least proven, because it depends
   on the ported raycast (`Core/SurfaceSnap.cs`). Test small first: one column, 3x3,
   Local axes, "keep upright", on a slope. Watch the status line for skipped cells and
   `[NativeTrace] ... DistributeOntoSurface` in tool.log for placed/missed/failed.
   The technique itself is PileGenerator's, which works; the risk is in the porting.
2. **Grid source: Selection** (F6) - tiling copies of a selection, including a
   multi-entity assembly. Check that per-instance recolours survive and that cell
   (0,0) keeps your originals un-reparented.
3. **Auto Origin** (F5) - re-origin several composite prefabs at once. Meshed entities
   should be SKIPPED and named, not moved.
4. **The hotkey freeze fix, round two** - Ctrl+Shift+P on a big selection. The proof is
   in tool.log: `[SelectInEditor] applied: N/M ...; X stale deselected; Yms`. If
   `stale deselected` is 0, the engine replaces the selection wholesale and the old
   clear-loop was pure waste; if it is non-zero, it adds, and the new ordering is still
   correct but the cost is inherent.
5. **Grow Selection** (Ctrl+Numpad+) - confirmed to open and index (12,078 entities on
   CC_76_battle_testing, instantly); the styling fix and the three step sizes
   (plain 1 / Shift 5 / Ctrl 0.25) have not been seen yet.
6. **Randomize Rotation** (F5 Pile Generator), **Mirror through EntityCloner** (F6 -
   mirror a renamed, recoloured entity and check the copy keeps its colours),
   **Category import/export** (F8 Category Editor round-trip), **backup consolidation**
   (an unchanged scene should log a skip and raise NO "avoid Save" warning), **log
   rotation** (tool.log rolls to tool.log.old at 10 MB).
7. **The new data** - four palettes (Italian, European, CC74/CC76 Battle), three new
   cultures (italian, european, roman_ruins), and the eight rebuilt BuiltIn presets
   (75-154 rules each). Worth spot-checking one preset apply against a real scene
   before trusting the bulk-generated rules.

## Open questions for the user

- **FiefMaps is deselected in the launcher**, which is why it does not appear in the
  editor's Save As list. Offered to flip `IsSelected` to true in LauncherData.xml;
  no answer yet. It also only exists in the `<SingleplayerData>` block, so a
  multiplayer-context session would need it added there too.
- **The `stone` category's Include patterns contain "floor"**, so every floor material
  classifies as stone as well. The runtime resolves the overlap alphabetically (floor
  wins) and the preset rebuild follows the same rule, so nothing is broken - but if
  that overlap was not deliberate it is worth pruning in the Category Editor.

## Accepted, not bugs

- **Tool toggles do not fully isolate tools.** Disabling a tool kills its hotkey and
  its own tick, but other tools still call into its code (one assembly). Documented in
  TROUBLESHOOTING.md. The crash A/B testing that motivated strict isolation is
  resolved, so this no longer matters.
- **Deliberate near-duplicates remain**: `PanelPositionStore` / `ScreenshotManager`
  (separate data folders by design), `PanelDrag` (tied to those stores),
  `EntitySelector`, `LivePrefabSwapper` (MST's serves the Scene Analyzer fixers, PST's
  is the full-featured one). Merging needs a data-path migration plan.
- **PileGenerator keeps its own copy of the raycast maths** rather than calling
  `Core/SurfaceSnap`. Deliberate: it works today, and the point of the shared copy is
  to stop new callers re-deriving fragile maths, not to refactor a working feature.

## Fixed this session

All verified by compiler and deployed; see the testing list above for what still needs
eyes on it in-game.

1. Two separate causes of the hotkey freeze: an empty-selection fast-path falling
   through to a full scene scan, and `ApplyEditorSelectionNow` clearing the old
   selection one native call at a time (~12ms each).
2. F6 would not open at all - a `</ButtonWidget>` that should have been `</ListPanel>`.
   Gauntlet parses panel XML at open time, so the build reported 0 errors. The build
   now validates every panel/brush XML before compiling (`ValidatePanelXml` target),
   proven by deliberately re-breaking a panel.
3. Mirror re-instantiating by entity `.Name` instead of `GetPrefabName()`.
4. Scene-switch stale-pointer cleanup only running when MaterialSwapTool was enabled.
5. Triple backup timers with un-gated "avoid Save" warnings.
6. Last-operation HUD stuck on screen when Numeric Transform was disabled mid-session.
7. "Rename Changed" stacking `_mst` suffixes on repeat applies.
8. Multi-prefab shift-drag copies not recording for Shift+R (selection compared
   index-by-index when the engine promises no ordering).
9. Continuous Recolor's Close button sitting outside the panel; Grow Selection panel
   styled as a blue slab.
10. Unbounded tool.log growth; stale in-editor documentation; `Distribution/` stale
    snapshot hazard.

## From the tutorial-video review (2026-10-04, see docs/VIDEO-VS-DOCS-REVIEW.md)

Behaviour the user demonstrated on the 2026-08-23 build and that nothing since has
addressed:

- **Full Scan (F7) can break the editor's placement preview** (the ghost shown before
  placing an entity). Save and reload clears it. Cause unknown; F7 in general suspected.
- **Delete Duplicates sometimes removes nothing until Tag Duplicates has run first.**
  Running Tag then Delete worked every time.
- **Shift+R occasionally repeats in the reverse direction on X or Y, never Z.**
  Undiagnosed; possibly world-vs-local. One Undo away.
- **Color factor quirks:** re-applying a color factor to a just-colored slot sometimes does
  nothing; undo occasionally leaves a color factor wrong. Revert to Normal is the reset.
- **Siege checks:** the castle-corner warnings and the siege-engine spawner warnings have
  never been validated against a known-good siege scene.
- **Stutter warnings** have never been seen to fire (user's machine is too fast). Unverified.
- **Repair Unselectable Copies (F5) and Strip Physics From SAVED Scene (Pile Generator)**
  are both manual fallbacks for passes that now run automatically (scene-open flag sweep;
  post-save strip). Expected result in normal use: "nothing found" / "already stripped".
