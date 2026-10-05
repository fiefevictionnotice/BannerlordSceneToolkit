# Changelog

## 2026-10-04 - Ctrl+Shift+0 centres every open panel

- New `Core/PanelRecenter` (generation counter). The hotkey, handled in MaterialSwapTool's
  tick OUTSIDE the panel-focus gate, bumps the counter; each of the three `PanelDrag` copies
  compares it on Tick and moves its open panel to offset (0,0), saving that position. Panels
  closed at the time sync on open and keep their saved spot. `PanelScale.HandleHotkeys` now
  ignores Ctrl+0 while Shift is held so the two do not both fire. Documented in the Shortcuts
  topic and the shipped README.
- **Live test: Ctrl+Shift+0 never reached the game.** No request logged; the plain Ctrl+0
  size reset fired at the same instant. Root cause confirmed in the registry
  (`HKCU\Control Panel\Input Method\Hot Keys\00000104`: modifiers Ctrl+Shift, virtual key
  0x30): Windows reserves Ctrl+Shift+0 for input-language switching and swallows it.
  Bindings widened to **Ctrl+Alt+0** and **Ctrl+Shift+Numpad0** (the original still
  accepted if Windows lets it through); the first five Ctrl+0 presses per session log the
  modifier state the engine reports, as `[PanelRecenter] zero pressed: ...`.

## 2026-10-04 - Native cursor, second attempt (deployed, CONFIRMED working by the user)

- First live test: no visible change with the 10-03 version, toggle included (tool.log
  shows the prefix applied and the setting ON). Diagnosis: the editor most likely reports
  its mouse as always visible, so the visibility-gated prefix never closed and the
  hover-time `ActivateMouseCursor(Default)` swapped the hardware cursor every time.
- `ToolkitCursor.ActivateMouseCursorPrefix` now skips the call UNCONDITIONALLY in the
  editor while native mode is on, and logs the first five calls per session as
  `[ToolkitCursor] ActivateMouseCursor(<type>) call #n: engineMouseVisible=<bool> ->
  SKIPPED`. If the cursor still swaps with those lines present, the swap is native and
  the remaining route is a half-size cursor file (MouseManager.SetMouseCursor, or
  replacing Data\cursors\mb_cursor.cur).

## 2026-10-04 - Documentation pass from the tutorial video

Source: docs/TUTORIAL-TRANSCRIPT.md (the 2 h 31 m tutorial, recorded 2026-08-23) compared
against the in-editor docs; findings in docs/VIDEO-VS-DOCS-REVIEW.md. User guide only
(DocumentationVM.cs); no behaviour changed except one button label.

- **Corrected:** numeric transform keeps off-axis drag movement (doc said it reset);
  there ARE two LOD fix buttons, colour and material, both LOD0-outward and ~90%
  reliable (doc said material was never auto-fixed); Batch History rows each have
  Undo/Redo in any order (doc said review-only); the panel button is "Simplify for
  Export", every "Solidify" in the docs renamed; the Culture Preset Generator's
  category-role fallback bridge documented (doc claimed nothing is generated without a
  shared Empire source).
- **Added, previously undocumented:** weighted / multi-output rule syntax and per-prefab
  variance, Dedupe Rows and Dedupe Multi-Output; Distribute Along Path; relative vs
  absolute spacing and the count minimum; Set Primary / Match Secondary Origin to Primary
  (furniture decorating) and the no-empty-no-origin rule; the "why this tool exists" LOD
  slot explanation on the F8 overview; camera lock while typing; Shift+R caveats (save
  heals odd copies, no scale, rare X/Y reversal); numeric readout timing and the editor's
  own Alt+W/Alt+L; UID tag semantics (orphaned by deletion, shared by clones, survives
  reload); Revert to Normal's reference-data mechanism and the colour-factor quirks;
  backups advice (no warranty, rotate scene names); Delete Interiors safe routine (Save the
  whitelist, read the list); Tag Invisible/Locked search tips; Mirror custom-point gotcha
  and true-flip explanation; which swap to use for real vs broken prefabs; Pile Generator
  anchor-should-be-an-empty, layer settling, texture capture; palette provenance (Craglow,
  Terra Volus) and the hex-length refusal.
- **Demoted:** "Strip Physics From SAVED Scene" relabelled as a manual fallback (the
  post-save pass runs automatically; the button's normal answer is "already stripped").
  Repair Unselectable Copies noted the same way in KNOWN-ISSUES.
- **KNOWN-ISSUES** gained the video's undocumented behaviours (Full Scan breaks the
  placement preview, Delete Duplicates needs Tag first, Shift+R reversal, colour-factor
  quirks, unvalidated siege warnings, stutter warnings never seen). **ROADMAP** gained
  the alpha-texture filter for culture presets and retiring Dry Run's reselect.
- Compiled with SkipDeploy; DEPLOY PENDING with the Grow/scaling key split below.

## 2026-10-04 - Grow Selection and panel scaling get separate keys

- **The collision.** Grow Selection opened on Ctrl+Numpad+ OR Ctrl+Equals (08-23 alias);
  panel scaling (09-13) took Ctrl+Minus/Equals/0 AND their numpad twins on every focused
  panel. Which one fired depended on panel focus, and inside the Grow panel one
  Ctrl+Numpad+ both stepped the radius by 0.25 and enlarged the panel.
