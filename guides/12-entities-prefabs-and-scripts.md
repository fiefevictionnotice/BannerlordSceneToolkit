# Guide 12: Entities, Prefabs and Scripts

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - Unparenting is not breaking. Right-click > Break is what makes a prefab self-contained; a prefab whose XML is not in players' installs makes MP maps crash or props walk-through (1.x, 2026).
> - Never use `default_empty` as an "invisible" material. It crashes scenes. Use `editor_gizmo`: it hides the visuals and keeps the physics.
> - Parent prefab contents to an empty entity, never a mesh, and put scripts (wave_floater, path_navigator, mesh_bender, rotator) on the empty. This one habit avoids scale resets, drifting rotation and unclonable parents.
> - Collision is a separate `bo_<meshname>` physics mesh/shape. No body means walk-through, and editor test success is not proof: a missing `bo_` shows up only on a server.
> - Moving scripted props (path_navigator, windmill, rotator) are not synced in multiplayer, and chairs / "press F" need custom scripts on native MP maps. Plan MP scenes as static.

Version labels: "e1.x" = early access (2020-22), "1.x" = after the Oct 2022 renumbering. Labels follow the finding's date, not patch notes.

Scope: entity and prefab handling in mission scenes. Terrain, navmesh and paint layers are in other guides.

---

## 1. Placing, selecting and scaling entities

1. Add an entity from the prefab menu (prefabs only), the flora menu (flora kinds only) or the resource browser. A mesh from the resource browser is not an entity: add an Empty Entity first, name it, then add the meta mesh (e1.x, 2022).
2. Press T to translate (works before placing). Hold P to snap to terrain/scene, I to snap with normal rotation, G to move on the XY plane. Alt+W / Alt+L toggles world/local gizmo. Shift+drag the gizmo duplicates. Double-click an entity in the Scene tab to frame it.
3. Select children of grouped prefabs with Shift+click or the Scene-window dropdown.
4. Judge scale with the `adam` / `adam_0` prefab (1.8 m person, editor-only).
5. Find placed entities with `t:tagname` in the scene window search.

**Gotchas**
- Negative scale (mirroring) and double negative rotations are not saved reliably (1.x, 2024-26): roughly 30% of flipped entities moved, lost rotation or got broken normals on reload, and some crash. Safer: flip the mesh in Blender and reimport. Fallback: copy the transform, delete, re-place, paste the transform (FiefEvictionNotice, 2026).

Wiki: https://docs.bannerlordmodding.lt/editor/entities/ - new (the page is an empty stub). Basic hotkeys may overlap /editor/controls/.

---

## 2. Parents, grouping and bounding boxes

1. Create an empty entity at the pivot you want (centre of the group, scale 1).
2. Drag props onto it in the Scene tab (or "add parent prefab"). The right-click "assign new parent / group" menu made children vanish or crash in e1.x.
3. Scale or move the parent only.
4. If objects pop out at certain camera angles, resize the parent (even an empty) so its bounding box covers every child, or detach the children (Swan, Hart, 2024).

**Gotchas**
- Parent to an empty, not a mesh: modifiers apply to the whole prefab, T+P placement uses the parent's origin, and a parent can't be cloned (FiefEvictionNotice, 2026).
- Avoid child-of-child nesting. In 1.x (2023) nested children rotated or turned black with locked 360 rotation after reload. Fixes: delete and remake the child, zero its rotations (Shift-click), or keep a backup scene with the pieces separate (Capt Cole, Klassix, Sheepbitter).
- A scaled prefab resets to 1.0 on save: put it under an empty and scale the empty (Litauen, 2024, unverified).

Wiki: https://docs.bannerlordmodding.lt/editor/prefabs/ - new. The page holds only a video link and an XML-editing anecdote.

---

## 3. Prefabs: create, break, ship

