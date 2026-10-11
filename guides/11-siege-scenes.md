# Building Working Siege Scenes (Bannerlord)

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> 1. **Copy a working vanilla siege scene (or Failware's Minimal Siege Scene) and edit it.** Building from scratch is the main cause of "crash on Besiege" threads; tags, paths, layers and spawners are all hidden requirements. (Snorri, Capt Cole, hunharibo, 2024)
> 2. **Navmesh IDs tell the AI how to attack:** 1 inside the walls, 0 outside, plus unique IDs on the gate, ladders and wall segments that you also type into the matching script. Wrong IDs mean units stand around or stop after a gate falls. (Hart, Snorri, 2024-25)
> 3. **Never rename, prefab, or non-uniformly scale gates, rams, towers, ladders, or wall segments.** Renames break code lookups; non-uniform scale on physics objects crashes. (Byako, Hart, Klassix, 2024-26)
> 4. **A siege needs all three upgrade levels and a full engine set** (a level-1-only scene crashes; defenders need 4 engines, plus ram and tower spots), and each level needs its own navmesh for siege and for civilian mode. (Le Profyteur 2024, Hart 2025-26)
> 5. **Siegeable walls = one parent with `WallSegment` and two children, `solid_child` and `broken_child`, plus a `debris_holder` per destructible merlon.** Missing any of these is a load or besiege crash. (Snorri, Carolina Warlord, Exhorris, 2025-26)

Version labels: **e1.x** = early access (2020 to Oct 2022), **1.x** = after the Oct 2022 renumbering. Dates are the date of the finding.

---

## 1. Start from something that works

1. Copy a vanilla siege scene (reference: `khuzait_castle_002`) or download Failware's "Minimal Siege Scene" (Nexus mod 3930) and use it as your base. (FoozleMcDoozle 2020; Failware, vLove 2022)
2. Move walls, helpers and engines instead of rebuilding them. This saves debugging scripts, tags, siege paths and layers. (Capt Cole, Snorri, 2024)
3. Build one wall segment, test in game, then copy it. (Snorri, Hart, 2026)
4. Test through Custom Battle as a siege scene (the entry needs `is_siege_map=true` in `custom_battle_scenes.xml`). Do not drop files into base-game module folders. (Macbeth of Gondor 2020; fink, Sly 2026)

Build order that works: flat map, 4 towers, one wall per side, one outer and one inner gate, paths for towers and ram, ladders, navmesh, scripts and tags, engines for both sides. Test with ladders only, then add the ram, then towers, then engines one by one; do breaches last because their navmesh is the most work. (wiki, Snorri)

Gotchas
- Do not make a siege your first scene; do a few battle scenes first. (FoozleMcDoozle, NIN3, Snorri, 2020-2026)
- Copying a vanilla castle and renaming only some references can play fine but crash when besieged (siege tower missing). (Mysterious Adversary, e1.x 2022; unresolved)
- Editor test mode (SimpleMountedPlayer) cannot operate gates, towers, ladders, ballistas or rock piles. Use Custom Battle, Gotha's tester, or register the scene as a keep and visit it in the real game. (fink, Snorri, 2026)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - already covered (build order); Minimal Siege Scene and test-mode limits are new.

---

## 2. Levels, visibility, and the three upgrade levels

1. Layers are Level_1, Level_2, Level_3, Siege, Civilian, Sally, Base. Siege-scene visibility: Base off, Level 1-3 on, Siege on, Civilian off, Sally on for sally missions. (wiki)
2. Build all three levels even if they are identical. A level-1-only siege scene crashes. (Le Profyteur, 1.x 2024-01)
3. Engine placeholders do not need duplicating per level: place four and assign them to all three levels. (Le Profyteur, 2024)
4. Vanilla uses a `SceneLeveler` script on an empty entity; set visibility per level on children, or spawn wall prefabs under one parent and set that parent's visibility. (FoozleMcDoozle, Space_Pot8to, 2021)
5. Settlement entities need `banner_pos` on all 3 levels; wall empties' names (wall_1/2/3 with level suffixes) matter because code looks them up. (Carolina Warlord, hunharibo, 2023)
6. `attacker_boundary` / `defender_boundary` visibility per level does not show in the editor but works in game. (Snorri, 2024)

Gotchas
- Siege-equipment deployment points with wrong visibility levels crash the scene. (wiki; JOJABO 2026)
- Full crafting stations (forge/smithy) with scripts and tags crashed levels 2-3; vanilla uses bare versions without spawn points. (Snorri, 2024-01; single-report with confirmed fix)
- Since 1.3, a stale "base" visibility tag on siege equipment can break civilian mode. (Hart, Kysaro, 2026-05)
- Multi-stage sieges (several wall rings) are not supported; 3 gate layers ran but without AI logic. (FoozleMcDoozle 2021, Failware 2022)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - already covered (levels, visibility); SceneLeveler and the 3-level crash are new.

---

## 3. Required entities checklist

| Item | Requirement | Notes / source |
|---|---|---|
| Gates | One **outer** gate (tied to the ram) and at least one **inner** gate, CastleGate script with navmesh face IDs | Extra outer gates ignored, several inner OK (Hart 2025). An extra gate gets no AI reaction (Hippopo 2024, unverified) |
| Ram | Spawner plus path; mission expects it to exist | "Unwalled towns" are impossible: engine always spawns ram and towers (Hart 2024) |
| Towers | At least 2 spawners, each with unique tag, own path and spawn point | Exhorris, Swan 2024 |
| Ladders | Prefab, never scaled | Section 7 |
| Attacker engine slots | 4, within a deployment point's range | hunharibo 2023 |
| Defender engine slots | **4** (untick `visible` to fake "none") | hunharibo, Hart 2025 |
| Deployment points | For each team, in range of engines; others need `deploy` tag (default `no_deploy`) | NIN3 2020 |
| Wall segments | `WallSegment` parent, max 2 per scene, `solid_child` + `broken_child` | Section 5 |
| Breach walls | Two per level, children intact | Section 5 |
| `debris_holder` | Empty entity per destructible merlon | Section 5 |
| Camera | One per team; hard requirement | wiki; cameras can vanish or move after save |
| Boundaries | Soft borders; hard borders redundant ("not defined" warning harmless) | Snorri 2026 |
| `sp_battle_set`, `sp_player` | SP sieges; a buggy battle set crashes `TacticBreachWalls` | Le Profyteur, hunharibo 2024 |
| Spawn arrows | Attackers on navmesh ID 0, defenders on ID 1 | Failware 2022 |
| Archer positions | `archer_position` / `archer_position_attacker` prefabs; type `archer_point` tag fresh | Space_Pot8to 2021; Carolina Warlord 2024 |
| Paths | One per tower and ram; last node is the stop point | Section 4 |
| Flee points | In the 2020 setup list | NIN3 2020 |
| Navmesh | Siege set and civilian set per level | Section 6 |

Equipment uses `spawner` entities, not design entities. (Ladnar, 2022)

Gotchas
- Engines are individually optional for the AI, but the mission expects ladders and the ram; omitting facilities crashes. (Space_Pot8to, GourmetBean, e1.x 2022)
- Place the "debug" / `SpawnPointDebugView` entity and run its script to list what the scene type requires (limits are soft). (Hart, Snorri, 2024-25)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - adds detail (4 defender engines, gate counts, WallSegment rules are new).

---

## 4. Paths, rams, towers

1. Make a path per siege tower and ram. The engine snaps to the **last node** (position, Z rotation, terrain alignment). Move or rotate that node, not the entity. (Failware, e1.x 2022-04)
2. Path points must carry siege-level visibility; a missing level on path points caused an assertion crash. (Exhorris, Swan, 2024-10)
3. Set path interpolation to "Quadratic Hermite (Siege path)"; undocumented. (unverified; [TAOM]Shogun 2026)
4. Tag merlons that should drop when the tower deploys with `tower_merlon`; ramps may alternatively rest on top of merlons. Optional `AddOnDeployTag` for ramps and attached clutter (entity must be invisible in the editor). (Exhorris, Snorri, 2024-26)
5. If AI wait stuck at a tower, delete `wait_pos_ground`. (unverified; Klassix 2022)
6. Towers use runtime dynamic navmesh prefabs (connector and blocker IDs, larger connector faces). The ramp must line up with wall navmesh, and the tower should not be turned much. (hunharibo 2022; Klassix, Snorri 2024-25)

Gotchas
- Do not copy, paste or prefab siege towers and ladders; set them up manually in each scene. Saving siegable walls with their navmesh IDs as a prefab is fine. (Hart, 1.x 2026-06)
- With several towers, AI often abandons the rest after the first reaches the wall. (unverified; Snorri, Klassix 2024)
- Only the 2 base towers seem to work reliably; large ramparts make towers unusable. (disputed: tower counts differ between reports; Snorri, Hart, 2026)
- Engines in one lane clip and jam; send one at a time through bottlenecks. (Hart, 2025)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - adds detail (last-node rule, path interpolation, tower_merlon are new).

---

## 5. Walls, breaches, destructibles

1. Siegeable wall: **one parent** entity with `WallSegment` (at most 2 in a scene), `DefenseSide` Left/Middle/Right, tag `wall_segment_left` / `wall_segment_right`; children `solid_child` (used by ladders and towers) and `broken_child` (breach). Search `s:WallSegment` to verify. (Byako, Hart, 1.x 2025-02; Snorri, Jarl 2026)
2. Custom walls need a `debris_holder` empty per destructible merlon with a `DestructableComponent`, or the scene crashes. (Carolina Warlord, Exhorris, 2025-05)
3. Destructible prefabs: put the Destructible Component on the parent only. Children carrying it take damage independently. (Snorri, 2024)
4. "Besiege" crash on a custom campaign map: each settlement prefab has two breach walls per level; compare their tags, scripts and children with a vanilla settlement, or copy a vanilla settlement in. (hunharibo, Swan, et al, 2024-08)
5. Custom rubble model (Sturgian example): make a prefab from `mi_stu_wall_1s_collapsed` and replace the "broken" child of the breach prefab for each wall and level. (unverified; Space_Pot8to 2022)
6. Pre-destroy one merlon: shift-click it, press Delete. (unverified; CaptainFRACAS 2023)
7. Breach/ditch filler: tag the object with the name you enter in the siege-engine script, hidden at start. AI often ignores it, and the wiki notes vanilla never uses it. (Snorri, Exhorris, 2026)
8. Archer positions vanish when a destructible within 3 m is destroyed; parenting does nothing. (unverified; Failware reading code, 2022)

Gotchas
- Deleting a debris holder or destruction phase, a wrong script on a merlon, mesh_bender on walls, or renaming a spawn point can crash. Save under a new name and delete segments to bisect. (Snorri, 2024-05)
- Destructible HP must end in 99 (disputed: Reynauld 2024 says so; Snorri notes vanilla uses round numbers). Maximum five destruction states.
- `DestructableComponent` is sealed; you cannot extend it. Stretching mission walls breaks them. (Failware, Byako)
- MP: only the breach/barricade filler approach works; `solid_child` / `broken_child` are SP-siege specific. (Snorri, Illu, 2024)
- Vanilla merlons are too low to cover defenders; build taller ones. (Snorri 2025; wiki agrees)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - partly covered (HP, merlons); the WallSegment, children and debris_holder rules are new.

---

## 6. Navmesh for sieges

1. IDs: **1 inside the settlement and walls, 0 outside.** Without them troops cannot deploy. (wiki; Failware, Hart, Snorri)
2. Give gates, ladders and each wall segment their own face ID: press 3 for face mode, set the ID in the Inspector, then enter the same ID in the entity's script. (Ladnar, 1.x 2023)
3. Keep separate navmesh per level and mode: 6 layers (3 levels x siege/civilian) is the vanilla pattern. An edge connects to only one face, so each layer needs its own copied base. Back up before pasting; paste can corrupt layers. (Omnissiah_Priest, Snorri, 2023-24)
4. Breach without rubble: two patches (one each side of the wall) with a unique ID, set on the wall parent only. A simplified solid/broken ID pair worked in 20+ battles. (Hart, Exhorris 2024; Omnissiah_Priest 2024, single-report)
5. Navmesh under defender archer positions: ID 1, with the `TacticalPosition` script. (Le Profyteur, 2023)
6. Hand-build everything inside and near walls; autogenerate open ground only. (Snorri, 2024)
7. Ladder navmesh: bidirectional and welded to the neighbours; build by extrusion; directional faces need at most 2 connections. Put navmesh slightly above the ladder end. (NIN3 2020; Space_Pot8to 2022; wiki)
8. Face cost adds that many metres to a path: about 50 on ladder faces makes them non-preferred while the gate is open; above ~2 on large areas is too much. (TW tips, 1.x 2022-11)
9. Campaign siege volume box (white ring): if on impassable navmesh the siege can crash. (unverified; FoozleMcDoozle, npc99., 2021-22)

Gotchas
- Siege entities with no navmesh in the scene can crash the editor, even for MP maps. (Snorri, 2023-24)
- Missing gate IDs leave troops stopped after the gate is destroyed. (Hart, 2025)
- Early IDs (door 501, ladders 522/523 or 722/723) varied per scene; copy from your base scene. (unverified; CaptainFRACAS, e1.x 2021)
- Defenders bunch at a breach if the navmesh over debris is not connected. (Narce6, safran, 2022)
- Overlapping navmeshes (tower door vs wall) do not connect; add a connector face. (Klassix, 2023)

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ - partly covered (IDs 0/1, 6 layers, ladder tips); per-asset IDs, path costs and breach pairs are new.

---

## 7. Gates, ladders, and AI behaviour

1. Gates: do not edit the gate prefab's middle/wait points (crash on save). Put the outer gate's `middle_pos` behind the inner gate or attackers hang at the gate. Defender positions go behind the innermost gate on ID 1. (Kysaro, Hart, Exhorris, Revand, 2024-26)
2. Open-gate start needs an interactable gate in open state; a non-mission gate is decoration. (Hart, 2025)
3. Ladders: never scale. Tilt at least 12 degrees (16+ better) in the 2025 report, while the wiki says ladders should face walls nearly flat and MP pre-deployed ladders fail beyond roughly 10-15 degrees from vertical (disputed: sources describe the angle differently; test in game). Vanilla uses 4 per side. (Failware 2022; Hart 2025; wiki)
4. A target-wall tag such as `level_1_left` sets where AI waits (`mid_pos`, `wait_pos`). The big arrow on a wall marks where defender infantry stand when a tower arrives. (Hart 2025; Macbeth of Gondor, e1.x 2020)
5. Archer reach: defenders about 40 m, attackers about 60 m (unverified; Hart 2025). AI puts archers into few groups; singular positions stay mostly empty. (Snorri 2026)
6. Place 4 defender mangonels with clear sight of the enemy; trebuchets have the shortest range, so line up attacker engines to it. Range follows missile speed, not a script value. (wiki; Errayn 2023, unverified)
7. Defender AI uses only the closest tactical position to its spawn and cannot hold two choke points. (unverified; Capt Cole 2026)

Gotchas
- AI not attacking the inner gate (also in vanilla since a 2024 patch): spawner just behind the gate, the gate script's one-way navmesh option, or barriers. (Snorri, Swan, 2024-10)
- More than 4 ladders per side work but are crash-prone, and defenders use one defence point per side. (Hart 2025)
- Ladder counts: 3-4 close together is typical advice; disputed: Hart saw AI loop on dropped ladders with 3 and used 2 (2026).
- Invisible barriers under every non-solid stair and around props stop AI falling. (wiki)
- Ladder height clamps near -11.54 (e1.x? 2022, toy boat); for >6 m chain duplicate ladders. (Reynauld 2024)
- Ballista speed above ~200 (default 170) crashes; "destructible only by stone" must be off for ballistas to damage. (unverified; Annana 2024)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - adds detail (ladder angle, engines and barriers covered; gate and tactical-position rules are new).

---

## 8. Scale and the non-orthogonal frame error

1. Scale uniformly (B key or equal XYZ) and scale the parent, not the children. Non-uniform scale on gates, sally doors or scripted walls crashed. (Klassix, 1.x 2024-01)
2. The message "Entity with physics shapes do not support non-orthogonal frames" comes from stretched or rotated parents whose children carry physics (also multi-select scaling, mesh_bender curves, mirrored -1 scale). Fixes: remove the physics shape, detach and nudge, or copy Transform Local into a freshly made copy. It often returns after reload. (Hart, Kaseke, 2025)
3. Doors need equal X/Y/Z scale or they stretch when opened; sally doors cannot be resized. (Secretblaze123, Klassix, 2024)
4. Never scale ladders. (Failware, 2022)

Gotchas
- Severity is disputed: Hart and FiefEvictionNotice (2026) call the warnings harmless, while LizardWizard and Klassix (2023-24) saw test crashes. Treat them as real on gates, ladders, towers, walls and merlons; clear with "Check for problems".
- First reported in e1.x (2021) on `bo_siege_engine`, so it is old behaviour.

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - new.

---

## 9. Crash on besiege: diagnosis table

| Symptom | Likely cause | Fix | Source |
|---|---|---|---|
| Crash at Start or load | Missing gate script, wall segment tag, or engine slot | Compare to vanilla; verify tags and `is_siege_map=true` | NIN3 2020; Space_Pot8to 2022 |
| Crash on "Besiege" click, custom campaign map | Breach wall lost its children | Copy vanilla settlement; recheck both breaches per level | hunharibo, 2024-08 |
| Crash only on levels 2-3 | Full crafting stations, or missing level | Bare versions; all 3 levels present | Snorri, Le Profyteur 2024 |
| `ArrangeDestructedMeshes` crash | Wall segment tag missing | Restore the tag | wiki crashes page |
| `SiegeTowerSpawner` crash | Custom `name=` on the spawner | Remove the name | wiki crashes page |
| `TacticBreachWalls` crash | Buggy `sp_battle_set` or breach points | Default set positions; check breach points | Le Profyteur, hunharibo 2024 |
| Crash after 1.3+ upgrade | Ranged siege weapons not destructible; MP engines in SP scene; camera or placeholder sides | Make engines destructible; use SP engines | Sheepbitter, Snorri 2026 |
| Crash after editing a prefab part | Debris holder or phase deleted; renamed spawn point | Restore; bisect | Snorri 2024 |
| Crash on save | Edited gate middle/wait points | Revert | Kysaro 2026 |
| Crash with MP prefab | `mangonel_a_spawnerMP` saved into a new prefab (unverified) | Keep MP prefabs raw | k1ng 2022 |
| Crash outside siege mode | Siege spawner entities in a non-siege scene | Use raw engine prefabs | Snorri 2026 |
| Editor crash on placing ladders | No navmesh in scene | Add simple navmesh first | Snorri 2023-24 |
| Works only at noon/midnight | Custom textures on keep/stairs (unverified) | Revert to native | Ladnar 2022 |
| Crash with odd time of day | disputed: locked time in code vs object counts | Test with default time | Carolina Warlord, hunharibo 2023 |
| Crash only in field battle | Gates, ladders, rock piles, catapults in a non-siege battle | Test as siege | Horris 2024 |
| Crash with rock piles | Misconfigured piles (unverified) | Remove, re-add | Hart 2025 |

Gotchas
- The problem finder passing does not prove correctness. (Hart, 2025)
- Tag typos are the classic cause: `defender_archer` did not work, a freshly typed `archer_point` did. (Carolina Warlord, 2024)
- A debugger or dnSpy finds easy bugs; the stack trace names the failing script. (wiki)

Wiki: https://docs.bannerlordmodding.lt/modding/crashes/ - adds detail (2 of 16 rows covered).

---

## 10. Related MP notes

- Spawn zones use tags `sp_zone_0` to `sp_zone_6` (zone N active after N flags); children need `spawnpoint` plus `attacker`/`defender`; one parent per tag. All flags are required and cannot be added beyond default. (docs excerpt 2022; ICEMAN1779 2025)
- `_MP` spawner prefabs (e.g. `ballista_a_spawner_MP`) work in TDM; players can man engines, AI cannot; mangonel is stationary. (Victxrcabx, Boggo 2024; Sheepbitter)
- Bots do not support MP siege. (FoozleMcDoozle, e1.x 2020)

Wiki: https://docs.bannerlordmodding.lt/editor/siege_scenes/ - new.

---

Contributors: NIN3, Kipsta, WatchOutPoison, FoozleMcDoozle, Macbeth of Gondor, CaptainFRACAS, Space_Pot8to, Dejan (TW), Klassix, Failware, vLove, Embo, hunharibo, GourmetBean, Ladnar, Le Profyteur, Carolina Warlord, Snorri, Hart, Exhorris, Swan, Byako, Revand, Kysaro, Reynauld, Omnissiah_Priest, Capt Cole, Sheepbitter, JOJABO, Jarl, [TAOM]Shogun, Illu, Errayn, Annana, toy boat, poheniks, Narce6, LizardWizard, Secretblaze123, fink, Sly, Horris, ICEMAN1779, Victxrcabx, Boggo, Gotha, FiefEvictionNotice, Kaseke.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/11-siege-scenes-sources.md](sources/11-siege-scenes-sources.md)** (128 of 129 linked). The links only work for members of the Mount & Blade Modding server.