- **The split (user's call: "a separate shortcut is the most obvious").** Grow Selection
  owns the NUMPAD: opener is Ctrl+Numpad+ only (Ctrl+Equals no longer opens it). Panel
  scaling owns the NUMBER ROW: Ctrl+Minus / Ctrl+Equals / Ctrl+0 only, numpad aliases
  removed (`PanelScale.HandleHotkeys`). Inside the open Grow panel both rows and the
  arrows still step the radius, as before. `HandleHotkeys` also gained a `handleKeys`
  switch, false on the Grow layer, so scale keys are ignored there outright.
- Shipped README.txt panel-size section and the in-editor Shortcuts topic updated.
  Compiled against a running editor (SkipDeploy); DEPLOY PENDING until it is closed.
- Isolate's tag-based restore (10-03) confirmed working across reloads by the user.

## 2026-10-03 - Isolate survives saves, reloads and test mode

- **Why it failed.** `IsolationManager` kept its restore record only as live entity
  pointers and its own comment said hidden state never reached disk. Wrong on both
  counts: scene.xscene stores `visible="false"`, and the record was dropped by
  `InvalidateSceneState` on every scene-screen teardown, which fires on ENTERING TEST
  MODE too. Isolate + save + reload, or Isolate + test mode + back, left the scene
  hidden with Shift+O reporting "Nothing is isolated".
- **Fix: the scene carries the record.** Every entity Isolate hides gets the tag
  `mst_isolated` (saved with the scene, untouched by test mode); Restore un-hides by tag
  as well as by the memory record. Teardowns (`Clear`) and scene opens (new
  `OnSceneLive`, called from BackupManager's scene-change block) arm a deferred tag
  scan that runs from the new `IsolationManager.Tick` 3-5 s after the scene is live,
  rebuilds the record and raises an editor warning "Isolate is still active from an
  earlier session ... Shift+O restores them". Deliberately not on the first resumed
  frame: a whole-scene native query there is what crashed the editor on 2026-08-31.
- **Save-while-isolated warning.** While isolated, the scene file's write time is
  polled once a second; a save raises one warning that the hidden state is now in the
  file. Entities already hidden before isolating are never tagged or touched.
- Only entities Isolate actually hid are recorded now (the old dictionary also stored
  "was already hidden" entries it never acted on). UNTESTED IN-EDITOR at the time of
  writing. Smoke test: isolate, save, grep scene.xscene for `mst_isolated`, reload,
  expect the warning, Shift+O, expect everything back and the tags gone; then isolate,
  enter test mode, return, expect the warning, Shift+O.

## 2026-10-03 - Native cursor in panels; Backups folder size on F9

- **Panels no longer swap in the engine's large Gauntlet cursor.** Every toolkit layer
  called `InputRestrictions.SetInputRestrictions()` with its default `isMouseVisible=true`;
  `ScreenManager.UpdateMouseVisibility` then asks the engine for a visible mouse, which in
  the editor means the game's hardware cursor (`Data\cursors\mb_cursor.cur`) replaces the
  Windows arrow. The flag has no other effect (hit-testing, focus, clicks and wheel all come
  from the `InputUsageMask`, verified against the decompiled ScreenSystem), so all 28 layers
  now open through the new `Core/ToolkitCursor.ApplyInputRestrictions`, which passes
  `isMouseVisible=false` in native-cursor mode. A Harmony prefix on
  `MouseManager.ActivateMouseCursor` additionally skips the hover-time cursor-type swap while
  the engine cursor is hidden (editor only; the game client is never touched).
- **Switchable: F9 -> Shortcuts -> "Native cursor: ON/OFF"** (`ShortcutSettings.NativeCursorEnabled`,
  default ON). Toggling re-applies to every open panel on the spot via
  `ToolkitCursor.ReapplyToOpenLayers`, so if the cursor ever vanishes in native mode the way
  back is one click. UNTESTED IN-GAME at the time of writing - the first smoke check is: open
  F9, confirm the Windows cursor stays, hover/click/scroll a panel, then toggle both ways.
- **F9 shows the Backups folder's size on disk.** New line under the backup count: whole
  folder (scene backups + `_ToolData` + anything else), the two parts broken out, and free
  space on that drive. `BackupManager.GetStats` gained `ToolDataBytes`, `FolderBytes`,
  `DriveFreeBytes` plus a `FormatBytes` helper (KB/MB/GB). Retention only prunes scene
  backups, so the per-scene number could stay flat while the folder grew - that is why it is
  a separate line. Panel height 610 -> 638; Shortcuts flyout 608 -> 654.

## 2026-09-13 - UI scaling is now global (was per-panel)

- **One scale for all panels.** The per-panel scheme meant a window popped out after you
  shrank another one opened at the default size instead of the size you had just set.
  `PanelScaleStore` now holds a single global value; every panel reads it on open. Call
  sites are unchanged (the `panelKey` argument is now ignored).
- **Open panels resize live.** Each open panel re-syncs to the global scale on its tick, so
  a change made from any panel reaches every other open panel within a frame. It only writes
  when the value actually differs, so steady state is a float read + compare with no layout
  churn (no per-tick reassert).
- **Ctrl+0 resets to the default (80%), not 100%.** Reset was snapping panels *up* to 100%,
  which is bigger than the 80% default; it now returns to `PanelScaleStore.DefaultScale`.
- Keys unchanged: Ctrl+Minus shrink, Ctrl+Equals grow, Ctrl+0 reset; range 50%-150%.

## 2026-09-13 - Per-panel UI scaling

- **Each panel can be scaled independently.** Requested because the F8 Material Swap
  panel is too large on a friend's screen. Every toolkit flyout is its own GauntletLayer
  with its own UIContext; setting that context's `ScaleModifier` rescales one panel and
  all its children without touching any other panel or the editor's own UI.
  - Live keys while a panel holds focus: **Ctrl+Minus** shrink, **Ctrl+Equals** grow
    (the unshifted `+` key), **Ctrl+0** reset to 100%. Numpad -, + and 0 also work.
  - Range clamped to **50%-150%** so a panel can't be shrunk to nothing or grown until its
    Close button leaves the screen.
  - Per-panel value persists across sessions in
    `Documents\Mount and Blade II Bannerlord\MaterialSwapTool\PanelScales.json` (keyed by
    the same short panel name PanelPositions.json uses; hand-editable, re-clamped on load).
  - New `PanelScaleStore` (Core) + `PanelScale` helper (GUI); `PanelScale.Apply` on open and
    `PanelScale.HandleHotkeys` per tick wired into all 28 interactive layers. The passive
    Last-Operation HUD readout is intentionally not scalable (no focus/hotkey path).

## v0.8.0 - post-snapshot fixes (2026-08-27 .. 08-31)

Shipped in the 2026-09-13 package (v0.7.0 -> v0.8.0); these post-date the
08-24 snapshot below and were the delta packaged for release.

- **Editor no longer crashes on leaving test mode.** With the toolkit enabled,
  exiting test mode reliably crashed the editor (worst on large scenes like
  CC_88_battle). Root cause: `BackupManager.OnEditorScreenTearingDown()` nulled
  `_lastSeenSceneName`, so the next tick treated the resumed scene as a brand-new
  open and re-ran a whole-scene native entity sweep on the still-streaming resume
  frame -> native null deref. Fix: don't null it on teardown; returning to the same
  scene is now "no change" and the sweep is skipped. (BackupManager.cs, 08-31.)
- **Material Swap "Apply Anyways".** An unrecognized material no longer forces
  auto-correct-or-cancel; apply-as-is is now an option. nord_terrain_rock and other
  post-War Sails nord assets are recognized, and a full material re-export was merged
  in. (MaterialSwapVM.cs / MaterialSwapPanel.xml, 08-27.)
- **LOD-mismatch false positive fixed** for the default_regular placeholder: it is a
  placeholder default, not a real per-LOD mismatch, and is now guarded. (LodMismatchChecker.cs,
  IsPlaceholderDefault, 08-28.)
- **Backups panel (F9)** close button now fits on screen; button widths tidied.
- **Pile Generator UI**: recipe scrollbar enlarged, rock-count inputs widened for two
  digits, dedupe-multi-output button widened, fill placement from selected coordinates.
  (PileGeneratorPanel.xml, 08-27.)

## v0.8.0 - 2026-08-24 (snapshot)

Full-source snapshot at `BannerlordModArchive\v0.8\`; SubModule.xml bumped
v0.7.0 -> v0.8.0. Captures the 2026-08-23 marathon and the 2026-08-24 docs split:
the numeric-transform overhaul and final one-axis semantics, the whole mirror/
distribute/swap/undo fix stack, weighted-rule granularity, the Delete Physics
five-round saga (broken-out pieces + saved-scene stripper + auto re-strip), the
live-reference swap fixes, cross-culture definitional-children handling, Rotation
(Entity/Grid), the panel-focus drag-ghost fix, and the User Guide / Technical Docs
separation. Everything below this line is that body of work.

## 2026-08-24 - Documentation split: User Guide vs Technical Docs

- The in-editor documentation is now TWO windows. The Documentation buttons
  everywhere open the USER GUIDE - how things behave today, with changelog-style
  histories removed (kept only where a brief why-it-works-this-way genuinely helps).
  A new TECHNICAL DOCS button on F9 opens the migrated material: 15 topics of
  engineering history, confirmed-bug diagnoses and engine limitations (selection
  timing, the 2026-08-19 color discoveries, the Delete Physics five rounds,
  CopyFrom tree behaviour, undo trust, weighted-roll granularity, stale editor
  state, isolate binding history, flora API limits, and more). Nothing deleted -
  migrated, with pointers from guide topics to the matching technical topic.
- Stale user-guide content updated in the same pass: numeric transform (final
  one-axis semantics, L/C keys, slider range, box-select commit), Pile Generator's
  Delete Physics (broken-out pieces, pile_nophys, auto re-strip, reload rule),
  Prefab Swapper (scale toggle + Z-rot/scale-mult inputs, definitional-children
  rule, live-reference exact-copy semantics), Distribution (Rotation Entity/Grid,
  name-only fill targeting, Fill From Selection capturing the origin), Continuous
  Recolor (Apply Colors one-shot).

Version history for the Bannerlord Scene Toolkit. There is no git here by choice - this
file plus full-source snapshots in `Documents\BannerlordModArchive\` are the version
trail. Snapshot before every large change (v0.6 set the pattern); day-to-day edits just
get dated entries here.

## 2026-08-22 - Distribution panel UI rework (mode selector)

Source snapshot of the state immediately before this pass:
`BannerlordModArchive\2026-08-22-pre-surface-ui-rework\`. Direct feedback: "the
distribute onto surface UI is REALLY confusing."

- **One mode on screen at a time.** The Distribution panel (F6) now has an In Grid /
  Along Path / Onto Surface selector at the top; only the active mode's controls are
  shown (the same IsVisible group-collapse pattern the browser panels use), with a
  per-mode explainer under the selector and ONE Distribute button at the bottom that
  runs whichever mode is visible. The old layout showed all three modes' fields in one
  undivided stack with three buttons - reading it required already knowing which fields
  fed which button.
- **Surface filter relabeled as a filter.** "Surface target ... entity name to land on"
  read as if the name POSITIONED the grid; it never did - it only restricts which
  raycast hits count as ground (the ray passes through everything else). Now labeled
  "Only land on (optional)" with an explainer saying exactly that, and the fill button
  reads "Use Selected Entity".
- **Live summary line.** A plain-language read-back above the Distribute button,
  rebuilt from the typed inputs on every change: what will be placed, how many, along
  what / onto what, upright or tilted - plus a warning when a surface-mode axis with
  count > 1 points up (Z).
- **Surface mode no longer errors out of the box.** DistributeOntoSurface rejected ANY
  vertical axis, but the default Axis2 is Local Z (right for stacking wall courses in a
  flat grid) - so a fresh panel's single-row surface run failed immediately. An axis
  with count 1 never steps and is now exempt from the check.
- **Prefab row hides in Selection mode** instead of sitting there with an "ignored
  when..." caveat.
- Fixed the initial path-spacing label still claiming the spacing box is ignored in
  Relative mode - it has been a gap on top of the measured size since the
  PrefabDistributor change, and the toggle already said so.
- **"Keep upright" now actually keeps things upright (by-name mode).** Confirmed via
  tool.log: the toggle state arrived correctly (alignToSurface=False), but upright mode
  copied the origin entity's rotation VERBATIM - so a tilted origin (placed with the
  editor's own align-to-ground, or a piece from a previous tilt-mode run) tilted every
  instance in both modes and the toggle looked dead. Upright mode now erects the
  rotation (AlignUpToNormal against world up): heading and scale survive, pitch/roll
  are zeroed, an already-upright origin is untouched. Selection mode deliberately still
  keeps each source's rotation as authored - a leaning assembly must not be
  straightened. Surface runs now also log the origin's up vector for diagnosis.
- **Surface filter accepts multiple targets.** The "Only land on" box takes
  comma-separated names, and "Use Selected Entities" fills it from the whole current
  multi-selection (distinct names) - so one run can span several surfaces at once, e.g.
  a grid crossing two terrace pieces and the rocks between them. A cell counts a hit as
  ground if the entity (or its prefab root) matches ANY of the names; matching is still
  by name, and position selectivity still comes from the raycast footprint, not the
  names - a same-named entity outside the grid's footprint never sees a ray.
- **Fill Target Surfaces** (Onto Surface extent toggle, prefab-name source only). The
  grid's footprint is derived from the captured target surfaces' combined world
  bounding box instead of typed as counts - cells step along world X/Y at the
  Axis1/Axis2 spacing+gap (relative spacing measures a probe instance of the prefab,
  placed above the box and removed), and the per-cell filtered raycast trims the
  rectangle to the surfaces' actual silhouette for free (off-surface cells miss and
  are skipped). The footprint uses the ENTITY REFERENCES captured by Use Selected
  Entities, not a name lookup - a bounding box is a scene-wide claim in a way a
  raycast is not, and a same-named entity across the map would silently inflate it.
  Piece heading comes from the first captured surface; the 500-instance cap reports
  the derived grid size and suggests larger spacing when exceeded. Same
  manual/derived pairing as the path's Fill Path Length toggle.
- **By-name surface placement no longer inherits the target's scale.** This engine
  encodes scale as the rotation basis vectors' lengths, and by-name Onto Surface
  borrows its heading rotation from another entity (the origin; in fill mode, the
  first captured surface) - a scaled terrace's scale rode along inside that rotation
  (confirmed in the log: originUp z-length 1.219) and every placed prefab inherited
  it. The borrowed rotation is now normalized to unit basis lengths, so instances
  keep the prefab's own authored scale. Selection mode is deliberately untouched -
  there each piece's rotation (and scale) is its own real content.
- **Max Instances cap is typed** ("Max instances per run" box, default 500, clamped
  1-100000), applied by every distribute mode; the fill-mode cap error now spells out
  the arithmetic (cells = footprint / cell size - the number of captured surfaces
  never mattered) with the actual footprint and cell size in the message.
- **Fill Target Surfaces: the active selection IS the targets.** Selecting surfaces
  and then capturing them with Use Selected Entities was pure ceremony in fill mode
  (the selection means "origin" only in counts mode) - direct feedback: "can't the
  active selection be sufficient????". Whatever is selected when Distribute is
  clicked is now the footprint, and a BLANK filter box auto-derives the ray filter
  from the targets' own names (the mode is called Fill TARGET Surfaces - landing on
  clutter sitting on them would betray the name; a typed filter is still respected).
  The captured set remains only as a fallback for an empty selection, and the status
  line reports which source was used plus any auto-derived filter.
- **Mirror works on prefab-less entities + honest failure.** Mirror was the only clone
  consumer without the GameEntity.CopyFrom fallback, so a hand-assembled/combined
  piece with no saved prefab (confirmed report: fief_aserai_villa_arch_mod wall in
  CC_76) could tile in a grid but not mirror. It now clones via CloneSourceAt (prefab
  route first, live CopyFrom fallback). Also: a mirror where every clone failed used
  to CREATE an empty _Mirrored anchor and report success - now it's an error, no
  anchor, and the per-entity clone failure reason is logged to tool.log.
- **Mirror contamination tripwire.** Confirmed in CC_76: a mirror source that already
  CONTAINS mirror-flipped geometry (a stowaway copy from an earlier run nested inside
  it, spread further by shift-drag CopyFrom) mirrors into doubled/inside-out chaos
  that reads as the tool exploding when it's garbage-in. Mirror now counts
  negative-determinant frames in the selection's tree up front and appends a WARNING
  to the status naming how many already-mirrored pieces the source contains. Also
  confirmed from the same scene save: the engine persists reflections correctly
  (rotation_euler + negative scale), so mirror on a clean source survives save/load.
- **ADD mode for all swaps** (default: SWAP/replace - revised by request the same
  evening; Add is the opt-in).
- **Transparency/overlay materials purged from culture definitions** ("having a
  transparent thatch roof or an ivy/moss wall that's transparent is stupid"): every
  ivy, moss, and alpha-variant material removed from all cultures' Common lists -
  44 entries from the live Documents file, 47 from shipped ReferenceData. The
  '_noalpha' materials are the OPAQUE versions and were kept (the first sweep
  wrongly caught them; corrected before saving). These were only ever safe as
  decoration-slot targets, which generation can't guarantee.
- **Swap tool batch (fief_villa_arch_mod_v3_2 report)**: (1) LIVE-REFERENCE SWAPS
  COPY THE WHOLE TREE - CopyFrom copies the root but not runtime-ATTACHED children
  (re-parented survivors of an earlier swap; on a worked-on composite that is all
  the visible geometry), so the copy arrived empty/invisible ("didn't work"). Any
  reference child missing from the copy is now cloned and attached recursively
  before positioning. (2) UNDO/REDO FORCE NEUTRAL TOGGLES - the restore ran through
  SwapEntity, which honours the panel's global toggles: with ADD mode on, "undo"
  placed the restored prefab NEXT TO the copy instead of replacing it; 1x Scale
  stripped a scaled original's scale on restore. Both (plus the new Z-rot/scale
  extras) are forced neutral for the duration. (3) NEW: Z Rot (deg) + Scale mult
  inputs on the swap panel - extra world-Z rotation and uniform scale applied to
  everything swapped in (regular swaps, live reference, swap sets); 0/1 = off.
  (4) Known limitation stated: BY-NAME swap instantiates the prefab DEFINITION -
  live edits on a placed copy don't ride along; use live-reference mode for that.
- **Cross-culture swaps no longer keep the old prefab's parts** ("empire to aserai
  castle tower kept the old empire merlons on top of placing new aserai ones", via
  a swap set): the preserve-extras pass compared old children against the NEW
  prefab's children by name - on a cross-culture swap the names never match
  (empire_merlon_* vs aserai_merlon_*), so the old prefab's own baked-in parts read
  as hand-placed extras and were carried over. The reinstantiability probe already
  builds a pristine instance of the OLD prefab; its child names ARE the definition,
  and old children matching them (unique _NN suffix ignored) now cascade-delete
  with the old entity. Genuinely hand-added children still survive as extras.
  Applies to Swap Selected, Swap All, and swap-set Apply alike.
- **Live-ref copy recurses into matched children** (second round of the villa
  report): the missing-children clone only descended into children it had itself
  cloned - a child the copy already carried by name was skipped wholesale, so
  grandchildren CopyFrom left behind stayed missing (trace: cloned=0, only 6
  definitional children). Matched source/copy pairs are now compared level by
  level all the way down, and every live-ref swap logs source child count + total
  cloned.
- **Live-reference swap no longer duplicates parts** ("instead of swapping things
  out ... it like duplicated stuff"): follow-up to the whole-tree copy fix the same
  evening - the copy's tree is complete now, but FinishSwap still re-parented the
  OLD entity's children on top (name-swap's preserve-extras heuristic), and the
  supersede-by-name check never matched because clones carry auto-suffixed unique
  names - every part doubled on a like-for-like variant swap. Live-reference mode
  now carries NOTHING from the old entity: children are removed with it and its
  mesh/color overrides are not re-applied - an exact copy of the reference is the
  entire point of the mode. (Name-swap keeps the preserve-extras behaviour.)
- **Swap set browser: Show/Hide Rules works + readable errors**: the expand toggle
  set an IsVisible binding on a nested ListPanel that never took effect inside the
  row template - the VM now fills the bound Pairs list only while expanded (an
  empty CoverChildren list IS collapsed). Both status lines grew 18->56px - long
  apply errors were auto-shrinking to fit one line, "IMPOSSIBLE to read"; root
  1000->1076.
- **Texture sets apply to simple props + browser reachable from Pile Generator**
  ("the texture overrides menu is impossible to reach from the pile generator menu,
  and doesn't work when i made my own"): two independent faults. APPLY: a preset's
  override keys ('log_beech_c') were matched against mesh names EXACTLY, but a
  prop's meshes carry dot suffixes and LOD segments ('log_beech_c.1',
  'log_beech_c.lod2') - zero matches, no children to fall back to, and the zero was
  swallowed. Mesh names are now also matched with LOD segment + '.N' suffix
  stripped, and a preset that matches nothing lands in TextureFailed with the set
  named instead of applying "successfully". REACH: new Texture Overrides button on
  the Pile Generator's recipe row (the browser was only reachable from the Prefab
  Creator panel).
- **Re-Settle can only settle DOWN now** ("stuff starts to float ... self
  replicates up even higher. The initial result is ALWAYS more settled than after
  resettling"): the settle cast started 300 units above the WHOLE pile and took the
  first hit - for a bottom piece that is the TOP of the pieces stacked above it, so
  every resettle perched pieces on their neighbours' heads and ratcheted the pile
  upward. Two rules now: the cast starts 0.5 units above the piece's OWN height
  (it can only see the ground or genuinely lower surfaces), and a piece may only
  move down (the 0.5 allowance covers popping out of interpenetrated ground) -
  "more settled than before" is guaranteed by construction. Kept the button.
- **Recipes removed**: Rubble - Aserai Siege Debris (its aserai_l*_debris_* prefab
  names don't instantiate) and Rock Pile - Highland (Battania) ("the stones you
  used are giant obelisks") deleted at user request. 4 of the 6 generated recipes
  remain.
- **Physics strip re-runs itself after every save** ("the strip physics button
  isn't working?" - it worked: strip at 23:25:02 removed 36 nodes, the EDITOR
  SAVED at 23:25:15 and re-emitted all 36, confirmed by file timestamps): the
  save-first/strip/don't-save-again ritual was too easy to get wrong, so
  BackupManager's existing scene-file watch now re-runs the stripper automatically
  ~2s after any detected save when the file carries pile_nophys pieces, with an
  editor warning saying it happened. Loop-safe (the stripper's own write updates
  the baseline; a run finding nothing never writes); prestrip .bak copies pruned
  to the newest 3. The manual button remains for on-demand use.
- **Numeric transform: one axis at a time, final spec** (user's words: "it should
  reset the active unfinished transform on the one you aren't transforming"): a
  typed value produces a PURE single-axis move from the pre-drag position - the
  other axes of a diagonal drag reset the moment a number is entered. Nothing
  typed still holds the full drag (no jump on open or right-click reset); Esc
  still cancels to pre-drag. The intermediate residual/per-axis experiments are
  gone.
- **F6 Fill From Selection captures the origin too** ("it ALSO defaults to origin
  coordinates and fills that from selection - do you see how that's easier?"): one
  click now fills the prefab name AND switches Origin to Coordinates filled from
  that entity's position, freeing the selection for target surfaces - which fill
  mode needs it for. Heading still reads from a selected entity when one exists.
- **Delete Physics, round five - saved-file surgery** ("they still have physics
  after i used physics: delete", with the saved xscene as proof again): even the
  broken-out CopyFrom pieces serialize a <physics shape="bo_..."/> node - the
  serializer writes the physics SHAPE REFERENCE from entity metadata no reachable
  runtime API clears, so the loader rebuilds the body every load regardless. New
  button on the Pile Generator: "Strip Physics From SAVED Scene" - removes the
  physics nodes from pile_nophys-tagged entities in scene.xscene directly
  (timestamped .bak taken first). Workflow: save -> click -> reload. The editor
  re-emits the nodes if the stale session is saved again - re-run after the final
  save. Runtime strip still runs for in-session effect.
- **Numeric takeover: nothing typed = hold the FULL drag** ("it'll only reset the
  movement on ONE when i start typing" - the log proved the two-axis capture works;
  what still jarred was the open/right-click-reset zeroing the edited axis): with
  no number entered, the piece now stays exactly where the drag left it on every
  axis; a typed value replaces only the on-axis component (perpendicular movement
  survives, as before); Esc still cancels back to pre-drag. Right-click reset now
  means "back to the drag", not "back to zero".
- **Physics-stripped pile pieces tagged pile_nophys** (on top of the pile reselect
  tag and any placement tag) so the broken-out ones are findable as a group;
  auto-prefabbing them back together = the prefab-consolidation ROADMAP item.
- **Delete Physics, round four - the REAL fix** ("they have physics after reload" +
  the saved xscene as proof): a plain Instantiate saves as a bare
  <game_entity prefab="X"> reference and the loader rebuilds physics from the
  prefab definition every load, so all three rounds of runtime stripping were
  correct AND doomed - nothing per-entity persists on a prefab reference. Delete
  Physics pieces are now created as BROKEN-OUT CopyFrom entities (template
  instantiated, copied, template removed, flags cleansed): those serialize with
  explicit components and carry a <physics> node only for a body that exists at
  save time, so the post-generation strip finally survives save/reload. Trade-off:
  the piece saves as loose meshes, not a prefab instance - exactly what a
  no-collision decoration wants. Also: UpdateSceneTree nudge after generation
  (stale editor state made visibility toggles and physics changes invisible until
  reload - now documented in KNOWN-ISSUES).
- **Undo: clones no longer break UID matching** ("the MST undo is like fully
  broken" - the change log PROVED the entries carried UIDs; the Manipulation log
  showed shift-drag copies of the recolored house being made at the same moment):
  cloning duplicates tags including the UID tag, and multiple tag-sharers always
  demoted the whole batch to confirmation - a rule from 08-18, not a regression.
  Position now disambiguates: a recolor moves nothing and clones were dragged
  elsewhere, so when exactly ONE tag-sharer sits within 10cm of the logged
  position, it is the original and is trusted. Two equally-close sharers still
  demote. Also: RecomputeBoundingBox guards before the surface/probe measures
  (rglEntity.h:2068 !is_bounding_box_dirty assert), and a drag-vector capture log
  line for the still-reported two-axis loss.
- **Undo trust for UID-less batches + CR entries get UIDs** ("you might have just
  totally broken the undo? It no longer works at all" - the log showed the undo
  RUNNING and refusing all 96 entries as low-confidence): Continuous Recolor
  entries never carried UIDs, and RevertManager never auto-trusts a name+position
  match without one - CR batches were ALWAYS un-undoable; the earlier working
  undos were UID'd Apply batches. Two fixes: CR entries now stamp UIDs like every
  engine entry, and a no-UID entry is trusted when EXACTLY ONE same-named entity
  sits within 10cm of the logged position (recolors move nothing; a second
  equally-close name-match still demotes to confirmation).
- **Delete Physics round three**: flags + PhysicsDisabled still left the registered
  body solid - SetPhysicsState(false, children:true) added on top, targeting the
  body itself. Diagnostic "[Swap] extras active" log line added for the live-ref
  Z-Rot report (says whether the typed value even arrived).
- **Extra heading split into Rotation (Entity) + Rotation (Grid)** ("it doesn't
  allow a non-world-aligned orientation of the grid itself"): Entity spins each
  placed piece in place (the old behaviour); Grid rotates BOTH step directions
  about world Z so the whole cell lattice pivots around the origin - a
  non-world-aligned grid without rotating any surface. In fill mode the grid
  rotation applies before coverage projection and the relative-spacing probe, so
  spacing measures along the final directions. Both about world Z, both 0 = off,
  independent and combinable.
- **NEW: Extra heading (deg) input for surface distribution** ("impossible for the
  column's rotation to apply because i was using source prefab name ... I have to
  have the target surfaces selected"): fill mode consumes the selection as targets,
  so a by-name source had no way to carry its own rotation - the heading always
  came from the first surface. The new box (Surface mode, above the extent toggle)
  rotates placed pieces about world Z on top of the derived heading; 0 = off.
  Applies to counts mode and Fill Target Surfaces alike; selection-mode sources
  keep their authored rotations and ignore it.
- **Native drag-placement ghost no longer dies while panels are open** ("the
  placement preview stops working after a while of having the F7 menu open"):
  EVERY toolkit panel's outside-click handler nulled the SCREEN-WIDE
  EventManager.FocusedWidget - intended to defocus the panel's own text fields
  when clicking into the viewport, but the same click stole focus from the
  editor's resource browser at the exact moment a drag-placement starts, killing
  the drag ghost whenever any panel was open. All 24 panels now route through
  PanelFocus.DropFocusOnOutsideClick, which drops focus ONLY when the focused
  widget belongs to the calling panel.
- **Swap undo restores the pre-swap frame** ("if I apply a Z rotation or scale with
  swap selected and then undo, the undo doesn't undo the Z or scale"): undo swapped
  the old prefab back in at the NEW entity's current frame - which carries whatever
  Z-rot/scale the forward swap applied, so they survived undo. SwapResult now
  stores the old entity's frame from BEFORE the extras were applied, and the
  session undo path restores at it. (Cross-session log-based undo remains
  position-only best-effort, as before.)
- **MST undo no longer flattens per-layer colors** ("sometimes when we undo it, a
  per-layer color factor colors the ENTIRE entity afterwards"): undoing an
  entity-wide color entry called SetFactorColor(old) - which, per the 08-19
  discovery, writes across EVERY mesh's own color - so one flat color repainted
  per-layer colors the batch never touched. Intermittent because it needed the
  batch to contain an entity-wide entry at all. The forward apply now snapshots
  every mesh's color into the entry (MeshColorSnapshot) before SetFactorColor
  runs, and undo restores them individually. Batches logged before this build
  still undo the old way - re-apply those tints by hand if bitten.
- **Numeric takeover keeps the drag's second axis** ("a two axis translate ...
  forgets about 1 of the 2 axes"): the takeover snapped entities back to their
  pre-drag frames and applied the typed value along the ONE detected axis, so a
  diagonal drag's other-axis movement silently reset. The drag vector is now
  captured per entity at takeover, and translate-Apply preserves its component
  perpendicular to the edited axis - typing refines the axis you're on without
  undoing the rest of the drag. Recomputed live when you switch axis (C) or
  World/Local (L); cold opens store zero and behave exactly as before; rotate mode
  untouched.
- **Delete Physics, round two - the flag is the lever** ("still bugged": the log
  showed the pass running with every BodyFlag reading None afterwards, yet pieces
  still collided): clearing body flags does not unregister the already-registered
  physics body, and no physics-disable API exists - but EntityFlags.PhysicsDisabled
  provably kills collision (it is what made CopyFrom clones unclickable). Stripped
  pieces now get that flag on every entity in their tree. FlagRepair never touches
  it on pile pieces (none of its trigger flags present); shift-drag duplicating a
  stripped piece re-enables collision on the duplicate (cleanse clears the flag).
- **Pile Generator's Delete Physics actually strips physics** ("the delete physics
  thing on the pile generator doesn't work"): the strip pass SKIPPED any entity
  whose BodyFlag read None - but a prefab's collision shape can sit on a child
  reporting None while still perfectly solid, so those pieces were never touched.
  Every entity in the piece's tree now gets RemovePhysics + SetBodyFlags(None)
  (harmless where there truly is nothing), and any piece whose flags read back
  non-None afterwards is NAMED in tool.log, plus a summary line - a failed strip
  can't be silent again.
- **Local axis directions are unit-length now** ("it's still not doing local
  correctly"): ResolveAxisDirection returned the origin's RAW basis vectors, whose
  length is the entity's SCALE in this engine - every cell offset silently
  multiplied by it, and Flatten deliberately preserves length so it rode through.
  World axes were unit all along, which is why only Local misbehaved. Local axes
  now come back normalized; magnitude belongs to spacing alone.
- **Fill Target Surfaces tiles along the surface's own axes** ("empire_column_a was
  angled 35 degrees and the grid it generated was made aligned to world axis"): the
  fill lattice was hard world X/Y - the only steps that trivially tile a
  world-aligned AABB - so every fill on a rotated surface came out world-aligned.
  The lattice now walks the first target's flattened unit local X/Y, covering the
  AABB by projecting its corners into lattice coordinates; overhang cells miss the
  filtered raycast and are skipped (they do count toward the Max Instances cap -
  noted in the cap message). The relative-spacing probe measures along the lattice
  directions too. World X/Y remains the fallback for degenerate/vertical targets.
- **Fill Target Surfaces accepts name-only targeting** ("is fill target surface
  compatible with a name-only option?"): with nothing selected or captured but
  names typed in Only-land-on, the names resolve to entities (same exact-name match
  the rays use) and those are filled. The footprint spans every same-named entity
  in the scene - as a typed request that span is the point, and the status reports
  how many matched. Selection stays the primary path.
- **Relative spacing works in surface mode by-name again** ("Relative to local...
  no longer works with distribute onto surface????"): the up-front measurement read
  from the SOURCES list, which in by-name mode is empty (or holds the origin entity
  - the surface itself), so FirstOrDefault() was null, the measure threw, and the
  catch silently fell back to Absolute+Gap - in Relative mode that is just the gap,
  piling every instance onto the same cell. By-name surface mode now measures from
  the FIRST PLACED INSTANCE, exactly like DistributeInGrid always has (a prefab's
  real size is only known once one exists); selection mode still measures the
  sources up front; Fill Target Surfaces already used a probe instance and was
  unaffected.
- **Apply Colors is a one-shot now** ("the apply color button is pointless since
  you can't click it during the disarmed state?" - correct: it routed through
  ReapplyIfArmed, whose disarmed early-return made it dead exactly when a manual
  button is useful, while armed mode already recolors on selection by itself). The
  button now recolors the current selection once, armed or not; the armed path
  shares the same ApplyToCurrentSelection body. Status prefix says "Armed" vs
  "One-shot" on the nothing-matched message.
- **NEW: two Dedupe buttons** (next to Invert Rules; semantics settled after two
  same-day corrections, by example): **Dedupe (Rows)** keeps ONE row per input
  material - first wins, later rows with that input deleted, whatever kind.
  **Dedupe (Multi-Output)** collapses each weighted To spec to its FIRST output
  ("roman_roof:5, timber_frame_c:5" -> "roman_roof") - no rows added or removed,
  the alternatives inside each rule are what get deduped.
- **Edit Categories button on the Culture Preset Generator** (beside Edit
  Cultures): the Category Editor existed but was only reachable from Continuous
  Recolor; role matching runs on categories, so bridge gaps (the timber_frame case)
  are fixed there without a round-trip through chat.
- **timber_frame now bridges to wall-less cultures + generator names its drops**
  ("why did timber_frame_c receive no override ... is that because it considers it
  unique to vlandia?"): NOT uniqueness - Unique patterns are only used for
  tag/culture inference; generation reads Common lists. timber_frame_c classified
  ONLY as 'timberframe', a self-referential category (its patterns match nothing
  but timber_frame materials), so cultures without timber (empire, khuzait) offered
  no counterpart and MatchByRole silently dropped it. Fixes: 'timber_frame' added
  to the 'wall' category's Include patterns (Documents override + shipped
  ReferenceData), so timber-framed walls role-match other cultures' walls; and the
  generator now logs every material it drops WITH the reason ("matches no
  category" / "no target carries [cats]") instead of omitting silently.
- **Duplicate From rows are now rolled between, not discarded** ("Vlandia Feudal -
  this rule has a bunch of inputs that repeat themselves. Your weighting algorithm
  ignores the leftside randomness?"): the rule map collapsed rules sharing a
  FromMaterial to .First(), silently dropping every other row - so repeated
  left-side inputs applied the first row uniformly. All rows are kept as
  alternatives now: one row is picked per top-level prefab (WeightedTarget.PickIndex,
  same anchor and determinism as the weighted-target roll), and the picked row's own
  ToMaterial/ColorFactor then apply - so each alternative keeps its own color, and a
  picked row that is itself a weighted spec still resolves as one.
- **Weighted rolls anchored to the TOPMOST parent** (same-day refinement, "if I
  select 50 prefabs the top parent should define the inheritance for those below"):
  keying the roll by TARGET made the outcome depend on how targets were gathered -
  hand-picked roots rolled per prefab, but filter/scene mode hands child entities
  over as their own targets, putting patchwork back inside a prefab. The roll is now
  anchored to each mesh-owning entity's topmost parent
  (EntitySelector.TopMostParent, new single-entity form of PromoteToRoots' walk) and
  cached across the whole Apply: uniform within a placed prefab regardless of
  selection mode, varied across prefabs by the weights.
- **Weighted picks: ONE roll per placed prefab, final round** ("it's STILL
  INCONSISTENT across LODs. Shall we just remove this weighting concept entirely?"
  - the trace showed the LOD tiers now agreeing; the remaining patchwork was
  duplicate SIBLING parts: six child entities all named ..._corner2_..., identical
  structure, different origins, each rolling its own pick - brick, wall_b, even
  floor on a wall). Verdict: weighting stays, its granularity was wrong. Weights
  exist for variety ACROSS a scene's instances, never inside one placed prefab.
  A weighted rule now resolves ONCE per target root per rule (seeded by the root's
  name+position, deterministic as ever); every mesh, LOD tier, and child under that
  root shares the pick. The per-part pre-pass is deleted - uniform-within-instance
  makes LOD and sibling consistency true by construction.
- **Weighted picks LOD-consistent, second round** ("still getting inconsistencies
  ... across layers"): part-name seeding fixed tiers that PAIR by name, but a merged
  simplified LOD mesh ('bridge.lod2' covering several full-detail 'bridge.N' parts -
  a normal LOD pattern per LodMismatchChecker) pairs with nothing and still rolled
  its own dice. Weighted resolution is now a per-slot pre-pass: full-detail meshes
  roll; each LOD mesh copies its counterpart's pick by part name; a merged LOD takes
  the rule's MAJORITY full-detail pick. An independent roll only remains for a
  weighted rule that matched nothing at full detail in that slot.
- **Slot-exact layout batches are undoable now** ("I can't undo apply B to A?" -
  the undo RAN but reported all 694 entries as low-confidence, not applied):
  RevertManager never trusts a name+position match without a UID tag, and
  ApplyMaterialLayout wasn't stamping them. Its entries now go through the same
  EnsureTrackingUid as every MaterialSwapEngine entry (made internal for it), so
  Undo Last Batch auto-applies. Batches from before this fix remain
  confirmation-only; the clean recovery is re-running slot-exact against a freshly
  placed pristine copy of the prefab as B.
- **Weighted picks are now LOD-stable** ("creates inconsistencies across layers??"
  on fief_empire_brick_bridge): the weighted-target seed used the raw MESH INDEX,
  and each LOD tier of a part is its own mesh index - so full detail could roll
  wall_brick while its LOD rolled floor, a visible material pop on LOD switch. The
  seed now uses the LOD-stripped part name (LodMismatchChecker.StripLodSegment, now
  public - the established "same part, different tier" identity), so all tiers of a
  part share one roll. Index remains the fallback for unnamed meshes. Note: seeds
  changed, so a re-apply of an old weighted rule may pick differently than before.
- **Slot-exact layout now pairs by part name, not raw index**: Apply B's Materials
  to A previously required identical mesh order AND identical LOD splits; it now
  matches slots by LOD-stripped mesh name (index fallback for unnamed meshes), so
  it survives reordering and differing LOD tiers between variants. Entity-level
  pairing is still by hierarchy order with a mismatch warning.
- **Rules now match override-clone materials** ("only half the textures got changed
  at all?!" - tool.log showed the final Apply matching NOTHING: touched=0): a mesh
  carrying overrides reports its material as a runtime clone named 'x(copy)', which
  never matched a rule written against 'x'. On a mixed entity the plain-named meshes
  swapped and the cloned ones silently didn't - a half-applied look. Rule matching
  in MaterialSwapEngine (both the lookup and the stale-Factor1 probe) now strips the
  suffix, as does Get Input Rules from Selection's material seeding; the change log
  records the stripped name so reverts restore a real resource.
- **NEW: Apply B's Materials to A (Slot-Exact)** ("shouldn't it be overriding stuff
  more selectively?"): when one of A's materials maps to SEVERAL of B's depending on
  the slot (the retaining-wall case - one desert material vs four empire ones), a
  name-based rule can only express a weighted dice roll, even though the pairing
  walk knows exactly which slot should get which material. The new button (orange,
  under the two Infer buttons; same Set Input Entity (A) + select B flow) uses that
  knowledge directly: sets each of A's mesh slots to the material B carries in the
  same slot. Dry-pass first so the confirmation names the exact slot count,
  structure mismatches warned, backup + change-log batch (undoable), '(copy)' names
  normalized on both sides. The weighted Infer rule remains right for BULK
  conversion of many look-alikes; slot-exact is for making THIS pair match.
- **Infer Material no longer produces '(copy)' rules** ("roman_hippodrome_wall_brick
  (copy):17 ... this is not a valid rule"): a mesh carrying runtime overrides hands
  back a CLONED material named '<base>(copy)' - not a real material resource, so an
  inferred rule targeting it could never apply, and the clone and its base also voted
  as two different targets, splitting counts and manufacturing weighted specs out of
  what is really one material. InferMaterialRules now strips the (stackable) suffix
  before voting, so rules name real materials and split weights merge. A pair that
  matches after stripping (A='x(copy)' vs B='x') now correctly infers nothing.
- **Unparseable color factors now warn instead of silently skipping** ("the
  per-material color factor is broken AGAIN" - investigated via tool.log: it was
  NOT broken and none of the 08-19/20 fixes were reverted; SetMeshColor fired with
  #FFE193FF on every timber_frame_b mesh in the same Apply, while the timber_frame_c
  rule arrived with a blank/invalid color - a typed typo like "#ffwe193" fails
  ColorHex.TryParse and the tint was skipped without a word, which is
  indistinguishable from broken). Now: every Apply logs each rule WITH its color
  ("(UNPARSEABLE - tint will be SKIPPED)" flagged), invalid per-rule and entity-wide
  colors are collected into ApplyResult.InvalidColorFactors, and the panel status
  line + an editor warning name the bad value(s) outright.
- **Grow Selection opens from the main keyboard too**: Ctrl and the number-row +/=
  key now opens it alongside Ctrl+Numpad+ (and number-row - gives the same "grow
  first" hint as Numpad- cold). Inside the panel the keyboard +/- step exactly like
  the numpad pair, so whichever pair opened it keeps working. Hints and F1 docs
  updated.
- **Clicking Apply on Grow Selection no longer deselects everything**: the grown
  selection was already applied, but the Apply CLICK also reaches the native editor
  underneath (the documented DeferredSelection hazard) and resets the selection to
  whatever the mouse is over - the panel, i.e. nothing. Radius steps survived because
  each queues through SetEditorSelection; commit queued nothing, so the wipe was the
  last word. Commit now re-queues the final result on the way out, landing it after
  the editor has processed both the press and the release. (Enter already worked -
  no click involved.)
- **Typing takes over a LIVE drag again** ("now I've lost the ability to type a
  number during the transform?"): mid-drag typing only ever worked because the
  mid-drag settles kept flagging the operation as finished, so removing them closed
  the takeover window until release. CanTakeOver now opens while the watcher is
  in-motion too; the live preview keeps the detected mode/axis current so the modal
  opens on the right one; and TakeSnapshot mid-drag ends the observation (so the
  release doesn't also record the raw drag) and, for an unsettled shift-drag
  duplicate, hands over the COPIES anchored at their start - not the originals in
  Before, which would have re-triggered the "typing affects the original" bug.
- **Live readout during the drag** ("I want it to stay displayed the whole time" +
  "takes a little too long to display"): once the settle gate stopped mid-drag
  settles, the readout went silent until release - the mid-drag settles had
  accidentally been what made it feel live. The HUD now shows a running delta
  ("Now: Move (...)") rebuilt every 0.06s watcher sample, ALWAYS measured from the
  pre-drag frames, so it appears almost instantly and never resets its origin when
  the drag changes direction. Display only - the recorded operation (Shift+R,
  numeric takeover) still lands once, at release, shown as "Last: ...".
- **A pause mid-drag no longer splits the operation** ("translate -8, move back 2
  ... it tells me +2"): the watcher's 0.22s stillness settle could fire while the
  mouse button was still HELD, recording the drag-so-far and resetting the baseline
  to the pause point - the rest of the same drag then measured as a separate fresh
  move from there. Settle is now blocked while LeftMouseButton is down, so the
  recorded delta (and the last-operation readout) is always relative to the pre-drag
  position and lands once, at release. G/Z modal drags are unaffected - they never
  hold the button, and the watcher still doesn't need the mouse to notice them.
- **Box selections no longer commit the numeric modal mid-drag** ("specifically when
  you click and drag ... a box selection"): the commit check ran per tick, so a
  marquee drag committed the instant the box first touched ANY entity - against a
  half-made selection. The check now freezes while the mouse button is held and
  evaluates only on release, so the FINISHED box selection is what applies+closes.
- **Slider release no longer applies-and-closes the numeric modal**: clicks on our
  panels leak through to the editor underneath (the long-documented click-through
  hazard), so releasing the slider also SELECTED whatever entity sat behind the
  panel - which the new commit-on-selection-change read as "picked the next
  target" and committed. The commit check is now gated on a click that lands
  OUTSIDE the panel (0.6s window), so slider drags, mode buttons, and every other
  panel interaction are inert; clicking an entity in the viewport still applies
  and closes as designed.
- **Swap removal is verified, not assumed** ("swap sometimes doesn't remove the
  original"): Scene.RemoveEntity can silently fail (pieces inside placed prefab
  instances; unregistered live-copy ghosts) and the swap then reported success
  while old and new overlapped. FinishSwap now checks whether the original is
  still in the scene afterwards, logs a [Swap] warning naming it, and every swap
  status line (Swap Selected, live-reference, swap-set Apply) reports "N
  original(s) could NOT be removed" with the delete-by-hand / use-ADD-mode advice.
  Also: F7 Scene Analyzer root 1040->1100 - the earlier trim clipped the Close
  button off the bottom.
- **RGL popup dismisser v2** (Tools\Dismiss-RglPopups.ps1): now also answers the
  two follow-up dialogs per error - "Faced a problem, would you like to proceed?"
  gets NO (declining log collection) and "Always ignore" gets OK - but ONLY while
  the newest TaleWorlds process is under 5 minutes old, so genuine mid-session
  problem dialogs are never auto-answered. Root cause on record: these popups fire
  during NATIVE editor startup before any module DLL loads, so no in-process fix
  can exist (which is why TextureRescue below could not catch them).
- **TextureRescue: kills "Unable to find texture" popups from inside the toolkit.**
  File placement could not fix CCModule_SP's missing UI sheets (the editor ignores
  every loose-texture location the client honours), so the toolkit now fixes it at
  the engine level: at submodule load - before the editor screen and its AlwaysLoad
  sprite categories initialize - Core/TextureRescue scans every module's
  GUI\SpriteSheets for PNGs and preloads each into the engine's texture registry
  via Texture.LoadTextureFromPath, named by filename. The sheets extracted from
  CCModule_SP's pack0.tpac earlier (10 of 11; ui_ccbanners_1 was never shipped)
  are exactly what it finds. Toggle-independent; logs a preload count.
- **UI readability batch** (2026-08-23, ~10 direct reports): F9 - last-backup line
  gets two wrapping lines instead of one shrunken one, "Leave blank..." and the
  Backup Tool Data note taller, Backup Tool Data button moved to its own FULL-WIDTH
  row; F8 - Continuous Recolor button 200->230, Check LOD Mismatches 220->242,
  Revert to Normal 190->209, and Browse Presets recolored GOLD (#d4af37) - asked for
  repeatedly, finally distinctive; Continuous Recolor - palette list 60->220 tall
  (was unscrollable), root 580->740; Pile Generator - retitled "Pile Generator /
  Surface Scattering", recipes list +30% (160->208), Export All Recipes 170->196,
  root 1240->1300; Scene Analyzer - both section descriptions 32->46 (full-size
  text), known-broken subtitle 18->26, root trimmed 1104->1040 to reclaim the
  trailing blank space.
- **Shift+R no longer races the settle window** (2026-08-23, the user's own diagnosis
  cracked it: "I never released Shift between shift+drag and Shift+R, and that
  stopped copies"). The watcher records a drag only after ~0.5s of stillness; the
  natural fast rhythm - release mouse, tap R with Shift still held - beat that
  window, so the copy recipe didn't exist yet and Repeat's SuppressSelfEdit then ate
  the pending observation. Releasing Shift first only "fixed" it by adding human
  delay. Repeat now force-settles any in-flight observation before reading the
  recipe (ManipulationWatcher.ForceSettlePending), so the fast rhythm works. Also:
  C selects the Y axis in Numeric Transform (editor's native rotate keys are Z/X/C),
  and the panel got readable two-line hints plus the slider-range note beside
  Mode/Axis.
- **Numeric Transform overhaul** (2026-08-23, four direct reports in one): (1) "cannot
  type higher than 20" CONFIRMED - the slider clamped the typed value via its
  every-tick echo; the pegged-slider echo is now ignored, typed values exceed the
  slider freely, rotate range is +/-360, and move range is cyclable
  (5/20/50/100/500, default 20) via a panel button. (2) World/Local axes toggle
  (panel button + L key) - World is now the default for BOTH modes; note rotate
  previously always used Local (identical for upright entities). Local uses the
  first captured entity's basis, unit-normalized so scale never leaks into the
  distance. (3) Selecting another entity now COMMITS the pending value and closes
  the modal, like Enter - an empty selection (the editor clears on keypresses)
  still never closes it. (4) "typing after a shift-drag copy affects the ORIGINAL"
  CONFIRMED - the takeover adopted the pre-drag snapshot, which for a duplicate is
  the originals; it now adopts the COPIES anchored at their start position
  (current frame minus the settled delta - ordering-proof, no source pairing).
- **REVERTED: CopyFromPrefab and AttachEntity** (2026-08-23 ~01:25). CopyFromPrefab
  was tried as a registration-clean clone route and is DAMAGING: it treats the live
  source as a template and the source lost children ("my prefab got halfway
  destroyed" - corroborated by save-file child counts: fewer door-children per
  villa-arch root after the runs). Never reintroduce it against live scene entities.
  AttachEntity removed in the same revert - unknown native semantics are no longer
  acceptable in this path. What REMAINS deployed and safe: CopyFrom + recursive flag
  clears (now incl. PhysicsDisabled), SetReadyToRender, UpdateSceneTree, cleanse-at-
  birth, scene sweeps, never-clone-a-clone chaining, micro-move filter, diagnostics.
  DOCUMENTED LIMITATION that survives it all: CopyFrom clones of prefab-less sources
  stay unselectable until the scene is SAVED (the editor rebuilds its click registry
  on save; no reachable managed API does the same). The candidate real fix, not yet
  built: a hand-rolled clone assembled purely from editor-born pieces (CreateEmpty
  isModifiableFromEditor:true + AddMultiMesh copies + AddChild) - tracked in ROADMAP.
- **Shift+R chains never clone a clone** (2026-08-23, the decisive fix after the
  selection saga): a gen-1 CopyFrom of a real editor-born entity is confirmed
  selectable, but a CopyFrom OF a CopyFrom-product comes out natively locked in a
  way that survived every reachable fix (flags recursively cleared - the sweep
  proves zero remain - AttachEntity enrollment, UpdateSceneTree, SetReadyToRender).
  The chain now remembers the ORIGINAL sources and press N places a fresh gen-1
  clone of the ORIGINAL at N times the offset - same marching-copies result, built
  exclusively from the confirmed-working operation. Also logs each chain step.
- **Micro-moves no longer clobber the Shift+R recipe** ("shift+r of copies is broken
  again" - it wasn't: the log showed every press succeeding, but replaying a 20cm
  nudge). Clicking an entity to SELECT it drifts the mouse a few pixels, the editor
  treats that as a drag, and the watcher recorded it - one recipe-clobbering
  "Move (0.22, -0.02, 0)" per selection click, wiping the Copy+move the user meant
  to replay. Vector moves under 0.5m are no longer recorded (logged as "ignored
  micro-move" instead); a deliberate tiny shim is Numeric Transform's job. Copies
  are never filtered - a shift-drag is always deliberate.
- **CopyFrom clones now register with the editor's selection system** ("goes away if
  I save, but I shouldn't have to save"): the repair sweep finding ZERO flagged
  entities proved the residual click-proofing was never flags - the editor's
  click-select registry simply doesn't learn about CopyFrom products until something
  rebuilds it, which saving does. Both CopyFrom sites now call
  GameEntity.SetReadyToRender(true) + MBEditor.UpdateSceneTree(doNextFrame: true)
  (found by reflecting the shipped assemblies) - the same rebuild a save triggers,
  coalesced to one per frame. Hover always worked because highlighting reads render
  data, not the registry.
- **Flag contamination now self-heals - no button needed** ("isn't there a better
  fix?" - yes): (1) the signature-locked repair sweep runs automatically every time
  a scene opens or changes, healing all legacy stock; (2) ManipulationWatcher
  cleanses every native shift-drag duplicate AT BIRTH the moment it detects one,
  killing the spread vector (duplicates of flagged stock inherit the flags). The F5
  button remains as a manual diagnostic - after one scene reload it should always
  report "no contaminated copies found."
- **"Repair Unselectable Copies" button (F5)** - the code fix couldn't heal copies
  already in the scene, and the editor's own shift-drag duplicates of pre-fix copies
  INHERIT the flagged children, so the contamination outlived the fix and spread. The
  scene-wide sweep is signature-locked: only TRUE ROOTS whose own flags are clean but
  whose descendants carry the runtime flag trio get repaired - legitimately-flagged
  engine helpers have flagged roots and can never match. Flags only; nothing moves,
  nothing is created or deleted; names logged as [FlagRepair].
- **CopyFrom flag clear also drops WaitUntilReady** (2026-08-23, "the final entity -
  hover highlights it but I can't click it"): CopyFrom stamps THREE flags, and the
  third leaves the newest copy click-proof until the engine processes it - hover
  highlighting uses a different path than click-select, hence highlight-but-no-
  select, always on the most recent copy. Safe to clear: the copy's source was
  already fully loaded, nothing is actually streaming.
- **CopyFrom flag clear is now RECURSIVE** (2026-08-23, "entities I make with
  shift+r become at least temporarily impossible to select"): CopyFrom stamps
  DontSaveToScene + NonModifiableFromEditor on the WHOLE copied hierarchy, and only
  the root was being cleared - viewport clicks land on CHILD meshes (collision
  lives there), so copies of composite pieces couldn't be picked. Both clone paths
  (CloneSourceAt and the live-reference swap) now clear the flags through the whole
  tree.
- **Shift+R chaining no longer races the key release.** "Loses selection after one
  copy": a repeat selects its new copies via the 2-tick deferred queue (tuned for
  mouse clicks), but the R key's RELEASE lands several frames later and clears the
  editor selection again AFTER the deferred apply - whether the copies stayed
  selected was a race against how long the key was held. Chaining now remembers the
  previous repeat's copies internally (pointer-validated) and falls back to them
  when the selection reads empty, plus feeds them into SelectionMemory; the visible
  editor selection is best-effort only. Pressing Shift+R repeatedly now steps along
  deterministically even if the highlight flickers off.
- **Shift+R copy-replay gets the CopyFrom fallback.** RepeatLastTransform's
  CopyTranslation replay cloned via EntityCloner directly - the LAST clone path
  without the fallback - so replaying a copy of a prefab-less entity failed with
  "Nothing could be copied ... has no prefab name" while grid/path/mirror all
  handled it. It now clones through the shared CloneSourceAt (made internal); a
  sweep confirms no direct EntityCloner.Clone callers remain outside it. Failure
  messages now name the entity too.
- **Scale toggle for swaps: "Scale: 1x" / "Scale: Inherit" (1x default).** The old
  "scale can't carry over - no setter" claim was HALF WRONG: the engine stores scale
  in the frame's rotation basis lengths, so Instantiate at the old entity's frame
  silently inherited its scale all along (confirmed live). Now explicit: 1x
  normalizes the basis so the new entity gets the prefab's authored scale; Inherit
  keeps the old entity's scale. Applies to swap, add, live-reference, and swap-set
  paths. Sits beside a shortened Mode button on one row in F5; stale doc claims
  corrected.
- **Panel sizing pass** (all direct reports): F5 root 992->1040 (Close button had
  slipped off the bottom after the mode row landed), Swap Sets browser 800->1000
  with the sets list 260->420 and edit list 150->190 ("scrolls too fast" = tiny
  viewport, big wheel jumps), Pile Generator root 1060->1240 with the rows list
  260->400 and the intro paragraph 40->72 tall (Gauntlet shrinks text to FIT the
  widget height - 3 wrapped lines in 40px is why it read "super small").
- **Six new pile recipes** in PrefabCreatorTool\PileRecipes, every prefab name
  verified against prefab_catalog.csv: Rock Pile Highland (battania_stone_a-c +
  ground rocks), Rock Pile Desert (arabian_stone + cave desert rocks), Aserai Siege
  Debris (l1/l3 debris pieces), Beech Log Pile, Market Crates & Barrels, Grey Cave
  Scree.
- **Isolate rebound twice, now Shift+O (Ctrl NOT held).** Ctrl+Shift+I: the editor's
  bare-I (place matching ground rotation) fires even with modifiers held, stamping an
  entity per isolate. Ctrl+Shift+O: a native combo HID the selection instead - the
  exact inverse of isolate. Shift+O avoids both, confirmed only by live use like its
  predecessors; known cost is that a capital O typed into a text box may trigger it.
  Plus a once-per-session hint on first activation - "Shift+O Isolate mode active,
  Shift+O to reverse" to the editor overlay and the log - with an F9 toggle
  (Isolate first-use hint, default on). Docs/README/F9 panel all updated.
- **Prefab consolidation recovered into ROADMAP.md** as the user's top tracked item -
  asked 2026-08-22 16:00 ("convert selection from any subset of child entities to
  prefabs"), lost between sessions, recovered from the session logs. See ROADMAP for
  the design questions and why the evening's bug parade makes it structural. First real swap-set run revealed a
  landmine: pieces selected inside placed prefab INSTANCES made the removal step trip
  the editor's "break prefab?" dialog once per piece, removals half-failed, and
  old+new stacked with z-fighting. New Mode toggle at the top of F5's swapper
  section: ADD places the new prefab at each target's exact frame and KEEPS the
  original untouched (no removals, no dialogs; delete originals in one manual sweep
  when satisfied) - SWAP is the old destructive replace. Applies to Swap Selected,
  Swap All Matching, live-reference swap, and swap-set Apply; all confirm dialogs
  say which mode is active. Add-mode additions go on Distribute's created-entities
  undo stack (swap history's re-instantiate-the-old-name undo model doesn't apply
  when nothing was removed); they also skip the reinstantiability check, so Add
  works on hand-built originals a swap would refuse.
- **F5 swap fixed: it was reading F6's stale selection cache.** The v0.7 move embedded
  PrefabSwapperVM into F5, but PrefabSwapperTool.EntitySelector's Manual cache is
  refreshed only by the F6 layer's hooks - with F6 closed, every F5 swap action
  (fills, Set Live Reference, Swap Selected, swap-set Apply) acted on whatever was
  selected the last time F6 was open (the mirror anchor, all evening). Manual mode
  now reads the live selection first (one native call per button click) with the
  cache as the click-cleared-selection fallback - fixing all 14 call sites at once.
- **Fill From Selection resolves the REAL prefab name** (GetPrefabName) with display
  name only as a flagged fallback - display names drift (editor _2 suffixes, _mst
  renames, recolor suffixes) and a drifted name in the swap box swaps to or matches
  the wrong thing.
- **Swap-set Apply matches by prefab name too** (then display name, then display name
  with the editor's trailing _digits stripped) - it matched display names only, so a
  selected instance of exactly the pair's old prefab failed with "none match" once
  the editor had renamed it. The no-match message now lists what WAS selected and
  what the set expects.
- **Mirror anchors count up** - wall_Mirrored, wall_Mirroredx2, wall_Mirroredx3 -
  instead of wall_Mirrored_Mirrored_Mirrored (direct request; typed names unchanged).
- **10 culture-equivalency swap sets generated** into PrefabSwapperTool\SwapSets,
  every name verified against prefab_catalog.csv: Empire<->Aserai in both directions
  for Grandbazaar (18 pairs), Dungeon Kit (17), Castle Walls-Gates (97), Castle
  Towers (49), and Columns (2). Name-parallel kit pieces are the size-equivalency
  guarantee - modular kits are built interchangeable. The extraction found 423 shared
  Empire/Aserai suffixes total; more pairs or other culture pairs can be generated
  the same way on request.
- **Mirror expands bare anchors into their children.** The frame-refresh nudge was NOT
  enough - the winding cache sits below anything managed code reaches. So the flip is
  avoided instead: mirroring a previous run's anchor (the natural "mirror that group
  again" gesture) now mirrors the anchor's CHILDREN individually. Prefab-backed
  pieces then go through Instantiate directly AT their final frames, which renders
  correctly at any handedness - and a double mirror's final frames are right-handed
  anyway. The stale-winding case now only remains for hand-built (prefab-less)
  sources mirrored an odd number of times, where save + reload stays the workaround.
- **Mirror "inside-out" mystery solved: stale render state, data was right.** User
  report closed the loop: the double-mirrored result looks inverted/inside-out in the
  live session but is CORRECT after save + reload. Setting a handedness-flipping
  frame on an already-built subtree (the CopyFrom path) leaves children's cached
  winding stale; a fresh Instantiate directly AT a mirrored frame never shows it.
  This is what every "totally messed up" report tonight actually was, compounded
  earlier by the copies also vanishing on save (so the corrected reload was never
  seen). CloneSourceAt now re-sets every subtree node's frame after the flip as the
  strongest managed-code refresh; if the engine still won't recompute live, save +
  reload is the documented workaround.
- **CopyFrom clones are now editable, and anchors never inherit scale.** Follow-ups to
  the save-flag fix, both confirmed in the 22:15 CC_76 save: (1) the flag log proved
  CopyFrom sets "DontSaveToScene, NonModifiableFromEditor" - the second flag left the
  now-persisting copy unselectable in the editor; both are cleared now, here and in
  the live-reference swap. (2) CreateAnchorAndAdopt copied the reference entity's
  frame verbatim, so mirroring/distributing from a SCALED source (a squished
  0.918x0.699 wall) produced an anchor with non-uniform scale baked in - children
  compensate so it looks right until anything is manually rotated under it, at which
  point non-uniform parent scale turns rotation into shear. Anchor basis is
  normalized to unit length now; origin and heading unchanged.
- **CopyFrom clones now survive scene saves.** Confirmed across three CC_76 runs:
  GameEntity.CopyFrom's product exists, renders, and parents at runtime, then
  VANISHES from the saved scene - mirror/grid runs whose source had a real prefab
  went through Instantiate and persisted, while every CopyFrom-fallback product
  saved as an empty anchor. CopyFrom marks its product DontSaveToScene (runtime
  entity); CloneSourceAt and LivePrefabSwapper's live-reference swap now clear that
  flag (and CloneSourceAt logs the flags seen, so tool.log alone can confirm the
  diagnosis if persistence ever fails again). Mirror-of-a-mirror was the visible
  victim: the second mirror's source is the first mirror's ANCHOR (no prefab), so
  the copy took the CopyFrom path and evaporated on save.
- **SceneGuard teardown hook actually installs now.** It had been failing on every
  launch since at least the CC_76 session: SceneEditorScreen does not override
  HandleDeactivate in v1.4.8 (it inherits ScreenBase's), and Harmony refuses to patch
  inherited slots - so the scene-close cleanup it exists for never ran, which is the
  state the 20:56 CC_76 scene-close crash happened in. The patch now lands on the
  DECLARING type (ScreenBase) with an instance-type filter in the prefix, and logs
  when it actually fires. Whether the missing guard caused this specific crash is
  unproven (the crash was in native Qt teardown after Scene_view::clear_all), but the
  guard being inert was a real, separate defect either way.

## v0.7.0 - 2026-08-22 (consolidation pass)

Source snapshot of the state immediately before this pass: `BannerlordModArchive\v0.6\`.

- **One backup system.** Prefab Swapper's and Prefab Creator's own BackupManagers are
  now thin shells delegating to MaterialSwapTool's - one timer, one Backups folder
  (`Documents\...\MaterialSwapTool\Backups`), F9 settings/verification/notification
  gating now cover every tool. The old `PrefabSwapperTool\Backups` and
  `PrefabCreatorTool\Backups` folders are frozen legacy. The shared Tick is wall-clock
  based so all three patches can drive it, which also means the scene-switch state
  invalidation (EditUndo etc.) now runs regardless of which tools are toggled on.
- **No pointless backups or warnings.** The unchanged-on-disk skip now applies to every
  non-manual backup, including before-apply (the newest identical backup is the
  pre-apply marker - the log says so). The "avoid Save for ~10 seconds" warning only
  appears when files are actually being copied.
- **Log rotation.** All four tool.log writers share one implementation
  (`Core/LogFile.cs`) that rolls the file to tool.log.old at 10 MB.
- **Mirror fixed and made faithful.** MirrorGroup re-instantiated by entity NAME, which
  broke on anything renamed (Material Swap's own `_mst` rename guaranteed it). It now
  clones via EntityCloner: real prefab name via GetPrefabName, and per-mesh
  material/color overrides carried onto the mirrored copy instead of reverting.
- **Randomize Rotation** on the Pile Generator panel: per-axis max degrees (Z yaw
  defaults 180, X/Y lean default 0), every piece draws its own angles; acts on the
  selection (anchors expand to children) or the last generated pile. Undoable.
- **Multi-prefab shift-drag copy now records for Shift+R.** The manipulation watcher
  compared selections index-by-index, but the engine makes no ordering promise between
  two selection queries - with several prefabs selected, a reshuffled same-selection
  read as "different entities" and reset the observation mid-drag, and a settled
  multi-copy measured its offset between UNRELATED source/copy pairs (or misread the
  basis mismatch as a rotation). Single-entity gestures never hit any of this, which
  is why they always worked. Now: selection identity is a pointer set, change
  detection pairs frames by pointer, a copy's recorded delta is the group centroid
  offset (ordering-proof, translate asserted), and a mid-motion selection change that
  is NOT a clean copy leaves a `[Manipulation] observation dropped` line in tool.log.
- **Select Whole Prefab** (Ctrl+Shift+P, switchable from F9 -> Shortcuts; also a
  button on F7's select row): promotes the current selection to its top-level prefab
  roots - click a child part of a composite prefab, press it, the whole prefab is
  selected. Dedup by pointer, works on pile/distribution anchors, selection-only.
  Placed in the shared shortcut layer rather than any one tool because "I clicked a
  part but meant the prefab" comes up in every workflow (swap, recolor, isolate,
  transform).
- **Material categories import/export** on the Category Editor (Export All / Import
  (merge) / Open Folder, via `MaterialSwapTool\Exports\Categories`) - the one shareable
  data set that had no import/export. Import merges rather than replaces: new
  categories added, existing ones gain missing patterns, nothing removed.
- **Continuous Recolor's Close button was off the right edge of the panel** - the
  action row's fixed widths summed to ~914px inside an 840px content area. Row
  trimmed to 830 and a width-budget comment left on it (and on the Category Editor's
  row) so the next added button doesn't repeat this.
- **Fixed: the hotkey freeze, second cause** - `ApplyEditorSelectionNow` cleared the old
  selection *first*, one `DeselectEntityOnEditor()` native call per previously-selected
  entity. Measured from tool.log at ~12ms each, so the cost scaled with the OLD
  selection, not the new one: 10 selected = 101ms, 120 = 1695ms, 137 = 1728ms.
  Ctrl+Shift+P always has a big old selection to throw away, which is why it was the
  one that showed it. Reflection confirms the engine has no bulk deselect, so the
  order was inverted instead: `Utilities.SelectEntities` (one bulk call, sets the
  selection wholesale) runs first, and the per-entity loop then only touches whatever
  it actually left behind - normally nothing. The log line now reports stale-deselect
  count and elapsed ms so the engine's real semantics stay visible.
- **Grid distribution can now tile a SELECTION, not just a named prefab.** New
  "Grid source" toggle directly above the Distribute in Grid button. Selection mode
  copies what is selected into each cell via EntityCloner (falling back to
  `GameEntity.CopyFrom` when the entity has no saved prefab at all), which means it
  works on unsaved composites, keeps per-instance material/colour overrides, and tiles
  a multi-entity assembly as a unit with each piece's relative offset intact. Cell
  (0,0) is left for the originals, which are not re-parented. Relative spacing is
  measured off the selection's combined bounding box. Instance cap counts entities,
  not cells.
- **Distribute Along Path accepts a selection too**, so the Source toggle now governs
  all three distributors (Path, Grid, Onto Surface) and moved above them rather than
  sitting beside the Grid button. Path was the last one on prefab-name-only simply
  because it never needed a selection - the path supplies position and facing, so
  "type what to place" was already complete. The real difference from the grid: a grid
  steps in fixed world directions so member offsets apply unchanged, but a path TURNS,
  so offsets are taken in the first source's local space and re-applied in each path
  frame's (TransformToLocal/TransformToParent). That makes a multi-entity assembly
  rotate to follow the curve instead of shearing apart on a bend. Originals stay put
  and fill the first slot, as with the selection grid.
- Removed a triplicated "new copies become the selection" block in the path command -
  three identical copies, two of them no-ops.
- **Panel heights measured rather than guessed.** F6 was left 118px too tall after its
  swapper moved out (1500 -> 1392) and F5 108px (1090 -> 992). New
  `Tools\Measure-PanelHeights.ps1` sums each panel's rows and reports needed vs
  declared height (`-Fix` applies it); it refuses to touch panels containing a
  scrolling list, where slack is deliberate.
- **F5/F6 reorganised along what each panel is actually for.** F6 had become two
  unrelated panels sharing a hotkey - a prefab swapper on top, everything to do with
  distribution below. The swapper (old/new prefab + fills, Swap Selected, Swap All
  Matching, Live Reference, Undo Last Swap, Undo/Redo History) and **Swap Sets**, now
  leading the panel, moved to **F5 Prefab Creator**, which is where prefab authoring
  and origin work already live. **F6 is now purely Distribution** and retitled to say
  so. The swap logic was NOT copied: PrefabCreatorVM holds a private PrefabSwapperVM
  and mirrors only the handful of bound properties and commands, so the orchestration
  that took real crashes to get right stays in one place.
- **The "Retired Features" notice is gone from F5.** A panel should not advertise what
  it deliberately cannot do; docs/ROADMAP.md is now the only record, and says so.
- **F6's anchor made coherent.** The anchor NAME shared a row with the prefab name
  while the anchor POINT (coordinates) and its Fill From Selection sat ~150 lines
  below - which is why the anchor looked like it had no fill button. Name and point
  are now adjacent, the prefab row is only about the prefab, and the point is the one
  with coordinates and a fill button.
- **Crash on closing a scene** ("CC_76_testing crashes every time I close the file").
  Two defects found from the engine log's teardown sequence:
  (a) closing a scene ran NO invalidation at all - the scene-change handler only
  reacted to a transition to a different *named* scene, so closing a file back to no
  scene left every cache, undo step and open layer pointing into a scene the engine
  was destroying; (b) `LastOperationHudLayer.Close()` removed its layer from
  `ScreenManager.TopScreen` rather than from `_screenItWasAddedTo` - at teardown those
  are different screens, so the removal silently did nothing and the dying screen was
  left holding a layer whose managed side had been dropped. That layer is also the only
  one that re-opens itself every tick, so it now refuses to open while no scene is open.
  All scene invalidation is now one guarded routine (`InvalidateSceneState`) used by
  both the switch and close paths, logged as `[SceneGuard]`. NOT proven to be the whole
  story: the crash log also carries the Qt null-receiver warning associated with the
  long-standing native instability.
- **Distribution's three roles separated** (F6). The panel used to take WHAT to place,
  WHERE to start, and WHAT to land on all from one selection, so in selection-source
  mode there was no way to say "copy this, starting there, onto that". Now:
  *Grid source* (prefab name / selection copies) is what to place; a new **Origin:
  Selected entity / Coordinates** toggle with its own coords box and Fill From
  Selection is where to start (coordinates supply position only - heading still comes
  from the selection, so Local axis choices keep working, and with coordinates set no
  selection is needed at all for a by-name grid); and a new **Surface target** box
  with its own Fill From Selection restricts what counts as ground.
- **Surface target filtering**: blank means "whatever is beneath" as before; a name
  restricts hits to that entity and its children (collision usually lives on children).
  Clutter in the way is passed through - the cast resumes just below a non-matching
  hit, up to six times - so a prop lying on the floor does not punch a hole in the
  grid. Normal-estimation samples honour the same filter, falling back to world up
  rather than tilting toward a neighbouring object.
- **Grow Selection now draws its radius**: one debug sphere at the centre of the
  original selection, sized live as the radius changes. Bound via reflection because
  `RenderDebugSphere` carries `[Conditional("_RGL_KEEP_ASSERTS")]`, which would delete
  a direct call at compile time; reflection sidesteps the attribute without defining
  that symbol assembly-wide. Nothing is created in the scene - debug primitives last
  one frame, so there is no entity to undo and nothing that can be saved by accident.
  UNVERIFIED: whether the engine's debug renderer draws at all in the editor build is
  not yet confirmed; the binding result is logged once either way.
- **Distribute Onto Surface**: the same uniform grid, but every cell is raycast down
  onto whatever is beneath it, so a run of columns follows terrain, steps or a slope
  instead of hanging off one flat plane - Pile Generator's technique minus the
  deliberate randomness. Honours the Grid source toggle (prefab name or selection
  copies), and has its own upright-vs-tilt-to-ground toggle beside it (upright by
  default: a column on a slope should be vertical, not leaning). The grid's step
  directions are flattened to horizontal so a tilted origin entity does not fight the
  raycast for control of height; heading still follows the entity's local axes.
  Raycast maths lives in the new `Core/SurfaceSnap.cs`, a verbatim port of
  PileGenerator's working implementation rather than a re-derivation - the retired
  RaycastPlacement is the cautionary tale, and PileGenerator's copy is left untouched.
- **Fill From Selection for distribution**: fills the prefab-to-distribute box from
  the selected entity - resolving `GetPrefabName()`, not `.Name` (the distinction that
  broke Mirror). Both distribution modes already require a selection to set the grid's
  origin, so typing that prefab's name by hand was the odd step out.
- **Grow Selection by proximity** (Ctrl+Numpad+, switchable from F9 -> Shortcuts):
  live modal panel; everything whose origin is within the radius of an originally-
  selected entity's origin joins the selection. The numpad pair and the arrow pair are
  interchangeable (Numpad+/Up grow, Numpad-/Down shrink), with the modifier choosing
  the step: plain 1, Shift 5, Ctrl 0.25. The number row types an exact radius, Enter
  applies, Esc restores. Radius is always measured from the ORIGINAL selection, so shrinking is
  exact and round-trips. Cost is handled by reading every entity origin ONCE into a
  managed array (cached 10s) and doing all radius maths in managed code behind an
  AABB pre-filter - no per-keystroke engine calls. Result capped at 2000.
- **Fixed: every hotkey stuttered the editor** (reported as "the editor freezes for a
  bit when I hit Ctrl+Shift+P"). `EntitySelector.GetLiveManualSelection` treated an
  empty result from the fast native selection call as "the call might have no-opped"
  and fell through to a full scene enumeration plus one `MBEditor.IsEntitySelected`
  per entity - ~7,700 entities on CC_74_battle, ~10,800 on CC_76_battle -
  synchronously inside the frame. Because the editor CLEARS the selection on a
  keypress before our Postfix runs, that empty path was hit by *every* hotkey press
  (Ctrl+Shift+P, Ctrl+Shift+I, Shift+R, Ctrl+Shift+T) by definition, and by
  ManipulationWatcher's 0.06s poll whenever nothing was selected. An empty result is
  now trusted (the same call is what clears the selection elsewhere, so it is known
  good); only a throw falls back to the scan. `RefreshManualSelectionCache` now reads
  through the same path instead of running its own full scan on every click.
- **Auto Origin** on the Prefab Creator panel (F5): re-origins every selected prefab
  to a named point on its OWN bounding box - each prefab measured and moved
  independently, unlike every other bulk transform here (which use a group pivot).
  Two cycle buttons (Axis X/Y/Z + Side Min/Center/Max) name the seven presets between
  them - Bottom/Middle/Top Center and -X/+X/-Y/+Y Center - with a label spelling out
  the current pair. Reuses `OriginToAnchor.MoveOriginTo`, so children are world-
  restored and the prefab does not visibly move. Entities with geometry of their own
  are skipped and named, not moved: an entity's mesh is drawn at its frame with no
  offset to compensate with, so re-origining one is impossible by definition.
  Backed up and undoable (roots + direct children captured, for an exact inverse).
- **Culture Preset Generator**: built-in (shipped) cultures are pinned to the top of
  both culture lists and tinted gold; custom cultures follow in blue. Backed by
  `CultureMaterialInference.IsBuiltInCulture` (reads the shipped defaults regardless
  of the Documents override).
- **Three new built-in cultures** in culture_definitions.json (shipped + merged into
  the Documents override, which wins at runtime): `roman_ruins` (the roman_*
  architecture family - aqueduct, brick, columns, hippodrome), `italian` and
  `european` (the 48/45 materials observed painted in blmm_terravallis /
  blmm_craglowe_v2; no Unique patterns, so they exist for preset generation, not
  name-based inference). Originally added as `terravallis`/`craglowe`, renamed same
  day at the user's request.
- **Four Continuous Recolor palettes generated from saved scenes** (written to
  Documents\...\MaterialSwapTool\Palettes, available in the palette browser):
  Italian (from blmm_terravallis), European (from blmm_craglowe_v2), CC74 Battle,
  CC76 Battle - dominant painted color per category, mined from each scene.xscene's
  mesh color factors with materials resolved via explicit overrides or
  mesh_slot_defaults.
- **The eight BuiltIn presets regenerated for coverage** (26->154, 24->130, 5->121,
  14->75, 21->124, 26->127, 23->117, 23->122 rules): every existing rule kept
  verbatim; added FROM rules for all six factions' Common materials that classify
  into a category each preset already converts, targeting that preset's own proven
  TO vocabulary (weighted specs where a category has several TOs). Culture tags
  added. Category attribution follows ApplyPalette's alphabetical-first overlap rule,
  which keeps floor targets out of wall specs.
- Small fixes: the last-operation HUD no longer sticks on screen when Numeric Transform
  is switched off mid-session; "Rename Changed" no longer stacks `_mst` suffixes on
  repeat applies; the unsaved-changes nag says "Scene Toolkit" instead of naming the
  wrong tool.
- Dedup: `ColorHex`, `EditorFrameSync`, `FamilyAutoPlacer` moved to `Core/` (three,
  two, and two identical copies respectively). Left per-tool on purpose:
  `PanelPositionStore`/`ScreenshotManager` (deliberately separate data folders),
  `PanelDrag` (tied to those stores), `EntitySelector` and `LivePrefabSwapper`
  (genuinely diverged - MST's swapper serves the Scene Analyzer fixers, PST's is the
  full-featured one).
- Module manifest (`SubModule.xml`) now deploys with the build instead of needing a
  manual copy; version bumped to v0.7.0.
- Distribution folder renamed `MaterialSwapOnly` -> `SceneToolkit`; old zips and the
  stale unzipped tree moved to `Distribution\Archive\`. New manual packaging script:
  `Tools\Package-SceneToolkit.ps1` (never runs as part of a build).
- Docs: in-editor Documentation corrected (Shift+R binding, 30 s takeover window,
  duplicate detection description) and extended (Randomize Rotation, backup
  consolidation); repo README/ROADMAP/KNOWN-ISSUES/TROUBLESHOOTING updated or added.

## v0.6 - 2026-08-19 .. 2026-08-22 (pre-consolidation state, snapshot only)

The label for everything between the v0.5 merge and the v0.7 pass - numeric transform
and drag takeover, the shared EditUndo stack, isolate/repeat shortcuts, backup
settings panel with verification and tiered retention, Scene Analyzer growth, panel
position persistence, the documentation overhaul. The live SubModule.xml still said
v0.5.0 throughout; the full source is archived as `BannerlordModArchive\v0.6\`.

## v0.5.0 - 2026-08-18 (the merge)

MaterialSwapTool + PrefabSwapperTool + PrefabCreatorTool merged into one assembly and
module, one Harmony instance, per-tool enable switches in tool_toggles.txt. Two layer-
order collisions and a panel-name collision fixed during the merge. Built to simplify
the crash investigation (later resolved: deploys while the editor was open).

## v0.4.0 - 2026-08-18 (three-mod archive)

The three standalone mods as they were at merge time, frozen under
`BannerlordModArchive\v0.4\`. No longer loaded or built.
