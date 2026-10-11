# Bannerlord Materials, Textures and Shaders

Status: community-sourced, compiled 2026-10-10 from the #bl-3d-art channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - **Materials never come from Blender.** The FBX carries only material *names*. Import textures, create a material with the exact same name in the Resource Browser, *then* import the mesh and it auto-assigns to every LOD. No `.001`, no spaces, lowercase only on 1.3+.
> - **The `_s` map is packed, not specular:** R = metallic, G = gloss (inverted roughness), B = AO. Normals are DirectX (invert green from OpenGL). Wrong packing causes most "dark", "plastic" and "black chainmail" complaints.
> - **Three flags fix most broken armour:** `use_specular` (flat metal), `do_not_use_vertex_color_as_occlusion` (dark patches), and `skinning` (rigged items float or T-pose; it must be OFF on non-rigged items).
> - **Alpha, emission and multi-pass alpha are paid for by the whole material.** Put hair, feathers, glow and chain holes on their own small material and keep the rest opaque.
> - **Since 1.3, vanilla textures referenced from your own materials often render dark or black.** Export them with TpacTool and re-import under your own lowercase names. Override all four maps or none.

Conventions: "TW" = TaleWorlds staff. Unmarked claims are confirmed by staff or several modders. Where findings conflict, the newest is used and the version noted.

---

## 1. Set up a PBR material (the basic pipeline)

1. Name the Blender material slot exactly what the editor material will be called: no spaces, no `.001`, lowercase, English letters. Every UV island must be covered by a material, and slots with no faces are dropped on import. (Solnear, Easton, lemanier, Ragnar Hrodgarson, GulagEnabler, 2021-24; lowercase rule since 1.3: Aquila, Snorri, X7 Dragon, 2025-26)
2. Put PNGs in your module's `AssetSources` folder and import via Resource Browser > Import new asset. Use PNG, not JPG. DDS is needed only for hair/feather/alpha-heavy textures (Avelium, npc99, Ragnar Hrodgarson, 2021-23). PNG is not blurrier than DDS (confirmed by several).
3. Create the material in the Resource Browser with the same name as the Blender slot. Assign `_d` to DiffuseMap; `_n` and `_s` assign automatically when names match.
4. Use `pbr_metallic` for almost everything (gkx, TW, 2020; PlebeianKilo, 2025). Avoid `pbr_shading`, an older compromise shader that can look very dark (PlebeianKilo, 2025). Changing shader type later breaks metallic reflections (ExpertShip64350, 2026).
5. Tick `use_specular`: a new material lacks it, so the spec map does nothing (gkx, 2020). Tick `do_not_use_vertex_color_as_occlusion`. Tick `do_not_use_alpha` if there is no transparency.
6. Tick `skinning` (Vertex Layout, near the bottom) only if the item is rigged; see section 7.
7. Import the FBX. If material order is wrong, use Blender Mesh > Sort Elements > Sort by Material, or restart the Kit (PlebeianKilo, 2023; Ragnar says order matters only in game).

Native materials are protected and edits revert: copy under a new name (Dawa, Badger, 2022; Ragnar, 2023). Right-click "Create Override" exists for materials (gkx, 2020). (disputed: Ramsay says a same-name material in your module overrides; DirtyFaced could not override `banner_icons`, 2023.)

**Gotchas**
- The editor keeps its own copy of textures. Edit the source in AssetSources and re-import; replacing files on disk is unreliable, so delete and re-import via the UI. Deleting a diffuse still used by a material can crash the tools. (BD, gkx, 2020; Ragnar, BlackRose, 2023)
- Sizes must be standard power-of-2; rectangles must be multiples of 4 (Link69100, Ragnar, 2020-22). Non-square such as 4096x2048 works (GulagEnabler, 2025). UDIM is unsupported: use one material per UV tile, trimsheets, decals (Dawa, Ragnar, 2023).
- Blender cannot hold two slots with one name; assign duplicates by hand after import (MrAdrianPl, 2024).
- A mesh named like its material mounts it automatically (Easton, 2021).