**Create**
1. Parent the props to an empty (section 2).
2. In the entity inspector choose "Save As" prefab. The saved name is the entity name. Saving to an existing XML adds rather than overwrites, so keep many prefabs per file grouped by theme (1.x, 2026).
3. Prefabs are plain `game_entity` XML in your module's `Prefabs` folder, beside `SceneObj`; they are not stored in the xscene. A scene copied to another module without them may fail.
4. Save into your own module, never Native or another base module (updates delete them). A shareable module needs only a `Prefabs` folder and `SubModule.xml`.

**Break vs unparent**
- Break: right-click > Break prefab ("this will break the prefab" warning). The instance becomes plain entities stored in the xscene, so the scene no longer needs the prefab XML. The icon turns bold/shattered.
- Unparent: detaches an entity from its parent. It does not break anything.
- Break every custom prefab built purely from native assets before shipping to MP or a Native-compatible map. Non-native prefab XML is not baked into players' installs, so MP maps crash or entities are walk-through (FiefEvictionNotice, Sheepbitter, Emma, 2026; LizardWizard 2023 and Gingy 2026 agree). Breaking does not measurably hurt performance but grows the file.
- Give custom prefabs a naming convention (a suffix) so you can mass-break them.
- Break also when you need to Shift-drag children, and when seasonal child models all show in game at once (confirmed, e1.x 2021 and 1.x 2023-24).
- Nesting prefabs in prefabs: select the parent level, not each child, or they flatten and lose the middle parent (unverified).

**Gotchas**
- Do not reuse an existing prefab name: collisions override vanilla prefabs and deleting can crash (Hart, Reynauld, 2024). Community prefixes: `ss_`, `pe_`, `lv_` (Errayn, 2023, unverified).
- Editing a shared prefab corrupted one modder's scenes (Hart, 2024). Copy, don't edit in place.
- Scripts with XML data (`sp_conversation_dialog_spawn_*`) crashed the editor when added; TW hotfixed it in May 2026 (Dejan). Searching "conversation" in the prefab list crashed in 1.4.x.

Wiki: https://docs.bannerlordmodding.lt/editor/prefabs/ - new (break vs unparent, shipping, naming). /editor/scenes/ already covers prefab packing to shrink the xscene.

---

## 4. Meshes, custom-mesh props and physics

**Custom mesh as a prop**
1. Create a module. Put the FBX in a subfolder of `AssetSources` (the root itself is rejected since e1.x 2021) and "Scan new assets". Delete incomplete `.tpac.ptemp` files and re-import.
2. Assign a real material to the mesh in the resource browser, or you get RGL content warnings. Native-compatible maps may use only in-game meshes.
3. Make an Empty Entity, add the meta mesh (tick the metamesh filter to see it), or drag the mesh into the scene.
4. Duplicate the mesh in Blender, rename it `bo_<meshname>`, export with the visual. Concave shapes are allowed. Use a ramp, not steps, for stair collision.
5. The material name on a `bo_` mesh is its physics material (e.g. `metal`). Unmatched names fall back to default silently.

**Physics rules**
- Collision comes from the physics shape, not the physics material. Remove the shape (X beside Physics Body) to let people walk through. A mesh without a body won't carry NPCs.
- To hide only visuals tick Visible off; to disable only physics set the Disabled body flag. To view physics alone, tick Show Physics and untick "game entities" in the Visibility window.
- A missing `bo_` means players clip through walls on a server even if editor test worked (confirmed, 2025).
- Native prop without a shape: pick a similar one in the inspector Physics dropdown (e.g. `bo_barrier_sphere`).
- Fill bumpy physics with a flat extra body so AI doesn't stick. Keep `bo_` simple, not per roof tile.
- "Non-uniform scale/physics" warnings on scaled mission objects break focusable items.

**Materials and textures**
- Native materials can't be edited: right-click empty space in your module's asset folder > Create override > Material. An override changes every mesh using that material and must ship with the scene.
- Factor colour affects one LOD; use "Apply Color Factor to Components" or Components > metamesh > custom mesh parameters. Textures and materials must be set on every LOD slot (0-5) or they pop at distance.

