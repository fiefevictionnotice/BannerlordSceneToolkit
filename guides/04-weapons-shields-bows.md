# Bannerlord Modder Guide 04: Weapons, Shields, Bows

Status: community-sourced, compiled 2026-10-10 from the #bl-3d-art channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - Every weapon is a crafted weapon. "Non-craftable" just means a piece with `is_hidden="true"`; a single mesh that skips crafting works but deals roughly 10% damage (torch-like).
> - Four XMLs must agree on every ID: crafting_pieces, crafting_templates, weapon_descriptions, weapons. A typo gives an empty slot, a crash at battle load, or an infinite loading screen.
> - Center each piece's pivot on its bounds, set `length` in cm to the true Blender Z size, and treat offsets as small tweaks. Only the handle's `piece_offset` places the hand.
> - Since game 1.3.13, mod weapons can turn invisible unless they have a LOD1; battles effectively use LOD1 where you expect LOD0.
> - Shields need three meshes (visual, `bo_`, `bo_cap_`) with correct physics-material names, no skinning, one material, and heraldry only via tableau (no team colours).

Conventions: XML lengths are centimetres, Blender is metres. "Disputed" means sources disagree. Version notes say which game build a finding applies to.

---

## 1. Build a craftable weapon (the four XMLs)

1. Model each part (blade, guard, handle, pommel; polearms add head/shaft) as its own mesh.
2. Register each part in `crafting_pieces.xml`: `id`, `piece_type`, `mesh`, `length`, `weight`, culture, tier.
3. Put the pieces in a `CraftingTemplate` (`UsablePieces`). Make your own template rather than editing a native one (Kaffegorare, PlebeianKilo, 2020-21). A spear needed a 4-piece template even though the docs called pieces optional (Not A Box, 2021).
4. Make sure `weapon_descriptions.xml` lists the pieces. Since 1.7 your module cannot reference native weapon usages, so copy the ones you need into your own `weapon_descriptions.xml`, or the template crashes or vanishes (PlebeianKilo, Carolina Warlord, Hayster, 2021).
5. Define the finished item as a `CraftedItem` in your weapons XML, pointing at the template.
6. Add name strings in your own strings XML. Missing strings only show "Error Strings XML" labels (Ragnar, Not A Box, 2022).
7. To extend native templates use XSLT instead of copying them; mismatched piece IDs there crash smithing or hide the weapon (Ragnar, Skye, Ramsay, 2022-23; Ramsay, Haterade, TheCoolerJason, 2025).
8. Reference any new XML file in SubModule.xml, rename your files, and copy vanilla template/part IDs rather than overriding them (Ragnar, 2023).

Naming rules:
- Piece IDs lowercase, no capitals or numbers (TAL[OS], Clockwork, 2022, unverified).
- A custom template must copy all weapon entries under it, or you get "Index was outside the bounds of the array" (TAL[OS], 2022, unverified).
- Typos surface only as "Could not load merged xml file".

Smithy visibility:
- `is_hidden="true"` on a piece hides it from the smithy (PlebeianKilo, 2021).
- A weapon needs all its template's pieces. For a smithy template with fewer slots, supply a blank/default piece or a custom template (Ragnar, Ramsay, Carolina Warlord, 2022).
- Scabbards are not crafting pieces; a competition set may use at most 16 pieces, each once, on one atlas (Dejan, TW, 2022).