Wiki: https://docs.bannerlordmodding.lt/3d/material/ and https://docs.bannerlordmodding.lt/3d/rename_material/ - covers the click path and basic flags. **Adds detail** (naming rules, lowercase, version breaks).

---

## 2. Pack the specular (`_s`) map and export from Substance

1. Treat `_s` as a data map: R = metallic (full red = metal), G = gloss (inverted roughness, not roughness), B = AO; alpha = translucency on vegetation shaders only (gkx, TW, 2020; Kemo III, BD, Ragnar).
2. In Substance Painter use a Bannerlord export preset (community "MB_export_presets", Bannerlord.spexp in the pinned 3D Asset Workflow doc). Stock UE4 and default presets use the wrong channel order and roughness (SnotFlower, UnseenSpi, Ragnar Hrodgarson, 2020-23). Old Allegorithmic presets can be copied to the Adobe documents folder (BlackRose, 2022).
3. Work metal/roughness in Substance; the preset converts roughness to gloss (Hakan, gkx, 2020). Do not export linear maps as sRGB (gkx, 2020).
4. No AO? Fill blue fully white or the material darkens (Ragnar, 2022).
5. In the Kit greyscale maps look red because only the red channel is used. Not a bug (Ragnar, npc99, 2023-24).

**Gotchas**
- "Black chainmail", "oily" or "dark" metal usually means roughness not inverted, a missing blue channel, or a standard export (Apolon, Claiton, Solus, Kemo III, 2020-26).
- (disputed: "yellow"/inverted spec or normal in the editor was blamed on BC5, cause never confirmed; Dawa, John_M, Major Roy, 2021.)

Wiki: https://docs.bannerlordmodding.lt/3d/material/ - has the channel layout only. **Adds detail** (presets, failure symptoms).

---

## 3. Normal maps

1. Bannerlord uses DirectX normals. From Substance export DirectX (MB preset; getting both `normal` and `normal_directx` means the default preset ran). From OpenGL sources (e.g. Poly Haven) invert only the green channel (Ragnar, Avelium, Shawmuscle, GulagEnabler, 2022-24). There is no flip-Y option (gkx, 2021).
2. Normals compress to BC5 so they look yellow in the editor. This is normal (gkx, TW, 2021). "Don't compress" shows blue at about 4x memory.
3. Name them `_n` to get BC5; unsuffixed normals default to DXT1 and look blocky (gkx, pinned, 2020).
4. Parallax: tick `seperate_displacement_map` and supply `_h` (gkx, 2020). Artefacts: try "calculate normals and tangents" and untick "remove duplicate verts" (DG, 2021) (unverified). Relief works on armour (GulagEnabler, 2024) but is wasteful on shields/props (GulagEnabler, 2026) (unverified). Raising bump intensity per material helps chainmail (FierceRebel, 2020) (unverified).

**Gotchas**
- TpacTool-exported normals can have an empty blue channel; re-export as RGB instead of auto, or rebuild blue with levels (Badger, Ragnar, L. Rizz Hubbard, 2022).

Wiki: https://docs.bannerlordmodding.lt/editor/textures/ - only covers texture scaling. **New** for normal conventions.

---

## 4. Texture naming and compression

| Suffix | Use | Compression |
|---|---|---|
| `_d` | albedo (alpha = opacity) | BC7 / DXT |
| `_n` | normal | BC5 (auto) |
| `_s` | packed metal/gloss/AO | BC7 / DXT5 |
| `_h` | height | auto-converted |
| `_c` | team-colour mask (red/green) | - |

(gkx, pinned, 2020; Badger, Ragnar, 2022). Other names still work but compression is not chosen automatically; change it in the texture editor (BC1/3/4/5/6/7).

