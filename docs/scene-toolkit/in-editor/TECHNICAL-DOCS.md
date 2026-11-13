# Scene Toolkit Technical Docs (in-editor)

> Generated from `src/BannerlordSceneToolkit/MaterialSwapTool/GUI/DocumentationVM.cs` by `Tools/Export-InEditorDocs.ps1`. Do not edit by hand: change the code and re-run the export.

The engineering notes behind the **Technical Docs** button on F9: confirmed bugs, their diagnoses, and the engine limitations that shaped the tools.

## Topics

- [About these notes](#about-these-notes)
- [Selection timing - why clicks used to wipe selections](#selection-timing---why-clicks-used-to-wipe-selections)
- [Color mechanisms - the 2026-08-19 discoveries](#color-mechanisms---the-2026-08-19-discoveries)
- [Entity-wide color - why blank means reset](#entity-wide-color---why-blank-means-reset)
- [Backups - consolidation history](#backups---consolidation-history)
- [Out-of-bounds check - the (x,z) bug](#out-of-bounds-check---the-xz-bug)
- [Isolate - why the binding is Shift+O](#isolate---why-the-binding-is-shifto)
- [F6 fixes - mirror origin, gizmo sync, per-scene history](#f6-fixes---mirror-origin-gizmo-sync-per-scene-history)
- [Numeric transform - watcher internals & the two-axis saga](#numeric-transform---watcher-internals--the-two-axis-saga)
- [Delete Physics - five rounds and what the scene file proved](#delete-physics---five-rounds-and-what-the-scene-file-proved)
- [Live-reference swaps - CopyFrom's tree behaviour](#live-reference-swaps---copyfroms-tree-behaviour)
- [Undo trust - UIDs, clones and position disambiguation](#undo-trust---uids-clones-and-position-disambiguation)
- [Weighted rules - how the roll granularity evolved](#weighted-rules---how-the-roll-granularity-evolved)
- [Runtime-created entities - stale editor state](#runtime-created-entities---stale-editor-state)
- [Misc: Culture Editor lag, palette rows, Flora API limits](#misc-culture-editor-lag-palette-rows-flora-api-limits)
- [Tooltip layer - ToolkitHints and the Hint widgets](#tooltip-layer---toolkithints-and-the-hint-widgets)
- [Unsaved-change guard - UnsavedGuard](#unsaved-change-guard---unsavedguard)
- [Crash ledger - NativeProbe and the Statistics scan](#crash-ledger---nativeprobe-and-the-statistics-scan)

## About these notes

These are the engineering notes behind the User Guide: confirmed bugs, how they were diagnosed, and the engine limitations that shaped the tools' designs. Nothing here is needed to USE the toolkit - it exists so that when something looks wrong, the history of what already looked wrong (and why) is one click away.

The complete dated record lives in docs/CHANGELOG.md in the source repository; docs/KNOWN-ISSUES.md tracks what is still open.

## Selection timing - why clicks used to wipe selections

WHY SELECT-IN-EDITOR LOOKED BROKEN FOR A WHILE (fixed 2026-08-21). Selecting does not happen at the moment you click. A click on one of the toolkit's panels also reaches the editor's own click-to-select handling underneath, which then resets the selection to 'whatever the mouse is over' - the panel, i.e. nothing. The old code selected inline and was wiped in the same frame: the reported count was correct while the editor showed nothing selected. Selection is now queued and applied two ticks later, once the editor has finished with that click. Two ticks, not one, because the mouse RELEASE arrives on the following frame and can clear a selection just as well as the press did.

RELATED (2026-08-23): every panel's outside-click handler used to null the SCREEN-WIDE focused widget, which stole focus from the editor's own resource browser at the exact moment a drag-placement started - killing the native drag ghost whenever any toolkit panel was open. All panels now drop focus only when the focused widget belongs to that panel (PanelFocus.DropFocusOnOutsideClick).

## Color mechanisms - the 2026-08-19 discoveries

THE 2026-08-19 COLOR DISCOVERIES, in order:

1. The per-rule color factor used to go through MetaMesh.SetFactor1 - a property covering a whole MetaMesh component. A normal building has exactly ONE MetaMesh covering every part across every LOD tier, so every rule matching anything on that entity fought over the same single color value; whichever rule processed last silently won. That is why several per-material colors used to collapse into one uniform tint. Fixed by switching to Mesh.Color - the per-part granularity Fix Color Mismatches had already proven live.

2. GameEntity.SetFactorColor does NOT behave like an independent tint layered over Mesh.Color - confirmed via tool.log, GetFactorColor() read back the exact color just written to individual meshes in the same Apply. It writes ACROSS the entity's own mesh-color state. Running the entity-wide clear AFTER the per-mesh loop therefore stomped every correctly-applied color back to white ('I applied #ffd580 but it's white') - it now runs FIRST.

3. Entities colored by the pre-fix Factor1 path kept a permanent stale Factor1 tint multiplying into the render on top of correct Mesh.Color values. Apply now clears stale Factor1 on entities it touches (never scene-wide - unrelated native systems use Factor1 too).

4. Mesh.Color was confirmed as a real live per-INDIVIDUAL-slot tint via a test scene (european_city_house_d3) where exactly one LOD5 submesh carried a non-white Color while Factor1 and FactorColor were both white. Continuous Recolor and Fix Color Mismatches use this same mechanism.

LATER (2026-08-23): unparseable typed colors (e.g. '#ffwe193') used to be skipped silently, indistinguishable from 'broken' - they now warn on screen and in the log. MST undo used to restore an entity-wide entry via SetFactorColor(old), which (per discovery 2) flattened per-layer colors the batch never touched; entries now snapshot every mesh color before the forward SetFactorColor and undo restores them individually.

MESH.COLOR IS STORED LINEAR (2026-11-12). Typed colors came out lighter and less saturated than the hex, always by the same curve (#BAFFAF -\> #DDFFD7, #802020 -\> #BB6363). The editor's own color field converts the typed sRGB hex to linear first; the toolkit wrote it raw. ColorHex.SrgbToLinear / LinearToSrgb (gamma 2.2, alpha untouched) now convert at the typed-input write sites: Continuous Recolor palette and single-color apply, the Material Swap rule color factor, and the seeded hex of Get Input from Selection. Unchanged on purpose: undo/redo logs, the LOD color fix, Fill In Overrides and color presets copy stored values mesh-to-mesh, so they stay raw and round-trip exactly. The entity-wide color factor (SetFactorColor) is not converted; whether it has the same issue is untested.

## Entity-wide color - why blank means reset

WHY BLANK MEANS RESET on the entity-wide color box: GameEntity.SetFactorColor is a persistent property that never resets itself between Apply calls. Treating blank as 'don't touch' (correct for per-rule boxes, where each row is independent) meant a previously-applied saturated entity-wide tint silently kept multiplying into every per-rule/per-LOD color applied afterwards - the new colors WERE applying correctly and just looked wrong under the leftover tint. The box is a single always-visible field representing current intent, so blank unambiguously means 'no entity-wide tint', same as clicking Clear (both resolve to white, the multiplicative identity).

## Backups - consolidation history

HISTORY. Backup settings used to be compile-time constants - the direct reason backups once sat switched off for three days with nothing saying so; they persist to backup_settings.json now. F9 used to open Flora Swap's placeholder before the Backups panel took the key.

CONSOLIDATION (v0.7): Prefab Swapper and Prefab Creator used to run their own separate copies of the backup system - three independent timers copying the same three files into three folders, with only Material Swap's copy honouring the F9 settings. Everything routes through the one system now; the old PrefabSwapperTool\\Backups and PrefabCreatorTool\\Backups folders are frozen history, no longer written.

The 2026-08-17..19 crash-on-save investigation briefly blamed background File.Copy racing the editor's save; the engine's own logs disproved it - builds were deploying module files while the editor was open, and the editor read a half-written file. Backups had been off the whole time. Hence the deploy-on-exit discipline.

## Out-of-bounds check - the (x,z) bug

The out-of-bounds check computes a 2D convex hull from border_soft marker positions. Bannerlord is Z-UP, so the ground plane is X-Y - an earlier version tested (x,z), mixing a horizontal axis with HEIGHT, and therefore flagged the entire scene as outside. It now also refuses to report 'everything is outside' (never a real result) and logs its reasoning as '\[Boundary\]' in tool.log.

## Isolate - why the binding is Shift+O

Isolate was Ctrl+Shift+I, then briefly Ctrl+Shift+O, both on 2026-08-22: the editor places on bare I even with modifiers held (matching the ground's rotation), and a native Ctrl+Shift+O HID the selection - both collided. Shift+O with Ctrl NOT held avoids both natives, which is why the binding checks that Ctrl is up.

THE RESTORE RECORD (2026-10-03). The first version kept the record as a dictionary of live entity pointers and its own comment claimed isolation never reached disk. Both were wrong: scene.xscene stores visible="false", and the record was dropped on every scene-screen teardown - which includes ENTERING TEST MODE (the same HandleDeactivate hook as a real close; see the test-mode crash notes). Isolate + save + reload, or Isolate + test mode, therefore left the scene hidden with Shift+O reporting nothing to restore. Fix: each hidden entity carries the tag 'mst_isolated' (saved with the scene, untouched by test mode); Restore un-hides by tag as well as by record; teardowns and scene opens arm a deferred tag scan (3-5 s after the scene is live, never on the first resumed frame - that whole-scene native walk is what crashed the editor on 2026-08-31) which rebuilds the record and warns. The scene file's write time is watched while isolated so a save-while-isolated warns once per save.

## F6 fixes - mirror origin, gizmo sync, per-scene history

MIRROR WITH 'EXACT ORIGIN POINT' used to reuse the source's origin unchanged, so a mirrored prefab ended up with literally the same origin as its source; it reflects the anchor's origin across the mirror plane now.

TRIAD/GIZMO SYNC: SetGlobalFrame moves an entity but the editor keeps its own cached frame for the transform gizmo. For an entity just created, that cache is world origin - the 'transform panel is right but the gizmo is at the map corner, and dragging it teleports the entity' symptom. Every toolkit move now calls UpdateTriadFrameForEditor (+ all-children) afterwards; this affected every F6 placement tool.

PER-SCENE HISTORY: the swap log is one shared file across all scenes. A batch from a different scene, undone here, re-finds entities by NAME AND POSITION - and the fief_\*/material_test_\* scenes are copies of each other with entities at near-identical coordinates under near-identical names (and cloned mst_uid tags), so a cross-scene undo could silently swap the wrong thing. Cross-scene batches are refused outright; refusals log as '\[SceneGuard\]'.

MIRROR'S RESIDUAL LIMITATION: a hand-built prefab-LESS piece mirrored an ODD number of times renders inside-out until the scene is saved and reloaded - the saved data is correct; the live winding cache is not reachable from managed code.

PATH ROTATION ON GAME v1.5.7 (2026-11-11). After the v1.5.7 update Path.GetFrameForDistance returns the position but an identity rotation, so Distribute along path placed every instance world-aligned. PrefabDistributor.PathFrameAt now samples two positions 5 cm apart, flattens the direction to the ground plane and builds the yaw-only frame the engine used to return. All path sampling goes through it.

MESH BENDER ON SWAPS (2026-11-12). Instantiate only brings the new prefab's own definition and the live-reference path discards everything from the old entity, so every swap used to drop the native mesh_bender script. It is carried now with GameEntity.CopyScriptComponentFromAnotherEntity, and the two float values with Get/SetNativeScriptComponentVariable; each carry logs '\[MeshBender\]' with the values read back from the new entity.

## Numeric transform - watcher internals & the two-axis saga

HOW THE TAKEOVER KNOWS ANYTHING: the editor exposes no manipulation state at all - no rotate flag, no gizmo axis. The ManipulationWatcher polls the selected entities' frames and looks for the SHAPE of an operation: change followed by stillness. That covers gizmo drags, G/Z modal drags and typed transforms alike, and yields the frames from BEFORE the drag - which is what lets a typed number replace the drag. Rotation is checked before movement (rotating a group also moves each origin); the settle never fires while the mouse button is held (a pause mid-drag used to split one drag into two recorded operations); duplicates are recognised by the selection switching to a NEW entity while the original sits untouched.

THE TWO-AXIS SAGA (2026-08-23, four iterations): the original behaviour - typed value replaces the whole drag with a clean single-axis move - turned out to be what was wanted all along; the intermediate 'preserve the perpendicular residual' and 'per-axis values' designs both read as bugs in practice. Final semantics: nothing typed = hold the full drag (no jump on open or right-click reset); a typed value = pure single-axis move from the pre-drag position, other axes reset; Esc = cancel to pre-drag.

Shift+R replays through the same watcher; its early failures were, in order: clones of prefab-less sources, selection not chained to the new copies, click-jitter micro-moves overwriting the recipe (0.5m filter now), and typing Shift+R before the settle window closed while Shift was still held (ForceSettlePending closes that race).

## Delete Physics - five rounds and what the scene file proved

DELETE PHYSICS took five rounds because each layer of the engine hides the previous one:

1. RemovePhysics + skip-if-BodyFlags-None: the skip was wrong (collision shapes on children can report None while solid), and RemovePhysics alone left collision.  
2. + SetBodyFlags(None) with readback: readback showed all flags cleared - still solid. Flags are not the registered body.  
3. + EntityFlags.PhysicsDisabled (the lever that provably makes CopyFrom clones unclickable): still solid. The flag is honoured at body CREATION, not retroactively.  
4. Broken-out CopyFrom pieces (a plain Instantiate saves as \<game_entity prefab="X"\> and the LOADER rebuilds physics from the prefab definition every load - nothing per-entity persists on a prefab reference): the pieces now serialize with explicit components... and the serializer STILL wrote \<physics shape="bo_..."/\> from entity metadata no runtime API clears.  
5. File surgery: the pieces are tagged pile_nophys at generation; the stripper removes their \<physics\> nodes from the saved scene.xscene (timestamped .bak first). An editor save from a stale session re-emits the nodes - confirmed live by matching timestamps - so the stripper also re-runs automatically ~2s after every detected save. Collision state in a LIVE session only updates on load; judge results after a reload.

Every claim above was verified against the saved xscene, not inferred.

REMOVE PHYSICS OUTSIDE BORDER (F7) was still the round-1 approach until 2026-11-12: it skipped every entity whose own BodyFlag read None (every placed prefab root) and called RemovePhysics, which never persists. It now uses round 5: each out-of-border entity is tagged oob_nophys, live prefab links among them are broken first (a prefab="X" entity has no children in the file, so there would be nothing to strip), and the stripper removes the \<physics\> nodes of the tagged entity AND its whole subtree after each save. The stripper covers subtrees for pile_nophys too. This rework is not covered by the 'verified' line above.

## Live-reference swaps - CopyFrom's tree behaviour

GameEntity.CopyFrom copies the ROOT entity and its prefab-definitional children, but NOT children attached at runtime (re-parented survivors of earlier swaps, hand-added decorations) - on a worked-on composite that is all the visible geometry, so early live-reference swaps produced empty invisible copies. The swap now clones missing children explicitly and recursively, comparing matched source/copy pairs level by level (grandchildren CopyFrom drops are cloned too).

The name-swap 'preserve extras' pass then DOUBLED every part on like-for-like variant swaps: clones carry auto-suffixed unique names, so the supersede-by-name check never matched. Live-reference mode now carries NOTHING from the old entity - an exact copy of the reference is the mode's entire meaning. Name-mode swaps distinguish the old prefab's DEFINITIONAL children (probed from a throwaway instance of the old prefab - they die with it) from genuinely hand-added extras (preserved) - the fix for cross-culture swaps keeping the old culture's merlons.

CopyFromPrefab (a different API) DAMAGES live source entities' children and must never be used against scene entities - confirmed live 2026-08-23, reverted same day.

CopyFrom products are flagged DontSaveToScene | NonModifiableFromEditor | WaitUntilReady | PhysicsDisabled at birth - unselectable and unsaveable until cleared. Every clone path clears the tree recursively; a scene-open sweep repairs copies made before the recursive clear existed (clean root + flagged descendants is the signature that makes a whole-scene sweep safe). Click-selection registration of fresh copies remains imperfect until a save rebuilds the editor's registry - the hand-rolled clone in docs/ROADMAP.md is the tracked real fix.

## Undo trust - UIDs, clones and position disambiguation

RevertManager only auto-applies a change-log entry it can TRUST to the right entity:

- A UID tag match within position tolerance is trusted. Cloning duplicates tags (including UIDs), and multiple tag-sharers used to demote the whole batch to confirmation - the 'undo is fully broken' report was a recolor-then-clone-copies workflow. Position now disambiguates: exactly ONE tag-sharer within 10cm of the logged position is the original (recolors move nothing; clones were dragged away).  
- Entries without UIDs were NEVER auto-trusted - Continuous Recolor entries carried none, so CR batches were always un-undoable until 2026-08-23. CR stamps UIDs now, and a no-UID entry is trusted when exactly one same-named entity sits within 10cm.  
- Cross-scene batches always demote (see the F6 topic).

Prefab-swap undo runs the swap machinery backwards, which used to honour the panel's global toggles: ADD mode made 'undo' place the restored prefab NEXT TO the thing it should remove; 1x Scale stripped a scaled original on restore; the Z-rot/scale extras would distort it the same way. Undo/redo force neutral toggles for their duration and restore at the recorded PRE-swap frame (the swapped entity's current frame carries whatever extras the forward swap applied).

## Weighted rules - how the roll granularity evolved

A weighted To-spec ('matA:4, matB:1') resolves deterministically from a stable hash - never string.GetHashCode(), which .NET randomises per process. The seed's granularity took four iterations (2026-08-23):

per-mesh -\> LOD tiers of one part rolled apart (material popped with distance);  
per-part-name -\> merged simplified LOD meshes pair with nothing and still rolled alone;  
per-target -\> filter/scene modes hand children over as their own targets, patchwork again;  
per TOPMOST PARENT (final) -\> everything under one placed prefab shares one roll, cached across the Apply; variety lives BETWEEN instances, which is what weights were for.

Duplicate From-rows used to be silently collapsed to the first (.First() in the rule map); they are alternatives now, one row picked per top-level prefab, each row keeping its own color factor. Rules also match through the '(copy)' suffix the engine gives override-clone materials - a mesh reporting 'x(copy)' matches a rule written against 'x'; half-applied-looking swaps were the plain-named half matching while the cloned half never could.

## Runtime-created entities - stale editor state

Runtime-created entities (pile pieces, clones) hold STALE EDITOR STATE until a save + reload rebuilds the editor's caches: visibility toggles don't take, physics changes don't show, click-selection may miss. UpdateSceneTree is nudged where it helps; the full heal is save + reload. Same underlying registry limitation as the CopyFrom ghost story (see Live-reference swaps) - the hand-rolled clone in docs/ROADMAP.md is the tracked real fix.

Related engine truths collected along the way: entity SCALE is encoded as the frame's rotation basis lengths (there is no scale setter; 'scale can't carry over' was half wrong - it silently ALWAYS carried in the frame, which is why 1x/Inherit is an explicit toggle now); a freshly Instantiated entity's bounding box can still be dirty on the next call (the rglEntity.h:2068 assert - RecomputeBoundingBox before measuring); Instantiate auto-suffixes names for uniqueness, so name matching strips trailing _NN throughout.

## Misc: Culture Editor lag, palette rows, Flora API limits

CULTURE EDITOR LAG: a culture's Common list runs 60-145+ entries; rendering every culture's list as live text at once is what made the panel lag - lists are collapsed to a count + Edit button per row now.

RECOLOR PALETTES: category rows used to share ONE color for every armed category; per-row colors replaced that, with Fill Color as a bulk-fill preserving the old workflow.

FLORA LAYERS: the live scripting API exposes exactly two read-only flora counters (GetFloraInstanceCount, GetFloraRendererTextureUsage) and nothing else - no enumerate, no read-where-painted, no write. Confirmed by reflecting the shipped engine DLL. Editing terrain flora layers from a mod is an engine-side impossibility, which is why Flora Swap only ever handled placed flora PROPS and is retired.

CCMODULE_SP STARTUP: its wEditor bin was missing (DLL popups - fixed by populating it); the RGL texture warnings are unfixable from outside - the sheets exist in the module's pack0.tpac but the editor doesn't mount them and ignores loose GUI PNG paths (extraction was tried; correct layout, still not consulted). An external watchdog dismisses those popups at launch instead.

## Tooltip layer - ToolkitHints and the Hint widgets

THE SHARED TOOLTIP LAYER (2026-11-12, Core/ToolkitHints.cs). A hint is declared in the panel XML as a hidden text child with Id="Hint" inside the widget it belongs to - normally a ButtonWidget, or the wrapper Widget of a text field:  
  \<TextWidget Id="Hint" IsVisible="false" DoNotAcceptEvents="true" Text="..." /\>  
Nothing else is needed: no view-model property, no command, no per-panel code. Adding a hint to a new button is one XML line.

HOW IT IS SHOWN. Once per frame ToolkitHints looks at every Gauntlet layer on the top screen, asks its event manager which widget the mouse is over, and walks up at most four parents looking for a 'Hint' child. After a dwell of 0.45 s the text is shown in ONE shared layer (ToolkitHintPanel.xml). It is ticked from all three editor tick patches, so hints keep working whichever tools are toggled off.

That layer is not a focus layer and every node in it is DoNotAcceptEvents, like the last-operation readout: it is drawn over the viewport and must never take a click or a keystroke. A mouse press hides the hint and keeps it hidden for that widget until the mouse moves to another one. The box flips left / up near the right and bottom edges and follows the shared panel scale.

SWITCH: HintsEnabled in shortcut_settings.json (default on), shown as 'Tooltips: ON/OFF' on F9 -\> Shortcuts.

SAFETY NET. The first live build laid the box out at full-screen size, so every tooltip covered the editor (two background sprites were StretchToParent children of a CoverChildren widget and took their size from the full-screen root). The sprites now sit on the CoverChildren containers. In addition, a box that measures more than 60% of the screen in either direction switches hints off for the session and logs a '\[Hints\]' line.

WHY NOT GAUNTLET'S OWN HintWidget: it relies on the game's global information layer, which is not known to exist in the editor screen, and needs a view-model property per button.

## Unsaved-change guard - UnsavedGuard

Core/UnsavedGuard.cs (2026-11-12) is the 'Discard changes / Keep editing' question of the three row editors: Interior Whitelist, Category Editor, Culture Editor. Those panels only write on Save, and Close / Esc used to discard edits without a word.

DETECTION compares a signature of the rows with the signature taken when the rows were last loaded or saved (HasUnsavedChanges on each view model). No setter has to report a change, so none can be missed.

ONLY THE USER'S OWN CLOSE BUTTON AND ESC GO THROUGH IT. Forced closes (parent panel closing, scene switch) still close at once - asking a question during a teardown is worse than losing an edit. A second Esc while the question is up is ignored. If the question cannot be shown, the panel closes and a '\[UnsavedGuard\]' warning is logged, so a failure never traps the user in a panel.

An inquiry has two buttons, so the question is 'discard?' and the safe answer keeps the panel open. A 'save and close' button was left out on purpose: a save that fails would then also close the panel.

RELATED: F7 Delete Interiors checks InteriorWhitelistLayer.HasUnsavedChanges and refuses while it is true. The delete acts on the SAVED whitelist; with the panel edited but not saved, what is on screen there protects nothing.

## Crash ledger - NativeProbe and the Statistics scan

Core/NativeProbe.cs (2026-11-12) is a crash ledger for engine calls that have not yet been proven safe in the editor. A native crash is not a managed exception: try/catch never sees it and nothing is logged. The first live run of the F7 Statistics scan took the editor down that way, with half a dozen new engine calls in one loop and no way to tell which one did it.

HOW IT WORKS. Each group of unproven calls runs inside NativeProbe.Run(name, ...). Before it starts, 'trying|name' is appended to the ledger and flushed to disk; when it returns, 'ok|name' is appended. If the editor dies in between, the file still ends on 'trying' for that name. The next time the toolkit loads, it reads that as 'this call crashed the editor', appends 'crashed|name', and Run() skips that name from then on. So a bad call costs ONE crash, names itself in the file, and the feature keeps working without it. A managed exception is not a crash: it is recorded as survived and rethrown.

THE LEDGER is MaterialSwapTool\\native_probe_ledger.txt, one 'state|name|time' line per event, read once per editor process; the last line for a name wins. Deleting the file retries everything. To retry one name, delete all of its lines - removing only the 'crashed' line leaves the 'trying' line last, which reads as a crash again.

THE STATISTICS SCAN runs in passes for this reason. Pass 1 uses only long-proven calls (live prefab name, mesh component count). Each newer call gets its own pass under the ledger: Scene.RootEntityCount, the baked-copy name match (GameEntity.PrefabExists), the component counts for cloth, particles, decals and lights, the script count, the light getters, and the mesh-name + Mesh.GetFaceCount pass of the Heaviest Meshes tab. A skipped pass shows as 'NOT COUNTED' in the panel.

WHAT IT FOUND: GameEntity.GetOldPrefabName(). The ledger ended on 'trying' for that call on the second crash. It is no longer called anywhere in the statistics code and must not be called from this toolkit: it takes the editor down natively. Baked copies are therefore named from their entity name instead - the engine is asked whether a prefab of that name exists, and if not one _2 / _3 / _mst style suffix is stripped and it is asked again (up to four steps, cached per distinct name).
