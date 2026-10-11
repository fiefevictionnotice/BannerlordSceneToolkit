# 15. Custom Campaign World Maps

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - Never work under the name `Main_map`. Save as `modded_main_map` and load it with Aurelian MapFix. `Main_map` is special-cased: the editor strips its paint layers and crashes on it.
> - Calradia's tileset is compiled into the engine. Set the vista "Tileset Name" to `none` (literal text) before editing or saving, then supply your own vista and normal map.
> - The settlement scripts write `settlements.xml` and `settlements_distance_cache.bin` to a location that changed between versions (Sandbox, later your module). A mismatched pair is the top cause of crashes at first movement. Recompute after ANY navmesh or settlement change.
> - Campaign navmesh is flat 2D: one connected body, no islands, no Z overlap, no ID 0, and gates, spawns and settlements all on passable faces. A hole or an island crashes at load or first move.
> - A settlement is a packaged object, not a mesh: `campaign_icon_capsule_N` parent, Town Scene Manager, gate, click physics body, banner positions and breach walls on all 3 levels, siege boxes. Clone a vanilla one.

Version labels: **e1.x** = early access (2020 to Oct 2022, for example e1.6.5, e1.8.0). **1.x** = after the Oct 2022 renumbering (1.0.0 onward). The label comes from the date of the finding, not from what the poster typed.

Scope: map-side work in the editor. XML gameplay (cultures, notables) appears only where it breaks the map.

---

## 1. Choose the map name and load method

**Steps**
1. Open vanilla `Main_map` from Sandbox in the editor and use **Save As** into your own module. Do not copy or rename the folder by hand: the folder name must equal the scene name stored in `scene.xscene`.
2. Name the new scene `modded_main_map`.
3. Install Aurelian MapFix (Harmony DLL by Aurelia). Put `Aurelian.MapFix.dll` and `0Harmony.dll` in both `bin/Win64_Shipping_Client` and `bin/Win64_Shipping_wEditor`, unblock the DLLs, load Harmony first, and add a SubModule entry. A different scene name can be set in `MapFix.txt`. MapFix is not needed to view the map in the editor.
4. Optional: `managed_modded_parameters.xml` adjusts camera settings.
5. From e1.8.0 the module `SubModule.xml` uses `ModuleCategory value="Singleplayer"` instead of the Singleplayer/MultiplayerModule tags (MapFix and Harmony modules need it too).

**Gotchas**
- Saving as `Main_map` makes the editor delete every paint-layer material map except the base layer, and touching anything often crashes the editor (e1.8.0, Feb to Apr 2022; same advice in 2023). You can still save to `Main_map` just to preview.
- Hand-renaming a map folder gave flat terrain and mismatched shader caches (e1.6.x). The error text is "scene folder is not the same as the scene name". Renaming a folder does not rename the scene: open it and Save As.
- Why MapFix exists: the physics heightmap `main_map_physics_heightmap` is hardcoded to Calradia's low-resolution one; overriding it crashed from e1.6.1. MapFix loads your scene name and re-enables physics and terrain-mesh-blending flags. A custom physics heightmap (1K to 2K) is optional; without it parties may float or sink.
- 2026 reports (single each, unverified): renaming a working folder to `Main_map` broke textures and coordinates, so copy instead; a module-level `Main_map` crashed until the terrain file was copied into the module.
- For 1.3+ ports see section 12.

Contributors: Aurelia, npc99., FoozleMcDoozle, Carolina Warlord, Anate, PibbJont.
Wiki: https://docs.bannerlordmodding.lt/editor/world_map_general/ - **new** (that page covers only War Sails; MapFix and naming rules are undocumented).

---

## 2. Tileset, vista and terrain painting

