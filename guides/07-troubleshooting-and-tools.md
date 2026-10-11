# Bannerlord 3D Troubleshooting and Tools

Status: community-sourced, compiled 2026-10-10 from the #bl-3d-art channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Check these first** (they explain most failures in the data)
> 1. **Rigged item (armor, helmet, gloves, horse tack) misbehaves** (floats, T-pose, trails the head, crashes on equip): tick **Skinning** on every material of every LOD. Unrigged items (weapons, shields, props) need it **off**.
> 2. **Skeleton export**: armature named `human_skeleton_notused`, **Add Leaf Bones off**, mesh and armature both selected, transforms applied. Never import the skeleton as an asset.
> 3. **Names must match exactly**: Blender material name = editor material name, XML `mesh=` = mesh name in the Resource Browser, no spaces, no `.001` suffixes, **lowercase only on 1.3+**, one shared UV-map name before joining meshes.
> 4. **Purple, black or dark in the normal launcher but fine in the Modding Kit**: the material points at native or protected textures, or the tpac is stale. Export the textures with TpacTool, reimport them under your own lowercase names, and replace all four maps.
> 5. **Tool and game versions must match** (editor, TpacTool fork, Blender). After a patch, delete and reimport assets rather than "Reimport", and expect a long first load.

Conventions: *Source* gives the data slice (s00 = 2020 ... s11 = 2026) and main contributors. "(unverified)" = a single report. "Wiki:" = a page on BannerlordModding.LT (docs.bannerlordmodding.lt) that already covers it; "New:" = not found in the wiki mirror. Where later reports contradict earlier ones, the newest is stated and the version is noted.

---

## Part A. Symptom index

### A1. Editor crashes and tool problems

| Symptom | Likely causes (most common first) | Fix | Version notes | Source |
|---|---|---|---|---|
| Editor will not start, or "no target file" | Editor and game on different drives or versions; stale layout; hidden Resource Browser window | Same drive, same version, verify files. Delete `Documents\Mount and Blade II Bannerlord\Editor Files` (layouts). Maximize/Move the hidden Resource Browser from the taskbar | Wiki: editor general notes (drive, version). New: layout reset, hidden window | s07 Chase K; s11 Hyarti; s04 Kilo |
| Crash while assigning/importing a texture | **Uppercase letters in texture or material name** (a ~1 KB tpac appears); non-Latin characters in the source folder path; size not a multiple of 4; "do not compress/degrade" flag | Delete the texture in the editor, rename to lowercase, reimport from an ASCII path. Resize to a multiple of 4. Leave the compress flags alone | Uppercase crash: **1.3.x onward**. Wiki: non-Latin path only. New: the rest | s10 Aquila, AllieAdler; s11 X7 Dragon, Snorri; s00 Apalmemnom; s10 Cozur (unverified) |
| Editor hangs on load, random crashes in battle/campaign after a patch | Old tpacs built on the previous version | Reimport every asset from the original FBX/PNG. Item XML mostly survives; troop XML may need new upgrade targets | **1.5.7** invalidated 1.5.6 tpacs | s02 John_M, ellis |
| Model Viewer or cloth editor crashes | Mismatched beta tool branch; closing the viewer without removing the entity; first load of a new animation; builds 1.6.4 to 1.7.x | Remove the entity before closing, match the non-beta game, restart, verify files, clear editor data in Documents. Cloth save errors: restart the kit and re-save | 1.8.0 fixed most; stricter checks since 1.9 | s03 Dawa; s05 lemanier; s08 Ragnar, Chase K |
| Crash when creating an animation clip or loading a new one | Known bug | Retry once; create an *override* of an existing clip instead of a new one | 2022-23 builds | s06 nwetta; s07 Belisariusss |
| Crash after deleting or renaming | Deleting a diffuse still used by a material; reimporting a mesh that had cloth; renaming the module (path baked into tpacs) | Delete dependents first; use Cut/Paste of folders between modules, not renames | Dejan says cut/paste works; one user saw it go wrong | s07 hunharibo, Dejan; s08 Efarys |
| Import silently does nothing, or "Package path must contain /Assets/" | Name collides with an existing asset; stale file left in `AssetSources`; FBX outside the module's Assets mirror; "Assets" in the module name | Rename the mesh, delete the stale source file, import via right-click *Import new asset* from a subfolder | Wiki: "no model" = duplicate name. New: the rest | s06 Ragnar; s09 vorobei0609, DirtyFaced; s11 Kemo III |
| Hours-long first load or shader compile | First run after a patch or new asset; alpha-test or multi-pass alpha where not needed; `bo_` name typos looping the compiler | Wait it out; remove needless alpha; check recent `bo_` names | Dev-confirmed 30-40 min worst case (2021). 2026: an 80-unit army took 10 min from a bad alpha method | s03 Dejan; s06 Ragnar; s11 GulagEnabler, DarthKiller454 |
| `compressed_shader_cache.sack` huge (30 GB+); exploding or stretched textures | Corrupt shader cache after an update or overheating during publish | Delete the D3D11 shader folder and the module shaders folder, verify files | Bloat after **1.3**; published shaders stretched textures in Feb 2024 | s08, s11 Sly, Dejan, Haterade |
| Crash at campaign start | Almost always XML (stray `<` or `/`, bad path) | Remove XMLs from `SubModule.xml` one at a time, then move tpacs out | all | s01 Kilo, nwetta; s05 Ragnar |
| Crashes only in 400+ troop battles with custom units | Too many unique materials or large textures (unsolved) | Share materials, 2K maximum | (unverified) | s04 Mysterious Adversary |

