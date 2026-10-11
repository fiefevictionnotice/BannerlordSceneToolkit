# Scene Setup: Files, Registration, Testing, Backups and Performance

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - A scene is two folders. `SceneObj\<name>` is what the game loads; `SceneEditData\<name>` (with `terrain_ed.bin`) is what the editor needs for terrain. Ship both or the next person opens a flat map (FoozleMcDoozle, 2020-2025).
> - Never rename or copy a scene in Explorer. Use Save As into your own module; the name is also stored inside `scene.xscene` and cannot contain spaces (FoozleMcDoozle, 2020-2024).
> - Editor "Start Mission As" has been broken for years. Test with a Custom Battle entry, a settlement in your own `settlements.xml`, or Gotha's test-mode mod (many, 2021-2026).
> - Never save on exit. Zip the scene after every save and keep rolling backups; crashed saves leave corrupt or flat scenes (Hart, 2024; FiefEvictionNotice, 2026).
> - Occluders save CPU only and terrain never occludes. Put them inside hills and cliffs, sparingly; prefabs with hundreds of children lose LOD and culling (Muret, TW, 2023; Le Profyteur, 2024).

Version labels: "e1.x" = early access (2020 to Oct 2022); "1.x" = renumbered series (Oct 2022 on), by finding date. "(unverified)" = single report. "(disputed: ...)" = people disagreed.

---

## 1. What lives where