**Steps**
1. In the terrain Vista settings, set **Tileset Name** (`vista_tileset`) from `Worldmap` to `none`. Type the literal text.
2. If you start from Calradia's terrain: from 1.0.0 on, deleting terrain crashes the editor, so either delete it before anything else or build a fresh scene and paste entities from Main_map's `scene.xscene` (text-editor copy works for the whole entity section; groups carry IDs outside it, so copy parents and children instead of groups).
3. Generate terrain (max 16 x 16 nodes). Import a heightmap: Ctrl+A to select all nodes, import, set scale by trial and error, leave offset alone.
4. Supply vista diffuse and vista normal. Colour comes from the vista; paint layers act as colourless detail overlays (transparent monochrome "mainmap" textures).
5. Alternative: disable the vista and paint colour with layers. With a vista, layer colours are replaced by the vista background colour.

**Gotchas**
- Calradia is a compiled tileset (Houdini output streamed with Granite) and shows white in the editor. Saving with the Worldmap tileset on crashes the editor. The "vistas_record_.tileset != nullptr" assert when opening Main_map is harmless.
- Vista renders pure black in game when it conflicts with the tileset: set tileset none and blend mode multiply (late 2022). On 1.5.3, multiply reportedly fails (black plus crash); use multiply with a changed texture on a layer (single report, 2026; unverified).
- Vista size 16384 x 16384 max; 32K cannot be compiled; non-power-of-two (16384 x 16385) fails, so check the console. A vista yields only 1K per node, so paint layers give the close-up detail. Paint layers drop out at a LOD distance. Regular mission-scene diffuse gives black or tiled output; use main_map-variant textures.
- Keep heightmap and vista normal in sync; re-bake after terrain edits. Remove blue from large normal maps yourself.
- Layer limits (1.0.x): 16 layers at 256 x 256 node resolution, 3 to 4 at 512 x 512 (terrain compression utility is capped at 4 GB). Add 12 to 14 layers at the start; adding layers late to a heavy file causes red terrain (fix: delete shaders). A base paint layer is mandatory or terrain goes black.
- Flora layers on the campaign map are invisible until you enter and leave a mission scene once (known e1.7 to e1.8 bug; one report says fixed in 1.2.5). Trees and bushes follow flora flags: only floras flagged "fading_out" disappear zoomed out; duplicate a flora to change flags without affecting other scenes.
- Map limit is 16 nodes per side (engine). 16 x 16 nodes of 530 m is about 10x Calradia. Party icons are about a quarter scale, so maps feel smaller than they look.
- Saving a huge map can exceed 16 GB RAM; wait until usage is under ~80% before saving (1.1 to 1.2).
- Custom outer mesh: export the heightmap, build a larger-node scene around it, sculpt, displace a dense plane in Blender, cut out the centre. No LODs needed.
- Low-resolution look in game: check terrain LOD multiplier, colour-map resolution (native 512) and the "Don't Degrade" flag.
- Atmosphere: `Native\Atmospheres\Interpolated\campaign_map.xml` cannot be overridden by a module (e1.8.0). For a day/night cycle, add an empty entity with the MapColorGradeManager script (late e1.8.0, 1.0.x).
- Editor lighting is darker than in game on the campaign map (single report).

Contributors: npc99., FoozleMcDoozle, Snorri, Fatrod, Lemmy1916, Hart, Carolina Warlord, Lukar (TLD).
Wiki: https://docs.bannerlordmodding.lt/editor/heightmap/ (adds detail), https://docs.bannerlordmodding.lt/editor/world_map_problems/ (already covered: red terrain). Tileset, vista and layer limits are **new**.

---

## 3. Custom campaign textures

**Steps**
1. Place replacement textures in `AssetSources\world_map` inside your module (another folder crashes the game).
2. Recognised names: `main_map_physics_heightmap`, `worldmap_wave_map`, `worldmap_colorgrade_grid`, `main_map_snow_flowmap`, `worldmap_river_mask`, `worldmap_battle_scene_grid`.
3. On import tick Do not compress, Do not generate mipmaps and Don't Degrade. The river mask worked only as DDS.