- Tests: BC7 for colour/spec and BC5 for normals looked best (Apalmemnom, npc99, 2020). Swyter's opinion: BC3 over BC2, BC7 over BC3, BC1 only for noisy environment textures (2020). Vanilla armour used DXT1/DXT5 plus BC5 (Apalmemnom, 2020) (unverified).
- "Do not compress / do not degrade" fixes quality loss at a cost (npc99, 2022) (unverified), but crashed 1.3.x on some textures (Cozur, 2025) (unverified).
- Leave the Granite "streaming" option off unless the texture is in that format (dstn, 2020).

**Gotchas**
- Native texture sources are not shipped; the edit-pencil works only on your imports (Muret, TW, 2020). Some vanilla DDS (`beards_c_d` BC7, scatter maps BC6H) will not open in TpacTool or Substance; convert elsewhere first (npc99, BlackRose, 2021-22).

Wiki: https://docs.bannerlordmodding.lt/editor/textures/ - **new** (suffix and compression rules absent).

---

## 5. Texture size and material budgets

1. Armour/weapon body: 2K is the norm; smaller parts 1K or 512 (John_M, BD, Ragnar, Dawa, 2021-22; GulagEnabler: 2K body, 1K the rest, 2024-25).
2. 4K only for race/mount bodies, atlases shared by many items (almost all Vlandian helmets share one), or mesh-blended scene buildings (John_M, Dawa, npc99, 2021). 4K pays off only if 5-6 items share it and costs 4x the memory; one report says 6-8x the load (Ragnar, 2023) (unverified). 8K is pointless.
3. Aim for 1 material per armour piece (2 with cloth). Each adds a draw call and only identical materials batch (Dawa, John_M, GulagEnabler, K.Savo, 2021-26). Share belt/boot/glove materials across troops. Many materials are cheap in armies using shared sets but cost more on a lone hero (Ramsay, 2024).
4. (disputed: some run 3-5 materials per item with no battle impact, and a small tiling mail plus a plate sheet saves VRAM versus one 4K atlas - Dawa, Ragnar, Apolon, 2022-23; others say 3+ per slot is bad - GulagEnabler, 2026.)
5. Tableau materials stay small, 512 (Ramsay, 2024). Campaign-map icons 2K or lower (Xtra Dip, 2021) (unverified).

**Gotchas**
- Bake high, export 2K; they are independent (Ramsay, Dawa, 2022).

Wiki: https://docs.bannerlordmodding.lt/3d/polycount/ - triangle budgets only. **New** for texture and material budgets.

---

## 6. Transparency (alpha)

1. Put opacity in the alpha of `_d`; save a real RGBA PNG and check it in the texture inspector. In Substance activate the opacity channel before building layers (Dawa, Ragnar, 2022).
2. Use `pbr_metallic` (or its alpha-test variants) and tick `alpha_test`. Tune the Alpha Test value: higher widens the alpha area (about 40-64 typical, 90-120 for fringes and chainmail) (Ragnar, hunharibo, slayergodfrey, 2023-24; Ramsay, 2025).
3. Blend mode "factor" suffices. Plain alpha blending alone was ignored in 2020 (DG, 2020).
4. After creating the material, press "compile shader" or restart the Kit if alpha does not show (Ragnar, PlebeianKilo, DrakyD, 2023-26).
5. Put the alpha part (hair, crest, grille, chain) on its own material. Alpha-tested helmet parts otherwise make the whole helmet draw without early depth/stencil test, even with no transparent texels (gkx, TW, 2020-21).

**Gotchas**
- Multi Pass Alpha softens glass, ghosts and hair but compiles and loads slowly (disputed: Ragnar uses it for special cases; PlebeianKilo and Ramsay say avoid). It tints kingdom colours (Ragnar, PlebeianKilo, 2022). A wrong alpha method once made 80 units take 10 minutes to load; removing needless alpha_test fixed long armour loads (GulagEnabler, DrakyD, 2026).
- Halos or black outlines: raise the alpha factor; keep colour enabled on the Substance opacity layer (GulagEnabler, Rodric, Benjo, 2024-26). Try "single channel alpha" for black artefacts (Ramsay, 2025) (unverified).
- Hidden geometry: tick `disable_vertex_color_alpha` (gkx, 2020).
- Cloth sim plus transparency: the anchored cloth part vanishes because vertex alpha is read as opacity; tick "vertex color ignores alpha" (Chase K, 2024). A 2026 report says to uncheck "do not use vertex as alpha" (GulagEnabler) (unverified); test both.
- Hair cards: several small cards beat one big card under cloth sim; using a card as the sim mesh removes z-fighting (Ragnar, 2023). Alpha overdraw costs more than triangles (Not A Box, BD, 2021) (unverified). The hair shader takes no specular; sheen comes from the normal map, so reuse `hair_shift` (Ragnar, 2023) (unverified).