Gotchas
- Crash at battle load: usually mismatched names between the XMLs. Crash opening inventory with armour: mesh/ID mismatch. Save crash on load: misspelled culture (Not A Box, PlebeianKilo, 2021).
- Changing pieces of an existing `CraftedItem` crashed the inventory in 1.6.x. TW (kowal25, 2021) added the console command `crafting.reload_pieces {MODULE} {XML}` to reload piece XML without a restart.
- Mod load order matters: a later mod can override your pieces (Skye, 2023).
- (disputed: Ramsay and TheCoolerJason, 2026, say two mods XSLT-patching the same template can cancel each other; the same thread calls XSLT safe if IDs and mesh names are unique.)
- A weapon overwriting a native one can revert to the native mesh in battle while looking right in inventory (Craig_Tea_Nelson, 2024, unverified, unresolved).
- A misspelled `bo_` body in the item XML can freeze the game in battle while inventory and towns work (hunharibo, 2024, unverified).
- Particle effects via XML only work on ammo (arrows, bolts); anything else needs code (PlebeianKilo, 2021). To change a vanilla missile particle, recreate it under the vanilla name (shrike, npc99, 2023-24, unverified).
- Boolean-style flags such as `MultiplePenetration` or `AmmoSticksWhenShot` count as true if the attribute exists at all, whatever its value; delete the attribute to turn it off (Sly, PlebeianKilo, 2025).
- Extra usages (halberd spear+axe, bayonet) are another `<Weapon/>` block in the item XML; damage may not match the listed values (PlebeianKilo, 2021, unverified).
- Speed and damage derive from part weight and length. Keep length true, tune weights, then use the blade's damage multiplier (Ragnar, 2022-23, unverified).

Wiki: https://docs.bannerlordmodding.lt/modding/weapons/ (the mirror has no `entities/weapons` page; this is the weapons page). Already covered: the four-file split, `is_hidden`, `is_default`, the `holster_mesh` field, the unlock-all-pieces console command. Adds detail: ID-matching failures, the 1.7 usage-copy rule, XSLT, `reload_pieces`, flag behaviour.

---

## 2. Model and align crafting pieces

1. Build the whole weapon in Blender as it should look, then apply transforms.
2. Split into pieces. Give each piece an origin at its bounds center, not "origin to geometry" or center of mass, which shift tapered blades (Ragnar, 2022; Mick Williams, 2025). Trick: add a wireframe cube as bounding box, snap the cursor to it, set origin to cursor (Ragnar, BlackRose, Jesse, 2022). Script form: `origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')` (Mick Williams, 2025).
3. Orient the long axis along Z for polearms; some sword reports lay blades along a horizontal axis, so compare with an exported native sword. The grip center is x=0, y=0 for the whole weapon (Hayster, Ramsay, Ragnar, 2021-23).
4. Read the piece's Z dimension in the N panel (after resetting rotation) and enter it in cm as `length` (Ragnar, Ramsay, Alexander Drakos, 2023). Measure contact point to contact point. Wrong length leaves gaps in smithy thumbnails.
5. Fix overlaps and gaps with `previous_piece_offset` / `next_piece_offset` (Ragnar, John_M, 2023).
6. Set hand position with the handle's `piece_offset`, a Z shift that also moves the second-hand grip (Ragnar Hrodgarson, 2022; John_M, 2023). Rotation can only be changed in Blender (Mick Williams, 2025).
7. `distance` to next/previous piece exists for off-center pivots and cannot be 0 (Ragnar Hrodgarson, 2022). Length 0 plus a `piece_offset` lets a guard sit far down the shaft (Alexander Drakos, 2023).
8. Validate scale by exporting native parts via the editor or TpacTool and overlaying yours; bad scale shows as a giant polearm (Dawa, Carolina Warlord, 2021-22).
9. Test one custom piece inside a template of known-good native pieces and swap parts one at a time (PlebeianKilo, 2021). Avoid native pieces that carry their own offsets while debugging (PlebeianKilo, Dawa, Isaac, 2021).

Reference values: vanilla polearm shaft thickness about 3.26, about 3.9 before it looks like a log (EnumaEllis, 2024, unverified). Estimate weight from similar vanilla pieces (BraquemardLeBrav, 2024, unverified).

