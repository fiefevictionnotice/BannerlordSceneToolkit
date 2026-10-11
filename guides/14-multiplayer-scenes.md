# 14 - Multiplayer Scenes

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - MP loads only Native + Multiplayer content. Anything from another module (custom prefabs, materials, even some DLC assets) is silently missing or crashes. Break every custom prefab before shipping.
> - Most "crash on team select / after warmup" reports are one cause: missing `spawn_visual`, or a spawn point missing its `attacker`/`defender` tag (or tagged on the prefab instead of the child).
> - Do not trust the editor test mode for MP. Host a local dedicated server instead, and copy a native MP map (e.g. `mp_skirmish_map_002f`) as your template.
> - The server needs only `SceneObj`; `SceneEditData` (terrain_ed.bin) is for editing. Missing it makes terrain black on some setups.
> - Hard limits exist: about 32,768 entities (prefab children count), a scene file of roughly 50-80 MB, and 4087 scripted props.

Version labels: "e1.x" = early access (2020-22), "1.x" = 2022 onward, taken from the finding's date.

---

## 1. What MP loads, and what that means for assets

- MP loads Native and Multiplayer module content only (1.x, 2026; krex, FiefEvictionNotice, Klassix).
- Unbroken custom prefabs/materials from other modules are silently missing or crash the scene. The server may load fine while clients crash.
- Nord assets work without DLC; WarSails assets do not (1.x, 2026).
- The in-game map downloader transfers only `SceneObj` scene files, not prefabs or other module data (poheniks, 2022). Imported textures/decals/meshes need a module players have installed (Boggo, Kaseke, 2025).

Steps to ship a standalone MP scene:
1. Save the scene in your own module, never in SandBoxCore/Native (Secretblaze123, Steinmur, 2025). An editor-generated module may be bugged; build the module folder by hand.
2. Break every custom prefab: right-click, "Break prefab", then save the scene twice (re-break after the first save). Drag-placing from Add Prefab keeps the link; Shift-drag does not (Kaseke, Hart, 2025).
3. Use only native meshes/materials, or ship a module that both server and clients load.
4. Test with no DLC loaded.

Gotchas:
- Unbroken prefabs can be missing with no error on a server (BannerSwordOld, Yaldabog, Snorri, 2025).
- If a scene crashes, break all prefabs first (FiefEvictionNotice, 2026).
- Custom shaders/materials render pitch black on dedicated servers unless fully custom textures (Birke, Alphα, Snorri, 2026).
- Wiki: new (the wiki has no MP asset-loading page; the scene-size note under editor/scenes only partly overlaps).

## 2. Required entities per game mode

| Mode | Required | Notes |
|---|---|---|
| TDM | `mp_spawnpoint` arrows (attacker/defender both used), `spawn_visual`, camera, soft borders | No team-specific spawns. Needs no flags. |
| Skirmish | `skirmish_start_spawn` (one attacker + one defender child), respawn spawns, `flag_pole_sergeant` flags, `spawn_visual`, camera, soft borders | Without a respawn you respawn at the start spawn. No flags freezes the map after warmup. |
| Captain | As Skirmish, plus navmesh (bots) | Shares spawns with Battle/Skirmish. Early layout: 3 flags, 2 spawns (e1.x, 2021, unverified). |
| Battle | Two `skirmish_start_spawn`, flags A/B/C, `spawn_visual`, spawn camera, 4 `border_soft` | Attacker/defender tags on the spawn points, not the prefab. Cannot be tested alone. |
| Siege | Per-team parent spawn entity with `sp_zone_N` tags, all default flags, siege items | See section 5. |
| Duel | All 16 arena areas | One arena reportedly crashes after faction select (unverified). |

Reference maps: `mp_skirmish_map_002f`, `mp_sergeant_map_010` (FoozleMcDoozle, 2022).

Spawn sharing: battle, skirmish and captain share spawns; TDM, duel and siege differ (Reginald, Sheepbitter, 2023).

Minimum viable MP battle scene (FiefEvictionNotice, 2026): mp spawn camera (always looks straight down), spawn visual, attacker/defender spawns not inside prefabs (or all-lowercase names), no SP `sp_` entities. One map can support several modes.

Gotchas:
- Env probe also needed (Steinmur, Reynauld, 2024-25). Camera height is limited by `camera_height_limiter` for spectators only.
- Capitalized MP spawnpoint names inside prefabs can crash after warmup (single report, 2026).
- Wiki: adds detail (the test-mode-mod page covers Skirmish flag warnings only).

## 3. Spawn zones, tags and parenting