Wiki: https://docs.bannerlordmodding.lt/3d/material/ - only mentions `do_not_use_alpha`. **New** for the alpha recipe and costs.

---

## 7. Skinning, two-sided and vertex-colour flags

1. **Skinning.** Rigged armour, helmets, shoulders, gloves: ON for every material and LOD material. Unrigged items (weapons, shields, props): OFF, or they turn invisible (Solnear, Dawa, KingOfTheN0RTH, PlebeianKilo, 2021; TheCoolerJason, Chase K, 2026). "Skinning precise" helps stubborn cases (Ragnar, GulagEnabler, 2023-26).
2. **Two Sided** (Others tab). Fine for cloth, capes, tabards, curtains; cheaper than modelling extra faces, cost mostly one-time shader compile (Ragnar, Dawa, 2022). Use it as a test: if a part appears, normals are flipped, so recalculate outside (Dawa, 2022). Do not duplicate meshes with flipped normals; cloth sim then makes two cloth meshes. It can conflict with cloth sim (TheCoolerJason, Chase K, 2025).
3. **Vertex colour darkening.** Half-dark or banded shading means vertex colour leaks in: tick `do_not_use_vertex_color_as_occlusion` or delete the vertex colour layer in Blender (gkx, 2020; Ramsay, BlackRose, 2024). Black vertex alpha on cloth-prepared meshes cuts parts: paint it white or use the flag (Ragnar, 2023; Dawa says it did not always work). Vanilla hair meshes carry real vertex colours that darken reused hair (Ragnar, 2023).

**Gotchas**
- Rename UV maps identically (e.g. `UVMap`) in all objects before joining, or the second UV set resets and only the first material works (PlebeianKilo, Ramsay, Ragnar, 2021-23).
- Renamed or cloned shaders can crash or make assets invisible to other players; blood adherence is a shader flag, so do not clone shaders for it (PlebeianKilo, fedeita, 2025).

Wiki: https://docs.bannerlordmodding.lt/3d/material_issues/ - **already covered** for vertex-colour and alpha-hole flags; https://docs.bannerlordmodding.lt/3d/material/ covers skinning/two-sided basics. **Adds detail** (diagnostics).

---

## 8. Team colours (faction tint on armour)

1. Diffuse 1: white to light grey where tint applies; a dark base ruins it, and grey brightness sets how dark the tint looks. Grunge in the mask is black (Badger, PlebeianKilo, 2022).
2. Mask in Diffuse 2 (`_c` / `D2_d`): red = primary, green = secondary, golden yellow = mix, black = none. Half values blend original and tint. Swap red and green to swap colours. Red is for team colour, not sigils (Not A Box, Ragnar, PlebeianKilo, 2022-24).
3. Material: tick `use_double_colormap_with_mask_texture`.
4. Item XML: `<Flags UseTeamColor="true"/>` (gkx, ADamnSexyName, 2020). It goes in the item's Flags element, not the Armor component (Vulvix, 2026) (unverified).
5. Preview with the material factor colours. The model viewer may not show factor1; check in game (Oswaldo, Snorri, 2026).