Gotchas
- Offsets should be minor; centered geometry is the rule, asymmetrical heads the exception (Claiton Bigsby, Carolina Warlord, 2024).
- Blade gaps appearing only on equip (disputed: Ragnar says it is purely a pivot/mesh fault; Claiton Bigsby and Ramsay saw it with native parts after some patches, 2022). A pre-assembled entry avoided it.
- Piece `mesh=` must match the resource-browser name exactly; a misspelling gives an empty slot (PlebeianKilo, 2020; Haterade, 2025).
- Skinning on a weapon material hid metal parts and made crafted pieces vanish in hand. Disable skinning on weapons (PlebeianKilo, Ernst, 2020).
- Default crossbow animations assume roughly y+ down, z+ forward, x+ right; fix orientation in Blender (Kaffegorare, 2020, unverified).
- Pack a weapon set on one atlas like vanilla (TH3WICK3D1, gkx, TW, 2020).
- A shaft/head lighting seam is a normals problem, not a bake-together problem (Ragnar, Skye, 2022, unverified).
- Blood trails on blades need `use_vertex_color` and painted vertex color (KaBox, 2022, unverified, unresolved).
- Dual-wield via XML alone treats both swings as one blow using the right weapon's stats (nwetta, 2023, unverified).
- BlackRose published a Blender script that writes the crafting_pieces XML, inferring `piece_type` from the mesh name (2024, unverified).

Wiki: https://docs.bannerlordmodding.lt/modding/weapons/ (and https://docs.bannerlordmodding.lt/3d/workflow/). Status: new for `piece_offset`, `previous/next_piece_offset`, `distance`, pivot and length workflow; the wiki only shows the XML skeleton.

---

## 3. Single-mesh weapons and their damage penalty

Use for torches, training weapons, one-piece spears/lances, banners, and anything needing cloth.

1. Skip crafting: one mesh, a collision body, and an item entry of the "Item" weapon type.
2. Expect heavily reduced damage. Reports: capped around 9-30 (Ramsay, PlebeianKilo, Ragnar, 2022); about 10%, like the torch (hunharibo, 2025). Version note: still true in 2025 without custom code.
3. Mainstream advice is to split into blade/guard/handle/pommel and craft it (AverageAlien, John_M, 2020; Ragnar, 2023). One 2023 view: single-piece weapons are not cleanly supported.
4. Cloth physics: (disputed: 2021 and early-2022 reports say crafted pieces cannot have cloth, only single-mesh items such as shields, one-piece lances and off-hand banners; late-2022 reports got cloth working on flails and spear banners, with skinning on the cloth material.) Newest view: works, but test in game. Banner flags on crafted pieces would need shape keys, which are unavailable (Ramsay, PlebeianKilo, 2022).
5. Banners are usually shield-type items in the left hand; a one-piece pole+flag works with black vertex alpha on pole and flag base (PlebeianKilo, Kysaro, 2021, unverified).
6. Banner/faction colour does not work on crafting pieces (Ramsay, Snowi, 2024, unverified).

Gotchas
- Flails: the chain is cloth-simulated and the hitbox is the handle extension, not the head. Use a chain/hard preset, low-poly or alpha-plane chains, and a simple sim mesh (hunharibo, Ragnar, Ramsay, 2023).
- `scale_factor` did not scale banners via XML for one user (Phillus, 2020, unverified).

Wiki: https://docs.bannerlordmodding.lt/3d/cloth_physics/ . Status: new (damage penalty, cloth contradiction).

---

## 4. Shields

