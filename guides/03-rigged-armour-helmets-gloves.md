# Rigged Armour, Helmets and Gloves (Bannerlord)

Status: community-sourced, compiled 2026-10-10 from the #bl-3d-art channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - **Tick "Skinning" on the material of every rigged piece, and only on rigged pieces.** It is the single most repeated fix for floating, T-posed, invisible or crash-on-equip armour (gkx, TW, 2020; dozens of cases through 2025). Weapons and shields need it OFF.
> - **Name the skeleton `human_skeleton_notused` and export with "Add Leaf Bones" off.** A skeleton called `human_skeleton` clashes with the native one; leaf bones cause offsets, bobbing and sideways meshes.
> - **A helmet is just 100% weight on the `head` bone.** Never auto-weight it. For body armour, transfer weights from the vanilla body, then clean by hand.
> - **Gloves animate with shape keys, not finger bones.** You need Basis plus 26 keys, hands hidden in the item XML, and (with newer Blender) possibly an old Blender to re-export, or the glove goes black or "paper".
> - **Weights under about 0.05 and more than 4 influences per vertex only break in-engine.** Clean, Limit Total (4), Normalize All before every export.

## 1. Skeleton setup

1. Get a clean human skeleton: the community "Skinned Body Base Meshes" pack (Nexus 2286), John_M's `helmettest.fbx`, or a fresh TpacTool export of a rigged `body_male_a` from EmAssetPackages. Raw TpacTool skeletons are often broken (bone roll about 180 degrees off, wrong head/finger/foot bone) (ThrashManiac9, nwetta, 2020; Ragnar, 2022; Caskie, 2024; confirmed by several).
2. Blender import settings: Primary Bone Axis X, Secondary -Y, Add Leaf Bones off; otherwise bones arrive rotated 90 degrees and disconnected (nwetta, gkx, 2020).
3. Rename the armature object AND its armature data to `human_skeleton_notused` (no `.001`/`.002`) (PlebeianKilo; confirmed by many). The `notused` name makes the importer ignore it, so no second `human_skeleton` clashes with the native one. Skipping it causes exploding or unrigged helmets and crashes on entering battle (Apolon, 2020; Alexander Drakos, Kemo III, ExpertShip64350, 2022-24).
4. Do not import the skeleton in the editor for ordinary armour: untick skeleton import in the Resource Browser (Apalmemnom, TurboLennsson, 2020).
5. Rig with the body mesh present, then delete it (or hide it in a separate collection) before export (PlebeianKilo, ThrashManiac9, 2020-21).

**Limits.** Maximum 64 bones per skeleton; 65 crashes on import/export, and 88 and 231 crashed too (nwetta, hunharibo, Byako; confirmed). Mount plus rider setups were reported to count together (Not A Box, 2021; unverified). Version 1.9 expanded monsters.xml bone references but kept the cap (nwetta, 2022).

**Gotchas**
- Do not edit or replace the default skeleton: all 2000+ base animations break. Custom races keep bone names, count and hierarchy and shrink via `bone_scales` in skins.xml (hunharibo, nwetta, 2023-26).
- The game ignores bone position/scale keys in animations on agent skeletons; only rotation counts (position only on the root) (ShortJake, hunharibo, 2025).
- Rigged meshes extracted from a mod's tpac arrive with a fake `_notused` skeleton; export static and re-rig to the native skeleton (TheCoolerJason, Chase K, 2024-26). In TpacTool choose "rigged" for vanilla assets (ThrashManiac9, hunharibo, 2024-25).
- Armour cannot add its own `bo_` collision beyond the standard human body (Kemo III, 2024; unverified).

Wiki: https://docs.bannerlordmodding.lt/3d/armature_skeleton/ - already covered (import, rename, parent with empty groups); the 64-bone cap, TpacTool skeleton warning and mod-tpac caveat are new.

## 2. Weights

