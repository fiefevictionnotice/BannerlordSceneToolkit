# Cloth Simulation and Physics Bodies (bo_ meshes) for Bannerlord

Status: community-sourced, compiled 2026-10-10 from the #bl-3d-art channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - Cloth is driven by vertex **alpha** (not vertex color, not weight paint): white = simulated, black = pinned. You always need some black, or the mesh falls away.
> - Switching cloth on resets that mesh's normals, even on black verts. Put cloth in its own material/submesh or give it a `clo_` sim mesh, or you get dark patches.
> - Cloth only simulates on LOD0, and is off in siege/castle and conversation scenes. Always weight-paint the cloth parts to the skeleton as a fallback.
> - The cloth editor is flaky. Save repeatedly, check the `_geo.tpac` is rewritten, delete/reimport on crashes. Vanilla `clo_` meshes and collision bodies are protected: copy, rename, repaint.
> - Collision for props is just a decimated copy named `bo_<name>` whose material name is a game physics-material name. A wrong name, pivot, or missing material causes crashes and endless loading screens.

Scope note: the source is community chat, not official documentation. Items marked "(unverified)" have one reporter; "(disputed: ...)" items have conflicting reports. Most claims date from 1.5.x to 1.2.x-era editors, so retest on your build.

---

## 1. Painting the cloth mask (vertex alpha)

1. In Blender, add a **Color Attribute** on the mesh (only one; extra attributes cause trouble in 2026 reports: KEYforce, 2026).
2. Paint the **alpha** channel: **white (1) = simulated, black (0) = pinned/fixed**. Paint black where the cloth meets the body, pole or belt, white elsewhere (gkx, TW, 2020; Dawa, 2022; hunharibo, 2024; KEYforce, GulagEnabler, Benjo, 2026).
3. Keep at least some black vertices. All-white cloth drifts off, sinks, or falls to the knees (Ragnar Hrodgarson, 2022; hunharibo, Chase K, 2024). All-black paint can freeze the viewer (PlebeianKilo, 2021, unverified).
6. Paint the **render mesh and the sim mesh** identically (Ragnar, Not A Box, Ramsay, 2022).
7. In the Bannerlord material, tick `disable_vertex_color_alpha` so black-alpha verts do not go invisible. To test the paint, temporarily untick it: if parts of the mesh vanish, your alpha is correct (Not A Box, PlebeianKilo, 2022).

**White vs. black: settled.** Every statement of the convention in this extract says white = free/simulated and black = fixed, from the earliest report (GulagEnabler, John_M, 2020) to the newest (KEYforce, 2026), and the wiki agrees. No bullet here claims the reverse, though the task brief says the wider chat disagreed; by majority and recency, white = simulated. If you see "black = cloth" elsewhere, treat it as wrong. A likely source of confusion: painting vertex *color* (RGB) instead of alpha does nothing (hunharibo, BRE_Sigismond, Ramsay, 2024-2025).

Gotchas
- Vertex weight paint is a different thing (the skeleton). Cloth does not read it.
- A cloth weight of 1 allows about 1 m of travel (GulagEnabler, 2020).
- Painting alpha across a whole armor makes black verts show as shadow; split cloth into dot-suffix submeshes (Chase K, Ramsay, Dawa, 2022-2023).

Wiki: https://docs.bannerlordmodding.lt/3d/cloth_physics/ - already covered (basic white/black rule); the alpha-test trick and gradient advice are new.

---

## 2. Setting up cloth in the editor

1. Import the FBX. Make sure the mesh is **rigged, single-material for the cloth part, and not open elsewhere** (Enpremi, PlebeianKilo, 2021).
2. Open the Cloth Editor, pick the mesh, tick **Use cloth**, and choose a material preset (medium leather is a good default) and a collision body (Not A Box, John_M, 2021; Alexander Drakos, Ragnar, 2023).
3. Set max distance around 0.2-0.3 as a start (Alexander Drakos, 2023). Lower it if the cloth flaps or floats; make it heavier if it flies.
4. If the part needs a sim mesh (section 3), assign it under *Simulation mesh properties*, not *Render mesh* (Ragnar, Dawa, 2022).
5. Press **Save mesh settings**. Weight at least one top and one bottom point when adding collision bodies (Ramsay, 2021).
6. Test in the model viewer with a human asset, not the inventory. Cloth is not shown in inventory previews; the weights set the look there (Not A Box, PlebeianKilo, 2022; John_M, Dawa, MRay6000 (TW), 2021-2022).