### A2. Import failures

| Symptom | Likely causes | Fix | Version notes | Source |
|---|---|---|---|---|
| Item 100x too big/small, floating above head, or "vanished" | Unit mismatch | 1 m in Blender = 1 m in game. Either export *Apply Scalings: FBX All* and import unit "none", or default export and import unit **m**. Check the editor has not switched to mm | Editor default drifted to MM for one user in 2025. Wiki: export page mentions oversize | s00-s11 many; s10 TheCoolerJason |
| Mesh invisible in game but fine in the kit | Typo or stray space in `mesh=`; FBX filename used instead of mesh name; Blender prefix left on the object; two objects named alike ("Cube") | Match the name from the Resource Browser exactly; rename every object before export | all | s02 Hayster; s05 Badger; s11 Chase K |
| One FBX imports as several meshes, or "meta mesh error" | Dot/underscore naming; unnamed submeshes | `base.sub` (dot) merges into one item; Blender's auto `.001` is read as a submesh. Join with matching UV names or name subs `main.x` | New | s07 Ragnar, Skye; s09 Kemo III |
| Mesh imported as a physics shape | Name starts with `bo_` | Rename; reserve `bo_` for collision | 1.3+ report | s11 Benjo |
| Textures flat or UVs vanish after joining | Different UV-map names | Rename all to `UVMap`; if already joined, separate, rename, rejoin. Keep one UV set | all | s03 Kilo; s10 Awallacer |
| "Material not found" per LOD; wrong materials | Blender names like `Material.002`; material not created before import; unused slots dropped; one material in two slots | Create textures, then the material with the exact name, then import (auto-assigns all LODs). Sort by material if order is wrong | Wiki: the error. New: create-first, slot order | s06 Ragnar; s08 Terath; s09 GulagEnabler; s11 Alexander |
| Reimport gives stale or mixed data | Reimport or file swap only refreshes geometry | **Delete the asset and import fresh**; restart the kit if still odd | worse from 1.6 | s05, s07, s11 Ragnar, Chase K |
| Everything broke after importing a mesh | "Import skeleton" ticked, overwriting `human_skeleton` | Untick skeleton and skeletal animations for plain meshes; if done, delete the files and reimport | all | s00 Apalmemnom; s11 Artem |
| Normal "ballooning" or odd shading | Engine triangulates differently from the baker; custom normals; negative scale | Triangulate before baking, rebake, reimport both; apply scale and rotation | One rebake did not help (disputed) | s10 GulagEnabler; s09 hunharibo |
| Weapon parts misplaced, handle deals huge damage | Pivot not at part centre | Centre pivots, origin at 0,0,0, measure in cm; export crafting pieces in one FBX without joining | 2025-26 | s11 Hyarti; s03 Ramsay |

### A3. Invisible or wrong-looking in game