1. Place a spawn-zone prefab. Start from `skirmish_start_spawn` and replace its `starting` tag.
2. Give each child `mp_spawnpoint` the `spawnpoint` tag plus `attacker` or `defender`.
3. Assign children by parenting them under the zone.
4. Tag the zone `sp_zone_0` ... `sp_zone_6`. Zone N activates once N flags are captured (official docs excerpt, 2022).
5. One parent per tag. Several tags may sit on one zone (2022 docs; 2025 report says one parent per tag - ICEMAN1779, Sheepbitter).
6. Use multiple groups so attackers do not trickle in.

Gotchas:
- Missing `attacker` tag on one spawn is a known crash cause (2025).
- Reinforcement spawns too far from the main spawn fall back to the initial point (Capt Cole, 2024, unverified).
- `exclude_mounted` tags on spawn points steer cavalry (Lord Eastwood, 2024, unverified).
- Both teams on one side breaks spawns (SP battles, FoozleMcDoozle, e1.x).
- Wiki: new.

## 4. Flags and capture points

- Use `flag_pole_sergeant` for capture points (FoozleMcDoozle, 2022).
- Domination-type modes need flags; Battle/Skirmish need A/B/C.
- Siege flags cannot be added beyond default; duplicates crash or remove both (Lord Eastwood, Justin, 2023; Secretblaze123, 2024). Flags cannot be sequence-gated in native.
- Flags with custom pictures need a different approach (FoozleMcDoozle, 2023).
- Custom scripted moving flags in Captain need server sync (HCT, 2024, unverified).

## 5. Siege-specific MP setup

Steps:
1. Each team gets a parent `skirmish_start_spawn`-style entity; parent `mp_spawnpoint` children to it.
2. Tag zones `sp_zone`, `sp_zone_1`, `sp_zone_2`... each matching one flag capture (ICEMAN1779, 2025). Spawn zones drive gradual siege spawns; study native MP sieges (Zanelious, 2026, unverified).
3. Keep every default flag.
4. Use MP spawner prefabs (e.g. `ballista_a_spawner_MP`) for engines.

Gotchas:
- Siege spawner entities crash outside siege mode; raw siege-engine prefabs are fine. Siege-tower spawners can use basic spawners with your prefab name set in the script (Snorri, Reginald, 2026). Older "siege-only" docs are outdated: engine spawners work in TDM with the MP prefabs (Victxrcabx, Boggo, 2024). Rogue siege gates crash starts.
- Players can man engines in MP battle mode, AI cannot (Sheepbitter, hunharibo, 2024).
- Destructibles: docs say siege-only, yet native non-siege maps use them (disputed: poheniks/FoozleMcDoozle, 2022). Fine in battle (2026). Custom destructible walls: only wall-breach/barricade filler works (Snorri, 2024).
- Pre-placed ladders: mesh, physics, `ladder` body flag, tag `do_not_optimize`; navmesh it for AI. Tilting 10-15 degrees from vertical makes it unclimbable (Klassix, poheniks, 2022).
- Saving MP siege prefabs (e.g. `mangonel_a_spawnerMP`) into a new prefab can crash (k1ng, Bullero, 2022, unverified).
- Shops/markets crash MP unless their scripts are removed.
- 1.3+: ranged siege weapons must be destructible or the map crashes on load; MP engines stopped working in SP siege scenes (Sheepbitter, Snorri, 2026).
- Reusing `castle_door_sally` works as an openable door; lowering sally door HP left them dead but standing (Reynauld, 2023, unverified).
- Wiki: adds detail (editor/siege_scenes and guides/troubleshooting_siege_scene cover SP only).

## 6. Borders and hard-border crashes

- `border_soft` is the red border starting the mission-exit counter (about 10 s return timer); `border_hard` is a physical barrier (Muret, TW, e1.x; Reynauld, 2024).
- Use four `border_soft` at the corners for MP battle. Hard borders are not needed and can crash; they are buggy walls in non-siege maps (Exhorris, Reynauld, 2024-25).
- A barrier-builder path just outside `border_soft` makes a nicer boundary (unverified).
- If border_prt probes fail to regenerate on a corrupt save, move one `border_soft` after using the terrain tool once (single report).
- Moved borders may show the old position in game (single report, 2021).
- Toggle physics view: Left Ctrl + Left Alt + O.

## 7. Dedicated-server scene deployment

