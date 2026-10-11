# Bannerlord 3D art and scene guides

Sixteen guides compiled from six years of community knowledge in two channels of the Mount & Blade Modding Discord:
- **#bl-3d-art:** about 113k messages, 2020-2026. Guides 01-07.
- **#bl-scenes:** about 88k messages, 2020-2026. Guides 08-16.

The messages were filtered to the technical ones and mined for findings that held up. The findings were then merged by topic, newest first, with version notes.

The knowledge belongs to the people credited inline in every guide. Each guide ends with a **Sources** section that links to a page listing every finding behind it, with its author, date, confidence and a jump link to the original Discord message. The jump links only open for members of the Mount & Blade Modding server.

For anything these guides cover only briefly, the community wiki **[docs.bannerlordmodding.lt](https://docs.bannerlordmodding.lt/)** (maintained by Litauen) is the main reference. Each guide section says whether it is new, adds detail to the wiki, or is already covered there.

## 3D art (#bl-3d-art)

| # | Guide |
|---|---|
| 01 | [Materials, textures and shaders](01-materials-textures-shaders.md) |
| 02 | [Mesh import/export and LODs](02-mesh-import-export-lods.md) |
| 03 | [Rigged armour, helmets and gloves](03-rigged-armour-helmets-gloves.md) |
| 04 | [Weapons, shields and bows](04-weapons-shields-bows.md) |
| 05 | [Cloth and physics bodies](05-cloth-and-physics-bodies.md) |
| 06 | [Characters, mounts and scene props](06-characters-mounts-and-scene-props.md) |
| 07 | [Troubleshooting and tools](07-troubleshooting-and-tools.md) |

## Scenes (#bl-scenes)

| # | Guide |
|---|---|
| 08 | [Scene setup, files and testing](08-scene-setup-files-and-testing.md) |
| 09 | [Terrain and flora](09-terrain-and-flora.md) |
| 10 | [Navmesh, spawns and AI](10-navmesh-spawns-and-ai.md) |
| 11 | [Siege scenes](11-siege-scenes.md) |
| 12 | [Entities, prefabs and scripts](12-entities-prefabs-and-scripts.md) |
| 13 | [Lighting, atmosphere and water](13-lighting-atmosphere-and-water.md) |
| 14 | [Multiplayer scenes](14-multiplayer-scenes.md) |
| 15 | [Campaign world map](15-campaign-world-map.md) |
| 16 | [Scene troubleshooting](16-scene-troubleshooting.md) |

## Check these first

**3D art**
- **"Skinning"** on the material is the most common fix in the channel's history. Tick it on every rigged material and LOD, and leave it off for weapons, shields and props.
- **Skeleton:** name it `human_skeleton_notused` and turn off Add Leaf Bones (Blender switches it back on every session). Export the mesh together with the armature.
- **Scale:** export with "FBX All" and import with unit "none", or export with defaults and import with "m".
- **Materials** are made in the editor under the exact Blender material name, before you import the mesh. Since 1.3, texture names must be lowercase.
- **The `_s` texture** is packed: R metallic, G gloss (inverted roughness), B AO.

**Scenes**
- **Testing:** don't trust the editor's "Start Mission". Test by replacing a Custom Battle scene, or on a dedicated server for multiplayer.
- **Two folders:** a scene is `SceneObj` (what the game loads) plus `SceneEditData` (`terrain_ed.bin`, which the editor needs). Rename or copy scenes only with Save As.
- **Saving:** never save on exit, and zip the scene after every save.
- **Shader cache:** blue or black terrain usually means a stale shader cache under `ProgramData\...\Shaders\TerrainShaders`.
- **Flora:** layer flora only spawns in scenes that contain `border_soft` entities, and the order you place them in doesn't matter. Paint trees with the flora painter; tree layers can leave invisible trees that still have collision.
- **Multiplayer:** MP loads only Native and Multiplayer, so break every custom prefab (unparenting is not enough).

## Version labels

TaleWorlds restarted version numbering in October 2022:
- **"e1.x"** means the early-access era (2020-22).
- **"1.x"** means 2022 onward, including 1.3.x / War Sails (2025-26).

## Corrections

These are summaries of chat, not official documentation. If a finding is wrong for your game version, or you are a credited author who wants something changed or removed, open an issue or pull request.

Related: the [blenderlord-reference-materials](https://github.com/fiefevictionnotice/blenderlord-reference-materials) repo has Blender reference files rebuilt from Bannerlord's own scenes and prefabs, plus the human skeleton and body reference meshes.