**Gotchas**
- Replaceable (Apr 2022): wave map, colorgrade grid. Not replaceable then: physics heightmap (MapFix handles it) and snow flowmap.
- Wave map is an SDF-style gradient with offset; keep it outside the strict water mask.
- Snow: `main_map_snow_flowmap` red = snow amount fed from C#, blue masks underwater. Overrides crashed (e1.6 to e1.7). (disputed: e1.8.0 beta worked for Carolina Warlord; others still crashed, and an all-blue map removed snow but crashed.) Native size is 1024 x 1024; a 4097 import crashed. In 1.2, crashes at map borders came from the green channel and Cephas patched it with a Harmony finalizer. The flowmap, not the navmesh, drives seasons.
- Winter battles: snow flowmap plus battle grid (disputed: red alone failed for one user, a white/purple mix worked for another, 2025).
- Snow depends on the ocean mask, so non-Calradia shapes may get no snow (e1.7, single report).

Contributors: FoozleMcDoozle, npc99., BlackRose, Carolina Warlord, Le Profyteur, hunharibo.
Wiki: not covered - **new**.

---

## 4. Camera bounds: border_min / border_max

**Steps**
1. Create two empty entities named exactly `border_min` (SW corner, z = 0) and `border_max` (NE corner, z = height cap; Calradia uses 620).
2. Coordinates are positive; map origin is the SW corner (0,0).
3. If you cannot click the map or snow is missing, check these two first.

**Gotchas**
- They only bound the camera. Where parties can walk comes from the navmesh.
- `border_max` Z sets maximum zoom-out. March 2022 reports (e1.8.0) used `border_minimum` / `border_maximum`; every report from late 2022 on uses `border_min` / `border_max`. Search "max" in the scene menu to see which exists in your copy.
- Before e1.6.1 the bounds lived in `managed_core_parameters.xml` (CampaignMapCenterPointX plus allowed-distance X/Y); a custom copy was loaded via `ManagedParameters.Instance.Initialize` (e1.x 2020). Removed from e1.6.1.
- A physics body can stop the camera clipping. On a large map the edge of TW's water table shows above ~2,075 m.
- Minimum entity set for a custom map (1.1 to 1.2): `spawn_visual`, border probes, settlement entities for every vanilla settlement (or a full custom XML), navmesh, siege entity script, cloud plane.

Contributors: npc99., FoozleMcDoozle, hunharibo, Maegfaer, Xylord, Reynauld.
Wiki: not covered - **new**.

---

## 5. Campaign navmesh