1. Put scene files in `SceneObj\<scene>` of the Multiplayer module or your own module (krex, Reynauld, 2024).
2. Remove duplicate copies in other folders; two copies broke map recognition (Yaco, 2025).
3. Add a strings xml so the map name does not show as unknown (scene_strings-style).
4. Optionally add map pictures in a client-side module (thumbnails/vote images need it; Le Profyteur, 2024).
5. Match client and server files: scene versioning attributes exist for compatibility checks (patch notes, 2022); version mismatches caused server crashes in Dec 2023 (poheniks, unverified).
6. Publishing options: client, dedicated, editor. Omit "editor" and others cannot edit; tick "dedicated server" for physics (Haterade, ARC4N0N, 2026).

Gotchas:
- Seasons masks apply client-side only; server entities need manual removal (2026).
- Parent `visible_only_when_editing` is inherited by children after a 2026 update, hiding assets in MP; make the empty visible (unverified).
- Child positions below about -100 X broke MP compression; parent entities at 0,0,0 (Birke, 2025, unverified).
- Terrain textures at quality "low" crash the join for custom textures (Reynauld, 2024).
- Server error "get_missing_shaders_path / Invalid Program" means a map problem, no fix known (2025).
- Downloaded maps arrive without edit data.
- Wiki: new.

## 8. Testing an MP scene locally

1. Host a local dedicated server (or a VPS). The editor test mission is unreliable (Trench, Secretblaze123, 2024; "never trust test mode", 2023).
2. For solo tests add `MinNumberOfPlayersForMatchStart 0` (Sheepbitter, 2023). MapTimeLimit/WarmupTimeLimit are in minutes; RespawnPeriod/RoundTimeLimit in seconds. A high min-players value stalls spawning.
3. Alternative: Gotha's test-mode mod (2024). Tags `sp_play`/`spawnpoint_player` choose the spawn, `enforce_troop_spawn` names a class; key O times travel, P shows flag areas. Unblock the DLL.
4. For diagnosing crashes: search the xscene for `default_empty`, copy and delete terrain/flora, break prefabs, halve entities repeatedly (FiefEvictionNotice, 2026).

Gotchas:
- Bots in MP only help with navmesh and correct spawners.
- Wiki: already covered (editor_test_mode_mod) for the mod; new for server testing.

## 9. Performance and limits

- Entity ceiling about 32,768, prefab children included; the server loads scenes that crash clients (AllieAdler, d&h, 2026, unverified).
- Scene file above about 50-80 MB crashes. Move content into prefabs; edit the xscene text to remove entities to recover (2026).
- More than 4087 scripted props prevents spawning (Sheepbitter, 2022, unverified).
- Very large maps hurt MP compression (14k scripted objects reported on one; unverified).
- No minimum scene size (poheniks, 2023).
- Crowd spam (many crow prefabs) crashed a server (Trench, 2024).
- Avoid big "indoor" rooms, parented `merchant_stand_market`, animation-point children and sandbox prefabs (2026).
- Players mostly disable shadows, so very dark covered areas hurt play; lower contrast via lighting or bake (Kaseke, 2025).
- Visuals that fail in MP: vertex painting, road decals after warmup (acknowledged 1.8+ bug, Dejan, TW), ambient sounds, moving physics bodies (desync), DoF/PRT cubemap settings on dedicated (single report). Animated NPCs via `skinned_mesh_viewer` work; the arena crowd does not.
- Editor cannot test MP performance. Vanilla fields converted to MP take about 15-60 min each (Daggfinnr, 2023).
- Wiki: adds detail.

## 10. Converting SP to MP, battle spawns (SP/campaign contrast)

- Convert: replace `sp_` spawns with skirmish spawns, delete flags, add `spawn_visual`, copy terrain (Sheepbitter, 2023).
- Campaign/custom battle scenes use `spawn_path_01...` (long straight line, scales with army size) or `sp_battle_set`, plus borders and navmesh (Zedforen, Sly, 2025). Names must follow the convention; whether multiple paths are chosen randomly is disputed (disputed: one tester saw only path 01 used, TW staff say random, e1.x 2020). 1.7.2 added three reinforcement paths.

---

Contributors: NIN3, Muret (TW), FoozleMcDoozle, Suspicious Man, Klassix, poheniks, Sheepbitter, Reginald, Secretblaze123, Reynauld, Victxrcabx, Boggo, Lord Eastwood, krex, FiefEvictionNotice, Snorri, Kaseke, Steinmur, ICEMAN1779, Errayn, Birke, Gotha, Haterade, Trench, Le Profyteur, Dejan (TW), Bullero, AllieAdler, Yaldabog, Hart, BannerSwordOld, Zedforen.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/14-multiplayer-scenes-sources.md](sources/14-multiplayer-scenes-sources.md)** (70 of 73 linked). The links only work for members of the Mount & Blade Modding server.
