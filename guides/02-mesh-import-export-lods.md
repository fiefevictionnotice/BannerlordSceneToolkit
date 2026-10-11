# Getting Meshes from Blender/Max into the Bannerlord Editor: Export, Import, Naming, LODs

Status: community-sourced, compiled 2026-10-10 from the #bl-3d-art channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - **Scale is the number one cause of "my mesh vanished".** Either export with Apply Scalings = "FBX All" and import with unit "none", or export default and import with unit "m". A mismatch gives giant, tiny, floating or "invisible" gear (Ragnar Hrodgarson, nwetta; confirmed by many, 2020-2026).
> - **Untick "Add Leaf Bones" every session.** Blender re-enables it on restart. It makes helmets and armor ignore the skeleton and it changes the bone count (ThrashManiac9, PlebeianKilo, Ragnar; confirmed repeatedly).
> - **Names are the whole system.** The XML mesh id must equal the mesh object name (not the FBX filename); `.lodN` makes LODs; a `.` suffix merges sub-meshes while `_` splits them; Blender's auto `.001` is read as a sub-mesh; a `bo_` prefix makes a physics shape.
> - **Do not trust Reimport. Delete the asset and import it again**, and clear stray files from AssetSources. Reimport leaves stale data and causes odd deformations, reverts and crashes (Ragnar, hunharibo, Chase K, 2022-2026).
> - **Negative scale and unapplied transforms make faces invisible in game** while Blender looks fine. Apply with Ctrl+A, recalculate normals (Shift+N), use a Mirror modifier, and triangulate the low poly before baking.

## 1. Set up the Blender scene (units, axes, transforms)

1. Model at real scale: 1 Blender m = 1 Bannerlord m. The male body is about 1.79 m tall (Ragnar, 2022; Not A Box and Isaac, 2021, agree within a few cm).
2. Apply all transforms (Ctrl+A) and keep scale at 1 before export. Floating or oversized helmets are usually unapplied transforms (GulagEnabler, Terath, 2024; Kemo III, John_M, 2025).
3. Put the pivot at the part's real center. Inventory icons are rendered from the pivot, and a distant pivot places the item far away in game (Ragnar, 2022). For crafted weapon parts keep the origin at 0,0,0 with the part centered; a wrong origin breaks hitboxes (Hyarti, Iberian Wolf, BraquemardLeBrav, 2025-2026).
4. Scale items in Blender, not in the editor; editor scaling does not change held items (ThinRizzie, Dawa, 2021).
5. Axes (disputed: Not A Box, 2022, wanted Z up and -Y forward; PlebeianKilo saw no effect, only bone axes matter). Latest advice is Z up (Kemo III, John_M, 2025). For skeletons use Primary Bone Axis X, Secondary -Y (nwetta, Dejan of TW, 2021).

**Gotchas**
- Mirrored meshes with negative scale show flipped normals and half-missing geometry in game. Quick test: tick "Two Sided" on the material; if the mesh appears, normals or scale are wrong. Use the Face Orientation overlay (Ragnar, Chase K, 2022-2023). Two Sided hides the problem at a shader cost.
- Applying transforms can itself flip normals; recheck afterward (Hayster, Major Roy, 2022).
- Blender 4.2.1's FBX exporter broke morph animations; re-export from an older Blender (Ramsay, 2024). A glove broken by a Blender-version round trip was fixed with normals plus triangulation export settings (Ramsay, Sir Anthony, 2026, unverified).

Wiki: https://docs.bannerlordmodding.lt/3d/export_to_fbx/ (only mentions "model too big in Blender") - **new** for axes, pivots and negative scale.

## 2. Export the FBX

1. Select the render meshes (all LODs). For rigged gear (armor, helmets, boots, gloves, shoulders) also select the armature. Shields additionally need their two `bo_` elements.
2. File > Export > FBX. Tick **Limit to Selected Objects**, untick **Add Leaf Bones**, bake animation off, no cameras or lights.
3. Scale choice (pick one and match it on import, see section 3):
   - Apply Scalings = **FBX All**, then import unit "none" (Ragnar, 2022-2023); or
   - default (All Local), then import unit **m** (PlebeianKilo, Claiton, Jesse confirmed).
