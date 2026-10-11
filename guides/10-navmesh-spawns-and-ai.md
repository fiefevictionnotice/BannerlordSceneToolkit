# 10 - Navmesh, Spawns and AI (Mission Scenes)

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - **Generate Grids beats autogen.** Autogen is slow, crash-prone and leaves giant or terrain-buried faces. Grid-generate a terrain-only base (5 m or less, borders set first), then hand-fix around props.
> - **Face IDs are gameplay data, not decoration.** Spawns, villagers, animals, siege inside/outside, gates, ladders and breaches all key off the ID. A wrong ID usually means idle or missing units, not a visible error.
> - **The "1.5 m rule" is a vertical gap limit, not a minimum face size.** Practical advice is to stay within about 0.5 m of the surface in mission scenes. Campaign maps follow different rules (see guide 15).
> - **navmesh.bin is portable between scenes and versions.** This is the standard rescue for broken grid generation, an outermesh that hangs generation, or a corrupted save.
> - **The AI treats navmesh as a loose hint.** Barriers, small faces near obstacles and face costs shape behavior. The mesh alone will not stop AI from falling off ledges or sticking on props.

Version labels: e1.x = early access (2020-22), 1.x = after the Oct 2022 renumbering. Claims are dated by when they were reported. "Wiki:" lines compare against docs.bannerlordmodding.lt.

---

## 1. Build the base navmesh

Every scene with AI needs navmesh: town and village scenes, hideouts, sieges and custom battles. MP maps need it only for bots and for horses to flee, but bake probes also depend on it (see 9). With no navmesh, AI stands still.

Steps:
1. Build in this order: terrain, flora, entities, navmesh, outermesh. (FoozleMcDoozle, e1.x)
2. Set soft borders first (see 5). Grids and autogen then stay inside the playable area. (matthewster, 1.x 2026; Bullero, e1.x)
3. Place an `sp_player` (or similar spawn) entity. In 1.x, Generate Grids only fills areas reachable from it. (vLove, Klassix, 1.x 2022)
4. Open the navmesh tool (the compass-arrow icon on the top bar) and run **Generate Grids**. Use a grid size of about 5 m or less. Since e1.6.1 it needs "Auto Nav Debug Tools" and only fills clear ground. Grid size 3-5 m with "Use Boundary" ticked works for terrain-only battle maps. (FoozleMcDoozle, Muret/TW)
5. Delete grid faces under walls, props and buildings. Grids ignore entities and trees, so AI will think it can walk through them.
6. Add and fix faces by hand. Press Space to enter vertex-add mode, place 4 vertices, then hold Shift to fill. Ctrl+click an edge loop and extrude. Hover with Alt to delete faces. "Save navmesh only" under File is much faster than a full save. (toy boat, npc99, e1.x)
7. Run "Check for problems" and delete error faces before baking. Double-click a console error to jump to it (face selection mode). (hunharibo, 1.x 2023)