| Symptom | Likely causes | Fix | Version notes | Source |
|---|---|---|---|---|
| Missing faces, see-through cape or helmet interior | Flipped normals (negative scale, mirrored parts) | Apply scale (Ctrl+A), recalculate outside. Test: tick **Two Sided**; if it appears, the normals were wrong. Use Two Sided on purpose for cloth, but turn it off if cloth sim clips | Wiki: Two Sided in material setup | s00, s05, s07 Ragnar, Chase K; s10 TheCoolerJason |
| Holes in model | `disable_vertex_color_alpha` off | Tick it | Wiki: material_issues | s01 gkx |
| Weapon or shield invisible | Skinning ticked on an unrigged item; strap-deformer shader on a shield | Skinning off; use the standard shield material | all | s02 safran; s06 Dawa |
| Item dark or invisible only in game | `UseTeamColor` set but Diffuse2 mask empty; flag in the wrong element (must be in `Flags`, not Armor); stray `HasString="true"` | Fill the mask (black/red/green) or remove the flag | New | s02, s08 PlebeianKilo; s11 Vulvix |
| Sigil/banner not showing | Needs `using_tableau="true"`, Diffuse2 filled, `banner_replacement_mesh` on every LOD. Works on **LOD0 only** (unsolved). Tableau and team colour cannot share a material | Drop LOD1-3, keep 4-5, as a workaround | Wiki: sigil page. New: LOD0-only, one banner per material. Horse-armour team colour works from **1.4**, not on the world map | s09 Litauen; s10 GulagEnabler; s11 Thyrell |
| Shield shows team colours not seen in tools | Diffuse2/tableau flags | `using_tableau="false"` | (unverified) | s04 Kilo |
| Armor fine in editor, missing in game | An overriding mod replaced your exported materials | Test with only your module; use unique material names | (unverified) | s10 Sir Lee |
| Inventory icon garbled or black | Known icon bug; pivot off-centre; Diffuse2 empty | Fill Diffuse2, centre the pivot; framing is trial and error | Icon bug fixed in **1.2 beta** | s06, s08, s09 hunharibo |
| Helmet clips or looks oversized in game | Engine scales the head with morphs; viewer does not | Leave clearance; choose a hair-cover type (some hide ears) | Head-shrink changed in **1.9.0** | s04, s06 Dawa, Ragnar |
| LOD1 appears in place of LOD0, or item pops | Missing LOD or LOD material | LODs share the LOD0 material; make LOD1 presentable or delete it | Crafted weapons show LOD1 since **1.6.0** (TW: not a bug) | s03 Kilo, John_M; s11 TheCoolerJason |
| Crash on equip or save load with `covers_head` | Used on a head-slot item; also breaks hand morphs | Put it on a body/cape piece | Promised fix 1.6.1, still crashing 2021; partly fixed in **1.4.2 hotfix** (Benjo disagrees) | s03, s10, s11 |

### A4. Purple, black or dark materials

| Symptom | Likely causes | Fix | Version notes | Source |
|---|---|---|---|---|
| Purple/pink in the normal launcher, fine via mod tools | Cloned material references protected native textures; module not published; custom or renamed shader | Reimport textures into your own module with your own material; publish. Do not copy shaders | Dev and testers disagreed in 2020; Wiki: none | s00-s01 gkx, Kilo; s10 Kilo |
| Dark or black "since 1.3" | Native textures referenced from a new material, or only one native map overridden | Export native textures, rename, reimport; override **all four** (diffuse, diffuse2, normal, spec). Keep textures in the same module as the material | **1.3+** (character materials too) | s10 Litauen; s11 John_M, Rodric |
| Darker than in Substance | Vertex colours on mesh; metallic on non-metal; blue (AO) channel missing | Tick `do_not_use_vertex_color_as_occlusion` or delete the colour layer; fill blue white | Wiki: material_issues. New: AO fill | s01 gkx; s06 Ragnar |
| Plastic, chrome or black metal | Roughness not inverted; default Substance preset | Use the Bannerlord preset: R = metallic, G = gloss (inverted roughness), B = AO | Unchanged since 2020 | consensus s00-s11 |
| Normals look yellow or inverted | BC5 compression (normal); OpenGL normals; TpacTool normals with empty blue | DirectX normals; rebuild blue; to view in Blender invert **green only** | Yellow is expected | s02 gkx; s06 Ragnar |
| Flat, no shine | `use_specular` unticked on a new material | Tick it | Wiki covers | s00 gkx |
| Half-dark or banded rigged mesh; cloth anchor vanishes | Vertex colour or alpha read as occlusion/opacity | Flags above; paint alpha white; for cloth plus transparency untick "vertex alpha as occlusion" | Flag works for some, not all | s08 Dawa; s09, s11 Chase K, GulagEnabler |
| Halo or black outline on alpha edges | Alpha-test factor too low; multi-pass alpha on | Raise the factor; keep colour on the opacity layer; avoid multi-pass | Alpha sometimes needs a kit restart or "compile shader" | s08, s11 Ragnar, Rodric |
| Emissive dark by day, or shader crash on save | No exposure compensation; mask applies to whole material | Own material, self-illumination plus exposure; strength in vector argument `w` | Exposure added **1.8.0**; save crash (unverified) | s05 Ragnar; s11 Alphα |
| Black right after import | Material not refreshed | Restart the kit | 2023 | s08 Ragnar |
| Face texture on eyes and mouth | Replacement head lacks eye/mouth tags | Copy the tags from the vanilla head | | s06 Reznov315 |