1. Make three meshes at the same origin and pivot: `SHIELD`, `bo_SHIELD`, `bo_cap_SHIELD`.
2. Keep physics meshes simple (front-face shapes). A bad `bo_`/`bo_cap_` mesh, or a high-triangle `bo_` split, gives an infinite loading screen (Ramsay, ThrashManiac9, Secretblaze123, 2024-25).
3. Name physics materials in Blender before import. Wiki: `wood` on `bo_cap_`, `wood_shield` on `bo_`. Community: valid names include `wood`, `wood_shield`, `metal_weapon`, `metal_shield`; a wrong name crashes the kit when clicked (AggressivePo, Ragnar, 2022). (disputed: 2021-22 posts call `bo_` the hitbox and `bo_cap` the projectile body; the wiki says `bo_cap` is likely the ragdoll hitbox and `bo_` the projectile/melee one. Unresolved.)
4. In the item XML set `body_name="bo_cap_..."` and `shield_body_name="bo_..."` (Ragnar Hrodgarson, PlebeianKilo, 2021-22). A worked set: `item_holsters="shield:shield_2:..."`, flags `HeldInOffHand` and `ForceAttachOffHandSecondaryItemBone`, `weapon_class="LargeShield"` (Not A Box, 2021, unverified).
5. Disable skinning; do not rig to a bone (PlebeianKilo, 2020; Ragnar, Alexander Drakos, 2023).
6. One material per shield. Multi-mesh shields use names like `shield_001` (Ragnar, 2022, unverified). (disputed: PlebeianKilo got two materials working, Gulag says one, 2022.)
7. Tune hold pose with XML position/rotation; pre-offset the mesh in your DCC as native shields do (Meowkov, 2023). Check with the model viewer on the forearm bone with defend animations (deleted users, 2020).
8. Usage `hand_shield` (round, Sturgian-style) versus `shield` (heater) (PlebeianKilo, 2020; Litauen, karaboga pasha, 2024).
9. A shield with a two-handed weapon needs a custom item usage that drops the empty-off-hand requirement (Ragnar Hrodgarson, RBM Philozoraptor, 2022, unverified).

Heraldry
- Only tableau (clan sigil) works; team colour/ID mask does not (Ragnar, Alexander Drakos, 2023; Ragnar, Terco_Viejo, 2023-24). Version note: 1.8.0 fixed opacity on shield items, but the colour-ID mask still fails on shields (PlebeianKilo, Ragnar, 2022).
- Mark the sigil area in the diffuse alpha channel; put a banner texture in `diffuse2` to preview (PlebeianKilo, Dawa, 2022). Position with Vector Argument 1; rotate texture/UV in Blender if the sigil is sideways or upside down (PlebeianKilo, 2021).
- `dont_use_tableau` stops tableau on a material; `use_double_colormap_with_mask_texture` removes the icon, leaving background colour; XML `using_tableau="false"` also gives solid colours (Ragnar, Terco_Viejo, 2023-24, each single-report).
- Banner background must match the top-box colour (BoominRandy, 2021, unverified).

Gotchas
- Animation clipping is fixed only by reshaping/rotating the mesh or moving the handle; some designs cannot work with vanilla animations (John_M, PlebeianKilo, 2020).
- Holster position can be shifted in XML, but rotation needs a copied `item_holster` with changed `holster_rotation_yaw_pitch_roll` (PlebeianKilo, John_M, Cozur, 2021).
- No hit/collision for clients or other players: ship the body mesh in both AssetPackages and DS asset packages, ticking Dedicated Server when publishing (DirtyFaced, 2023; WolFChaos, 2026, unverified).
- No custom hitbox: reuse native `shield_body` names or battles hang; removing custom physics shapes fixed shields floating on the ground (BeerRune, karaboga pasha, 2026, unverified).
- Native `bo_` meshes are reusable if the shape matches; some custom `bo_` meshes crashed (Dawa, Claiton, 2022).
- Cloth on shield decorations works (Ragnar, 2022).

Wiki: https://docs.bannerlordmodding.lt/3d/shields/ . Already covered: three meshes, material naming, skinning off, sigil/tableau workflow, Vector Argument 1. Adds detail: server asset-package rule, hang causes, flags, usages, XML hold-pose tweaks.

---

## 5. Bows and crossbows

1. Bows are single items, not crafted.
2. Vertex-paint the bend: non-moving parts full green, limb tips and string with less or no green, a gradient between for smooth bending (hunharibo, Ha_rang, TW tutorial quote, 2024).
3. Assign `bow_deformer` (or `longbow_deformer` for long bows) as the final material shader instead of pbr_metallic. No Blender shader is needed and skinning is not required (Dawa, bamba jay, Resuprobe, hunharibo, Chase K, 2022-24).
4. Keep arrow, quiver and bow on separate materials (hunharibo, 2024).
5. Check colours in the editor's import preview, not in Blender.