**Steps**
1. Delete the outer mesh first, otherwise the grid covers up to 16x the area and takes hours.
2. Use Generate Grids rather than autogen as the base: grid size about 5 to 8 m (1.6.1+ "Auto Nav Debug Tools" fills only clear areas). Too small a size eats RAM. Grids ignore obstacles, so fix by hand. (disputed: autogen crashed for several after 1.1.0 and FoozleMcDoozle avoids it; Bullero's custom settings work for others.)
3. Build ONE connected navmesh over the whole playable area. Plug every hole (rivers, sea, mountains) with an impassable face.
4. Assign IDs in face mode (key 3). Typical IDs: 1 normal passable, 4 forest (slowdown), 6 shallow/ford, 7 mountain/cliff, 10 impassable edge/fill, 11 river, 14 urban under settlements so parties cannot path through. Do not leave ID 0 (0 is for mission scenes; the debug menu can select all ID 0).
5. Verify that gates, villages, hideouts, siege attacker boxes, hardcoded player spawn (bottom-left) and clan spawns (`spclans.xml` coordinates, keep on land) lie on passable faces.
6. Run "check for problems" (inverted normals); delete tiny, thin or edge triangles.
7. Recompute the distance cache (section 8).

**Gotchas**
- Pathing is 2D: only X/Y matter; height is taken from the highest physics mesh. Two navmesh layers cannot overlap in Z: the engine takes the first physics object, so a water ID under a bridge blocks the over-bridge navmesh (1.x, 2024). Remove roof/bridge physics where parties must pass under.
- Crash causes: islands of unconnected faces, faces intersecting or sinking below terrain, concave/inverted faces, settlements or spawns outside navmesh, old Calradia navmesh left over, unnavigable tiles not fully enclosing navigable ones. Holes are acceptable only when enclosed by impassable faces; an open hole gives "could not find navmesh position" at load.
- Elevation: raising all faces slightly above terrain is the standard fix (intersecting or sunken faces crash). (disputed: Swan said elevation does not matter, 2024.)
- Old workaround: one huge face above the whole map as a catch-all (e1.6 to e1.8). It must be re-added after edits and npc99. advised against it.
- Since e1.7.0 the battle grid sets battle scenes, so impassable IDs are not needed for that; deleting faces works equally.
- Tile cost is a path-length penalty, not a speed change. Per-face costs may cause hitching on very large maps (disputed: TOR vs Shokuho). Too many faces hit a limit and the editor drops some at random (single report).
- Moving a settlement and recomputing rewrote `navmesh.bin` into a crashing state once (e1.8.0); `navmesh.bin` is forward/backward compatible, so recopy a backup (single report).
- A crash-bisect trick: set all faces to ID 1, recompute the cache, and if it loads, re-enable impassable areas piece by piece (single report).
- Navmesh generation may need a spawn point such as `sp_player` to exist (unverified).
- No engine feature excludes faces for cavalry; a Harmony patch on `MapScene.GetPathBetweenAIFaces` works but large exclusions hurt AI performance.
- Roads do not affect AI; use ID cost tiers with uniform face size.
- Sea/land IDs for sailing: (disputed: one user uses sea 6 and land border 7, another says sailable rivers use 11 and 6 is fording). War Sails values are in section 11.

Contributors: FoozleMcDoozle, npc99., Swan, hunharibo, Carolina Warlord, Eagle, Hart, Dejan (TW), BlackRose, Klassix, Lemmy1916, Bullero, g-spurs, Haterade.
Wiki: https://docs.bannerlordmodding.lt/editor/settlementpositionscript/ - **adds detail** (CheckPositions fixes for outside-navmesh settlements); the rest is **new**.

---

## 6. Rivers and water

**Steps**
1. Add a `river_script` entity at 0,0,0 using `worldmap_river_mask` (hardcoded name, max 1024 x 1024).
2. Blue channel (B 254) draws the river and its width on a background of R205 G206 B1. Red/green encode flow direction (cut sections from the template). The image is flipped on the N-S axis.
3. Assign the flowmap and `worldmap_river_floor_n` in the script and regenerate.

**Gotchas**
- Poor at sharp bends; avoid the centre of the flow range (sluggish flow). Output can float or be buried.
- Most mods abandoned it and used `road_instance` paths as rivers: easier and colourable.
- Opening and re-saving an unmodified Main_map can lose river textures (1.x 2022 to 2023).
- Coastal foam comes from `worldmap_wave_map`. Paths outline coasts and rivers for water sound.
- A collidable invisible plane at sea level stops walking on the sea floor; a "swamp" navmesh plus boat-model swap enabled sea travel (single report, e1.x).

Contributors: npc99., FoozleMcDoozle, Lemmy1916, Drezavelt.
Wiki: not covered - **new**.

---

## 7. Settlements on the map

**Steps (add one)**
1. Copy a vanilla settlement entity of the right type (`town_`, `castle_`, `village_`, hideout) with all children.
2. Rename it to the new unique id, equal to the `Settlement id` in `settlements.xml`. Also give it a unique town or village component id in the XML.
3. Parent it to the right `campaign_icon_capsule_N` (7a). Move the whole settlement, never children; Alt+W shows world coordinates, Alt+L local.
4. Press P to snap to terrain; level 3 is the reference for fitting terrain.
5. Gate position on passable navmesh just outside the gate; make the interior impassable (icons have no collision).
6. Check parts: `bo_town`/`bo_castle`/`bo_village` click body, banner positions and breach walls on all 3 levels, siege bounding boxes. Deleting mangonel/trebuchet spawn locations removes siege engines and makes breaches impossible.
7. Update `settlements.xml`, run the scripts (section 8), and **start a new campaign** to test.

**Gotchas**
- Minimum for a replacement icon: gate position, click physics body, level meshes (empty parents are fine), banner positions, siege boxes (missing = siege crash). Wall/tower meshes are cosmetic. Production icons come from the XML, not the prop. Without physics the settlement is unclickable.
- Names: `town_EW1` style; the prefab must be named exactly the settlement id (retirement_retreat etc.). Renaming settlement entities in the inspector crashed the editor in e1.7 (single report).
- An unclickable village means its `bo_` body was moved into a sub-entity. Villages technically have 3 levels plus looted; deleting them crashes. Village level comes from hearth (1 to 199 L1, 200 to 599 L2, 600+ L3), not from the bound settlement (1.x 2023).
- Missing Upgrade Level Visibility flags crashed sieges (single report, 2026). Looted smoke is a `siege_fire_particle` referenced via `Native\Prefabs\map_icon_parts.xml`; set level visibilities on the particle entity.
- `banner_pos` on a settlement prefab caused an xscene load crash for one user, resolved by removing (single report).
- To lift a settlement, untick "snap to terrain" in its visibility flags.
- A playable world needs at least 1 bandit camp and a city/castle plus villages per faction. Castle-bound villages crash at start with no town nearby. The XML `trade_bound` attribute was removed in e1.8.0 (delete it, keep `bound`); binding now uses the distance cache, not physical distance, so raise the trade-bound distance (1500 to 4000) or move a town closer to fix a food `getdistance` crash.
- Scene swap: `scene_name` on the centre location, `scene_name_1/2/3` for levels.
- Spawns: faction/mercenary parties use faction settlements; hideouts are bandit spawns. Player spawn and training camp are not in the scene (hardcoded; C# needed).
- Deleting: remove the entity, the XML entry, bound villages, then regenerate the cache. Deleting settlements or hideouts with XML left crashes. Decals remain until reload.
- 1.5.x: settlement renames need XSLT; entities shifted by identical offsets in one save, so back up daily and diff `scene.xscene` (single report, 2026; unverified).

### 7a. Capsules and managers
- Settlement entities with `town_entity_manager` sit under empty `campaign_icon_capsule_N` entities, which are non-overlapping geographic culling boxes. A stray manager outside one crashes at "Ticking map scene for first initialization". A lone capsule works but loads slowly.
- Also create one empty entity with the **Town Scene Manager** script. A missing `CampaignMapSiegePrefabEntityCache` returns null and crashes. Do not delete siege children in settlement prefabs.
- Tags: `town`, `castle`, `village`, `wm_hideout`. Reparent with "assign new parent"; `scene.xscene` stores child offsets.
- (disputed: e1.6.5 to 2025 reports say capsules are mandatory for icons, raid smoke and siege prefabs; others in e1.8 and 2024 removed them and it worked, one with choppy movement.) Safe approach: keep them, 8 to 10 settlements per capsule (TW style), one capsule per settlement defeats the culling.
- Entity cap about 512k per scene (disputed: failures near 550 settlements for one user, none at 613 for another). Cut entities per settlement or save regions as prefabs; prefabs are preferred on the world map.
- Never add a settlement-position script to a village entity.

### 7b. Map-icon textures
- Textures 2K or lower; material swaps on map icons caused crashes after inventory/trade screens or on first campaign load (e1.6 to e1.7).
- Custom icon prefabs crashed e1.7.0 (odd `prefab_name=_index91`; check `old_prefab_name` counts in `scene.xscene`).
- An empire wall without the `map_icon` tag crashed on a campaign map (e1.6).
- e1.7.2 broke campaign decals; ignore "Update decal atlas" prompts.

Contributors: FoozleMcDoozle, npc99., Jance, Lemmy1916, hunharibo, Litauen, Swan, Carolina Warlord, Bubu, Egg, Space_Pot8to, Haterade.
Wiki: https://docs.bannerlordmodding.lt/editor/world_map_settlements/ - **adds detail** (cloning, entity limit, deleting are already covered). Capsules, managers, minimum icon rules and icon textures are **new**.

---

## 8. settlements.xml and settlements_distance_cache.bin

**Steps**
1. Copy the vanilla `settlements.xml`. Keep every vanilla settlement. Every XML settlement must exist on the map (extra map entities without XML are fine).
2. Add map entities first, save, then edit the XML. Do this last, or remove the scripts while working.
3. On the settlement-scripts entity (plus an empty entity), press in order: **Check Positions**, **Save Positions** (writes map coordinates to 3 decimals), **Compute and Save Settlement Distance Cache**.
4. Expect about 3 minutes; the `.bin` stays 0 KB until done. 30 to 75 minutes signals a problem, typically navmesh.
5. Copy both files into your module and use XSLT patches (not raw edits). Start a **new** campaign.

| Era | Where the scripts write | Action |
|---|---|---|
| e1.6 to e1.8 | `Modules\SandBox\ModuleData` | Copy both files to your module; Steam verify or patch restores Calradia's |
| 1.0.x (late 2022) | Sandbox; not run automatically on save | Same; use the TradeBoundFix mod DLL |
| 1.1 onward (2023) | your module | Check the module copy; still verify game files |
| 1.5.x | module; editor has a generate-SDC button | Use the matching kit; module folder name must equal module name (a "(WIP)" suffix nulled the compute in 1.5.3) |

**Gotchas**
- Map save crashes when the XML names a settlement that is not on the map. Check Positions jumps to the bad one; the wiki workflow is: back up, press, fix, repeat, and a crash with no bad ones left means success.
- Duplicate ids or typos crash ("Generated a child" came from ids duplicating vanilla ones).
- Mismatched XML and cache crash on load. A settlement moved more than ~150 m from its owner can break (TradeBoundFix).
- Move settlements in the editor, never type coordinates; scene.xscene stores parent-relative offsets. A separate mod cannot move one vanilla settlement.
- The retirement settlement can crash Check Positions; remove it from the XML for that step and re-add for gameplay (single report).
- Cache fails if navmesh does not reach the settlement or a town/village exceed the allowed distance.
- Gate wrong after cloning: delete the cache and regenerate (wiki).
- Adding settlements beyond vanilla requires your own cache; the script needs the `settlement_scripts` entity in your scene. Hang of minutes during compute is normal.

Contributors: npc99., FoozleMcDoozle, hunharibo, BlackRose, Carolina Warlord, Dejan (TW), Litauen, Haterade, KILO TCDFB, Egg, Mark7, Nanu.
Wiki: https://docs.bannerlordmodding.lt/editor/settlementpositionscript/ and https://docs.bannerlordmodding.lt/modding/settlements/ - **adds detail**. Button behaviour is already covered; version-dependent write locations, order, timing and 0 KB behaviour are **new**.

---

## 9. Battle scene grid

**Steps**
1. Copy `worldmap_battle_scene_grid` (native 1024 x 1024 per wiki; one 2024 report says size is tied to terrain size 848, unverified) and edit in an image editor.
2. **Red** = battle scene index (max 255). Fill regions solidly with **exactly flat** values; feathered edges break lookup.
3. **Green** = approach direction/spawn side within a red block (fixed template). **Blue** = water mask, optional.
4. Map red values to scenes through `map_indices` in `sp_battle_scenes.xml`. Several indices can map to one scene; an index maps to one scene. Only `id` and `map_indices` are read.
5. Import with the flags from section 3 and wrong mips avoided.
6. To use only your scenes: remove base entries via XSLT, or use the wiki's Harmony patch on `Campaign.InitializeScenes`. New scenes through a plain submodule XML do not work without a patch on `GameSceneDataManager.LoadSPBattleScenes`.

**Gotchas**
- Before e1.7.0 the navmesh terrain-type IDs selected battle scenes; since then the grid does (the early mapping is why old navmesh IDs matter little today).
- A missing scene throws an exception but picks a random one. Only red values present in the XML work.
- Random start-of-campaign crashes with custom grids; wrong mipmaps matter.
- Test scene names in custom battle.

Contributors: Dejan (TW), npc99., FoozleMcDoozle, hunharibo, Carolina Warlord, Space_Pot8to, Litauen, Fatrod.
Wiki: https://docs.bannerlordmodding.lt/editor/battle_scene_grid/ - **already covered**; the exact-flat rule and missing-index behaviour are small additions.

---

## 10. Limits at a glance

| Item | Limit or rule | Era |
|---|---|---|
| Entities per scene | ~512k; settlement count disputed | 1.x 2024 |
| Terrain nodes | 16 x 16 | all |
| Layers | 16 at 256 res; 3 to 4 at 512 res | 1.0.x |
| Vista | 16384 max, power of two | e1.8 |
| Map icon textures | 2K or less | e1.6 to e1.7 |
| River mask | 1024 x 1024 | e1.6 |
| Editor RAM | Main_map save above 16 GB; 8 GB VRAM may be short (disputed) | 1.0 to 1.2 |

**Gotchas**
- Campaign frame drops come mainly from AI/behaviour ticks, not flora.
- Editor GPU overload (mass texture replacement, moving many settlements, big sculpts) can corrupt the scene; lower editor graphics and cap the framerate (2024). Soft borders caused it once.

Contributors: Bubu, Iberian Wolf, GourmetBean, Litauen, Snorri, npc99., Lemmy1916, Swan.
Wiki: https://docs.bannerlordmodding.lt/editor/world_map_settlements/ - **already covered** (512k limit); the rest is **new**.

---

## 11. War Sails (1.3+) maps

**Steps**
1. Navmesh tiles: shore border 7, shallow water 18, deep ocean 19, under bridges 25, navigable rivers 11, unnavigable fill 10.
2. Add the blockade script to port towns, with `port_town` children `blockade_start`, `blockade_end`.
3. Edit pirate spawns into the xscene by hand (editor saving erases them).
4. Keep port coordinates for only one port in `settlements.xml`, generate the three caches (`_Default`, `_All`, `_Naval`), then add ports one at a time.

**Gotchas**
- Saving re-adds deleted port coordinates; otherwise the cache crashes in `GetPathBetweenAIFaces` (2026).
- The DLC main map cannot be opened in the editor (RAM). Naval raidable villages need port coordinates plus a regenerated cache.

Contributors: Egg, Litauen, Sly.
Wiki: https://docs.bannerlordmodding.lt/editor/world_map_general/ - **already covered** (Egg's guide).

---

## 12. Version migration

| From | To | Action |
|---|---|---|
| e1.5.3 | e1.5.4 to e1.6.0 | Custom main maps broke (terrain overwritten by Calradia's, black map); community surface fix; e1.5.9/e1.6.0 fixed black terrain |
| e1.6.0 | e1.6.1 | `border_min`/`border_max` replace `managed_core_parameters.xml`; MapFix needed; vista tileset none |
| e1.6.4 | e1.6.5 | Capsules plus Town Entity Manager scripts; Town Scene Manager entity |
| e1.6.x | e1.7.0 | Battle grid replaces navmesh-ID scene choice; custom icon prefab crashes |
| e1.7.x | e1.8.0 | Delete `trade_bound` from villages; ModuleCategory tag; MapColorGradeManager entity; xscene gains `gi_data_exists`; open in 1.7.2 first if a "Generated child" crash appears |
| e1.8.0 | 1.0.0 (Oct 2022) | Terrain removal crashes; saving with Worldmap tileset crashes; scripts write to Sandbox |
| 1.0.x | 1.1 to 1.2 | Scripts write to your module; weather green-channel crash fixed by patch in 1.2 |
| 1.2.12 | 1.3.x | New `river_script`: set flow maps to `plain_black`, delete the river mesh. Copy `MapRainAndSnow` from native Main_map xscene. Create `ModuleData/DistanceCaches/` and regenerate the cache there as `settlements_distance_cache_Default.bin` with the 1.3 kit and your map loaded; the old `settlements_distance_cache.bin` is ignored, and Compute may silently do nothing without the folder or with duplicate settlement IDs. Fix `<water_properties>` too, or expect white fog and floating rivers (exact values unverified). Update XMLs for new kingdoms and clans |
| 1.3.x | 1.5.x | Loading-screen hang: change `vista_tileset="none"` to `""` in `scene.xscene`; recompute cache in the matching kit; terrain D3D errors tied to terrain quality (try high, tessellation off, static shadows); partial TW hotfixes |

**Gotchas**
- Unported 1.2 maps on 1.3 show white fog, floating rivers or weather crashes. A custom mesh once crashed character creation (the campaign loads fully before it).
- 1.2.12 to 1.3.x: pre-1.3 `Main_map` needs no rename.

Contributors: Gingy, Lemmy1916, Litauen, Sly, Fatrod, Muret (TW), Dejan (TW), Haterade, npc99.
Wiki: not covered - **new**.

---

## 13. New map from scratch: checklist

1. Install Harmony and Aurelian MapFix in both bin folders; unblock DLLs.
2. Create a module (folder name equals module name); set ModuleCategory Singleplayer in SubModule.xml.
3. Open Sandbox `Main_map`, Save As `modded_main_map` into the module. Back up the folder.
4. Set Tileset Name to `none`; remove Calradia terrain; generate terrain (max 16 x 16); import a heightmap.
5. Import vista diffuse and normal; add 12 to 14 paint layers including a base layer.
6. Place `border_min` / `border_max`; keep `spawn_visual`, siege entity script, cloud plane; add the MapColorGradeManager entity.
7. Copy vanilla `settlements.xml` (keep all settlements); place settlements under `campaign_icon_capsule_N`; add the Town Scene Manager entity.
8. Delete the outer mesh, Generate Grids, fix and raise the navmesh, plug holes, set IDs; confirm gates, spawns and hideouts are on passable faces.
9. Run Check Positions, Save Positions, Compute Distance Cache; copy XML and `.bin` into your module.
10. Build the battle grid and `sp_battle_scenes.xml`; put textures in `AssetSources\world_map`.
11. Optional: rivers (`road_instance` or `river_script`), physics heightmap, wave map.
12. Save (watch RAM), start a **new** campaign, and test one change at a time.

---

## Contributors

npc99., FoozleMcDoozle, Carolina Warlord, Lemmy1916, hunharibo, Aurelia, Litauen, Swan, Dejan (TW), Muret (TW), Hart, Egg, BlackRose, Snorri, Bubu, Jance, Haterade, Fatrod, Space_Pot8to, Gingy, Sly, Eagle, GourmetBean, Le Profyteur, Maegfaer, Xylord, Klassix, Bullero, Iberian Wolf, Reynauld, KILO TCDFB, Lukar (TLD), Mark7, Nanu, and others with single reports.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/15-campaign-world-map-sources.md](sources/15-campaign-world-map-sources.md)** (158 of 158 linked). The links only work for members of the Mount & Blade Modding server.