4. Apply modifiers on armor meshes but keep the Armature modifier pointing at the skeleton (PlebeianKilo, Dawa, 2021-2022).
5. Name the armature `human_skeleton_notused` on every armor and animation export. The suffix stops it overwriting the real skeleton on import (PlebeianKilo, Alpha Omega Man, 2021; Kemo III, Ramsay, hunharibo, 2023-2024). Inconsistent naming left armor stuck in a pose (Chase K, 2024).
7. Use FBX only. OBJ/MTL materials are ignored (Ragnar, Ha_rang, 2023).

**Gotchas**
- "Selected Objects" ticked with nothing relevant selected gives a roughly 4 KB empty file (ThrashManiac9, 2020).

Wiki: https://docs.bannerlordmodding.lt/3d/export_to_fbx/ - **adds detail** (armature name, scale pairs, Blender-version bugs); the basic recipe (selected only, no leaf bones, preset) is already covered.

## 3. Import into the editor (Resource Browser, AssetSources)

1. Place the FBX in your module's `AssetSources`, in a subfolder rather than the root (Kemo III, ExoBannerlord, 2026, unverified), with a mirrored `Assets` folder.
2. In the Resource Browser right-click empty space > **Import new asset**, ideally from a path outside AssetSources. Dropping a file into the folder, or hand-copying, does not register it and gave corrupt results (Ragnar, 2022; gorgos96, Kemo III, 2024). The scan button also helps.
3. Set the unit to match your export (section 1). In 2025 one user saw the default flip to "MM", so check every time (TheCoolerJason, unverified). Ticking "convert to meter units" fixed huge, rotated or blurry imports (absalon, Solus, Benjo, 2026).
4. Untick **import skeletons** (and skeletal animations) unless you mean to ship a skeleton. Importing one overwrites `human_skeleton` and breaks everything; fix by deleting the files and importing again (Artem, 2026).
6. Name Blender materials after the editor materials. The importer matches by name, which saves assigning each LOD by hand (gkx, TW, 2020; Chase K, 2026). When reimporting a renamed native mesh keep its material names so they auto-assign.

**Gotchas**
- "Package path must contain /Assets/ element": the file is outside the module's Assets/AssetSources layout. A module name containing "Assets" also caused copies into bin/Editor/Modules (vorobei0609, DirtyFaced, 2024; Kemo III, 2026; all single reports).

Wiki: https://docs.bannerlordmodding.lt/3d/editor_fbx_import/ - **adds detail** (the /Assets/ error, skeleton overwrite, unit drift); basic import steps and "model too big, use m" are already covered.

## 4. Naming rules

| Pattern | Effect | Source |
|---|---|---|
| `name` | The mesh id used in the item XML. Must match exactly, lowercase, no stray spaces | gkx; Hayster; Badger; Ragnar (confirmed) |
| `name.lod1` ... `.lod5` | LODs, in the same FBX; `_lodN` also works | consensus; GulagEnabler, 2026 |
| `name.001`, `name.sub` (dot) | Merged into one item at runtime; still separate in the cloth editor | Ragnar, 2023-2024 |
| `name_part` (underscore) | Separate item | Ragnar, 2023 |
| `name.001_lod1` | Sub-mesh suffix goes before `_lodN` | Ragnar, 2023 |
| `bo_...`, `bo_cap` | Physics/collision shape, not a visible mesh | Benjo, Ramsay, 2026 |

Steps and rules:
1. Rename every Blender object to something unique before export. Defaults like "Cube", "sphere" or "mergedblocks" collide; only one imports (Enclave, 2020; Dawa, 2022; Chase K, 2026).
2. The XML `mesh=` value is the name under the thumbnail in the Resource Browser, not the FBX filename. Still keep file and object names identical: mismatches caused errors in pre-1.8 re-imports (Xtra Dip, PlebeianKilo, 2022, single report) and LODs not populating (EnumaEllis, KEYforce, Gulag, 2026).
3. Use lowercase; the editor lowercases anyway. Capital letters may break hand-morph shape keys (Chase K, 2026, unverified; one counter-example).
4. One mesh cannot serve several item variants through XML: import it several times under different names, renaming the base and every LOD (PlebeianKilo, DG, 2020).
5. Sub-mesh convention changed over time. In 2022 people used `.extra` or `.partN` (Ragnar). Since 2023 the rule is base name plus a dot suffix, all in one FBX, each with its own LODs. Because Blender's automatic `.001` triggers this, rename deliberately: `-X` suffixes made separate metameshes (Wizcas Chen, 2024).
6. A FBX with several unnamed sub-meshes is not recognized as a meta mesh ("meta mesh error"). Name subs `main.something` or join into one mesh with multiple materials (Skye, Ragnar, 2023).
8. A mesh imported under an existing name replaces it. Rename, or copy the SandBoxCore XML entry into your module with a new id (Ragnar, Badger, 2022). Native assets can be overridden by importing `{name}` or `{name}_lod0` in your own module; tpacs cannot be repacked (DG, John_M, 2021).