**Gotchas**
- Team colour on with no mask in Diffuse 2 makes the item dark in game (ADamnSexyName, PlebeianKilo, 2020).
- To lock a part (e.g. hair) use a black mask or the built-in black texture in Diffuse2 (Ragnar, Ayden, 2020-23) (unverified).
- A stray flag can turn metal studs faction-coloured or make plate invisible (anonymous, iSimplyCD, 2020) (unverified); a `useteamcolors` tag made a coat invisible in combat (Lupercal, 2026) (unverified).
- Colour cannot be more saturated than the faction value; adjust luminosity via the mask. A missing primary colour was fixed by Metallic_Shader (Oswaldo, 2026).
- Horse armour: impossible through 2022 (Nomad, Ramsay, Ganael); works since 1.4 in inventory/missions but not on the campaign map (Thyrell, Chase K, Benjo, 2026). Weapons: only banner lance parts since 1.3 (wiki, Sh1ny4). Body-part items cannot take skin tints (Rohzdear, 2022) (unverified).
- Skin/hair colours in `skins.xml` are RGB floats 0-1: pick with Factor Color, divide by 255 (Ragnar, TBRO13, 2022).

Wiki: https://docs.bannerlordmodding.lt/3d/team_colors/ - **already covered** (mask colours, flag). This section adds only the pitfalls.

---

## 9. Banners and sigils on armour and shields (tableau)

**Armour/cape sigil**
1. Material named like `MODEL_sigil`. Assign the TableauMaskMap (white = sigil area). Tick `use_tableau_blending` and `use_tableau_mask_as_separate_texture` (new in 1.8.0, keeps alpha free) (kowal25, patch notes, 2022).
2. Item XML `using_tableau="true"`. Tag `banner_replacement_mesh` on the mesh in every LOD; a missing tag or LOD gave white-sigil failures (Terco_Viejo, GulagEnabler, ThrashManiac9, 2024).
3. Position with Vector Argument 1: xy = position, zw = scale; negative y flips, negative x mirrors; nudge in about 0.010 steps. Use `sigil_test_bounds_for_shields` in Diffuse2 for placement (wiki; Not A Box, DG, 2021).
4. (disputed: the wiki says remove Diffuse2Map after positioning or you see a yellow placeholder; 2024 reports say keep Diffuse2 filled so inventory icons do not render black - hunharibo, DirtyFaced.)

**Shield sigil**
1. Make the banner area transparent in the diffuse alpha; the banner shows only where Diffuse1 has alpha (gkx, 2020; Dawa, 2022). (disputed: TpacTool inverts alpha, so the coloured area may be the black part - 2026 single reports.)
2. `use_tableau_blending`, banner in Diffuse2, `using_tableau="true"`. Shields use banners, not faction colours; `using_tableau="false"` removes team colours (Ragnar, PlebeianKilo, 2022).
3. The vector cannot rotate a sigil 90 degrees; rotate the UVs and texture in Blender.

**Gotchas**
- Tableau and team colours cannot share a material; one banner per material (hunharibo, GulagEnabler, Ramsay, 2024-25). `using_tableau` set by mistake stops extra materials rendering (Luacha2000, 2025).
- Diffuse2 is also the emission mask, so tableau and emission conflict (Ragnar, 2023).
- Sigil on armour disables normal team colour on that material (PlebeianKilo, 2025).
- Horse harness sigils reported not working (LokiBaal, Thyrell, 2021-26). The emblem may show only in game (Not A Box, 2021).

Wiki: https://docs.bannerlordmodding.lt/3d/sigil/ and https://docs.bannerlordmodding.lt/3d/shields/ - armour setup **already covered**; **adds detail** (version flags, conflicts, disputes).

---

## 10. Glow and emission

1. Put glowing parts on their own material (duplicate faces if needed). Exposure acts on the whole material and turns black into grey (Ragnar, hunharibo, ThrashManiac9, slayergodfrey, 2023-24).
2. Emission texture (glow colour, black elsewhere) in the second diffuse slot; enable self illumination; set strength in Vector Argument 1 `w` (Ragnar, 2023).
3. For daytime glow tick `use_exposure_compensation` (since 1.8.0, 2022; before that glow worked only at night) (Ragnar, svang5142, 2022). `w` above the exposure value changes nothing (hunharibo, 2023).
4. Animated flames: a UV-sweep texture on a plane, speed and direction via vector arguments; no flipbooks (Ragnar, hunharibo, PlebeianKilo, 2022-24).