Gotchas:
- Autogen was reported to take 1-8+ hours, need lots of RAM and produce crude results with giant faces under the terrain. It broke for many users on e1.7.x and again after 1.1.0. (Cuce, Muret, others)
- (disputed: FoozleMcDoozle says avoid autogen entirely because it can put vertices on trees. Bullero and several 1.x users report that Bullero's custom autogen settings, followed by removing unreachable faces and unused vertices, give a near-usable result. Both camps agree it needs cleanup.)
- You cannot autogen only one region. Autogen the whole map, then redo the poor areas. (Snorri, LizardWizard, 1.x 2025-26)

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ - adds detail (the wiki page mainly links the official guides and lists controls).

## 2. Heights, slopes, face quality

Rules of thumb:
- The official rule: no face may be farther than 1.5 m (vertically) from the physics surface the agent walks on. It is not a minimum face size; faces can be much smaller in gatehouses. (Dejan, Fatih/TW, e1.x 2020)
- In practice stay within about 0.5 m (roughly adam_0 feet to waist) in mission scenes. Slightly clipped-into-ground faces are fine. (FoozleMcDoozle, npc99, 2022)
- No faces under solid objects. Spawn point pivots must be inside faces. Where jumping is required, leave no mesh. (TW doc via poheniks)
- `maximum_passable_slope` is 40 degrees; faces beyond it can be highlighted from the inspector selection section. (NIN3, e1.x)
- Prefer square-ish, evenly sized faces. Thin, long or steep faces make AI zig-zag, because AI paths through face centres and corners. Tiny triangles snag AI near obstacles. Large faces are fine; split big faces over valleys by dropping a new edge into the valley. (Space_Pot8to, Suspicious Man, Dejan, 1.x)
- To spot trouble: "Mark Elevation Problem Faces" logs offenders, and "select inverted faces" (Debugging Tools) finds flipped faces. "Tight faces" found by Check for problems rarely crash; ladders always have them. (Muret/TW; Sheepbitter, 1.x)

Gotchas:
- Navmesh cannot change per season. Add AI barriers around props that exist only in some seasons. Faces can be limited to a level, not a season. (toy boat, Snorri)

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ - adds detail.

## 3. Outermesh pitfall and cross-scene copy

An outermesh makes grid generation cover about 16 times the map, which hangs the editor for hours. (FoozleMcDoozle, e1.x 2020-21)

Workaround, also the standard fix for broken grid generation:
1. Make a blank scene with the same dimensions and import the heightmap. Delete any outermesh.
2. Generate Grids, save navmesh only.
3. Copy `navmesh.bin` into the real scene's `SceneObj\<scene>` folder.
4. Re-add the outermesh last.

Facts about navmesh.bin:
- It is a separate file, portable between scenes and versions, forward and backward compatible. (FoozleMcDoozle, npc99, Worlok, e1.6-e1.8)
- When grid generation was broken (e1.7.x), people generated in a scene or version where it worked (e1.6.0), or pasted a prebuilt flat 5 m grid `navmesh.bin` and used Select All then Adjust Height. (npc99, 2022)
- Moving a settlement and recomputing rewrote navmesh.bin into a crashing state in e1.8.0; re-copying an older file fixed it. Keep backups outside the Modules folder; backups inside it got corrupted. (BlackRose, npc99, 2022, unverified)

Gotchas:
- Do not copy a native interior's navmesh with face ID 0 (see 4).

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ - new.

## 4. Face IDs

Set the ID in face mode: press H to show helpers, then 3 for face mode (1/2/3 = vertex/edge/face), select faces, set the ID in the Inspector. If you cannot select anything, helpers are hidden. (Capt Cole, Omnissiah_Priest; wiki already has the H tip)

Reference for mission scenes (IDs are conventions drawn from native scenes and chat; always compare with a native scene such as khuzait_castle_002):

| ID | Meaning | Source/version |
|---|---|---|
| 0 | Outside the walls in a siege (attacker side). Also the "default" and, in town/village scenes, an error: spawn/NPC faces left at 0 broke spawns. The debug menu can select all ID 0. | Hart, Snorri, Klassix; 1.x |
| 1 | Inside the castle/settlement and wall top in a siege. In villages, the generic "other" ground. Defender archer positions also need ID 1 under `defender_archer_position`. | wiki, Snorri, Le Profyteur, 1.x |
| 2 | Main walkable face for spawns: spawn points, battle sets, notables and villager paths, roads. Civilian scenes use 2 for NPC paths. Faces must also be inside soft borders. | Kazet, Bullero, Klassix, Hart; e1.x-1.x |
| 3 | Animals/livestock. Acts like a wall for livestock. The documented DisableFaceWithIDForAnimals value is 3. | Kazet, Bullero, e1.x |
| 4 | Forest (campaign map, see guide 15) | |
| 10 | Impassable edges in one mission-scene report (single report: Space_Pot8to) | e1.x, unverified |
| 501 | Door (siege), one report; value varies by scene (CaptainFRACAS, e1.x, unverified) | |
| 522/523 or 722/723 | Siege ladders; varies per scene. Check the native scene you copy. | e1.x, unverified |
| 351 / 361 style | Breach pair: "wall solid" vs "wall broken". Entered in the wall script (`onSolidWallGenericNavmeshID`). | Omnissiah_Priest, 1.x, unverified |
| unique per piece | Each ladder, gate and wall segment needs its own unique ID, entered in the matching script field. | Ladnar, 1.x |

Gotchas:
- Village scenes: spawn points and roads use ID 2, animals ID 3, others ID 1. Without the documented IDs, villages show only notables. (Failware, Bullero, 2022)
- Siege attackers on ID 0 and defenders on ID 1 (Failware, e1.x). If siege units do not use any equipment and only move when gates open, suspect IDs. (Snorri, Revand, 1.x 2024)
- Directional (one-way) faces such as ladders can only be set on faces with at most two connections. (Space_Pot8to, Klassix, 1.x 2022)

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ and https://docs.bannerlordmodding.lt/editor/siege_scenes/ - the siege page already covers inside=1 / outside=0; everything else here is new.

## 5. Spawns and spawn paths

Steps:
1. Place `sp_player` for the player start; several of them give a random one. Spawn points and `sp_battle_set` must sit on top of navmesh (ID 2) and inside soft borders. Spawning out of bounds can crash. (FoozleMcDoozle, Exhorris, FiefEvictionNotice)
2. For battle scenes, build deployment sets with `sp_battle_set` points or spawn paths named `spawn_path_01`, `spawn_path_02` (TW changed from `spawn_path_1`; early scenes used `spawn_path_001`). A battle only picks one set; paths scale spawn spacing with troop count. (joseph ballin, 1.x 2026; FoozleMcDoozle, e1.x)
3. For notables, add `sp_notable_parent`; village-scene notables need the `rural` tag. (Le Profyteur, Servius, e1.x-1.x, unverified)
4. For town NPCs, spawn is randomized. `sp_npc_wait` seemed to have the highest chance and arrow-type spawners fill more, but more spawners add spots, not population. (Swan, Hart, 1.x 2024, unverified)
5. Common area markers (hideouts, alleys): set the `area index`; spawn points inside the marker radius join that area's population. The last common area has no prioritization and may lack bandits. The radius gizmo is broken; use the ruler tool. (Levente, Klassix, Capt Cole, 1.x)
6. Turn on **SpawnPointDebugView** to see what a scene type still needs (see 9).

Gotchas:
- `sp_battle_set` assumes its centre is normal terrain and offsets from corners, so units can land in odd places (Servius, e1.x).
- "Boundary projected formation position is not available" means too little room for the units. Spread sets or reduce troops. (Exhorris, FiefEvictionNotice, 1.x 2025)

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ - covers SpawnPointDebugView; spawn_path_nn naming, ID 2 and notable tags are new.

## 6. Soft borders, hard borders and barriers

Steps:
1. Show border visibility and place soft borders (red). Outdoor maps require them, they must be concave, and more than 4 are allowed. Autogen respects a border polygon. (FoozleMcDoozle, NIN3, e1.x-1.x)
2. Keep navmesh inside soft borders; faces outside the map or troops spawning outside happen otherwise. (Bullero, Worlok, Hart, 2022 and 2026)
3. Fence hazards, cliffs and unreachable areas with a **barrier path**: create a path, enable barrier build, then use Flip on the entity script. Barriers are one-sided (green passable, red blocked), so check the facing. Raise or lower as needed. (FoozleMcDoozle, Klassix)
4. Choose the type: AI barrier or "AI limiter" (AI only, players can pass), agent/barrier (players and AI), missile barrier (projectiles only). "Everything" also blocks arrows. Select the group marker, not an individual point, to see the option. (toy boat, Hart, Embo, 2021-26)

Gotchas:
- Hard borders do nothing; use path barriers instead. (Macbeth of Gondor, NIN3, e1.x 2021)
- Soft borders give players about 10 seconds in the red zone, so use hard barriers around hazards. (FoozleMcDoozle, 1.x 2022)
- Navmesh and barriers are unaware of each other. Do not run navmesh through a barrier; it remains pathable and AI will stall. (FoozleMcDoozle, 1.x 2022)
- Leave a navmesh gap at fences (AI treats over/under as passable and sticks). Trees need no cutouts; chairs and rocks lack AI capsules, so mesh around them. (e1.x, partly unverified)
- Put barriers on both sides of walkable logs, under non-solid stairs, and between props and walls. AI is also known to step about 0.5 m off the mesh, so add border mesh or barriers around anything it must avoid. (Snorri, Thil, 1.x 2025-26)

Wiki: https://docs.bannerlordmodding.lt/editor/paths/ - new (the paths page covers road and river paths, not barriers).

## 7. Siege levels, breaches, ladders and dynamic navmesh

Steps:
1. Each siege level and mode (civilian/siege) needs its own navmesh set; the editor error finder flags a missing set. Minimum: one for siege levels and one for civilian. (Hart, Kysaro, 1.x 2026)
2. Copy faces: select all faces in face mode, Ctrl+C, paste, realign exactly, then change the visibility mask while still selected, and only afterward deselect. Vanilla castles reuse the surrounding mesh per level and mode, giving up to 6 layers for 3 levels. (Omnissiah_Priest, Snorri, Hart)
3. Give inside faces ID 1 and outside ID 0, and enter specific IDs in the CastleGate / WallSegment scripts. Missing gate IDs make troops stop after the gate breaks. (Hart, Feli, 1.x 2025)
4. For breaches without rubble, use two navmesh patches, one each side of the wall, with a unique ID. Set the breach IDs only on the wall parent; children stay at default. (Hart, Exhorris, 1.x 2024)
5. Build ladder navmesh by extrusion and connect it. Use "civilian" ladders for non-siege AI. (Snorri, 1.x)
6. Dynamic navmesh (siege towers, breaches) exists as pre-made prefabs toggled at runtime (see the SiegeTower class). Entities placed in code get no cut. Use "Export faces as prefab" from navmesh options and import in another scene; use an empty parent with scale 1 and do not tick save position/rotation/scale or the import is distorted. (hunharibo, Space_Pot8to, npc99, 1.x)

Gotchas:
- An edge can connect to only one face and overlapping edges do not connect. Custom dynamic prefabs need connector faces; a floating static face at the meeting point also helps. (Omnissiah_Priest, Klassix, 1.x 2023)
- Navmesh bugs near ladders: mesh too close to the ladder re-takes the ladder path; mesh slightly higher than the ladder works better. Field-battle siege ladders crash. For heights over 6 m chain duplicate ladders. (siege wiki, Snorri, Reynauld)
- Siege navmesh quality matters more than anything: autogenerate open ground squares, but hand-build all areas inside and near walls and around anything above roughly 0.4 m. (Snorri, 1.x 2024)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ and https://docs.bannerlordmodding.lt/editor/navmesh/ - already covered: IDs 1/0, six layers and copy procedure; new: breach pair, prefab export, connector faces.

## 8. Navmesh deactivator and per-face control

Steps:
1. Place the `Navigation_Mesh_Deactivator` prefab. Its `DisableFaceWithID` script variable switches off one face ID at runtime. Civilian modes use it so NPCs follow roads (roads ID 1 or 2 depending on setup). The animals variant is `DisableFaceWithIDForAnimals`. (NIN3, Kazet, e1.x)
2. For a dynamic mask, attach a navmesh prefab to a moving entity (the negative-mask approach used by siege towers). Plan a 2x2 face grid with unique IDs for trap areas. (Hart, hunharibo, 1.x 2025, single report)
3. In code, `Agent.SetAgentExcludeStateForFaceGroupId` excludes faces for one agent. It does not work with dynamic mesh face IDs (offset about +100000). (poheniks, 1.x 2023, unverified)

Gotchas:
- NPC spawners walking into walls: usually navmesh on the wrong level, wrong ID, or a deactivator conflicting with siege entities that have navmesh ties (several levels, one set). Isolate navmesh-linked entities. (Hart, 1.x 2026, unverified)

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ - new.

## 9. AI pathing problems: checklist

How the AI works: long-range movement uses the navmesh and takes the route with the fewest faces, not the shortest distance. Close combat uses a separate local pathfinder within about a metre. Per-face cost steers routes. (FoozleMcDoozle, poheniks, e1.x)

Steps:
1. AI idle or running straight at you: the agent stands off the navmesh, the target is on a disconnected mesh, or the scene has no mesh. Fix connectivity. (FoozleMcDoozle, toy boat)
2. Stuck at props/stairs: add barriers under non-solid stairs and between props and walls. Keep faces small near edges so civilians do not cross fences and benches. A bench needs mesh only under its marked contact points. (Snorri, toy boat, 1.x)
3. Zig-zag: use squarer faces with extra mesh on both sides; lower the cost of centre faces. (Space_Pot8to, Sheepbitter, 1.x 2023)
4. Make AI avoid water or rivers: remove mesh in water or raise its cost; give a bridge cost 0. Costs seem to act as multipliers (examples 10 or 30) and should also be applied, smaller, to neighbouring faces. (FoozleMcDoozle, NIN3, Snorri, Thil; the multiplier claim is unverified)
5. Bridges need their own mesh; a gap anywhere makes AI stuck. (Lemmy1916, Haterade, 1.x 2026)
6. Field-battle AI ignores the mesh more often than siege AI. Dense or stretched triangles confuse it. (Snorri, LizardWizard, 1.x 2025)
7. Use "Show agent capsule debug" and the "Test path between faces" button. The capsule view crashed at huge vertex counts. (Maegfaer, Klassix, e1.x-1.x)
9. For terrain/baking: navmesh is not baked and needs no bake, only a save. It is used to place light probes: probes only activate with line of sight to a navmesh face, so no navmesh means every probe is inactive. Build navmesh before baking. (Alphα, LizardWizard, Kaseke, Byako, 1.x 2025-26)

Gotchas:
- Place SpawnPointDebugView: add an entity named spawnpointdebug, open its scripts, enable ActiveDebugUIEditor, then in the debug window choose your scene type and use Scene Entity Check and Count Entities to list missing spawners. (Zedforen via wiki)

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ and https://docs.bannerlordmodding.lt/editor/siege_scenes/ - the debug view and "AI treats navmesh as a tip" are already covered; the rest adds detail.

## 10. Campaign map pointer

Campaign-map navmesh has different rules (2D only, no overlap in Z, no holes that crash on load, IDs for forest, river and impassable terrain). See "15-campaign-world-map". Do not apply the 1.5 m gap or ID 2 conventions from this guide to the world map.

Wiki: https://docs.bannerlordmodding.lt/editor/world_map_problems/ - handled in guide 15.

---

Contributors: NIN3 (TW), Muret (TW), Dejan (TW), Fatih (TW), FoozleMcDoozle, npc99, poheniks, Snorri, Hart, Kazet, Bullero, Klassix, toy boat, Space_Pot8to, Omnissiah_Priest, hunharibo, Le Profyteur, Exhorris, Lemmy1916, Suspicious Man, LizardWizard, Sheepbitter, Capt Cole, Levente, Failware, Ladnar, Reynauld, Eagle, Swan, Kysaro, Alphα, Kaseke, Byako, Thil, Zedforen, and others.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/10-navmesh-spawns-and-ai-sources.md](sources/10-navmesh-spawns-and-ai-sources.md)** (115 of 115 linked). The links only work for members of the Mount & Blade Modding server.
