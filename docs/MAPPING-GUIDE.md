# Bannerlord MP mapping guide

A checklist and reference list for building multiplayer scenes in the Bannerlord Modding Kit, from
the notes FiefEvictionNotice [BLCC] keeps for Calradic Campaign mappers. It is about scene
requirements in general, not about this toolkit; where the toolkit can check or fix something for
you, that is noted.

The Scene Analyzer (F7) checks most of the multiplayer requirements below (spawn visual, camera
start position, soft borders, envmap, flags, climbable civilian ladders, siege-only destructibles,
unbroken custom prefabs) and offers one-click fixes for several of them.

## Native scenes worth studying

Opening a Native scene that has its scene edit data is the quickest way to see how TaleWorlds set
something up. Scenes without scene edit data open with completely flat terrain, so stick to the ones
below. You typically should not overwrite a Native scene under its original name, but you can open
it and look around safely.

**Multiplayer**

| Mode | Scene | Name in game |
|---|---|---|
| Battle | `mp_battle_map_002` | Osrac Insurrection |
| Captain | `mp_sergeant_map_008` | Druimmor Forest |
| Skirmish | `mp_skirmish_map_002f` | Town Outskirts |
| TDM | `mp_tdm_map_001` | Harbour of Ovsk, Winter |
| Siege | `mp_siege_map_003` | Skala Landing |

**Singleplayer**

| Kind | Scenes |
|---|---|
| Villages | `empire_village_003`, `khuzait_village_g`, `battania_village_k`, `sturgia_village_l` |
| Castles / towns | `khuzait_castle_002`, `sturgia_town_b` |
| Interiors | `empire_castle_keep_a_l3_interior`, `empire_house_c_tavern_a`, `empire_dungeon_a` |
| Other | `battle_terrain_v`, `arena_empire_a`, `sea_bandit_a`, `mountain_hideout_002`, `Main_map` |

## Multiplayer scene requirements (every MP map)

- [ ] **`spawn_visual`**: what players see during class selection. Ideally not reachable from the
      playable area; if it is, people will run around it during warmup just to be a nuisance.
- [ ] **`mp_camera_start_pos`**: always faces straight down (an engine bug, as far as anyone can
      tell). Place it above some point of interest.
- [ ] **Four or more `border_soft`** around the play area. Toggle their visibility with the
      "border" option in the visibility window.
- [ ] **Only Native prefabs.** Right-click and "Break" any custom prefab. Name your own prefabs with
      a convention such as `YourName_Culture_PrefabName` so you can filter to them easily.
- [ ] **No siege-only destructibles.**
- [ ] **No climbable civilian ladders.** Some ladders can work, but civilian ladders are potentially
      problematic.
- [ ] **Scene files in `SceneObj` and `SceneEditData`.** Do not rename them in File Explorer;
      generally rename only from inside the Modding Kit.

Soft requirement:

- [ ] **One global `envmap_prop`** (exactly one, with "is global" checked). It affects lighting, so
      do not bury it underground or inside a wall.

## Groupfighting maps

- [ ] **Starting spawns for each team.** Either use the skirmish start spawn prefab (one with
      individual spawns tagged attacker, one with individual spawns tagged defender) to keep spawns in
      a prefab, or don't. Some people always use that prefab for battle maps; placing a few
      `mp_spawnpoint_attacker` and `mp_spawnpoint_defender` points by hand is enough to make a scene
      usable for battle.
- [ ] **Flags A, B and C.** Still needed, because it is still battle mode, but on a groupfighting
      map they can sit far above or below the ground where nobody can reach them. If you do that, do
      not line them up vertically with the playable area.

## Battle maps

- [ ] **An attacker spawn area and a defender spawn area.** Use a skirmish start prefab or place
      spawns individually, but do not mix attacker and defender spawns in the same area; they belong
      in separate regions of the map. If you use the skirmish spawn prefab, try toggling the
      "snap to terrain" setting.
- [ ] **Flags A, B and C** in the playable area.

## Testing (any map type)

- `sp_play`: a tag you put on a spawn point to make it active for Gotha's scene editor tester
  (a separate mod, shared in the `bl-scenes` Discord channel). Gotha's tester still works better than
  the native tester for siege.
- `spawnpoint_player_test`: a tag for the native editor's tester, so you spawn there instead of in
  the corner of the map. It did not work for years, but now mostly does.

## Quality assurance

- **Physics view.** Toggle the physics view on and off, optionally with the entity view off, and look
  over your entire scene. Pay close attention to anything with a mesh bender script.
- **Occluder view.** Same again with the occluder view. Mesh bender scripts sometimes break the
  occluder shape; if that happens you can simply delete the occluder instead of finding a different
  mesh to bend.
- **Broken prefabs.** Check that you have broken every custom prefab you made. This only matters if
  you went out of your way to save a prefab for reuse: then right-click it and "Break" it, so it
  exists in the scene as individual parts rather than as a prefab saved in a module the players will
  not load.

## Heightmaps to terrain

There are many workflows. Some starting points:

- Macbeth of Gondor's video: https://youtu.be/dxdVPGVRnPQ
- Geo-to-heightmap for Blender users (shared in the `bl-scenes` Discord channel).
- Exporting a heightmap from Gaea: https://youtu.be/e3HPYVDp4K4
- Tangram Heightmapper: https://tangrams.github.io/heightmapper/ - zoom in manually, screenshot or
  render an image, then edit it in Photoshop, Affinity or GIMP. You can combine several real-world
  mountain heightmaps this way.

## Native polycounts

For how heavy Native's own render meshes and collision shapes are (LOD0 vs physics, per category,
with the heaviest buildings listed), see the polycount reference in
[blenderlord-reference-materials](https://github.com/fiefevictionnotice/blenderlord-reference-materials#polycount-reference).
