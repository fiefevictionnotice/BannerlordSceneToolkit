# Roadmap - pending, deferred, and retired features

This is the canonical list. It was consolidated on 2026-08-22 from
`Documents\AudioBot\deprecated-features.md` (which now points here) and from
scattered session notes, so that pending work is tracked inside the project it
belongs to. When a feature's status changes, update this file.

Ground rules carried over from earlier decisions:

- Retired features keep their code in the tree, disconnected from the UI, so a
  revival starts from something instead of from zero.
- Nothing in "Deferred" or "Retired" gets built or re-wired without being asked.
  Items under "Proposed" are candidates from the 2026-08-22 audit - identified,
  discussed, not committed to.

## Deferred (planned, not started)

### Prefab consolidation - convert a selection into a real prefab (USER'S TOP TRACKED ITEM)
Asked 2026-08-22 16:00 and lost between sessions until recovered from the session logs
on 2026-08-22 23:15 - original words: "Is there a tool that we can 'convert' selection
from any subset of child entities to prefabs? So if I select just part of a prefab I
can convert that to the top-level prefab? Which tool should this go into? I really
can't decide."

Why it matters beyond convenience: the evening of 2026-08-22 showed that nearly every
sharp edge in the toolkit traces back to entities that are NOT real prefabs -
hand-built composites can't Instantiate (mirror needed a CopyFrom fallback with its
DontSaveToScene/NonModifiableFromEditor flag traps and inside-out stale-winding
rendering), swaps refuse them as un-undoable, and pieces living INSIDE placed prefab
instances trip "break prefab?" dialogs on removal. Consolidating selections into real
saved prefabs drains that swamp at the source.

Open design questions: where it lives (Prefab Creator is the natural home - it already
owns New Prefab/anchor creation); whether "convert" means writing an actual prefab XML
resource the engine can Instantiate by name (the valuable, hard version) or just
restructuring under an anchor (Mode 1 already does this); and how naming/dedup works
when the selection is a subset of an existing prefab.