Gotchas
- (disputed: which channel? 2021-22 reports used red on bending parts; 2023 said bow red, string green, with one user first needing the whole bow red; 2024-04 says green on static parts; 2024-11 and 2025-08 say blue, not red.) Newest (2025, hunharibo, Chase K): blue, with the string needing the deformer shader plus painted alpha. Probably version-dependent; test in game.
- Paint only string and limb ends; painting the whole bow moved the handle when drawing (Kysaro, 2021, unverified).
- Meshes exported from the editor or TpacTool lose vertex colours in Blender; repaint (Dawa, Ragnar, John_M, 2022; hunharibo, 2025).
- Vanilla string meshes are the template for string animation (DrakyD, 2024, unresolved).
- A TW forum tutorial covers custom bows, arrows and quivers (John_M, 2022).

Wiki: not covered (no mirror page mentions `bow_deformer` or vertex-colour bending). Status: new.

---

## 6. Quivers and arrows

1. In the ammo/quiver XML, `mesh` is the nocked arrow and `flying_mesh` the in-flight arrow. Point `flying_mesh` at the full arrow to show heads and fletching (Dawa, PlebeianKilo, 2022).
2. Up to 8 arrows can vanish from a quiver as they are used, driven by vertex alpha of about 0.1 to 0.8, one value per arrow in order (Dawa, 2023, unverified). Javelin quivers use the same vertex-colour shades (Chase K, 2026, unverified).
3. Ranged weapons default to their ammo's holster whenever ammo is equipped, even with their own holster assigned (PlebeianKilo, 2020, unverified, unanswered).

Gotchas
- Editing a vanilla holster in the tools changed the base item, not a module copy (PlebeianKilo, 2020, unverified).
- Only ammo gets XML-driven particles (see section 1).

Wiki: https://docs.bannerlordmodding.lt/modding/weapons/ (only the javelin `holster_mesh` field). Status: new.

---

## 7. Scabbards and holsters

1. Scabbard: a field on the blade crafting piece (reported as `holster_mesh`, with `holster_body_name` and `holster_mesh_length`) pointing at a mesh. Blade-type pieces only; no rigging (Dawa, 2021; Ramsay, 2024; Ramsay, DarthKiller454, 2026).
2. The template's `use_weapon_as_holster_mesh` is an alternative (Dawa, 2021, unverified).
3. No holster: omit the holster mesh. Tall weapons that clip are fixed by lowering the holster position in a custom template (Ragnar, 2023, unverified).
4. Slots live in Native `item_holsters.xml` and can be custom-defined (hunharibo, 2024, unverified). For quiver or shield placement edit the holster entries (PlebeianKilo, Sh1ny4, 2025).

Gotchas
- Throwing axes: a custom holster mesh dropped the other axes from the stack; a vanilla holster mesh fixed it. Holstered textures show the holster's UV unless it shares material/UV (Major Roy, PlebeianKilo, 2021). The native axe holster is a ring, so custom axe textures break on it; deleting it mounted the axe inside the body (Carolina Warlord, 2024).
- A custom blade crashed as a 1h mace on unholster; holster body and `holster_mesh` attempts also crashed (Budmao, Dawa, 2022, unverified, unresolved).

Wiki: https://docs.bannerlordmodding.lt/modding/weapons/ . Already covered: `holster_mesh` BladeData fields. Adds detail: blade-only rule, holster pitfalls.

---

## 8. Invisible weapons and deliberately invisible items