### A5. Animation and rig issues

| Symptom | Likely causes | Fix | Version notes | Source |
|---|---|---|---|---|
| Helmet or armor lags, floats, bobs sideways, explodes, or sits in T-pose | **Leaf bones on**; skeleton not renamed; skinning off; auto-weights; armature modifier applied; no armature in export | Skinning on, leaf bones off (Blender re-enables per session), `human_skeleton_notused` with no `.001`, select mesh and armature, parent *with empty groups*, helmets 100% head bone | Wiki: armature_skeleton, export_to_fbx | dozens, s00-s11 |
| Crash entering world or on equip | Skinning off; two skeletons with the same ID | Same as above | all | s01 gkx; s09 ExpertShip64350 |
| "Noodle" limbs or stretch only in engine | Tiny stray weights (<0.05), more than 4 bones per vertex, unnormalised, unweighted parts | Clean, Limit Total 4, Normalize All; transfer from the body then hand-fix; auto-weights only as a start | New | s07 Ragnar, hunharibo; s11 Benjo |
| TpacTool skeleton wrong (roll, sideways) | Bone axis defaults; TpacTool roll values | Import primary **X**, secondary **-Y**; use a clean skeleton from the wiki download | Wiki: armature page | s01 nwetta; s10 ThrashManiac9 |
| Gloves show an open palm | Missing hand morphs | Basis plus 25-26 shape keys on LOD0, hide-hands/`covers_hands` true, only the Armature modifier, do not tick Apply Modifiers on export. Dark gloves: recompute normals in the editor | Wiki: Gulag script generates them. Disputed: `covers_head` conflict | s05 Ragnar; s09 Chase K; s11 Ramsay |
| Cloth invisible, dark or jittery | No separate material; vertex alpha misuse; cloth only on LOD0; two-sided counts as two layers | Own material, alpha black = fixed, white = simulated; remove baked AO colour | Cloth setting can reset in graphics options | s00 gkx; s08 DarthKiller454 |
| Custom skeleton crashes editor | More than 64 bones; missing parent; changed default skeleton breaks 2000+ animations | Keep bone names and hierarchy; scale via `bone_scales` in skins.xml | Only rotation keys apply, plus root position | s02, s09 Byako; s10 ShortJake; s11 hunharibo |
| Imported animation zero-length or crashes viewer | Blender FBX export | Rest pose keyed at frame 0, last frame = N-1, `_notused` on the root | **Blender 3.3.1** bad, 3.4.1 fine | s07 Kemo III |
| Horse armor head floats | Wrong horse skeleton; missing bone parents | Use the native horse skeleton | | s05, s10 GulagEnabler |

### A6. Patch-specific breakage