Gotchas
- "Cloth simulation must be enabled from config" means `enable_cloth_simulation` in `engine_config.txt` is off (-1). Set it to 1, and recheck after game updates, which reset it (PlebeianKilo, 2021; gazu, 2023; Chase K, Jarl, hex, 2026). Also enable cloth in the in-game options (PlebeianKilo, John_M, 2021).
- Cloth cost scales with polys in the simulated region, collision-body complexity, and preset stiffness. Higher subdivision gives smaller folds, not cleaner ones. Skip cloth on tiny parts such as neck mail (Ragnar, 2022).

Wiki: https://docs.bannerlordmodding.lt/3d/cloth_physics/ - adds detail (the wiki page mostly links to official guides).

---

## 3. Sim meshes (`clo_`) and layered cloth

Use a sim mesh when the cloth is multi-layer, chain-like, disconnected, or when black verts darken the shading.

1. Duplicate the relevant part of the armor, flatten it to a **single layer**, simplify it, and name it with the prefix **`clo_`** (Ragnar, Dawa, 2022; PlebeianKilo, Not A Box, 2022).
2. Give it the same vertex alpha paint (and weights/material) as the render mesh. Place it slightly inside the visible layer but **outside the collision body** (Ragnar, GulagEnabler, 2022).
3. Only one sim mesh per piece, and it cannot be multi-layered. The sim mesh is the only thing simulated; the render mesh follows it at a fixed offset, so layered cloth keeps its gap (John_M, Jester's Ego, 2021).
4. For multi-layer clothing (skirt over mail), use the lowest layer only, plus a suitable collision body (Ragnar, Alexander Drakos, 2023).
6. Disconnected pieces (beads, a tabard cut at seams) fall off unless they have a sim mesh or a plane through them (Chase K, Ragnar, 2023).
9. Keep the sim mesh low poly; high-face meshes cause random cloth-editor crashes (Byako, PlebeianKilo, GulagEnabler, 2024).

Gotchas
- Sim mesh material does not matter, and it can sit under the render mesh (John_M, 2021).
- Vanilla `clo_` meshes are "protected": errors, flying vertices, failed saves. Early advice was to reset and retry (PlebeianKilo, Areldir, 2021). Newer, safer practice (prefer this): **export the vanilla mesh, rename it, reimport it in your module and repaint** (Nicole UK, order_without_power, 2021; Litauen, Chase K, GulagEnabler, 2024-2025).
- Do not put an unrelated mesh in a slot meant to copy vanilla physics; a helmet "floated away" (PlebeianKilo, 2020). Version note: this was the 2020 slot behavior; the later model is the `clo_` sim mesh you author.

Wiki: https://docs.bannerlordmodding.lt/3d/cloth_physics/ - new (the wiki does not describe sim meshes).

---

## 4. Normals and shading problems

1. Enabling cloth on a material **resets that mesh's normals**, including on black-painted verts, giving dark patches or flipped shading (Enpremi, PlebeianKilo, Dawa, safran, 2021-2022). A TW forum suggestion was filed.
2. Fix: put cloth parts on their **own material** and keep metal/rigid parts on another. Importing the cloth part as a separate `.001` mesh also works. A `clo_` sim mesh clears black-vertex shading bugs (PlebeianKilo, safran, jacobins, 2021; Litauen, Chase K, 2024-2025).
3. Do not let the render mesh penetrate the sim mesh.

Gotchas
- Multi-material meshes: cloth scaled unevenly with head size (DirtyFaced, 2024) and shifted in game vs. the editor (Benjo, KILO TCDFB, 2026, unverified; workaround: one sim mesh per variation). (disputed: Ragnar said multiple materials help cloth editing; DirtyFaced disagreed, 2024.)

Wiki: https://docs.bannerlordmodding.lt/3d/cloth_physics/ - new.

---

## 5. LODs and where cloth does not run

- Cloth does **not simulate on LOD1 and above**; pieces freeze at the first LOD (Chase K, 2024; Chase K, KILO TCDFB, 2026, confirmed). Version note: the 2022 report framed range as "set by the LOD list, remove LODs to extend it" (Not A Box, 2022); the 2024-2026 reports say LOD1+ never simulates. Prefer the newer one.
- Cloth is **off in siege/castle scenes and conversation scenes** (PlebeianKilo, hunharibo, DarthKiller454, 2025). Item Preview and low-end graphics settings fall back to rigging.
- Not every player has cloth enabled (Ramsay, 2024; PlebeianKilo, Ragnar, 2022).

Safe fallback
1. Weight-paint the cloth parts to the skeleton as if there were no cloth (blur weights across skirts).
2. Add cloth on top for the live motion.

Gotchas
- Cloth on capes is disabled when a shield is on the back (patch note, 2021; DirtyFaced, GulagEnabler, 2025). (disputed: Dawa said it only reduces or stiffens shoulder-slot cloth, 2023, and Ragnar saw capes still clip through shields. Treat as "behaves badly with a back shield".)
- Cloth works on whole-piece weapons (bows, crossbows) and shields, but **not on crafted weapon pieces**; exploits were patched (John_M, Jester's Ego, Dawa, Ramsay, 2021-2022). A weapon with cloth (banner guard) reportedly crashed when leaving inventory (Artem, 2020, unverified).

Wiki: https://docs.bannerlordmodding.lt/3d/cloth_physics/ - adds detail (the wiki only notes that some scenes lack cloth).

---

## 6. Collision capsules (cloth collision bodies)

1. Collision bodies are **capsules attached to bones**, shared by every asset that uses them. Moving a capsule changes all of them. Do not edit stock bodies (John_M, bamba jay, Litauen, 2025).
2. Pick a vanilla body first: "leg medium" for skirts, medium leather for most things. Smaller leg collision means less float on mounts (Ragnar, Not A Box, 2022).
3. For a custom one: in the Cloth Editor use Edit > add collision mesh, duplicate an existing body, assign bones to every capsule (bones are not carried over by the copy), then adjust (Dawa, 2022). The result lives in `cloth_bodies.xml`. You cannot import collision from a model (Ramsay, Ragnar, 2023; Litauen, 2025).
4. One mesh uses one collision body. Split into submeshes if parts need different bodies (the sim mesh is still shared) (Ramsay, Ragnar, 2023, unverified).
5. Keep the cloth mesh outside the body (inside gets pulled in). Use a smaller leg capsule or a wider sim mesh. Arm clipping means the body must include arms (Ragnar, Ramsay, GulagEnabler, Gambit77, 2022-2024).

Gotchas
- Oversized thigh/pelvis bodies make riders float in the saddle. Three custom bodies (male, female, mounted) is a common outcome (Ragnar, Not A Box, 2022).
- The vanilla `human_body` preset collides only with legs; a mail layer will pass through a skirt (Dawa, 2022, unverified).
- Bodies **do not scale with character shape keys**, so long cloth clips on bulky characters; split long skirts (Benjo, Chase K, 2026).

Wiki: https://docs.bannerlordmodding.lt/3d/collision_body/ - already covered for the create/copy workflow; the multiplayer, shape-key, and one-body-per-mesh notes are new.

---

## 7. Cloth editor crashes and save/recook quirks

1. If saving crashes: check the mesh is rigged, single-material, and not open elsewhere (Enpremi, PlebeianKilo, 2021).
2. **Delete the mesh and reimport before changing cloth settings**; otherwise settings come out wrong (Enpremi, PlebeianKilo, 2021).
3. Reapplying after a sim-mesh change can misbehave: delete both meshes and redo, click Save mesh settings repeatedly, or restart the kit (Dawa, PlebeianKilo, Ragnar, 2022). Newer advice: save/recook several times and confirm the `_geo.tpac` is rewritten (Chase K, Jarl, hex, 2026).
4. Crash on opening the editor: delete the offending mesh from both AssetSource and Assets (the tpac), then reimport (Ragnar, Alexander Drakos, 2023). Deleting the cloth `.geo` file in the assets folder also cleared one corruption (Chase K, 2025, unverified).
6. "Package is protected" on save means the mesh is a native asset: export, rename, reimport in your module (Nicole UK, order_without_power, 2021).

Gotchas

Wiki: https://docs.bannerlordmodding.lt/3d/cloth_physics/ - new.

---

## 8. `bo_` physics bodies for props and prefabs

1. Make a copy of the visual mesh, decimate it hard, and name it `bo_<meshname>`. It becomes a physics shape on import (Zodiac, Secretblaze123, Paski, 2024-2025; Snorri, DarthKiller454, 2026).
2. Name it in Blender by **mesh name** (not only the FBX name), export it in the same FBX as the visual mesh, and tick *import physics shape* in the editor (hunharibo, 2023; Snorri, 2026).
3. Version note: 2020-2021 reports said to export bo_ as a separate FBX and assign it in the entity's physics properties (John_M, Dawa, 2021). The 2024-2026 reports use the same FBX. Use the newer flow and fall back to a separate FBX if import fails.
4. Give it a **material whose name equals an in-game physics-material name** (metal, wood, mud, etc.) (Ragnar, Xtra Dip, 2022; hunharibo, 2023). (disputed: Secretblaze123, 2024, said prop bo_ meshes carry no materials; the 2022-2023 reports say a matching name is required. Safest: name one.)
5. Without a bo_ mesh, buildings are walk-through. In the scene, set the entity's Physics Shape/body properties (Dawa, Resuprobe, 2021).
6. Character armor needs no bo_ mesh. Weapons need one only on the blade, and weapon hitboxes need `body_name` in the item XML (John_M, TortenSkjold, 2020; Ragnar, 2022).
8. No automatic collision mesh exists; dynamic colliders are converted to convex hulls (gkx, TW, 2020).

Polycount
- Keep it crude: boxes for corners and trim, no tiny cracks unless shootable. Vanilla props are around 100 tris, walls up to about 800 (Ragnar, PlebeianKilo, L. Rizz Hubbard, 2022).
- Scaled-up weapons (giant NPCs) need larger bo_ meshes or gaps appear (Ragnar, BlackRose, 2023).

Gotchas
- A missing physics material crashes the editor on interaction (Ragnar, 2023).
- Wrong bo_ names or an unreset pivot/scale give infinite loading screens and crashes. A shield hitbox appeared 5 m forward from unapplied pivot/scale (Ragnar, DirtyFaced, Alexander Drakos, 2023). Apply transforms before export; shapes offset in game mean the origin differed before and after export (Zodiac, 2024).
- In multiplayer the server holds hitboxes only (DsAssetPackages); client tpacs can differ, so MP hitboxes may not follow your client mesh (Alexander Drakos, 2023).
- `agent_only` / `missile_only` body flags are not mentioned anywhere in this extract.

Wiki: https://docs.bannerlordmodding.lt/3d/environment/ and https://docs.bannerlordmodding.lt/3d/polycount/ - new (the wiki polycount page excludes scene assets, and the environment page has no bo_ section).

---

## 9. Shields

1. Shields need three meshes in the same position: the visual `SHIELDNAME`, `bo_SHIELDNAME` (projectile and melee hitbox), and `bo_cap_SHIELDNAME` (ragdoll hitbox). Materials: `bo_cap_` uses `wood`, `bo_` uses `wood_shield` (wiki).
2. Keep both physics meshes very low poly. Center their origins: an off-center `bo_cap` caused a crash in 1.5.6 when a shielded unit died and dropped the item (PlebeianKilo, 2021, unverified).
3. Wrong `bo_cap` names cause battle-only loading hangs (Ragnar, Alexander Drakos, 2023). A missing bo_ material shows as missing in the shield setup (hunharibo, 2023).

Wiki: https://docs.bannerlordmodding.lt/3d/shields/ - already covered for naming and materials; the crash and holster notes are new.

---

## Open items for reviewers

Contributors: gkx (TW), GulagEnabler, John_M, PlebeianKilo, Artem, nwetta, TortenSkjold, Not A Box, Jester's Ego, Areldir, kowal25, Dawa, Resuprobe, Ramsay, Nicole UK, order_without_power, safran, jacobins, MRay6000 (TW), Ragnar Hrodgarson, Enpremi, Chase K, dzor7266, DirtyFaced, Xtra Dip, L. Rizz Hubbard, Alexander Drakos, BlackRose, gazu, Efarys, hunharibo, BRE_Sigismond, Litauen, Byako, Apolon, Gambit77, TheCoolerJason, ExpertShip64350, Maroon, Secretblaze123, Zodiac, Paski, bamba jay, DarthKiller454, TurukBeg, KILO TCDFB, KEYforce, Benjo, Jarl, hex, ExoBannerlord, Snorri, Dejan (TW)

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/05-cloth-and-physics-bodies-sources.md](sources/05-cloth-and-physics-bodies-sources.md)** (85 of 86 linked). The links only work for members of the Mount & Blade Modding server.
