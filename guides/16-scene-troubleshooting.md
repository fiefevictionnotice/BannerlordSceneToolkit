# 16. Scene Troubleshooting: Symptom Index

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

Version labels: "e1.x" means early access (Oct 2020 to Oct 2022); "1.x" means the restarted numbering (Oct 2022 onward), dated by the finding. Claims backed by a single report are marked "(unverified)". Source names are Discord handles; the full list is in the Contributors line at the end.

> **Check these first**
>
> - **Delete the shader cache.** Blue, red, black, garbled or "only base layer" terrain is cured by this more often than by anything else. Delete the folder named after your scene under `C:\ProgramData\Mount and Blade II Bannerlord\Shaders\TerrainShaders\<Module>\`, plus the scene's `ShaderCache` folder. Never delete the whole Shaders folder.
> - **Never save on exit.** Save by hand, then answer "No" to the save prompt when closing. Saving on exit corrupts scenes, and the backups made at the same moment are corrupt too.
> - **Make sure the scene has terrain and both data folders.** A scene with no terrain crashes on save. A scene missing `SceneEditData\<name>` opens flat in the editor. Folder name, scene name inside `scene.xscene` and the XML id must be identical, with no spaces.
> - **Do not trust the editor's "Start Mission" test.** Wrong colors, crashes and spawns in the corner are normal there. Test in Custom Battle or with a community tester module instead.
> - **When a scene crashes and you do not know why**, attach dnSpy, or bisect: copy the scene, delete half the entities, test, repeat. Check for stray scripts, non-uniform scale on physics objects, unbroken custom prefabs and missing siege tags.

---

## 1. Editor crashes (on open, save, exit, paint, undo)

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Editor closes at launch, or launcher closes with "no target file" | Missing Visual C++ redistributables (both x86 and x64); game and Modding Kit versions out of sync; graphics driver; multi-monitor or resolution quirks | Install both redistributables, verify files for game and kit, update or roll back the GPU driver | Redistributable fix confirmed 2026; driver rollback fixed one 2023 case | Litauen, Exo, Klassix, Feli |
| Editor opens with no toolbar or menus | Corrupt saved layout | Reset the layout from the drop-down, or delete the `editor_layouts` file / Editor Files folder under Documents\Mount and Blade II Bannerlord, then verify files | Seen e1.8 through 2024 | Chase K, Reynauld, Byako |
| Editor crashes on save | 1) Scene has no terrain (even interiors, main-menu scenes); 2) low RAM or disk space; 3) terrain data over the 4 GB compression limit (512-dim nodes with many layers); 4) save while another instance has the scene loaded; 5) renamed or moved `settlements.xml`, typos in `custom_battle_scenes.xml` | Generate terrain before saving; free disk space and RAM; keep nodes at 256 or less; check the XML ids | No-terrain cause confirmed 2024-2025 (1.2 to 1.3); interior-scene save crashes on 1.2.7 (Dec 2023, forum-reported) | Hart, Le Profyteur, Zedforen, npc99., hunharibo |
| Save never finishes ("infinite save") | Terrain too dense or too many layers; mesh-blend layer; certain flora in a layer; a hidden save pop-up | Wait several minutes first and check for a hidden pop-up; then disable mesh-blend layers or the offending flora layer on a copy | Several 2023-2025 reports; flora cause is one report (unverified) | Klassix, Reynauld, Levente, matthewster |
| Editor crashes or freezes on exit or when switching scenes | Known editor bug; loading one scene from another; after using a test-mode mod | Close the editor between scenes; open a New Scene first; restart after test mode | e1.8 to 1.3, still present 2026 | npc99., Bullero, Gotha, Kaseke |
| Crash when painting terrain | 1) RAM exhaustion (big terrain, 4K material maps, sparse maps); 2) node resolution was changed earlier; 3) too many layers; 4) first-time material-map import | Save after each import; stay under about 80% RAM; use 256 or lower node dimensions; for a map-import crash, paint a little with that layer first (unverified) | RAM crashes common in e1.5; undo-stack leak fixed in e1.6.5 | FoozleMcDoozle, Yurek, npc99. |
| Crash or freeze after Ctrl+Z | Undo stack leaks memory; undo after texture work corrupts the next save; undo cannot reverse "Effect Upper Layers" | Lower Undo Stack Size (50 to 5-10) on e1.6.4 and earlier; avoid undo on heavy scenes; save first | Leak fixed in e1.6.5 (confirmed); freezes on undo of paint still reported 2022 and 2025 | Klassix, npc99., Dejan, Hart |
| Crash when deleting a paint layer, road or path | Layer removal is very RAM-heavy; known editor bug | Untick the layer, or move it to the top before deleting; or convert it to "flora only", save, then remove; delete road components one at a time; last resort: export maps, remove terrain, re-import | Reported e1.8 through 2024 | BlackRose, Faustus, Hart, Swan, Stevoyd |
| Crash after changing a node's resolution | Per-node resolution change corrupts that node | Do not do it. Raising it always crashes; lowering works one node at a time and only as the last step. To change the whole map, export heightmap and maps, regenerate, re-import | Corruption warning repeated e1.5 to 1.x; Oct 2023 advice is that the engine already lowers detail with distance | FoozleMcDoozle, Klassix, Ted |
| Crash when selecting or searching certain prefabs or entities | Known crash-on-select prefabs (corpse, barmaid_test, particle_test, outdoor_fire_large_line, European barn); prefab search for short strings ("log", "st", "adam"); ram or siege tower in the entity list | Avoid scrolling through them; save before any prefab search; search "All Files" | Corpse crash still present Dec 2020; search crashes persisted into 2026 | FoozleMcDoozle, K1ng, Klassix, Harrison_Jones (unverified for ram) |
| Crash when setting a material to "None", or a prefab material to empty | Empty material slot | Never use "None"; use `editor_gizmo` for an invisible surface (physics stays) | Confirmed e1.5; `default_empty` also crashed maps (2023-24) | Suspicious Man, Snorri, Reynauld |

---

## 2. Corrupted or lost scenes

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Scene will not load after a crash | Crash during save; saving on exit; bad entity | 1) Copy `scene.xscene` from `SceneObj\Backups`; 2) delete `navmesh.bin` to test without it; 3) check for `temp_scene_save_folder_*` in SceneEditData, which can hold recent edits | All versions | Klassix, rainy-rays, FoozleMcDoozle |
| Terrain is flat or "waterworld", paint and flora gone | `SceneEditData\<name>` missing or empty; folder name mismatch; `terrain_ed.bin` is 0-1 KB (data corruption); "Error #40" | Restore `terrain_ed.bin` from backup. If none, export what you can and rebuild from heightmap. "rglBuffer::allocate(0) failed" also means corrupt edit data | Cause constant; most vanilla scenes ship no edit data (confirmed, all versions) | Zahkrosis, Hart, Sjanten, FoozleMcDoozle |
| Vanilla scene opens with no terrain | TaleWorlds ships edit data for only a handful of scenes | Rebuild: Heightmap Extractor (visit the scene in game) or the community tool bl-terrain-ed, then repaint | Tool reports 2026; about 98% of vanilla scenes lack edit data | npc99., Hart, Snorri |
| Scene was in Native/SandBox and is gone after an update | Updates wipe edited default modules | Keep scenes in your own module only | Jan 2021 report, confirmed | Suspicious Man, BD |
| Scene fails to save or loses terrain after hand-renaming the folder | Name inside `scene.xscene` differs from folder; spaces in name | Edit the name near the top of `scene.xscene` (and the XML id); use Save As, not folder renaming | Confirmed e1.5 through 2024 | Kazet, Stranger, npc99. |

---

## 3. Terrain looks wrong (blue, black, flat, spikes, floating flora)

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Blue, red, black or garbled terrain; layers missing; "only the base layer" | 1) Stale shader cache; 2) "mesh blend layer" ticked on layers that should not use it (check every season tab); 3) too many layers; 4) layer made after the scene got heavy | Delete the scene's `TerrainShaders` folder in ProgramData and the scene `ShaderCache`; untick mesh blend; save-as under a new scene name; save twice | Confirmed all versions. Early notes say the SceneObj cache folder did nothing; later notes list both. Wiki: Editor > World Map Problems (red terrain) | Space_Pot8to, FoozleMcDoozle, LizardWizard, Klassix, Fatrod, Snorri |
| Black squares or nodes | Corrupt base or other layer; LOD squares from small node size with few vertices; disabled base layer | Remove the layers, or remove terrain, re-import heightmap and material maps, rebuild only needed layers. Raise LOD multiplier. Keep the base layer enabled | e1.5 and 2025-2026 | NIN3, gkx, npc99. |
| Terrain very dark / vista black | Tileset conflict between vista and "Worldmap" tileset; vista multiply blend on 1.5.3 | Set vista tileset to "none" with multiply; use multiply with a changed texture on a layer | Confirmed 1.0; 1.5.3 report is single (unverified) | npc99., Lukar |
| Layers fine in editor, wrong in game (blue/brown, only base layer) | Area Map texture on a layer; low terrain quality setting (shows one layer); 4K textures on low terrain quality; layer detail distance 0 | Do not use Area Map textures; raise layer detail distance (try a large value); raise the paint "collision" setting; test at high terrain quality | Area Map breakage confirmed 2026 (1.4-1.5); the rest single-reports (unverified) | ARC4N0N, Hart, Le Profyteur, Bullero |
| Layers vanish, flicker or smear | More than about 13-15 layers (cap 16, shared with flora layers); layer resolution above terrain resolution | Create 12-14 layers at project start; match layer resolution to terrain | Confirmed e1.5 to 2026 | Bolsonarido, LizardWizard, Lemmy1916 |
| Terrain spikes after save or reload (even in vanilla scenes) | Editor bug after Lower tool, sculpting below 0, heightmap import; low node resolution | Smooth or flatten-then-smooth the spot; raise the area; use 128-256 per node; they can return on every save | Confirmed 1.3.x through 1.5 (TW acknowledged, no fix by Oct 2026) | Snorri, K.Savo, Dejan, Taylor |
| Heightmap imports with spikes, terraces or ridges | PNG range wrong; 8-bit source; true black at the edges; values near 0 misread | Use 16-bit grayscale PNG; raise the map off the floor; set scale = max minus min, offset = min | Confirmed all versions | Klassix, FoozleMcDoozle, Hart |
| Flora floating or not snapped to slope | Flora placed before terrain was final; layer-flora regression | Place flora after terrain is final; one soft smooth over the whole map (weight about 0.001), or toggle a layer on and off; for trees use the manual flora painter | Floating grass in 1.2.x and again in 1.3+; fix holds in game but returns in the editor | Spartan, FiefEvictionNotice, Lord Eastwood |

---

## 4. Scene loads but is broken in game (walk-through, invisible meshes, dark materials, no flora)

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Players walk through an object | No physics body (`bo_` shape) on the mesh; shape was removed; resource-browser objects need physics set by hand | Add the physics shape; the physics material only affects sound | Confirmed 1.0 | FoozleMcDoozle, Dawa |
| Invisible wall or invisible tree with collision | Flora layers with mixed trees and bushes overwriting each other; sprout flora; blocking boxes on large flora; stray collision on hidden entities | Use the manual flora painter for trees; separate layers for forest and grass; under about 5-6 flora types per layer; check hidden physics | Layer-flora unreliable since 1.3/War Sails (confirmed 2026) | Klassix, Reynauld, FiefEvictionNotice, Hart |
| Object visible in editor but not in game | Missing prefab on the server; unbroken custom prefab; module not loaded | "Break prefab" before shipping or serving; make sure the module is loaded | Confirmed 2025 | Kaseke, Hart |
| Flora not visible in game | Missing soft borders; season flora not set; layer visibility | Add four soft borders; copy flora from summer | Soft borders: e1.8 (Jul 2022, confirmed) | Dejan, FoozleMcDoozle |
| Dark materials / black materials | Vanilla textures referenced from custom materials render black or wrong in 1.3; blended grass looks darker in game; disabled base layer | Re-import the texture into your module; materials and textures must be lowercase; re-save scenes | 1.3 (Material Management crashed on capital letters; fixed in 1.3.10 tools) | Snorri, Eagle |

---

## 5. Crash on mission load / infinite loading

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Crash at the end of the load screen | Scene id does not match the folder; missing `sp_battle_set`, `spawn_visual` or soft borders; custom entry missing in `custom_battle_scenes.xml` | Match ids exactly; add the missing entities (scene needs terrain, navmesh, `sp_battle_set`, 4 soft borders) | Confirmed e1.8 through 1.3 | Ladnar, Snorri, Suspicious Man |
| "GetHeightAtPoint failed at CreateBoundaryEntity" | Barriers or soft borders outside terrain | Put borders above terrain; in later versions the message is harmless | Confirmed e1.5; called harmless in e1.9 | NuclearTrinity, FoozleMcDoozle |
| Crash with no obvious cause | Entities with scripts that do not belong; empty entities; merlons without scripts; siege objects in non-siege maps; orphan entities; unbroken prefabs; too many props | Search `s:` in the scene to list scripts; bisect; parent all objects; search the xscene for `default_empty` | Method confirmed 2024-2026 | Kaseke, FiefEvictionNotice, Snorri |
| Crash after mirroring or scaling | Negative scale or non-uniform scale on walls, gates or physics objects | Mirror only by exactly -1 on one axis; scale uniformly, scale the parent not children | Walls: confirmed 1.2 (Jan 2024). Warnings for non-orthogonal frames are mostly harmless, but merlon/destructible errors are not (confirmed Jan 2026) | Klassix, Hart, FiefEvictionNotice |
| Infinite loading screen, no crash | Shield/item XML with wrong `body_name` / `shield_body_name` stuck the preloader; renamed physics shapes after 1.3 | Fix XML names (dnSpy shows `PreloadHelper.WaitForMeshesToBeLoaded`). Wiki: Guides > Troubleshooting Game Hang | Wiki example plus Nov 2025 report | Eagle |

---

## 6. Siege-specific

Wiki: Guides > Troubleshooting Siege Scene (dnSpy walkthrough of a deployment point with no weapons). Start every new siege scene by copying a working vanilla one, and test it in Custom Battle with `is_siege_map="true"`.

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Crash on level 2 or 3 only | Full crafting stations (forge/smithy) with scripts and tags; deployment point with odd radius; missing level visibility on paths | Use bare versions of props; check radius against other deployment points; set levels on path points | 1.2 (Jan 2024); radius fix is from the wiki | Snorri, Omnissiah_Priest |
| Crash on a level-1-only siege scene | All three levels required (even if identical) | Create all three levels | Confirmed 1.2 | Le Profyteur |
| Crash on start | Missing gate scripts, `wallsegment` tags, required machine spawners or paths, camera, or boundaries | Add the missing parts; the "debug" entity or SpawnPointDebugView lists requirements | Confirmed all versions | NIN3, Hart |
| Crash on "Besiege" on a custom map | Settlement prefab lost breach wall children; missing siege bounding boxes or upgrade-level visibility flags | Compare against a vanilla settlement; copy a vanilla one | 2024, 2026 (single reports) | Snorri, Egg |
| Custom walls crash on load | No `debris_holder` per merlon with DestructableComponent; wrong scripts; mesh_bender on walls | Add holders; only the parent carries the script, with `solid_child`/`broken_child` children | Confirmed 2025-2026 | Carolina Warlord, Exhorris, Snorri |
| AI stuck at gates or towers | Extra gates ignored; navmesh ids; wrong outer gate middle position | Use one inner and one outer gate; put the outer middle_pos behind the inner gate; add a one-way navmesh or a spawner behind the gate | 2022-2024 | Exhorris, Swan, Snorri |
| Crash after upgrading | Ranged siege weapons must be destructible; MP engines no longer work in SP sieges; cameras and placeholders settings | Make them destructible; check cameras | 1.3+ (Mar to Jun 2026) | Sheepbitter, Snorri |

---

## 7. Campaign-map-specific

Wiki: Editor > World Map Problems (red terrain: delete shaders; create 12-14 layers at project start).

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Crash on save | `settlements.xml` lists a settlement not on the map (extra map settlements are fine); settlement script; low RAM (saving Main_map exceeds 16 GB) | Add map settlements first, save, then edit the XML; remove the scripts while working; wait for RAM to drop below 80% | Confirmed e1.6 to 1.1 | npc99., FoozleMcDoozle |
| Crash or hang computing the distance cache | Duplicate settlement ids; wrong ids; stale cache; missing `ModuleData\DistanceCaches` (1.3+, file `settlements_distance_cache_Default.bin`) | Fix ids, rerun Check, Save, then Compute. A few minutes is normal; 30-75 minutes is a problem | Cause identified Oct 2022; 1.3+ naming Oct 2025 | npc99., Lemmy1916, Litauen |
| Crash on movement, "getdistance" food crash | Cache not regenerated after navmesh or settlement edit; village far from a town | Regenerate; move the town closer or raise trade-bound distance | 2024 | Swan, npc99. |
| Crash after "Ticking map scene for first initialization" | Town or hideout manager script not inside a `campaign_icon_capsule` | Wrap in capsules, with one empty entity carrying the Town Scene Manager | e1.6.5 onward; capsules are only culling groups in later notes | npc99., FoozleMcDoozle |
| New campaign crashes, saved game works | Settlement moves need a new game; `trade_bound` attribute | Start a new game; delete `trade_bound` (keep `bound`) | e1.8.0 (Jul 2022); `trade_bound` obsolete 1.0 | Carolina Warlord, npc99., Sten |
| Custom Main_map breaks (paint maps deleted, black map) | Saving under the name "Main_map" deletes layer maps; hard-coded physics heightmap | Save as `modded_main_map` with Aurelian MapFix (needs Harmony); keep the working folder and copy it | e1.6.1 onward, still valid 2025 | npc99., FoozleMcDoozle, Hart |
| Crash overriding snow flowmap | Flowmap overrides; green channel crashes at borders | Do not override, or use the all-blue map; fixed by Harmony finalizer in 1.2 | e1.6 to 1.2 | npc99., Cephas |
| Custom maps hang at loading screen | Vista tileset; stale SDC; terrain quality / D3D errors | Set vista tileset to empty, recompute SDC in the matching kit, raise terrain quality | 1.5.x, 2026 | Fatrod, Snorri, Dejan |
| Port/raid crashes | More than one port coordinate in `settlements.xml` | Keep only one port | 1.3 / War Sails (2026) | Egg, Litauen |

---

## 8. Patch-specific breakage

| Version | Breakage | Fix | Source |
|---|---|---|---|
| e1.5.4-e1.5.6 (Dec 2020 to Jan 2021) | Main-map editing broke (painting crashes, navmesh, players spawn under the map); custom main maps lose terrain | Wait or work on a test copy; reported to TW | Deleted User, FoozleMcDoozle |
| e1.5.7 AMD update (Feb 2021) | Custom main menu crash; startup crashes | Delete the contents of `ProgramData\Mount and Blade II Bannerlord` and revalidate | beefbacon |
| e1.6.2-e1.7.0 | Terrain nodes vanish, layers turn blue after save, painting fails at node edges; re-entering custom towns crashes | Save As into a new module/scene, switch seasons, or return to 1.6.0/1.6.1; fixes came in e1.6.4/1.6.5 | Space_Pot8to, Levente |
| e1.8.0 (Jul to Sep 2022) | Scene switching crashes; assert pop-ups; "Generated child" crash; flora invisible without soft borders | Create a new scene before loading; Ignore asserts; open the map in e1.7.2 first or fix duplicate settlement ids | FoozleMcDoozle, BlackRose, Lemmy1916 |
| 1.0.0-1.1.0 (Oct 2022 on) | Terrain removal crashes; generating terrain crashed after 1.1.0; "retreat" settlement crashes the map | Take terrain from another scene, or a blank xscene template; downgrade to 1.0.3 helped | npc99., Carolina Warlord |
| 1.2.x (2023-24) | Editor "Start Mission" empty/crashing; 1.2.4 scenes broke; 1.2.7 interior saves crash; update overwrote modified XML and shaders | Use Custom Battle; add terrain; recheck XML after updates | Snorri, BlackRose, hunharibo |
| 1.3 / War Sails (late 2025) | Layer-flora unreliable; Material Management crashes on capital letters; renamed `bo_` shapes cause infinite loading; MP siege engines fail in SP sieges; river and weather scripts change; flags stop animating (not a bug: cloth auto-disabled) | Lowercase assets, re-import textures, update shape names; port old maps by setting river flow maps to `plain_black` and copying `MapRainAndSnow` from native | Snorri, Eagle, Hart, Sheepbitter |
| 1.4-1.5 (2026) | Terrain spikes persist; Area Map textures break; map loading hangs; editor tester blue landscape | See sections 3 and 7; delete shaders for the tester | Taylor, ARC4N0N, Fatrod |

---

## Safe working habits

- **Back up both folders.** Zip `SceneObj\<scene>` and `SceneEditData\<scene>` after every working session, and every few minutes during risky steps. Keep 15-30 rotating copies (a scheduled task works). Backing up only SceneObj lost terrain for two users. The editor has no autosave, and its `Backups` folder is just a copy made at save time.
- **Never save on exit.** Save manually, then decline the prompt when closing.
- **Save before risky steps**: layer deletion, node changes, big imports, prefab search, undo, "fix duplicates" (it can delete half the map), and anything that touches many entities.
- **Watch RAM.** Above roughly 80% of RAM, save and restart. Keep nodes at 256 dimensions or less and 12-14 paint layers set up at the start.
- **Clear shader caches** after any patch, hotfix or odd rendering: delete the scene's folder under `ProgramData\...\Shaders\TerrainShaders` and the scene's `ShaderCache`. The first reopen may crash once; retry.
- **Restart between scenes**, and after any test-mode session.
- **Keep scenes in your own module**, with matching folder, scene and XML names, no spaces.
- **Test route**: Custom Battle (add your scene to `custom_battle_scenes.xml`, or a community tester module), with `sp_battle_set`, spawn visuals, navmesh and four soft borders in place. Test sieges with `is_siege_map="true"`. Break custom prefabs before handing the scene to anyone or running it on a server. Wiki: Guides > How to Report a Crash and the advanced stack-trace guide for logs; crash logs sit in `ProgramData\Mount and Blade II Bannerlord\logs`.
- **Diagnose by cutting**: dnSpy (double-click the exception to find the entity), BetterExceptionWindow, bisecting entities in `scene.xscene`, or deleting terrain and flora in a copy.

---

Contributors: FoozleMcDoozle, npc99., Klassix, Snorri, Hart, Space_Pot8to, toy boat, LizardWizard, Lemmy1916, Carolina Warlord, hunharibo, FiefEvictionNotice, Kaseke, Litauen, Swan, Dejan, Reynauld, BlackRose, Le Profyteur, Exhorris, Thil, Bullero, ARC4N0N, Sheepbitter, Eagle, Gotha, Ladnar, gkx and Muret (TaleWorlds), and the many other channel members credited in the source files.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/16-scene-troubleshooting-sources.md](sources/16-scene-troubleshooting-sources.md)** (251 of 256 linked). The links only work for members of the Mount & Blade Modding server.