Weapon vanishes in game, likely causes in order:
1. Missing LOD1. Version note: since 1.3.13 mod weapons turned invisible without extra LODs. Fix by adding LOD1 or more (copy LOD0 as LOD1), using a vanilla part, or re-cooking; an "Invisible Weapons Fix" mod circulated (Akaconda, Hyarti, Judgement Majima, PlebeianKilo, 2025-12 to 2026-03).
2. In battle, weapons effectively render LOD1 in place of LOD0 (LOD0 is the smithing preview). Keep LOD1 undecimated or equal to LOD0 if it looks wrong. LOD-skipping bugs are still reported (John_M, Benjo, Gulag, Sly, 2026).
3. Skinning enabled on the material (section 2).
4. Pivot off-center or `length` wrong, so the part sits away from the grip (Ragnar, 2022-23).
5. Misspelled `mesh=`.
6. `using_tableau`: in the sources it only enables heraldry/banner replacement on armour (`using_tableau="true"`, TableauMaskMap, mesh tagged `banner_replacement_mesh`; MRay6000, TW, 2022). No source reports it as an invisibility cause for weapons (unverified). Tableau armours revert to the test banner at distance, an LOD issue (Meowkov, ThrashManiac9, 2023).

Deliberately invisible item (passive back shields etc.):
- Export an empty FBX: add a cube, delete its vertices, export with any material, set the item's `mesh=` to it, keep a `bo_` body (Dawa, 2021; used by RBM).
- Alternative: point `mesh=` at a non-existent mesh id (Hayster, 2022, unverified).

Wiki: https://docs.bannerlordmodding.lt/3d/create_lods/ . Already covered: LOD switch distances (15 m up to 210 m+), weapon LOD section. Adds detail: the 1.3.13 invisibility and LOD1-in-battle behaviour (new).

---

## 9. Testing and the crafting tool

1. Unlock all pieces with the console command `campaign.unlock_all_crafting_pieces` (Alt+`), craft in the smithy, press Ctrl+C to copy the CraftedItem code, paste into your XML.
2. Use `crafting.reload_pieces {MODULE} {XML}` to reload piece XML without restarting (kowal25, TW, 2021).
3. Use the in-game Item Position Editor (sakinolemir) for hold pose (wiki).
4. New piece IDs must also be added to the right categories (Ragnar, Not A Box, 2022).
5. Armour shop frequency follows `value`; a very low value makes it appear (PlebeianKilo, 2020, unverified).

Gotchas
- Adding your piece to a native template crashes; copy the template list into your own (Kaffegorare, PlebeianKilo, 2021). strings.xml is not moddable, so the craft menu shows text errors.
- Old tutorials are outdated; check dates (Ragnar, 2023).

Wiki: https://docs.bannerlordmodding.lt/modding/weapons/ . Already covered: unlock command and copy-to-clipboard flow. Adds detail: `reload_pieces`, category notes.

---

Contributors: gkx (TW), TH3WICK3D1, Dawa, PlebeianKilo, AverageAlien, Ernst, Phillus, Kaffegorare, John_M, AggressivePo, Not A Box, Isaac, BoominRandy, Cozur, Hayster, Ramsay, Carolina Warlord, Major Roy, kowal25 (TW), Kysaro, Ragnar Hrodgarson, RBM Philozoraptor, BlackRose, Jesse, Dejan (TW), Budmao, Claiton Bigsby, Gulag, MRay6000 (TW), TAL[OS], Clockwork, bamba jay, KaBox, Skye, Meowkov, Resuprobe, hunharibo, Chase K, Alexander Drakos, nwetta, shrike, npc99, DirtyFaced, Terco_Viejo, ThrashManiac9, DrakyD, Litauen, karaboga pasha, Ha_rang, BraquemardLeBrav, EnumaEllis, Craig_Tea_Nelson, Snowi, Mick Williams, Haterade, TheCoolerJason, Secretblaze123, Sh1ny4, Akaconda, Hyarti, Judgement Majima, Benjo, Sly, DarthKiller454, BeerRune, WolFChaos, sakinolemir (wiki)

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/04-weapons-shields-bows-sources.md](sources/04-weapons-shields-bows-sources.md)** (102 of 103 linked). The links only work for members of the Mount & Blade Modding server.