**Gotchas**
- Emission cannot share a material or Diffuse2 slot with team colour or tableau (PlebeianKilo, ThrashManiac9, 2023-24).
- Daytime glow still inconsistent in 2025 (Chains, ThrashManiac9, PlebeianKilo) (unverified); emissive materials crashed the shader on save in 2026 (Alphα) (unverified).

Wiki: no dedicated page in the mirror. **New**.

---

## 11. Overriding and reusing native textures (1.3+ breakage)

1. Export the native texture with TpacTool, edit, save PNG with the SAME name, drop into any module's assets; Native shows "Overridden" (Frankie, 2020). Vlandian armour's lion sits in the green channel of its mask.
2. You cannot override one map: replace diffuse, diffuse2, normal and spec together, or it silently fails in the shipped game while looking fine in the editor (kiet, 2020; Litauen, Ganael, 2025).
3. Since 1.3: custom materials referencing protected vanilla textures render purple, dark or black; character materials may use only textures from their own module (John_M, TheCoolerJason, 2026). Fix: export, rename lowercase, re-import into your module, keep them with the material (Rodric, Snorri, 2026).
4. Head swaps: copy sub-mesh tags (eye, mouth) from the vanilla head; use an override of the vanilla face material for tattoos (Reznov315, Astraali, 2022).

**Gotchas**
- Uppercase in texture or material names crashes the 1.3.x editor (a ~1 KB tpac appears). Delete, lowercase, re-import (X7 Dragon, Snorri, 2025-26).
- 1.3.2 and 1.3.8 betas crashed on assigning custom textures; mixed game/editor versions fail (Awallacer, Dejan, TW, 2025).
- Tiling: with the texture-scaler flag, vector 2 xy scales and zw offsets (wiki, Byako); also fill the four bottom vertex numbers; incompatible with Mesh Bend, so copy the material (Rodric, 2026).
- (disputed: subsurface scattering exists only on `faceshader_high`, very subtle - Avelium; Ragnar says SSS is not in the game, 2023.)

Wiki: https://docs.bannerlordmodding.lt/editor/textures/ - only texture scaling. **New** (override rules, 1.3 changes).

---

Contributors: gkx, TW (Muret, MRay6000, Dejan), Swyter, npc99, John_M, Apalmemnom, PlebeianKilo, UnseenSpi, ADamnSexyName, SnotFlower, dstn, kiet, Link69100, Hakan, Apolon, Dawa, DG, FierceRebel, Frankie, Maegfaer, BD, Not A Box, Ragnar Hrodgarson, LokiBaal, safran, GulagEnabler, Solnear, KingOfTheN0RTH, Easton, Ayden, Terco_Viejo, kowal25, Major Roy, Xtra Dip, Avelium, hunharibo, BlackRose, Badger, Ramsay, Nomad, Ganael, Alex, Chase K, svang5142, DirtyFaced, Reznov315, Astraali, Ginebro, lemanier, slayergodfrey, ThrashManiac9, Shawmuscle, Litauen, TheCoolerJason, Kemo III, Solus, Luacha2000, Chains, fedeita, Aquila, Batuhan, Cozur, Awallacer, Snorri, X7 Dragon, ExpertShip64350, FiefEvictionNotice, Taylor, Rodric, DrakyD, Benjo, Oswaldo, Vulvix, Lupercal, Thyrell, Sh1ny4, Byako, TBRO13, L. Rizz Hubbard, Rohzdear, iSimplyCD, MrAdrianPl, Claiton, K.Savo, Alexander, Alphα

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/01-materials-textures-shaders-sources.md](sources/01-materials-textures-shaders-sources.md)** (224 of 229 linked). The links only work for members of the Mount & Blade Modding server.