Helmets:
1. Align the helmet to the head, select helmet then skeleton, Ctrl+P > Armature Deform With Empty Groups.
2. Delete all vertex groups except `head`; assign everything at 1.0 (all red). Add `neck` only for pieces that hang low or move (PlebeianKilo; Skye, 2022; confirmed).
3. A helmet that follows spine or shoulders was rigged like body armour or has stray weights (ThrashManiac9, 2023). Check the mesh is parented to the Head bone: Blender silently selects the pelvis when entering weight paint (Ragnar, 2023; unverified).

Body armour and capes:
1. Parent with Empty Groups, then Transfer Weights / Data Transfer from the vanilla body (Source Layers By Name; Nearest Face Interpolated; ray distance 5) (Ragnar, GulagEnabler, 2022-25). Merge the body parts first so feet and toes are covered. Transfer gets 80-95%; fix armpits, belts and pauldrons by hand.
2. Avoid Automatic Weights: it leaves stray influences that distort only in-engine (Ragnar, nwetta, Dawa; confirmed). It is acceptable only as a quick re-skin of foreign armour already posed to the BL rest pose (Ragnar, Jerkov, 2022).
3. Delete leftover finger/hand groups after transfer to stop clipping. To transfer to part of a mesh, mask by vertex group (Jester's Ego, 2021).
4. Capes and long garments: keep only spine/spine1/spine2/neck, clavicles, upper arm (and twist), pelvis; strip leg weights when cloth sim is off (PlebeianKilo, slayergodfrey, 2021-24).
5. Double-shell meshes: identical weights on inner and outer layers; turn off "Front Faces Only" on the brush (Dawa, Ragnar, 2022-23).
6. Pre-export cleanup: delete unused groups, Clean, Limit Total to 4, Normalize All. Weights under about 0.05 cause artifacts invisible in Blender; more than 4 influences per vertex are dropped unpredictably; un-normalised weights cause "noodle" deformation and lagging legs (Arlaygio; hunharibo confirmed, 2023; John_M, MrAdrianPl, Arcaddon).
7. Anything left unweighted (feathers, hair, armpits) stretches with animation (ExpertShip64350, Benjo, 2026). A buckle that moves with the body has a tiny stray weight; clear its groups and reassign (Ragnar, 2022).
8. Test in Pose Mode at extreme poses. Preserve Volume on the armature modifier eases elbow and wrist twist (svang5142, 2021). Never move a rigged mesh in Object Mode; reset transforms (Alt+P Clear and Keep, Ctrl+A) and re-parent. Scale in Edit Mode (Ragnar, M4DCooper, Skye).
9. Foreign rigged armour: pose it to the BL rest pose, apply its armature modifier, delete its skeleton, re-parent to the BL one (ComradeCheekiBreeki, Chase K, KingOfTheN0RTH; confirmed). Arms and legs must align with the skeleton; spine and head tolerate error (TheCoolerJason, 2024).
10. Mirroring weights needs symmetrical topology and .L/.R bone suffixes; vanilla has none, so use a mirror-bones add-on (Ragnar, 2022; Jester's Ego, 2021).
11. Paint in 0.1 steps if gradients look odd (disputed: Ragnar suggested it, another user doubted it). Merge doubled vertices before transfer (Dawa, 2023; unverified). Very low (dark blue) weights do nothing (Kemo III, 2025; unverified).
12. "Bone heat weighting failed" is usually ignorable (Ragnar, 2022; unverified).

**Gotchas**
- Cloth physics reads vertex ALPHA (black skinned, white simulated), not vertex colour (gkx, TW, 2020). Give cloth its own material with cloth enabled.
- Hair and beard have no in-game physics, though they work in the mod tools (Ragnar, Ramsay, 2022).
- Holes at arm/torso in animation: weld vertices between pieces, not only join (Jerkov, Ragnar, 2022).

Wiki: https://docs.bannerlordmodding.lt/3d/weight_painting/ - adds detail (4-influence limit, 0.05 floor, normalization, cape groups); basics already covered.

## 3. Material, export and editor import

1. Material: tick **Skinning** (Other tab, last options) for anything rigged: armour, helmets, hair, beards, turbans, horse tack. Leave it OFF on shields, weapons, banners and arrows; ON makes them vanish, sit sideways or crash (Jester's Ego, Ramsay, Dawa; confirmed).
2. In Blender select mesh(es) plus `human_skeleton_notused`; Limit to Selected Objects; Add Leaf Bones off; armature modifier active, not applied. Save an export preset. Missing armature gives a static box or T-pose (Vulfar, Ragnar, 2023-24).
3. If the skeleton was ever imported or exported with leaf bones the extras persist; re-import it cleanly (Ramsay, Litauen, 2025).
4. In the editor the item's `mesh=` must match the mesh name (Hayster, 2021). Two armours with the same mesh but different textures need separate meshes (Chase K, 2026; unverified).
5. An item "lying sideways" in the Resource Browser usually forgot skinning (Dawa, 2022).
6. Helmets look oversized or clip in game because the head is scaled by morphs; the model viewer does not show it. Pull helmets slightly back and give hoods clearance. TW changed the head shrink in 1.9.0, re-breaking mod hats (Dawa, Ramsay, Areldir, 2021-22; confirmed). Since 1.4.x helmets reportedly auto-fit head shape (GulagEnabler, Ramsay, 2026; unverified).
7. "28 bones used" in the meta mesh editor on vanilla armour is normal (PlebeianKilo, 2020).

Wiki: https://docs.bannerlordmodding.lt/3d/export_to_fbx/ - already covered (leaf bones, selection); skinning-off-for-weapons and head-scale behaviour are new. Editor side: https://docs.bannerlordmodding.lt/3d/editor_fbx_import/

## 4. Variants: _slim, _converted, _converted_slim

1. Duplicate the finished armour, add `_slim`, scale outer sleeves in so they fit under gauntlets, give it its own LODs, export together (KingOfTheN0RTH, PlebeianKilo, Ragnar; confirmed).
2. Females use `_converted`; females with gloves need `_converted_slim`. Without `_converted_slim` the game falls back to the male mesh (Ramsay, PlebeianKilo, Dawa, 2022; confirmed). `..._female_slim` does not work (Ramsay, 2023; unverified).
3. Selection is by name only; no XML entry. Any equipped glove swaps in the slim mesh, not just hide-hands ones (Ramsay, 2022; ThrashManiac9, 2024).
4. Transfer weights to variants: copy mesh, parent with empty groups, Source Layers By Name (Ragnar, 2022). Keep weights identical across LODs and variants or parts stop stitching (Astraali, 2022; unresolved).
5. `has_gender_variations` reportedly needs all four meshes (Caskie, 2024; unverified). Quick sleeve shrink: scale end verts along normals (Alpha Omega Man, 2021).
6. Male and female share `human_skeleton` (Jarl, Litauen, 2026). No known way to disable `_slim` for specific gloves (open since 2021).

**Gotchas**
- Long sleeves can turn into needles in the slim version (ADamnSexyName, 2021).
- Keep a seam below the elbow so the slim cut works (TH3WICK3D1, 2020).

Wiki: https://docs.bannerlordmodding.lt/3d/workflow/ - adds detail (workflow lists the variants but not the fallback and `_converted_slim` rules).

## 5. Helmets: hair, ears and head cover

1. Rig as in section 2, check skinning, export.
2. Item XML `hair_cover_type` controls which hair shows. Behaviour varies per hairstyle: type1 lifts hair and chops ears, type2 hides ears; try each (PlebeianKilo, Ragnar, 2021-22; confirmed). Ears can be handled with a `deform_key id="hide_ears"` set to auto-activate when ears are hidden (John_M, 2021; unverified). Beard cover types work alike.
3. Hiding ears with the hair flag needs an edited head mesh, or the ears vanish (Chase K, 2024; unverified).
4. Helmets placed in non-head slots may not get head shape keys (TheCoolerJason, 2024; unverified).
5. Coifs and aventails: merge body and head meshes, transfer weights, smooth; hard-painted ones move like a plank (PlebeianKilo, Chase K).
6. Animal-head helmets can sit in the cape slot stacked over a real helmet in XML (unverified, 2020).

**Gotchas**
- A hat split into moving parts: each needs skinning and identical weights (Ramsay, 2022).
- Weighting a bascinet 50/50 to neck and spine locks rotation (Kipsta, 2026; unverified).

Wiki: https://docs.bannerlordmodding.lt/modding/items/ - new (hair cover and ear behaviour).

## 6. Gloves and hand morphs

1. Hands animate through morph targets (about 26 hand keys; heads about 100); finger bones never animate. A glove without keys shows an open palm (John_M, Ragnar; confirmed). Early on John_M called it keyframe-driven rather than named shape keys (disputed in 2021; later reports consistently describe shape keys).
2. Easiest path: edit native gloves (delete parts only; rebuilt areas lose keys and weights). New gloves: Gulag's script generates the keys from the rigged hand (Chase K, Benjo, 2026); older route is the Hand Morphing tool and tutorial (Enpremi, John_M). Modelling closed fists avoids it.
3. Blender: Basis plus 26 shape keys in native order (25 or 27 reportedly work), names irrelevant, second key right after Basis. Keys on LOD0 (disputed: Chase K says each LOD needs its own, Ragnar and KEYforce say LOD0 only). Apply every modifier except Armature (GulagEnabler, KEYforce, John_M, 2023-25).
4. Delete `r_finger`/`l_finger`; no Weighted Normals; no weights on finger bones; weight from the hand mesh only (Chase K, 2024).
5. FBX export: untick Apply Modifiers (keys vanish otherwise); untick Triangulate if keys still vanish. OBJ loses keys (Ragnar, 2022).
6. Editor import mode must be "morph animations", not separate meshes, or keys appear as separate times (DirtyFaced, 2024). No "skinning precise" with morphs (DrakyD, 2025).
7. Item XML: hide hands / `covers_hands` true or morphs never play. Bracers (no morphs) use false (Ragnar, Chase K, 2023). Wrist clipping with bracers is an engine trade-off (PlebeianKilo, 2022).
8. Test: add the glove as **mesh** (not "human") in Model Viewer, animation tab, enable Morph Target (Ragnar, John_M, 2022-23).
9. Works in viewer, not in game: rename the mesh, delete old weights and re-transfer, reimport (Chase K, Ramsay, 2024; disputed: some cases never explained).
10. **Blender breakage (since 4.0 per 2025 reports):** morph-exported gloves and meshes show wild or black shading. Import the FBX into an older Blender (4.0 or earlier per Byako, pre-4.0 per Ramsay) and re-export, or use "recompute normals" in the editor import settings (Byako, Chase K, Shawmuscle, Haterade; confirmed). Dark glove morphs also fixed that way (Anate, 2026). Earlier, 1.8 moved morphs to the GPU and some native gloves lost rigging (lemanier, hunharibo, 2022; unverified).
11. Mittens: re-transfer weights from the base body (Benjo, 2026). Finger morphs are intentionally asymmetric.

**Gotchas**
- `covers_head="true"` disables hand morphs (Byako, Secretblaze123, 2024-25). Dejan (TW) reported a fix in the 1.4.2 beta; Benjo still saw it on live (disputed).
- Ignore finger bone weights entirely.

Wiki: no page covers hand morphs - new. Nearest: https://docs.bannerlordmodding.lt/3d/model_viewer/

## 7. Head and body morphs, item XML covers_* flags

1. `covers_body` hides torso and legs; feet remain. Disabling the torso also removes upper arms and thighs (ThrashManiac9, Ramsay, 2020-22; confirmed). Cover types run all, none, 1-3 between. Armour cannot take skin tone because the engine passes no vertex colours to the skin material (Ragnar, xenoargh, 2023).
2. `covers_head` works on a body-slot item (a cape with body subtype) but crashes on a head-slot item, and crashes loading saves with the item equipped (Ramsay, Apolon, Dejan TW, 2021-25). Fixes were announced for 1.6.1 and the 1.4.2 beta; status disputed, test on your version.
3. Any body part can be hidden in XML with no downside except gaps (Carolina Warlord, 2024). Underwear shows unless body armour is worn; use a tiny hidden piece (PlebeianKilo, 2021).
4. Body size sliders are skins.xml bone scales, not morphs; only hands and head have morphs (hunharibo, 2023). Face keys: exactly 100 plus `basis` and `basis_0`, native order, names irrelevant (Ragnar, 2023); hair, beard, eyebrows follow them. A head without keys has no expression (Ragnar, 2022).
5. Armour placement is weight painting, not slot XML; pieces can occupy any slot (hunharibo, 2024; unverified).

Wiki: https://docs.bannerlordmodding.lt/modding/items/ - new (covers_* quirks). Race and morph basics: https://docs.bannerlordmodding.lt/guides/custom_creature_race/

## 8. Diagnosis: T-pose, floating, invisible, crash on equip

| Symptom | Cause | Fix |
|---|---|---|
| Helmet floats or trails the head; one pad ignores skeleton | Skinning off | Tick Skinning (Other tab) |
| Crash on equip or entering battle | Skinning missing; skeleton not `_notused`; `covers_head` on head slot | Tick skinning; rename; move covers_head to a body piece |
| T-pose, static box, sideways item | Armature not exported or modifier applied | Export mesh plus `human_skeleton_notused` together |
| Exploded, offset, rotated, bobbing | Leaf bones on; skeleton named `human_skeleton`; auto-weights | Leaf bones off; rename; clear groups, assign head |
| Invisible in game, fine in viewer | Skinning missing; wrong `mesh=` | Check skinning and XML name |
| Shield or weapon invisible | Skinning ticked on static mesh | Untick |
| Helmet sways with body | Weighted to spine or pelvis | 100% head only |
| Noodle arms, buckle follows body | Stray, low or un-normalised weights; over 4 influences | Clean, Limit Total 4, Normalize All |
| Gloves open palm | Missing keys or hands not hidden | 26 keys, `covers_hands`, morph import mode |
| Glove black or "paper" | Newer Blender FBX | Re-export from old Blender; recompute normals |
| Gloves fine in viewer, broken in game | Mesh name or old weights | Rename, re-transfer from hand mesh |
| Female wears male mesh | No `_converted` / `_converted_slim` | Add both |
| Ears missing under helmet | Hair cover type | Try other `hair_cover_type` |
| Editor crash on skeleton | Over 64 bones | Cut bones |
| Battle freeze with 1400-4000 poly helm, no log | Unknown (disputed: DAE then OBJ both blamed) | Replace LOD0 of a working helm |

## Contributors

gkx, PlebeianKilo, Apolon, Apalmemnom, TurboLennsson, ThrashManiac9, John_M, TH3WICK3D1, nwetta, Jester's Ego, Hayster, Not A Box, Skye, Ragnar Hrodgarson, Dawa, KingOfTheN0RTH, Ramsay, ComradeCheekiBreeki, Dejan, Arlaygio, hunharibo, MrAdrianPl, Arcaddon, GulagEnabler, Chase K, slayergodfrey, Byako, Shawmuscle, Caskie, ExpertShip64350, Kemo III, Alexander Drakos, Litauen, Vulfar, M4DCooper, svang5142, KEYforce, DrakyD, DirtyFaced, Secretblaze123, Benjo, Anate, Kipsta, Haterade, TheCoolerJason, xenoargh, Carolina Warlord, Jarl, Astraali, lemanier, Jerkov, ADamnSexyName, Alpha Omega Man, ShortJake, Areldir, Enpremi

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/03-rigged-armour-helmets-gloves-sources.md](sources/03-rigged-armour-helmets-gloves-sources.md)** (170 of 172 linked). The links only work for members of the Mount & Blade Modding server.