| Patch | What broke | Action |
|---|---|---|
| 1.5.3 | Model Viewer and cloth crashes | Test in game (s00) |
| **1.5.7** | Old tpacs invalidated | Reimport all assets (s02) |
| e1.6.0 | Crafted weapons display LOD1; TpacTool broke | Delete LOD1; hunharibo fork (s03) |
| 1.6.4 to 1.7.x | Cloth/viewer crashes | 1.8.0 fixed most (s05) |
| 1.8.0 | Spurious tool errors; some gloves lost rigging; exposure added | Reimport gloves (s05) |
| 1.9.0 | Head-shrink change refit helmets | Adjust clearance (s06) |
| **1.3.x** | **Capital-letter textures crash editor**; native textures dark; shader-cache bloat; TpacTool 0.4.1 broke | Lowercase; own textures; TpacTool 0.4.2 (s10-s11) |
| **1.3.13** | Mod weapons with only LOD0 turn **invisible** in battle (confirmed by several, 2025-12 to 2026-03) | Add LOD1+ (copy LOD0 as LOD1, or Gulag's auto-LOD), use a vanilla part, or re-cook (s11 Akaconda, Hyarti, PlebeianKilo) |
| 1.4.x | Horse-armour team colours; `covers_head` hand-morph fix (disputed) | Retest (s11) |
| **Blender 4.x** | FBX with morphs gets wild normals and black spots (confirmed on 4.2.1); bone select needs Alt; Color Attribute node replaces Vertex Color (3.2+). Avto tested on 3.6.2, issues from 4.1 | Round-trip through pre-4.0 Blender, or fix normals and triangulation on import (s09-s11 Ramsay, Byako) |

---

## Part B. Tools roundup

**TpacTool** (szszss original; hunharibo, Wuan23 forks; TpacToolMax). Opens vanilla tpacs to export meshes, textures, skeletons, animations.
- Caveats: version must match the game (0.2.0 for 1.5, 0.3.2.2 for 1.7, 0.3.3 for 1.8, 0.4.0 for the 2022-24 builds, 0.4.2 for 1.3.x, Wuan23 0.5.0 now). Open the whole Native folder, not one tpac. DAE with "rigged" keeps weights; OBJ has none. LOD0 only, no cloth alpha, doubled vertices, empty-blue normals, rarely correct skeletons. Animations need `human.tpac` beside the assets; modded assets export static. Some tile-set textures will not export. Do not rip other mods.
- Wiki: resources/tpactool (forks, DAE, human.tpac trick, inverted normals). New: version table.

**Gulag's Avto-WeightLODs (Blender add-on)**: weight transfer, empty-group cleanup, LODs (decimate 0.8, 0.4, 0.2, 0.1, 0.05), hand-morph generator (26 morphs from a rigged reference hand), and in 2026 a Bannerlord shader view plus `_d/_n/_s/_c` auto-apply. Caveats: tested on Blender 3.6.2, issues from 4.1; name meshes first, since the kit cannot rename. Wiki: editor/gulags_avto_weightlods_script. New: texture auto-apply. (s09-s11)

**Other Blender helpers**: decimate.py LOD script (gazu, s07); Modifier Tools for copying decimates; Transfer Mesh Data for vertex alpha; Mirror Selected Bones add-on; Shift+F Walk navigation to mimic the scene camera; ChatGPT-made XML scripts (exclude `_converted` and `_slim`; unverified). Hand-morph tutorial: TW forum thread "Hand Morphing Tool and Tutorial" (John_M, from Enpremi).

**Substance Painter**: use the community Bannerlord export preset (`MB_export_presets`, in the pinned 3D Asset Workflow doc) for D/N/S plus a transparency variant. The in-SP shader is irrelevant. Bake by mesh name with `_high`/`_low` suffixes, self-occlusion "only same mesh", explode layered parts, triangulate first, bake above final size. Opacity goes in diffuse alpha. No IOR support. Caveat: default presets give wrong channels or two normals. (s03-s11 Ragnar, hunharibo)

**Marmoset** (faster baking, s03). **Marvelous Designer**: thin, weld, quadrangulate (s01, unverified). **Photoshop**: CS6 PNG alpha unreliable, use TGA (s02, unverified); DDS only for hair/feather alpha.

**3ds Max / Maya**: Max wants X-primary bones; Maya reorders bones alphabetically (s03). Treat as legacy workflows.

**Editor-side and community tools**: Model Viewer (best rig test; Wiki: model_viewer), TAOM Crafting Tool v1.0 and Item Position Editor (sakinolemir), In-Game Shield Editor, SpriteSheetGenerator, HeightMapExtractor, AssertAutoIgnore (Wiki: resources/tools).

**Reference assets**: Nexus "Skinned Body Base Meshes" (mod 2286), John_M's `helmettest.fbx`, the modding_resources skeleton, and the pinned "Bannerlord 3D Asset Workflow" and "Armor Tutorial" docs.

---

**Contributors**: PlebeianKilo, gkx (TW), Ragnar Hrodgarson, hunharibo, GulagEnabler, Dawa, John_M, ThrashManiac9, nwetta, npc99, Ramsay, Chase K, Kemo III, Dejan (TW), Litauen, Benjo, ExpertShip64350, Hayster, Aquila, Byako, Snorri, and many single-report posters.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/07-troubleshooting-and-tools-sources.md](sources/07-troubleshooting-and-tools-sources.md)** (190 of 190 linked). The links only work for members of the Mount & Blade Modding server.
