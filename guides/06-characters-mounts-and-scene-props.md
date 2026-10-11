# Guide 06: Characters, Mounts and Scene Props

Status: community-sourced, compiled 2026-10-10 from the #bl-3d-art channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.
Scene building is covered in guides 08-16. Audience: Blender users new to Bannerlord. Version notes track game builds (1.0.x to 1.3.x era).

> **Biggest epiphanies**
> - Custom head material order is load-bearing: skin first, then eyes, then mouth/teeth, all in ONE mesh with Face/Eyes/Teeth tags. Splitting parts breaks character creation (since 1.0.3).
> - `skins.xml` is the whole character system. Never ship a full copy (old copies caused the "tiny heads" bug); patch it with XSLT, and relaunch the game to see changes.
> - Humans (male, female, giants, trolls) normally share `human_skeleton`, and size comes from XML scale, not new rigs. (disputed: one 2022 report said only mounts can use custom skeletons, but the wiki's Custom Creatures pages document custom skeletons for humanoid races: https://docs.bannerlordmodding.lt/guides/custom_creature_skeleton/)
> - Props get collision by naming a mesh `bo_<unique>` and naming its Blender material after an engine physics material; vanilla prefabs are just assemblies of meshes you can find in XML.
> - Gear is one mesh = one material set. Recolour variants need separate imported assets, but environment prefabs can swap materials in the editor.

---

# PART A: CHARACTERS

## A1. Get the reference body and head
1. Spawn the human entity in the Model Viewer (Visual, then Body Parts), or place one in the scene editor.
2. Export with TpacTool: choose "rigged" and DAE to keep the skeleton (kiet, ThrashManiac9, Bogheim, 2020).
3. Open `AssetPackages`, not `EmAssetPackages`, to avoid TpacTool crashes.
4. The body is about 1.8 m in Blender. Use the Adam prefab for scale checks in scenes (Secretblaze123, 2024).
5. TpacTool exports the default head's face shape keys, but every key shows range -1..1; the real ranges live in `skins.xml` (TheCoolerJason, 2024).

**Gotchas**
- Female "converted" armor uses the same `human_skeleton` as male (TheSchwarz, Ramsay, 2022).

Wiki: https://docs.bannerlordmodding.lt/3d/armature_skeleton/ and /3d/workflow/ - already covered (export basics); body-export tips are new.

## A2. Build a custom head (face, eyes, mouth)
1. Start from the vanilla head mesh so the roughly 100 shape keys and morph targets stay intact. Sculpt or mirror only the base shape; keep the vertex count (Ragnar, PlebeianKilo, 2023).
2. Join head, eyes, mouth/teeth and lashes into a single mesh. Material order must match vanilla: skin first, then eye, then mouth.
3. Put the engine tags on the materials: Face, Eyes, Teeth. Use the skin shader on skin and the eye shader on eyes. Missing tags make eyes/lashes show the wrong texture.
4. Merge seam vertices. Shape keys are summed in game, so one vertex slightly off in one key pokes out (typical on eyeballs). Test by exporting without shape keys (Ragnar, 2023).
5. Give the eyebrow mesh the same morphs. Basis key first, then identical keys in the same order.
6. Reimport the vanilla head with correct materials if LOD1 breaks eyebrows or ears (single report).
7. Agents far from the camera keep a low LOD, so face/hand morphs stop updating. Force it with `MBAgentVisuals.SetAgentLodZeroOrMax` (Artem, 2025) (unverified).

Version note: before 1.0.3 split head parts worked. Since 1.0.3 (Feb 2023) the modtools reorder materials on split import (.1/.2), which detaches the head and stops brows/hair following morphs. Reproduced by Ragnar; joining into one mesh with skin first fixes it (PlebeianKilo, Ragnar, Wizcas Chen, Hayster).

**Gotchas**
- Eyebrows are separate meshes using regions of one texture; a new brow texture needs mesh and UV edits. An edited vanilla brow looked shiny (order_without_power, 2020) (unverified).
- Monstrous heads (skeleton) have different shape keys, so rebuild rather than remap (John_M, Ragnar) (unverified).
- Modder-added face shape keys: one report of a 15-key head staying static; another says you can only reshape the base mesh, or use a head-as-helmet (disputed: Kemo III/Aquila 2025 vs the working 2023 method above; retest on current build).
- Custom race skins can render black if material textures come from another module (X7 Dragon, 2026) (unverified).

Wiki: https://docs.bannerlordmodding.lt/guides/custom_creature_race/ - new (head/material-order details are not on the wiki).

## A3. Hair, beards and eyebrow-style cards
1. Make hair as alpha cards: bake high-poly fibres onto a plane, duplicate cards sharing one UV, optionally scatter with Blender particles using a card collection as instance object (Dawa, Ragnar, GulagEnabler).
2. Keep the diffuse white/grey so vertex colour tints it; put alpha in the diffuse; use alpha_test or blend; DDS for the hair shader. Vanilla uses a shell plus card strips with diffuse and normal only.
3. Rig the mesh like a helmet (weights to the head bone). Floppy hair needs extra loops plus vertex alpha; plume cards stay under about 2k tris with painted tips for cloth.
4. Register it in `skins.xml`: copy an existing hair entry, change the mesh. Patch with XSLT. A new selection icon name index must be one higher than the last. It appears without a thumbnail until you add UI icons (Dawa, Astraali, BabyLettuce).
5. Replacing a vanilla mesh by the same name works without touching skins.xml (PlebeianKilo, Jarl, 2026).
6. Hair colours are easy to add in skins.xml.

**Gotchas**
- Black spots: stacked duplicate cards or the vanilla beard material's low alpha. Copy the material and raise alpha (Ragnar, 2023) (unverified).
- Give each card type its own material if alpha cards misbehave (Ogimaa, 2025).
- Hair has no cloth physics. Caps exist only for hairstyle and beard counts, not armor/weapons/scene assets (Ragnar, 2023).
- Long beards clip the chest unless weighted to chest/spine (Chase K, 2024).
- Replacing a beard wrongly can crash `AddSkinMeshesToEntity` (BlackRose, 2024) (unverified).
- (disputed: BlackRose says hierarchy order in the hair string matters and only the sprite needs a patch; hunharibo disagrees, 2025.)

Wiki: https://docs.bannerlordmodding.lt/3d/weight_painting/ - new (no hair/skins.xml coverage found).

## A4. Skins.xml, races and body overrides
1. Never ship the full `skins.xml`. Copy only changed nodes via XSLT so updates do not overwrite them. Version note: 1.5.6 shipped changed morphs with an unchanged head mesh ("tiny heads"); fixed in 1.5.9, but mods bundling an old full file still show it (PlebeianKilo, Jance, 2021). `skins.xml` loads through the project.mbproj path, not submodule.xml.
2. Edits show only after a full game relaunch (PlebeianKilo, Dawa, 2022).
3. New skin XML entries honor mesh and maturity/gender only; `skin_id` is unused, so a new skin overwrites an adolescent/child slot (nwetta, PlebeianKilo, 2020). A genuinely new race needs all face/body shape keys, plus child/teen/infant/female meshes for character creation (Haterade, 2025) (unverified).
4. Body swap: replace the body mesh with the same name, or rename it in skins.xml via XSLT so vanilla does not overwrite it (Ragnar, nwetta, 2022).
5. Face paint: `tattoo_material` (man/woman) entries. Body tattoos need code. Do not forget the shoulder piece or it crashes (Ragnar, 2022).
6. Skins Extender (mod) loads after native and allows partial edits (nwetta, 2021).
7. Changing skin/eye colour scales breaks every existing face key; only do so if you fix every NPC.
8. Skin-coloured armor parts are impossible (single skin texture). Show the real body and data-transfer weights from it (GulagEnabler, EnumaEllis, 2024).

**Gotchas**
- Underwear hides only when an armor slot is filled. Nude bodies need the underwear mesh replaced by one transparent triangle (PlebeianKilo, Ragnar, 2023). Underwear is not hidden if pants are part of the shoes.
- Custom race with non-human skeleton cannot restrict items yet: items lack family_type for armor (Ragnar, 2022) (version-sensitive; recheck).
- A custom skins.xml loaded before native worked for one user only (Kysaro, 2021) (unverified).

Wiki: https://docs.bannerlordmodding.lt/guides/custom_creature_race/ and /guides/custom_culture/ - adds detail (XSLT is covered; loading pitfalls and the 1.5.6 bug are new).

## A5. Helmets, hair covers and head fit
1. Choose the hair cover type in the item Armor tag. Underhelmet hair type 1 suits full hair; type 2 squashes ears; none clips (Kaffegorare, 2021).
2. Hide-body flags let long sleeves skip rigging the body beneath. Test in Model Viewer with the body unchecked.
3. A helmet cannot hide hair and keep ears. Hair-hide tags remove ears too; you can push ears in through XML while the helm is worn (Chase K, 2024; confirmed again 2026).
4. Fit helmets against the head mesh, not the morph. Expect male helmets to clip female heads (PlebeianKilo, Not A Box, 2021).
5. Crown offset: shift circlets about 0.15 on Y. Scale coifs about 1.05 on Y if they clip the neck after morph changes (PlebeianKilo, TheCoolerJason, 2024).

Version note: since 1.5.8 head sizes changed; about 250 helmet scaling factors per morph in skins.xml make helmets shrink/shift in game versus Model Viewer. TW (Dejan) acknowledged it (2022). Workarounds: DG zeroed offending morphs on the head mesh; PlebeianKilo set all helmet deforms to 0. Rigging the head bone does not fix it.

**Gotchas**
- `covers_head="true"` on a helmet crashed leaving to the map (1.5.x, 2021, confirmed by TimuR). Workaround: bake the helmet into the body armor mesh and set `covers_head` there; this also hides the head for non-human shapes. Later reports say the "cover head" tag works to hide face and hair (Ragnar, 2022; unverified on current builds).
- TW did not provide female helmet conversions.

Wiki: https://docs.bannerlordmodding.lt/3d/mesh_adjustment/ - new.

## A6. Custom creatures, giants and size
1. Humanlike giants/trolls: scale via XML (monster size); no new skeleton (Ragnar 2022, ThrashManiac9, Haterade 2024). Teen age gives a quick height change.
2. A new skeleton needs a monster category and animation sets; only mounts can use custom skeletons (nwetta, 2022; disputed: see the wiki's [Custom Creature Skeleton](https://docs.bannerlordmodding.lt/guides/custom_creature_skeleton/) page, which covers custom humanoid skeletons). Bone limit is about 64 and TW animations cannot be exported for reuse.
3. Non-human mods hide skin meshes and add body parts as armor (Jance, 2021).

Wiki: https://docs.bannerlordmodding.lt/guides/custom_creatures/ (and /custom_creature_skeleton/, /custom_creature_xml/) - already covered; read those instead.

## A7. Mounts and horse armor
1. Use the horse skeleton; mismatches crash (TimuR, kowal25, Zeyria, 2021). A wolf can use the dog skeleton.
2. Transfer weights from `horse_brown`. Using the sheep skeleton by mistake gives working-but-wrong animation (Ragnar, Dawa, 2022).
3. If it will not animate: skinning enabled on the material, Armature modifier active (not applied), no leaf bones, scale applied and rotation reset (Ragnar, hunharibo).
4. Horse-like mounts need only `horses_and_others.xml` entries; `monster.xml` is for genuinely new creatures. Edit the matching usage_set consistently (about four XMLs). Editing a new `monster_usage_set` without the human usage_set crashed on dismount. Version note: modules no longer need to load before Native (nwetta, 2022).
5. To rescale, reshape in Blender and set height/scale in XML to keep native animations (Dawa, 2022).
6. Horse colours: new textures work. Horse armor must be rigged to the horse skeleton; in Model Viewer assign an empty mesh with the armor attached (DirtyFaced, 2023) (unverified).

**Gotchas**
- Faction/team/tableau colours on mounts, horse armor and weapons were reported not working regardless of flags (hunharibo, DirtyFaced, 2023-24) (disputed: armor heraldry does work on body armor, see B6).
- Joints in the skeleton editor reported broken (2021).

Wiki: https://docs.bannerlordmodding.lt/guides/custom_mount/ - already covered; extras (weights, flags) are small additions.

---

# PART B: SCENE ASSETS

> Superseded for scene work by guides 08-16 (built from the #bl-scenes export). This part keeps only what #bl-3d-art said about props as assets; see 12-entities-prefabs-and-scripts.md and 09-terrain-and-flora.md for the full picture.

## B1. Props and physics
1. Import buildings like armor. Use multiple materials per face set; UVs may exceed 0-1 to reuse native tiling textures (Xtra Dip, 2021).
2. Collision: name a mesh `bo_<unique suffix>`, import it like a LOD, and name its Blender material after an engine physics material (e.g. `metal`). Pick it as Physics Shape in the editor (Ragnar, Justin, 2023). Unmatched names fall to default silently.
3. Keep collision coarse, not per roof tile.
4. Quick prop in-editor: Empty Entity, add Meta Mesh, set Physics Shape `bo_YourModel`, save as prefab (TortenSkjold, 2020). Or drag a custom mesh from the resource browser into the scene.
5. Reuse vanilla art: prefabs are assemblies. Open the prefab XML, find mesh names, export each with TpacTool (Carolina Warlord, Ragnar, 2023). The editor cannot export a cart/banner/Adam to FBX; get the source FBX (karaboga pasha, 2024).

**Gotchas**
- Bought architectural packs overlap UVs because each material tiles; assign new materials instead of rebaking (npc99, 2022).
- Trim sheets and tileable materials let many assets share one or two materials (Dawa, 2022).
- Flat vanilla textures on buildings allow material swaps in the editor (Secretblaze123, 2024).

Wiki: https://docs.bannerlordmodding.lt/3d/collision_body/ and /editor/prefabs/ - adds detail (bo_ rule is covered; XML-mining and trim workflow are new).

## B2. Prefabs and the scene editor
1. Prefabs are plain `game_entity` XML (meta_mesh_component, season_mask, mobility); readable in Notepad. Saving custom prefabs was flaky in 2020 (unverified).
2. Mirror a prefab by setting scale to -1 on one axis (Dawa, 2022). Entities can bend along a curve, particle instances are editable (npc99, 2021).
3. Sitting markers and scripts can be made in the editor and saved as prefab (Ragnar, 2022).
4. The `transform position` field only offsets child entities (npc99, 2023).
5. Open the resource browser inside the running game: console (Alt+`) `resource.show_resource_browser` (gkx, TW dev, 2020).
6. Cloth missing in a scene: comment out the `<cloth_simulator/>` entries in the .xscene (tested by Litauen and Snorri, 2024), or regenerate terrain and save.
7. Flags: use the `mp_flag` preset with a collision body and movement distance above 1 (Ragnar, 2023).
8. Particles attach to scene items/prefabs only, not crafted items (BlackRose, 2024) (unverified).

**Gotchas**
- MP: prefabs outside Native/Prefabs need breaking apart for maps to play without a client mod (Gingy, 2026) (unverified).
- Hovering vehicle trick: invisible small prefab with a physics shape beneath a flyer; AI fails without navmesh (nwetta, 2022) (unverified).

Wiki: https://docs.bannerlordmodding.lt/editor/prefabs/ , /editor/resource_browser/ , /3d/cloth_physics/ - mostly already covered; cloth_simulator and MP notes are new.

## B3. Flora, wind and vertex colours
1. Flora and detail meshes attach to terrain layers with density and scale, plus a flora brush (gkx, 2020).
2. Wind is enabled in the material settings; flora setup is in the flora editor (Ragnar, L. Rizz Hubbard, 2022).
3. Vertex-colour wind/AO conventions did not surface in this channel. Check the wiki and other channels before relying on a channel assignment.

Wiki: https://docs.bannerlordmodding.lt/editor/flora/ - already covered; vertex-colour detail missing from both sources.

## B4. Terrain textures, paint layers, decals
1. Meshes get only one extra paint layer in the editor; no layered-lit system (gkx, 2020).
2. World map: vista albedo, heightmap, splat maps and detail-layer materials (albedo/normal/materialparams/heightmaps). main_map paint layers add normal detail only; one mod used a 16K vista for colour (gkx, npc99, 2020).
3. Generate campaign terrain in World Machine, then sculpt in-editor (FoozleMcDoozle, 2022).
4. Custom decals work only if registered in `decal_atlas_all` on terrain (Dedmond, 2026) (unverified).

**Gotchas**
- Odd sun-angle shading or black terrain with a custom normal: unsolved in 2020.
- Custom world maps: height data needs the "Aurelian MapFix" module loaded before yours or Native heights apply (Carolina Warlord, 2022). 1.6.5 had node-render bugs; Calradia avoids them (npc99, 2021) (version-sensitive).

Wiki: https://docs.bannerlordmodding.lt/editor/paint_layers/ , /editor/decals/ , /editor/world_map_general/ - adds detail.

## B5. Armor heraldry and banners (tableau)
1. Item: `using_tableau="true"`. Add `banner_replacement_mesh` tag to the material on all LODs. Enable use_tableau_blending and use_tableau_mask_as_separate_texture. Align using `testbanner` in Diffuse2Map via Vector Argument 1, then remove testbanner (MRay6000, TW; safran, Meowkov, 2023).
2. Only one material slot may carry the tag; the projection cannot rotate (flip UVs); multiple banners per material are impossible (Ramsay, Litauen).

Version note: this is a 1.1.0 beta recipe; later betas broke shields/armor repeatedly.

Wiki: https://docs.bannerlordmodding.lt/3d/sigil/ and /3d/team_colors/ - already covered.

**Contributors:** order_without_power, kiet, ThrashManiac9, wu.p.run, Bogheim, nwetta, PlebeianKilo, Gambit77, Jance, Isaac, TimuR, Kaffegorare, Deleted User, Not A Box, Kysaro, Dawa, Reznov315, DG, Badger, Dejan, Astraali, Ragnar, Ragnar Hrodgarson, TheSchwarz, Ramsay, hunharibo, John_M, Wizcas Chen, Hayster, TheCoolerJason, DirtyFaced, Chase K, BraquemardLeBrav, GulagEnabler, EnumaEllis, BlackRose, Haterade, Kemo III, Aquila, Artem, Ogimaa, BabyLettuce, Jarl, X7 Dragon, kowal25, Zeyria, gkx, npc99, FoozleMcDoozle, TortenSkjold, Xtra Dip, Carolina Warlord, L. Rizz Hubbard, Justin, MRay6000, safran, Meowkov, Litauen, Snorri, karaboga pasha, Capt Cole, Secretblaze123, Gingy, Dedmond

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/06-characters-mounts-and-scene-props-sources.md](sources/06-characters-mounts-and-scene-props-sources.md)** (80 of 82 linked). The links only work for members of the Mount & Blade Modding server.