**Gotchas**
- Vanilla textures inside custom materials render too dark with broken shadows in game since 1.3. Use fully custom textures or re-import natives via the Native Texture Exporter (TpacTool). Also causes black buildings on dedicated servers (confirmed, TW investigating, 2026).

Wiki: https://docs.bannerlordmodding.lt/editor/assets/ and /editor/resource_browser/ - adds detail; bo_ naming, override materials and LOD gotchas are new.

---

## 5. Invisible parts, editor helpers and tags

**Invisible parts**
1. Select the object (one with collision) and set all its materials to `editor_gizmo`.
2. Keep the physics shape: you now have an invisible wall, floor patch or collision fix.

- `default_empty` crashed scenes (confirmed by many, 2023-26; the 2026 reports say in game only). If a scene won't load, search its xscene for it.

**Editor helpers**
- `editor_cube/plane/cone/cylinder/pyramids` have the "visible in editor" flag: H hides them, they never show in game. Strays on a campaign map can be deleted. Use `editor_plane` to block sun inside buildings.
- "Editor only" in Visibility hides an entity in game.

**Tags**
- `sp_play` spawns the player at that entity in editor test (confirmed, 2023). `spawnpoint_player_test` / `spawnpoint_player` control Start Mission spawn.

**Banner tags and cloth**
- `bd_banner_b` anywhere in a scene disables cloth physics for the whole scene (confirmed, 2023, 2025). Removing it restores cloth but banners become a default Vlandian lion (disputed: unresolved, 2024). `banner` worked once (single report, 2023). `bd_banner` takes the owner flag.

**Gotchas**
- NPCs spawn only in scene types matching their tags (`npc_common`, `guard`, `dancer`, `musician`); changing tags on a vanilla prefab does not retarget it.

Wiki: https://docs.bannerlordmodding.lt/editor/entities/ - new. The editor_gizmo rule is not on the wiki.

---

## 6. Scripts and components

**Add a script:** select the entity > Scripts in the inspector > add. The list is case-sensitive sorted. Custom C# scripts inherit `ScriptComponentBehavior` (or `UsableObject`) in a DLL and appear in the dropdown; the Scripts-tab "create" button crashed in e1.x.

**Rules for movement scripts**
1. Put the script on an empty parent with the scaled object as a child. Scripts reset scale to 1 and remember the transform at add time (confirmed, 2023-24).
2. After moving an entity, remove the script, move, re-add (rotator, wave_floater, windmill; windmill_burning_missile reverts after save).
3. `path_navigator` and `wave_floater` can't share an object: put wave_floater on a child.

**Movers**
- `path_navigator`: moves an entity along a named path. In e1.x it always teleported back to the start; TW listed a stop as in progress for 1.6.0. Loop by ending on the start point; reverse by placing points backwards on a new path. Use a quadratic path or rotation looks wonky. Standing in the way teleports you. The camera can't ride it.
- `wave_floater`: bobbing for boats, rafts, elevators and rope-swung props. Walking players can be launched off boats; safest is no physics on the floater with an invisible static duplicate (unverified, 2026). Mesh pivot matters.
- `mesh_bender` (script name "mesh"): bends walls and stairs around the empty's axis. Parent the object to an empty, put the script on the empty, rotate the empty to change axis (a zero parent above it helps). Physics bends with it; verify it. Retexture first: changing scale or materials afterwards reverts the bend (remove, change, reapply). Bent meshes vanish at LOD2+, occluders don't bend (remove them), and tilted platforms flipped on save (confirmed, 2024-26). Several modders advise avoiding it in prefabs.
- MP: path_navigator, windmill and rotator motion is not synchronised and depends on join time; physics is server-side (confirmed, 2022-25). `SyncMissionObject` did not help.