### Hand-rolled clone for prefab-less sources (fixes unselectable-until-save)
The 2026-08-23 marathon established: GameEntity.CopyFrom products are invisible to
the editor's click-select registry until a SAVE rebuilds it, and no reachable managed
API performs that rebuild (tried and disproven: flag clears of every kind - recursive,
sweep-verified; UpdateSceneTree; SetReadyToRender; AttachEntity; CopyFromPrefab, which
is outright DAMAGING - it mutates the live source's children, never use it). Editor-
born entities (CreateEmpty isModifiableFromEditor:true, Instantiate) never have the
problem. The fix is therefore to clone WITHOUT CopyFrom: CreateEmpty root + per-child
CreateEmpty/Instantiate + AddMultiMesh(metaMesh copy) + physics carry-over (bo_ shape
names are known; API for attaching them TBD) + AddChild. Until built, the documented
workaround stands: save the scene and CopyFrom clones become fully normal.

### MCP live-editing bridge
An MCP server letting Claude Code issue live editing commands to the running scene
editor, instead of the compile/deploy/manual-test loop. The tick patches already run
every frame and could poll a command queue (file or named pipe), executing through the
existing engines (PrefabDistributor, LivePrefabSwapper, MaterialSwapEngine). Agreed
design constraint from when this was first discussed: a reviewable, rate-limited
command queue, not a raw synchronous RPC bridge - and not while the native Qt5Core
crash investigation is still open.

### Cycle through overlapping entities
Press a key repeatedly to step through entities stacked at the same spot - the
"I cannot click the thing behind the thing" case. The pieces exist:
`LiveSceneChecks.CheckDuplicates` finds the clusters, deferred selection can select
one at a time.

### Alpha-texture filter for generated culture presets
From the tutorial video (1:03:45, 1:07:45): the Culture Preset Generator's worst results
are all alpha-tested materials (moss, transparency variants). Wanted: a button that
swaps every alpha material in a culture's lists for its non-alpha equivalent, and/or a
generator option that drops alpha materials from the bridge. Data-level, no engine work.

### Retire Dry Run's reselect in favour of "Select Rule Matches in Editor"
User's own suggestion on video (1:19:30): Dry Run's automatic reselect disturbs a manual
selection, while Select Rule Matches in Editor does the same job without that side
effect. Candidate: make Dry Run counts-only everywhere and keep the one selection
button. Needs the Manual-mode reselect claim in SelectInEditorDoc verified first.

### ~~Automated release packaging~~ - done v0.7
`Tools\Package-SceneToolkit.ps1`: stages the deployed module, warns when the deployed
DLL is older than the source, zips with README/LICENSE/tool_toggles.txt. Manual, on
request only - no build step invokes it, by explicit decision.

Note: the Replay Tool (record a match, play it back with a free camera) is tracked
separately - it is its own standalone mod at `Documents\BannerlordReplayTool\`, not
part of this toolkit, and deliberately stays out of this repo.

## Proposed (2026-08-22 audit) - status after the v0.7 pass

Decisions made 2026-08-22; the survivors stay here as candidates.

Still open, in rough order of value:

1. **Align selection** (Blender Object > Align): line up selected entities on an
   axis - min/center/max of their bounding boxes. Straightforward with
   SetGlobalFrame + GetGlobalBoundingBox. Would live on F6 (Prefab Swapper), next to
   Rotate/Mirror in the transform section - same selection plumbing, same undo
   capture. User interested, not yet requested.
2. **Frame Selected** (Blender numpad-period): one hotkey that calls
   MBEditor.ZoomToPosition on the selection's centroid. EditorNavigation.GoTo is
   already 90% of it. User notes 2026-08-22 ("save framing for later"): candidate
   key Ctrl+F (verify the editor doesn't own F/Ctrl+F first - the Ctrl+Shift+I
   lesson: native single-key actions fire even with modifiers held); do NOT zoom
   all the way in - back the camera off from the selection's bounding sphere
   rather than landing on top of it.
3. **Hide / Unhide Selected** (Blender H / Alt+H): plain hide with a restore list,
   complementing Isolate. IsolationManager's record-and-restore-verbatim approach
   carries over directly.
4. **Angle snapping in numeric/rotate operations**: a modifier or toggle that rounds
   to 5 or 15 degree steps, matching Blender's Ctrl-snap during rotation.
5. **Custom pivot everywhere**: Mirror already accepts a typed pivot point; Rotate
   (Specify) and the numeric modal still always use the group's bottom-center.
   Generalizing the pivot picker (bottom-center / first-selected / typed coords)
   would be the closest practical analog to Blender's pivot-point dropdown.
6. **Scene Analyzer report export** - findings currently live only in the panel; a
   "write report to file" button would make scans diffable between passes.
   Documented as a wanted feature 2026-08-22, explicitly not needed yet.
7. **Remaining dedup** of the deliberately-skipped near-duplicates
   (PanelPositionStore, ScreenshotManager, PanelDrag, EntitySelector,
   LivePrefabSwapper) - needs a data-path migration plan; see KNOWN-ISSUES.md.

Done in v0.7 (2026-08-22, see CHANGELOG.md): randomize rotation (per-axis-capped,
per-entity, on the Pile Generator panel), the shared backup service, log rotation,
mirror-through-cloner, the small-utilities dedup, packaging script, Distribution
rename, Select Whole Prefab (Ctrl+Shift+P), Grow Selection by proximity
(Ctrl+Numpad+), Auto Origin (F5), grid-from-selection and Distribute Onto Surface
(F6), material category import/export, built-in cultures pinned in the generator, and
the panel-XML build validation.

Declined 2026-08-22:

- **Select Similar** - filtered selection by name/material/rule already covers the
  need; judged extra complexity for now.
- **git init** - the user prefers no git here. Instead: offer a full-source snapshot
  to `Documents\BannerlordModArchive\` before any large change (v0.6 set the
  pattern), and keep docs/CHANGELOG.md as the version trail.

Not portable, checked and ruled out: true modal G/R/S over the native gizmo (MBEditor
exposes no manipulation state - the watcher-based takeover is the practical ceiling),
proportional editing, and anything requiring a scale setter.

## Retired from the UI (code intact, lowest priority)

**This file is the only record now.** Until 2026-08-22 the Prefab Creator panel carried a
"Retired Features" notice listing these; it was removed, because a panel is not the place
to advertise what it deliberately cannot do. The code remains in the tree, disconnected
from any button - so everything below is still revivable, and this section is what to
read before trying.

### Snap to Surface / Snap Into Pile - Prefab Swapper (retired 2026-08-18)
Drop selected entities onto whatever is below them via raycast; Snap Into Pile added
scatter and spin. Buttons removed from `PrefabSwapperPanel.xml`; logic intact in
`PrefabSwapperTool/Core/RaycastPlacement.cs` and the VM's ExecuteSnapToSurface /
ExecuteSnapIntoPile. Several real fixes landed (raycast self-collision, processing
order, centroid convergence, a scale-reset in the surface-alignment math) and live
testing still found it not good enough. Pile Generator's own snap/scatter (Prefab
Creator) uses the same technique, was not reported broken, and stays live.

### Identify Variations - Prefab Creator (retired 2026-08-18)
The cluster and same-prefab-instance duplicate detectors for turning hand-placed
color variants into reusable presets/prefabs. Pulled from `PrefabCreatorPanel.xml` on
direct report ("basically doesn't work right now. At all") without a debugging pass,
so no root cause is on record. Detectors and VM methods remain in
`PrefabCreatorTool/`.

### Texture Sets / Pairing Browser / Family Browser / Color Presets - Prefab Creator (retired 2026-08-19)
The F5 panel is deliberately cut down to naming prefix + New Prefab + Pile Generator.
The rest was retired after repeated instability including one confirmed crash (an
ArgumentException in ComboMemberRowVM.AutoPlace's setter - fixed, but the area was cut
anyway as a scope decision). All VMs, layers, panel XML, and stores remain in the
tree. One finding preserved from this area: GauntletUI can bind a native
`TaleWorlds.Library.Color` straight to a widget's `Color` attribute via
`[DataSourceProperty]` - confirmed working, reusable for any future UI.

### Flora Swap Tool (retired 2026-08-18)
F9 now opens Backups; Flora Swap left only a placeholder panel
(`FloraRetiredPlaceholderPanel.xml`) and its code stays per the retired-not-deleted
convention.