**Gotchas**
- A missing or misnamed mesh is silently invisible, which is also the trick for invisible helmets (PlebeianKilo, 2020).
- Typos or a missing `/` in the XML hide the item (Hayster, Jester's Ego, Corv0).

Wiki: https://docs.bannerlordmodding.lt/3d/blender_fbx_import/ (only "make the name unique, it becomes the XML name") - **new** for dot/underscore/`bo_` rules.

## 5. UV maps and materials

1. Before Ctrl+J give every UV map the identical name (e.g. "UVMap"), otherwise one mesh's UVs vanish on import (PlebeianKilo, Skye, Badger, Ragnar, 2020-2025; confirmed many times). If already joined: separate by the second UV, delete the extra map, rename, rejoin.
3. Multiple materials: assign faces to material slots in one object (the editor shows `name.0`, `.1`), or keep identically named objects with `.001` suffixes (Dawa, Ragnar, 2021-2022).
4. All LODs share LOD0's material and UV layout. A missing LOD material causes glitches (ADamnSexyName, PlebeianKilo, 2021). Copying materials onto LODs in the browser is unreliable; assign before import (GulagEnabler, 2024).

**Gotchas**

Wiki: https://docs.bannerlordmodding.lt/3d/material_issues/ - **new** (the UV-name rule is not on the pages checked).

## 6. Normals, triangulation, "invisible faces"

1. Fix flipped faces: Face Orientation overlay, then Shift+N (Recalculate Outside) or Alt+N flip (Ragnar, Grun, Dawa, 2022-2023). Inverted normals show as holes in imported buildings and as invisible gear.
2. Triangulate the low poly (Triangulate modifier) before baking so the bake matches in-engine triangulation (Ragnar, GulagEnabler, Solus, 2022-2026). "Marshmallow" ballooning normals were cured by triangulate, rebake, reimport (GulagEnabler, 2024-2025).
3. (disputed: Dawa and Skye saw no difference, Benjo's rebake did not fix his case; Gulag and Ragnar saw artifacts.) The engine triangulates on import automatically, so treat this as required only when shading looks wrong.
4. Mark UV seams hard rather than all edges; avoid custom normals / Auto Smooth on export, or reimport with Custom Normals off (Ragnar, 2022). N-gons cause shading errors (Ragnar, Dawa, 2023).
5. The engine reportedly simplifies skinned meshes on import, so a dense helmet's LOD0 can lose detail. Enabling both "skinning" and "precise skinning" on the material avoided it (GulagEnabler, PlebeianKilo, 2023-2026, unverified). Precise skinning cannot be used with glove morphs (wiki).

Wiki: https://docs.bannerlordmodding.lt/3d/material_issues/ - **new** for triangulation and negative-scale details.

## 7. Reimport and cleanup

1. To update a mesh: delete the asset in the kit, delete its leftovers in the module's AssetSources, restart the editor if it still misbehaves, then import fresh (Ragnar, Hayster, hunharibo, 2022-2026; confirmed).
2. (disputed: Ramsay, Chase K and Ragnar reported that the right-click Reimport fixed missing shape keys/weights, once ending five hours of glove trouble; others saw corruption and crashes.) Reimport is reasonable when the FBX was imported before its material existed (Chase K, 2026); otherwise delete and import.

**Gotchas**
- After the 1.5.7 patch every asset had to be re-imported from its original FBX/PNG; old tpacs hung the editor (John_M, ellis, 2021; multiple reports). Expect the same after major patches.
- Publishing builds the tpacs in AssetPackages (hunharibo, John_M, order_without_power; disputed: PlebeianKilo said they appear on import). An empty AssetPackages folder is not why an item is invisible. Shipping needs only AssetPackages, ModuleData and SubModule.xml (Ramsay, 2022). Leftover unpackaged assets can still crash a published module (John_M, 2020).

Wiki: https://docs.bannerlordmodding.lt/3d/editor_fbx_import/ - **new** for reimport, Publish and module-rename behavior.

## 8. LODs: counts, percentages, distances

1. Bannerlord does not generate LODs. TW's generator is in-house and not shared (gkx, 2020). Make them yourself in the same FBX.
2. Build LODs last, after rigging and weight painting, from a copy of the mesh (duplicate the mesh only, not the skeleton). Use Decimate before the Armature modifier; LODs need no shape keys (John_M, PlebeianKilo, Ragnar, ThrashManiac9, 2020-2024; confirmed). Deleting the Armature modifier on a copy also deletes the vertex groups (Ragnar, 2022). The pinned Avto addon and GulagEnabler's script (linked on the wiki) generate LODs and transfer weights in one click.
3. Typical ratios of LOD0 triangles: LOD1 ~80%, LOD2 ~50%, LOD3 ~20%, LOD4 ~10%, LOD5 ~5% (John_M derived from natives; Ragnar confirmed as TW's, 2021-2022). Looser 75/50/25 also works. Low-poly assets may skip levels; shields and weapons can have two or three LODs (John_M, 2021).
4. Default switch distances: LOD1 at 15 m, then 22.5, 30, 50, 70, 130 m, culled around 210 m (docs). A 2020 quote of agent-equipment distances (15, 22.5, 30, 48, 66, 106, 166) differs slightly (John_M, unverified). Distances are fixed, not customizable (Snorri, ExoBannerlord, 2026, unverified).
5. Naming `.lod1`...`.lod5`, parented to LOD0. LODs reduced to a point mean you exported empty LODs (Ramsay, 2022). Every sub-mesh of a multi-part asset needs the same LOD set (Ragnar, Dawa, 2022-2024).
7. Cloth: simulation only on LOD0. Banner makers use LOD0, 4 and 5, or start at LOD2; banners otherwise need no LODs (John_M, PlebeianKilo, Dawa, 2021-2022). Reimporting LODs wipes cloth setup, so do cloth last (Ragnar, 2022).

**Gotchas**
- LOD1 shows instead of LOD0 in game: crafted weapon pieces use LOD0 in the crafting menu and LOD1 in battle since 1.6.0 (PlebeianKilo, John_M, 2021; disputed: whether TW counts it a bug). Delete LOD1 for weapons or make it good. Rebuilding the module and reimporting fixed a similar case (Kaffegörare, 2020, unverified).

Wiki: https://docs.bannerlordmodding.lt/3d/create_lods/ - **adds detail** (percentages, naming, cloth and parenting quirks); distances and the weapon-LOD1 tip are already there.

## 9. Polycount and performance budgets

All counts are triangles, not faces or vertices (wiki; John_M).

| Asset | Budget | Source |
|---|---|---|
| Armor set, whole character | 8-12k (TW wording), 12k recommended, ~20k max; 15-25k at LOD0 in practice | TW quote; gkx; Ogimaa (disputed: ranges differ) |
| Helmet | 2-4k common, up to 5k; officers 7-11k | Garrettich, GulagEnabler, Ragnar |
| Weapon | 2.5k normal, ~4k special; muskets 5-8k | ShooterPerson11, NobitaNobi, Haterade (single reports) |
| Map/camp meshes | native structures 20-4k tris; 100k+ scans too heavy; a "65,000 polygon" limit reported | raccoononthetree, Snorri, 2026 (unverified) |

Rules:
1. Materials and draw calls cost more than triangles. Aim for 1 material per armor piece (2 with cloth), about 3 at most per full set (Dawa, John_M, Ramsay). (disputed: Dawa accepted 6 across a whole outfit; one 1000-vs-1000 test found extra materials harmless.) Bake rivets and repeated detail into normals; alpha and extra materials add draw calls (GulagEnabler, K.Savo, Solus, 2026).
4. Use LODs on everything: 18k+ armors without LODs caused missing horses and freezes, and 600k tris per set crashed battles (kosavva, John_M, momopirou, 2020).
5. No hard limit found: a 1M-triangle ship without LODs ran, a 2M-triangle helmet loaded, but only a few can be on screen (Dawa, Ragnar, 2022-2023). Collision mesh poly count matters most in big scenes (John_M, 2022).

Wiki: https://docs.bannerlordmodding.lt/3d/polycount/ - **adds detail** (crowd and material cost, later budget disagreements); its vanilla-count table is the base reference.

## 10. Fix-it table: invisible, giant, offset

| Symptom | Likely cause | Fix |
|---|---|---|
| Item exists, shows in tools, not in game | Mesh name typo in XML, or material not visible | Match XML to the Resource Browser name |
| Huge, tiny, floating above head, floor level | Scale/unit mismatch (sections 1-3) | Match export scaling to import unit; reapply transforms; delete and reimport |
| Vanished armor | Giant mesh, or exported without armature | Check scale; select mesh and armature |
| Helmet hangs behind head, won't animate | Leaf bones on, or wrong skeleton name | Untick leaf bones; `human_skeleton_notused` |
| Visible in editor, see-through in game | Negative scale / inverted normals | Ctrl+A, Shift+N; Mirror modifier; test Two Sided |
| Sits far from where it should | Pivot far from geometry | Center the pivot; origin at zero |
| Imports as nothing | Same name as existing asset, or `bo_` prefix | Rename |
| Only some pieces appear | Mismatched UV names, duplicate names, split sub-meshes | Sections 4-5 |
| Cloth `_clo` mesh gap along mirror seam | TpacTool export split nodes | Merge by Distance (Vulvix, Lupercal, Benjo, 2026) |

**Gotchas**
- Origin handling (disputed: hunharibo says the Blender origin is ignored and objects land at world origin; Ragnar says the pivot decides placement of unrigged meshes while rigged items follow the skeleton). Test with your own asset.

Wiki: https://docs.bannerlordmodding.lt/3d/export_to_fbx/ - **adds detail**; its Problems section covers only leaf bones, wrong mesh and scale.

## 11. Extracting native assets as a reference

1. TpacTool (use hunharibo's fork; the original breaks on each new branch, e.g. 1.6.0): export rigged as `.dae`, which keeps weights and shape keys; `.obj` loses them. Rigged export needs the human skeleton tpac in the same opened folder, so open the whole Native folder (hunharibo, Jarl, Kemo III, 2026; confirmed). Static exports lose skeleton and weights.
3. Clean up: Merge by Distance (DAE has doubled vertices), smooth shading for blocky normals, replace the often-broken exported armature (DirtyFaced, Ragnar, 2023; npc99, 2020).
5. Without TpacTool: drag the asset into a scene, select it, Edit > Export selected entities as OBJ with LOD "Export all lods". Unrigged only and often empty (20-30% success per npc99, 2022); OBJ merges LODs. Prefab export gives an `.mtl` but no textures (Byako, 2024).

Wiki: https://docs.bannerlordmodding.lt/3d/blender_fbx_import/ - **adds detail** (TpacTool export modes and clean-up).

## Contributors

absalon, ADamnSexyName, Alpha Omega Man, Aquila, Artem, Badger, Belisariusss, Benjo, Byako, Caskie, Chase K, Claiton, Clovis, Corv0, Dawa, Dejan (TW), DG, DirtyFaced, Enclave, EnumaEllis, ExoBannerlord, ExpertShip64350, Garrettich, gkx (TW), gorgos96, Grun, GulagEnabler, Ha_rang, Haterade, Hart, Hayster, hunharibo, Hyarti, IceBreaker, Iberian Wolf, Isaac, Jarl, Jesse, jacobins, jesjar, Jester's Ego, John_M, K.Savo, Kaffegörare, KEYforce, Kemo III, koya, kosavva, Lord Eastwood, Lupercal, Major Roy, Mandarinushka, Mark7, momopirou, nwetta, Not A Box, NobitaNobi, npc99, Ogimaa, order_without_power, PauCat, PlebeianKilo, Ragnar Hrodgarson, raccoononthetree, Ramsay, Reznov315, safran, ShooterPerson11, Shawmuscle, Sir Anthony, Skye, Snorri, Solus, strong bird, tardwrangler, Terath, TheCoolerJason, TheSchwarz, ThinRizzie, ThrashManiac9, Vulvix, vorobei0609, Wizcas Chen, Xtra Dip, ellis, josef, BraquemardLeBrav

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/02-mesh-import-export-lods-sources.md](sources/02-mesh-import-export-lods-sources.md)** (190 of 192 linked). The links only work for members of the Mount & Blade Modding server.