**Gotchas**
- Destructible script strips snow, decals and weather from the entity (workaround: tpac export, join, reimport) and overrides seasonal snow. Destructible merlons need their scripts and `debris_holder` children; deleting parts crashes test. To make unbreakable, remove the script and child together (confirmed, 2026). Destructible on hideout doors broke after 1.3.15 (open bug).

Wiki: https://docs.bannerlordmodding.lt/editor/entities/ - new. /editor/paths/ mentions path_converger only.

---

## 7. Sound, particles and lights

**Ambient sound**
1. Empty entity > script `AmbientSoundEmitter`. Event names: `Native\ModuleData\sound_event_data.gen.xml` (the dropdown vanished in 1.3), `Prefabs\sound_prefabs.xml`, or the event path of a vanilla sound prefab.
2. Global ambience: untick "Is Triggered", make it the master. Local sounds stay triggered. A master suppressor (with Is Triggered) silences masters in caves. Keep the red sphere larger than the green. Tick Override Sound before editing the linked distances.
3. To follow the player along a coast or river, put `path_converger` on the entity with a zigzag path (not path_navigator).
4. Arenas: a localized emitter plus a scene-wide one, plus an `arena_sound`-tagged empty.

**Gotchas**
- Range settings mostly do nothing (2026); range is baked into the sound file and meshes don't occlude sound. Time of day changes which ambient plays.
- TriggerShape is greyed until another script is added (e1.x). Toggle the sound after changing parameters.

**Particles**
- Own particle systems: create them in your module (resource browser > right-click > create); edits to native instances save only already-initialised fields, and saved ones default into Native (confirmed, 2023).

**Lights**
- Torch lights go off at daylight TOD unless the script override is set. `LightCycle`: one reading says torches are lit only at night, another that it hides child entities by day (disputed: day-only vs night-only, 2025). Put it on an empty parent so children appear at night only (confirmed, 2026), or tick `alwaysBurn`. Vanilla fire/candle prefabs misuse it (flame stays, light goes). Interior scenes always count as night.

Wiki: https://docs.bannerlordmodding.lt/editor/weather_effects/ and /editor/paths/ - adds detail (path_converger sound). AmbientSoundEmitter settings are new.

---

## 8. Roads and rivers (road_instance)