| Item | Location | Purpose |
|---|---|---|
| `scene.xscene` | `Modules\<mod>\SceneObj\<name>\` | Scene id (first lines), terrain parameters, paint layers, entities. Plain XML. |
| `terrain.bin`, `navmesh.bin`, `flora.bin`, prt data | same folder | Runtime terrain, navmesh, painted flora, lighting bake. |
| `terrain_ed.bin` | `Modules\<mod>\SceneEditData\<name>\` | Editor-only terrain, about 5x bigger (FoozleMcDoozle, 2021). |
| Prefab xml | `Modules\<mod>\Prefabs\` | Custom prefabs; the editor reads them here. |
| `Backups`, `temp_scene_save*`, `temp_mission_scene` | `SceneObj` / `SceneEditData` | Normal-save copies; crash and test-mission leftovers. |

- Scene id must equal the folder name, no spaces (Muret TW, Kazet, 2020).
- Most native scenes ship without `SceneEditData` and open flat; TW will not release it (npc99., LizardWizard, 2023). A pinned channel list shows which have it.
- Heightmap exports land in `bin\Win64_Shipping_wEditor` (Klassix, 2020).
- Prefabs are not stored inline in the xscene (Le Profyteur, 2026, unverified). Breaking one adds file size, sometimes about 100 kB (Thil, 2026).
- Toolbar missing: delete `Documents\Mount and Blade II Bannerlord\Editor Files\Layouts` and verify files (Chase K, 2023).

Gotchas:
- Backing up only `SceneObj` loses terrain (Kazet, 2020).
- With `SceneEditData` gone, terrain cannot be rebuilt from `terrain.bin`; renaming it does not work (toy boat, 2022).
- A 1 KB `terrain_ed.bin` after saving means corruption (FoozleMcDoozle, 2023).
- Interior/siege xscene files crash on entering at roughly 9 to 11 MB; pack repeated groups into a prefab (wiki).

Wiki: https://docs.bannerlordmodding.lt/editor/editor/ and https://docs.bannerlordmodding.lt/editor/scenes/ - **adds detail** (wiki has the folder split and size ceiling; file table and native-scene list are new).

---

## 2. Create, save and move a scene

1. File > Create New Module inside the editor, restart it, tick the module in the launcher. Modules made in Explorer do not appear (FoozleMcDoozle, 2020-21; LizardWizard, 2024). If the generated one misbehaves, build by hand (Secretblaze123, 2025, unverified).
2. Open the scene, File > Save As into your module. Both folders are written (FoozleMcDoozle, 2020).
3. Use underscores. If you saved a name with a space, Save As again and delete the old folder (Tjens, 2020).
4. Set the current game version in `SubModule.xml`; new modules came with an old default, 1.5.2 (FoozleMcDoozle, 2021; still reported in 1.x, npc99., 2022).
5. To copy or move a scene, Save As again, or move the whole module folder (Save As cannot write outside enabled modules) (Kazet, 2020).

Gotchas:
- Never rename scene or module folders in Explorer (FoozleMcDoozle, 2020-24; npc99., 2022).
- Folder name and internal name must match or saving fails (Stranger, 2022, unverified). Hyphens in module id broke loading once (eugenz, 2023, unverified).
- Save As to the same name does not fix a corrupt layer state; use a new name (Space_Pot8to, 2021, unverified).
- Do not keep scenes in Native, SandBox or SandBoxCore: updates and verify-files wipe them (Hayster, 2021; Reynauld, 2024), and strays can crash new campaigns (Litauen, 2024, unverified).
- (disputed: some saved into SandBoxCore and copied to `battle_terrain_001` fine (Macbeth, 2021); others got edge-node render bugs and AI ignoring navmesh (FoozleMcDoozle, 2021). 2025 reports say do not.)
- A scene saved under a native scene's name replaces it at load (see section 4).

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ - **new**. https://docs.bannerlordmodding.lt/modules/mod_release/ - already covers SubModule versions.

---

## 3. Navmesh, prefabs, flora kinds across scenes

- `navmesh.bin` can be swapped between scenes with identical terrain, or used to fix a vanilla scene without edit data: save it as a temp scene, fix, copy back (FoozleMcDoozle, 2021). On different terrain it may land at altitude 0; select all and raise (Le Profyteur, 2022).
- The File menu has "save navmesh only"; Alt+F2 saves (FoozleMcDoozle, 2020).
- Moving entities between scenes: parent under one empty, save as prefab, load in the other scene. Pasting `references.txt` fails (FoozleMcDoozle, 2022).
- Your prefabs go to `Custom_Prefabs.xml`, which receivers lack. Break them before sharing, and again after your first save (Dejan TW, 2021; BannerSwordOld, 2025). Contest entries could not include prefab XML (Dejan, 2021).
- Flora Editor changes land in Native's `flora_kinds.xml`; copy entries into your module (FoozleMcDoozle, 2021).
- Native materials are read-only. Make new ones, or use resource browser "create overwrite" (FoozleMcDoozle, Le Profyteur, 2022).

Gotchas:
- A corrupt prefab XML crashes the prefab panel; delete it (Le Profyteur, 2024, unverified).
- Re-parenting can make an entity vanish; duplicate it (Muret TW, 2020).

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ and https://docs.bannerlordmodding.lt/editor/prefabs/ - **adds detail** (file swapping and prefab breaking are new).

---

## 4. Registering a scene

**Custom Battle (field or siege)**
1. Put both scene folders in your own module.
2. Copy `CustomBattle\ModuleData\custom_battle_scenes.xml` into your module's `ModuleData` and replace the Scene nodes (Gingy, 2026). The scene need not be in SandBoxCore.
3. `id` must equal the folder name exactly or the load screen ends in a crash (Ladnar, 2022).
4. Sieges: add `is_siege_map="true"` (k1ng, 2022, unverified), or replace the Ab Comar castle entry (Boggo, 2024).
5. Restart the game. The scene needs terrain, navmesh, an `sp_battle_set` entity, spawns and four soft borders (Snorri, 2024).

Version notes: in e1.5 (2020) you had to edit the native file (Mylittletantan, unverified); from e1.6.0 (2021) other modules can append scenes (Levente, 2021); 1.x added translation keys like `{=...}` to scene names (Lars, 2022).

**Campaign scenes**
- `SandBox\ModuleData\settlements.xml` maps settlements to scenes. Copy it into your module and edit `scene_name` (FoozleMcDoozle, 2021). Appending an override node failed for some (Bolsonarido, 2021).
- Keep/tavern scenes are whatever `settlements.xml` assigns (Sly, 2025). A `center_passage` plus one navmesh face is enough to enter (Gotha, 2025).
- Saving under a vanilla name overrides it without touching vanilla files (e.g. `main_menu_a`; lowest module in launcher order wins) (FoozleMcDoozle, 2021-23; Hart, 2024). Start a new campaign afterward (Yivali, 2025). Quick targets: `empire_village_003`, `vlandia_village_g` (FoozleMcDoozle, 2021).
- Duplicating a settlement: add its id in `settlements.xml` and save (Belisariusss, 2025). It can leak a global docks ambience (Eogan, 2025, unverified).
- Distance cache (1.3+): folder `ModuleData\DistanceCaches`, file `settlements_distance_cache_Default.bin` (old name ignored). Generate it in the 1.3 editor with your map loaded; duplicate IDs or xscene/xml position mismatches make Compute do nothing (Litauen, Gingy, 2025).

Gotchas:
- Saving rewrites `settlements.xml`; a typo or missing file crashes the save (hunharibo, 2024; wiki).
- Renaming a map in xml for testing broke saving the original (BlackRose, 2024, unverified).
- Duplicate scene copies in two folders broke server map recognition (Yaco, 2025).

Wiki: https://docs.bannerlordmodding.lt/editor/battle_scene_grid/ - already covered for `sp_battle_scenes.xml`. https://docs.bannerlordmodding.lt/editor/scenes/ - **new** for `custom_battle_scenes.xml`, settlement tests, distance caches.

---

## 5. Test a scene

1. **Custom Battle (most reliable):** register per section 4, launch with your module, pick the scene. Atmosphere is overridden by Custom Battle presets (Justin, 2023). Shortcut: overwrite `SandBoxCore\SceneObj\battle_terrain_001` and pick Chertyg Mountain Pass (Justin, 2023); back up first.
2. **Gotha's Editor Test Mode mod:** unzip into `Modules`, unblock the DLL, enable it in the modding tool, File > Start Mission As (Gotha, 2025). Unsaved changes are lost on exit.
3. **Settlement route:** bind a keep or interior to a town in your own `settlements.xml` and visit it (Snorri, Hart, 2024).
4. **Servers:** put `SceneObj` in the Multiplayer module (Boggo, 2025). The downloader moves only `SceneObj` files, not prefabs or module data (poheniks, 2022, unverified); downloaded maps lack edit data (Kaseke, 2025). Custom meshes, textures or materials need a module players also have; native-only scenes do not (Le Profyteur, Lemmy1916, 2022-25).

Why editor Start Mission is unreliable:
- Since about 1.2.0 the dropdown is empty or crashes; TW has a forum bug thread (many, 2023-24). In e1.x it worked but spawned at origin, ignored `sp_player`, had no AI spawners, and the ground could vanish after returning (Muret TW, gkx, 2020).
- (disputed: it may have been unreliable well before 1.2 (Gotha, 2025).)
- After a crash, delete leftover `temp_scene_save` / `temp_mission_scene` folders (ARC4N0N, 2026, unverified).

Gotchas:
- No terrain means a crash on save, even for interiors; add a tiny hidden terrain (hunharibo, 2024).
- Do not save while a campaign map is open (Eisbar, 2024).
- Use a `SpawnPointDebug` entity to see what each scene type requires (Zedforen, wiki).

Wiki: https://docs.bannerlordmodding.lt/editor/editor_test_mode_mod/ - **already covered** for the mod; settlement and Custom Battle workflows and server notes are **new**.

---

## 6. Module setup and shipping

1. Make the module in the editor; set `SubModule.xml` versions. Clear DependedModules warnings by copying the block from a base module (Bolsonarido, 2021). Version lines can be edited or deleted to drop red marks (toy boat, 2021).
2. Exporting packs assets into one tpac in `AssetPackages` (ARC4N0N, 2026). Publishing is zipping the module (Garilla, 2022).
3. For release delete `Assets`, `AssetSources`, `RuntimeDataCache`, `GUI\SpriteParts`, `SceneEditData` (wiki); keep `SceneEditData` if others should edit.
4. For editing handovers ship `SceneObj`, `SceneEditData` and prefab XMLs; the kit version must match the game (switch in Steam) (ComprehendNubnes, Snorri, 2026).
5. Imported textures create three files (AssetSources copy, `*_tex.tpac`, RuntimeDataCache); deleting any breaks the material (greg, 2021, unverified). Terrain textures: no accents, spaces or subfolders, and use the "For terrain" flag (Byako, 2024-25).
6. 1.3+: Material Management crashed on capitalised names and vanilla textures in custom materials rendered black; re-import into your module (Eagle's NativeTextureExporter) and re-save. Fixed for lowercase in 1.3.10 (Snorri, Eagle, 2025).

Gotchas:
- Game and Kit must share one Steam `steamapps\common` path or the editor silently fails (toy boat, 2022; wiki).
- Stale assets can keep loading after deleting a module's asset folder; a fresh module fixed it (Kysaro, 2021, unverified).
- Reimporting native models like `intro_scene` can block editor launch until the tpac is deleted (Litauen, 2024, unverified).

Wiki: https://docs.bannerlordmodding.lt/modules/mod_release/ - **adds detail** (cleanup list covered; import triplet and 1.3 texture notes new).

---

## 7. Backups and corruption avoidance

1. Never save on exit: saving from the quit prompt crashed every time in e1.5. Use Save, then Exit and answer "No" (FoozleMcDoozle, NIN3, 2021; Hart, 2024).
2. Zip `SceneObj\<scene>` and `SceneEditData\<scene>` after every save (Hart, 2024). A scheduled task keeping 15 to 30 rolling copies works (FiefEvictionNotice, 2026).
3. Do not touch the editor during saves; they can take minutes, and hidden pop-ups look like hangs (Gotha, 2024; matthewster, 2026). Above 80% RAM, let the idle editor release memory first (npc99., wiki).
4. There is no autosave; `Backups` holds only copies from normal saves (FoozleMcDoozle, 2021).
5. After a crash, load the backup first (Tjens, 2020). If a temp `terrain_ed.bin` exists, rename the temp files and add an older `navmesh.bin`; otherwise delete (npc99., 2021).
6. Back up before every update: e1.5.7 broke older modules and forced updates deleted scenes (NIN3, 2021); updates overwrite modified XML and shaders (Snorri, 2024).
7. Navmesh saves crash often and the editor degrades the longer it runs (Lemmy1916, 2026).

Recovering a broken scene:
- Open `scene.xscene` in Notepad++. Bisect by commenting out halves of the entities; this found bad capture flags, an empty parent around tree_far objects, and a stray `references.txt` line (FoozleMcDoozle, Lemmy1916, 2022). Diffing works too (Litauen, 2026). Copy entities to a new scene to rescue them (npc99., 2020).
- Bad lighting bake: delete the scene's prt data (also in `ProgramData\...\prteditdata`) (Hart, Reynauld, 2025).
- Flat terrain with `rglBuffer::allocate(0) failed`: corrupt `SceneEditData`; restore from backup (Sjanten, 2026; wiki).
- Bulk find/replace in the xscene is legitimate (swap bricks, delete debris holders); back up first (FiefEvictionNotice, Bullero, 2025; Snorri, 2026).
- Crash logs: `C:\ProgramData\Mount and Blade II Bannerlord\logs`, overwritten each run; Better Exception Window, BUTR or dnSpy give method names (npc99., hunharibo, 2022-24).

Gotchas:
- Do not keep working on a flat mesh after corruption.
- Saves after heavy undo use crash most (npc99., 2021).

Wiki: https://docs.bannerlordmodding.lt/editor/editor/ - **adds detail** (corruption and save hangs covered; backup routine, bisecting, bake cleanup new).

---

## 8. Shader caches

- Deleting `SceneObj\<scene>\ShaderCache` cures some glitches (Muret TW, 2020). The SceneObj caches rebuild on save or in-game load; the ProgramData one later. The first open may crash, then works (Space_Pot8to, 2021, unverified).
- Delete CoreShaders and TerrainShaders in `C:\ProgramData\Mount and Blade II Bannerlord\Shaders` for black helpers, rainbow terrain, lost brush circles or stuck rendering (wiki; FoozleMcDoozle, 2022).
- Checkerboard layers after a save: save again, clear the cache, or save under a new name (Narce6, Bullero, 2022).
- `smooth_blend_amount="0.000"` breaks shader compile; set 1.000. "Missing shader from sack" for `pbr_terrain` is normal (Sly, HyperCharge, 2026).
- On-demand compile produced 15 to 25 GB bulk files; fixed in a later kit, raise pagefile 40 to 60 GB meanwhile (Sly, Dejan TW, 2026).

Wiki: https://docs.bannerlordmodding.lt/editor/editor/ - **adds detail** (ProgramData fix covered; per-scene cache and compile notes new).

---

## 9. Version compatibility

- Cross-version scene opening is unreliable (TW confirmed, Oct 2021, e1.6): rebuild terrain from height/material maps and copy `navmesh.bin` (Dejan TW, 2021).
- Kit and game versions must match (eugenz, 2023).
- 1.0: old scenes may lose materials (Le Profyteur, 2024, unverified). 1.2.x: updates overwrote modified XML and broke scenes saved in 1.2.4 (BlackRose, Snorri, 2023-24, unverified).
- 1.3: scenes with 1.3 assets will not load on 1.2.12 (errors, or crashes with new prefabs) (Snorri, 2026, unverified). Renamed physics shapes (`bo_composite_longbow_g` to `bo_longbow_g`) hung old scenes on load (Eagle, 2025, unverified).
- Scene levels must exist in SceneOptions before a visibility level is selectable (Exhorris, 2025, unverified).

Wiki: https://docs.bannerlordmodding.lt/editor/scene.xscene/ - **adds detail** (wiki lists 1.3 xscene entries only).

---

## 10. Performance

**Baseline**
- No hard entity or polygon cap; pixel polygon density matters (npc99., 2022-23). TW uses 16x16 nodes, at least 10 per side recommended (Dawa, 2023). Keep node vertices at 256x256 or lower (Swan, wiki).
- Editor fps is lower and unrepresentative; judge in game (Klassix, npc99., 2020-22). Hide icons/navmesh/helpers/shadows when profiling the editor (Bullero, 2022; wiki).
- Profile window: Ctrl+Alt+P, opened at the main menu before launching the editor (Dawa, 2023). Entity count: select all in the scene list; children of closed prefabs are not counted (npc99., 2024, unverified).

**Main offenders**
- Shadowed flora, grass, many flora kinds; road decals (a long road cost about 30% fps) (Bullero, Snorri, 2022-26); particles, especially collision, dense fog and distant VFX (set LOD distance, zero-intensity LOD1) (Klassix, 2025); water simulation.
- Lights: one radius-500 light instead of 50 ruined fps (beefbacon, 2020); overlap and envmap probes hurt most (Klassix, 2020). Baking removes static light cost; dynamic lights stay costly (Snorri, LizardWizard, 2024-25).
- Test by removal before blaming a feature; a logging DLL once caused choppy campaign movement (Carolina Warlord, 2022).

**Prefabs and entities**
- A prefab is no cheaper than loose entities. Prefabs with hundreds of children lose LOD/occlusion; detach them, keep under about 15 elements, or join into one LODed mesh (Le Profyteur, Reynauld, npc99., 2024).
- Buildings with 25 to 38k entities ran about 20 fps; about 100 per ship prefab is a friendly budget (LizardWizard, Hart, 2025).
- Draw calls matter more than faces (Hart, 2026, unverified). Strip physics from unreachable decor (HyperCharge, 2026). Spread objects instead of clumping; avoid thousands of tiny navmesh faces (FoozleMcDoozle, Dejan, 2021).

**LOD and culling**
- TW guide: occluders, LODs, no overlapping lights, no flora where nobody goes, terrain LOD multiplier 0 on the final save (the editor multiplier is editor-only) (Nordous, 2020).
- LODs are meshes with `.lod1` to `.lod6` suffixes exported together; entity LOD cannot be forced in game (npc99., 2022). Overriding a native asset minus early LODs pushes switches outward (npc99., 2024, unverified). Ctrl+M copies LOD 0 material settings (Reginald, 2025) (disputed: skinned items behave differently).
- Campaign-map props must be low poly; icon textures 2K, mission statics may be 4K (npc99., 2021-23).
- Small rocks vanish at distance; place distant cliff rocks beneath (FiefEvictionNotice, 2026, unverified).
- 1.3 bug: fences, planks, carpets, windows, tents culled around 100 m; the LOD setting works only in the editor. TW acknowledged a fix; workaround is a lighter variant (Snorri, Dejan TW, 2025-26).

**Occluders**
1. Terrain never occludes. Terrain draws with a depth-only shader, so occluders save CPU only (Muret TW, 2023; FiefEvictionNotice, 2026).
2. Put them inside big hills and cliffs on the line of sight; useless on gentle hills (LizardWizard, 2026).
3. Use `_occ_box`/occ_ prefabs or mesh Visibility parameters > Occluder Parameters. Walls and buildings occlude by default; big rocks do not.
4. About 80 to 100 on huge maps; hundreds are rasterised on the CPU and hurt (Samatko, 2024). Oversized ones make parts vanish (Boggo, 2025).
- (disputed: occluders may work in editor but not in game/MP; place below terrain or inside rocks (Le Profyteur, 2024).)

Gotcha: do not trust editor fps for final tuning.

Wiki: https://docs.bannerlordmodding.lt/editor/terrain/ - **adds detail** (wiki has node sizes; occluders, LODs and light budgets are new).

---

**Contributors:** FoozleMcDoozle, Muret (TW), Dejan (TW), npc99., Klassix, Le Profyteur, Lemmy1916, Snorri, Hart, LizardWizard, Sly, Gotha, Reynauld, Litauen, FiefEvictionNotice, Kazet, toy boat, Bolsonarido, Gingy, Eagle, Swan, Zedforen, Byako, Boggo, BlackRose, and others named inline.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/08-scene-setup-files-and-testing-sources.md](sources/08-scene-setup-files-and-testing-sources.md)** (170 of 173 linked). The links only work for members of the Mount & Blade Modding server.