1. Create a Path and name it (suffix `river_` or `road_`). Toggle helpers with H. Set interpolation to quadratic or cubic hermite. Do not "snap points to terrain".
2. Raise points above ground. Rivers must be flat, so fix each point's altitude.
3. Save. Place a `road_instance` prefab (click, don't drag) and rename it `river_<name>_road_instance`.
4. Script parameters: exact path name, road material (tick "is road" on the material) or a `wat_` water material (e.g. `water_main_map`), width, elevation offset, river mode.
5. Optional: "align terrain to road" shapes terrain along the path. Untick it when done.

**Gotchas**
- Flow direction can't be controlled. One `road_instance` per river. Removing the script removes the river.
- "Align terrain to road" can wreck terrain and Ctrl+Z may not undo it; save first.
- Deleting a path crashes the editor (edit scene.xscene to remove it). Drag-selecting path points crashes unless the path is selected first. Generated road decals persist after the path is deleted; find them under Roads in the scene window.
- `water_plane`/`water_body` prefabs give splash and sfx; plain planes with water material do not. No swimming, so keep water shallow. Scene Options has a global water level.

Wiki: https://docs.bannerlordmodding.lt/editor/paths/ and /editor/water/ - already covered for the workflow; deletion crashes and decal persistence add detail.

---

## 9. Decals

1. Make a PNG (1024 is fine; uncompressed, or the editor crashes) with an opaque diffuse where visible and an alpha mask (black = transparent).
2. Import it, make a material on the decal shader, set `render_on_terrain` / `render_on_objects` (the latter lets you preview).
3. Add `decal_mesh` to an empty entity (no prefab needed) and assign the material. The projection is a cube along Z.
4. Washed colours: alpha mode "Factor" with alpha test about 50.

**Gotchas**
- Custom decals need an entry in the decal atlas / `decal_textures.xml` to work on terrain (unverified, 2026). The atlas is exported with TpacTool.
- History: decals vanished in the real game (known engine bug, 2023-24), disappeared after MP warmup (Dejan confirmed; reported fixed on live Aug 2026), and respawn MP modes wipe decals including road paths (2025, unverified).

Wiki: https://docs.bannerlordmodding.lt/editor/decals/ - already covered for the workflow; MP/engine-bug history is new.

---

## 10. Spawners, passages, chairs, area markers and interior scenes

**Village (e1.x 2021, still applicable)**
1. At least 6 notable parent sets (`sp_notable_parent...`, tag `rural_notable_parent`); extra notables need `sp_notable_rural_notable`.
2. Spawns must be on navmesh ID 2. Villagers clustered at an edge means a wrong ID or a wrongly renamed scene.
3. Verify with the debug view's Count Entities (below).
4. Population comes from the campaign (hearth), not the scene (Dejan, TW).

**Town / castle**
1. Area markers (workshop, common area): spawners must sit inside the marker sphere, spheres must not overlap, and NPCs don't count without navmesh beneath them. Merchants and guards spawn more than townsfolk, so scenes use them to raise population (confirmed, 2026).
2. Passages (door to interior): the passage script on the main entity and `passage_use_point` on the arrow in front, inside `_barrier_passage_castle`. The target scene per upgrade level comes from `scene_name_1/2/3` in `settlements.xml`. Passage prefabs appear hardwired per template (town/castle) and crash in villages.
3. Castles do not support civilians without code; animals are fine. Lord's hall "Talk" uses a generic prepared scene; custom cultures fall back to a Vlandian keep background (2026).

**Tavern / arena / hideout / battle**
- `sp_player` spawns the player only in fixed-spawn scenes (tavern, keep); use `sp_play` for editor test.
- Skirmish/TDM: TDM uses `mp_spawnpoint`; `spawn_visual` snaps to terrain and must not be underground; two `spawn_visual`s or missing attacker/defender tags crash skirmish (confirmed, 2025).

- Arena scenes need 16 arena areas and `spawnpoint_area` entities, and the tournament arena needs an `arena_sound` empty, or the game crashes on entry (confirmed, 2023).
- `sp_battle_set` needs attacker/defender tags; several sets pick one at random (not in villages). Hideouts are night scenes (test at about TOD 4).

**Chairs and usable objects**
- SP chairs need a sitting animation and navmesh. MP sitting with a chair parented to an empty (disputed: Sheepbitter yes, MrRonan no, 2023). Later reports: chairs and "press F" don't work on native MP maps without custom scripts (confirmed, 2024-25).

**Animated statues and spectators**
Empty entity > human skeleton > body/head/hands/legs metameshes > `skinned_mesh_viewer` with an animation name (e.g. `anim_lord_idle_1`). Set meshes before the script, scale the metameshes not the root, add `disable_cloth_script` if clothing resets. Works in MP; 30-91 animated NPCs were fine (confirmed, 2024-25). `corpse_attacker` is expensive; keep to 3-4.

**Gotchas**
- Passages crash in villages; arenas crash without their areas and sound tag; NPCs do not spawn without navmesh or inside overlapping markers.

**Debug entity:** search "debug" in the entity browser, place it, enable SpawnPointDebugView / ActiveDebugUIEditor, pick the scene type, tick the Entity Check tab (NPC / Passages / Others / Animals) and press Count Entities (TW-confirmed; also on the wiki).

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ - debug entity already covered; village counts, passages, area markers and arena crash rules are new.

---

## 11. Level and season visibility

1. Select the prefab/entity > inspector > Visibilities. Tick the levels where it should appear: Level 1-3 and the Civilian, Siege and Sally flags (the wiki's siege page gives the per-flag table).
2. Wall pieces, gates and siege deployment points need correct level flags, or the scene can crash.
3. Season variants: duplicate with a whiter factor colour and set season visibility, or tick the season option on the object. Flora seasons are built into the flora type.

**Gotchas**
- An "all"-levels prefab disappearing on level 3 was reported (unverified, 2022).
- `mesh_seasonal_material` / `season_material_changer` on `outer_mesh_a` resets its material on save/load. Edit the material names in the script parameters, or remove the script to keep custom textures.

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - level table already covered; the per-prefab inspector note is new.

---

## 12. Barriers, occluders and scene size

- Occluders (`_occ_quad`, `bo_occ`) cull objects fully behind them (TW dev). Make them slightly smaller than the wall. Terrain is not an occluder.

**Gotchas**
- Scene size: interior and siege xscene files over about 9-11 MB can CTD with no message (wiki). Pack repeated groups into prefabs and save the scene again.

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ - size ceiling covered; barrier and occluder rules are new.

---

## Campaign map

The campaign map has its own guide; see it for settlements and map icons. One pointer only: a map icon capsule needs `campaign_icon_capsule_1` with `town_entity_manager` (unverified, 2025).

Wiki: https://docs.bannerlordmodding.lt/editor/world_map_general/ - pointer only.

---

## Reference table: scripts, prefabs and tags

| Name | Kind | What it does |
|---|---|---|
| `editor_gizmo` | material | Invisible visuals, keeps physics; use instead of `default_empty` |
| `default_empty` | material | Crashes scenes; avoid |
| `mesh_bender` ("mesh") | script | Bends meshes around the parent's axis; fragile |
| `path_navigator` | script | Moves entity along a named path; no MP sync |
| `path_converger` | script | Moves a sound emitter with the camera along a path |
| `wave_floater` | script | Bobbing boats and ropes; put on a child |
| `road_instance` | script/prefab | Generates road, river or mesh along a path |
| `AmbientSoundEmitter` | script | Ambient sound; Is Triggered / master / suppressor |
| `LightCycle` | script | Night-only light or children; `alwaysBurn` |
| `skinned_mesh_viewer` | script | Posed or animated NPC/statue |
| `passage_use_point` | script | Scene transition point |
| `SpawnPointDebugView` | script | Scene requirement checker and counter |
| `barrier` / `barrier builder` | prefab | Blocks agents; red blocks, green passes |
| `sp_battle_set` | prefab | Battle spawn set; attacker/defender tags |
| `sp_notable_parent...` | prefab | Village notables (min 6); tag `rural_notable_parent` |
| `sp_play` | tag | Player spawn in editor test |
| `spawnpoint_area` | tag | Arena areas (16 required) |
| `arena_sound` | tag | Tournament arena sound entity (crash if missing) |
| `bd_banner` / `bd_banner_b` | tag | Banner owner flag / disables scene cloth |
| `_occ_quad`, `bo_occ` | prefab/mesh | Occluders |
| `bo_<mesh>` | mesh prefix | Collision shape for a mesh |

---

Contributors: FoozleMcDoozle, Klassix, poheniks, npc99, Sheepbitter, Snorri, Hart, Secretblaze123, FiefEvictionNotice, hunharibo, NIN3, Space_Pot8to, Suspicious Man, Capt Cole, LizardWizard, Exhorris, Boggo, Byako, Lord Eastwood, Le Profyteur, Thil, Dejan (TW), gkx (TW), Muret (TW), Maegfaer, Reynauld, Litauen, Kaseke, Swan, Emma, Gingy, Errayn, and others credited inline.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/12-entities-prefabs-and-scripts-sources.md](sources/12-entities-prefabs-and-scripts-sources.md)** (194 of 195 linked). The links only work for members of the Mount & Blade Modding server.
