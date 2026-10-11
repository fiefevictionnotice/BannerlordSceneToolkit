# 09 - Terrain and Flora

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

Version labels: "e1.x" = early access (2020-22); "1.x" = after TaleWorlds restarted numbering (Oct 2022+). Labels follow the date of the finding. Single-report claims are marked (unverified); conflicting reports are marked (disputed: ...).

> **Biggest epiphanies**
> - The hidden ceiling is a 4 GB terrain-compression limit shared by all paint layers. At 512 vertices per node you get about 4 layers; at 256 about 14-16; at 128 you rarely hit it. Nothing warns you before the save fails.
> - Never change one node's resolution, and never grow a terrain after painting it. Both corrupt layer data. The reliable repair is export heightmap + material maps, delete terrain, regenerate, re-import.
> - Flora does not appear in game unless the scene contains `border_soft` prefabs (since e1.8). "Grass in editor, none in game" is almost always this.
> - Layer-based flora is fine for grass and bushes but spawns invisible trees that still have collision (1.3+). Paint trees with the flora painter.
> - About 95-98% of vanilla scenes ship without `SceneEditData`, so they open flat. Rebuild with HeightMapExtractor (heights only) or bl-terrain-ed (regenerates a `terrain_ed.bin`).

---

## 1. Terrain size math

Task: choose node count, node length and vertices per node before you generate terrain.

Terms:
- **Nodes (X x Y):** the grid. Editor maximum is 16 x 16. Far nodes get cruder LODs, so use many small nodes rather than a few giant ones (Muret/TW, 2020).
- **Single node size:** metres per node side. Scene side = nodes x node size.
- **Single node dimensions:** vertices per node side.
- **Vertices per side** = nodes x dimensions. **Heightmap pixels** = vertices + 1 (section 2).

Steps:
1. Decide the playable area in metres. Put an outer mesh beyond it; do not make terrain bigger than needed (gkx/TW, 2020).
2. Pick nodes so each is about 100 m: playable side / 100.
3. Pick dimensions: 64 for arenas, 128 for ordinary scenes, 256 for detailed ones. TW's rule of thumb: 1-2 vertices per metre; 0.5 m per vertex is very detailed.
4. Compute heightmap size = nodes x dimensions + 1.
5. Generate. Treat these numbers as final; changing them later is the main source of corruption (section 4).

**Worked example (1 km battle map):**
- 10 x 10 nodes, node size 100 m, dimensions 256.
- Scene side = 10 x 100 = 1,000 m (1 km2).
- Vertices per side = 10 x 256 = 2,560, so the heightmap is 2,561 x 2,561 px.
- Vertex spacing = 100 / 256 = about 0.39 m.
- Cheaper variant: dimensions 128 gives 1,281 px and about 0.78 m spacing, usually enough.
- Cross-check, Calradia: 16 x 16 nodes, 52-53 m, 256 dims = 4,096 vertices per side (4,097 px), about 848 m across.

Community reference sizes:
- Native open-field battle terrain: 10 x 10 nodes, 200 m, 128 dims (e1.x, 2021). TW guidance relayed in the channel: at least 10 nodes per axis, scene at most about 1.6 km at 100 m nodes (2020-22).
- Smaller scenes: 4 x 4 village, 5-6 x 5-6 battle map, 8 x 8 "plenty for a castle with surroundings" (LizardWizard 2022-23; Reynauld 2024).
- Campaign maps: 16 x 16 nodes, 105-200 m, 256 dims, about 9 layers; roughly 4x Calradia's area is a good balance (npc99., FoozleMcDoozle, 2022-23).

Node resolution, newest first:
- Do not exceed 256 per node. 128 or 256 is safe; 512 is a known route to a scene that can never be reopened (Swan, BlackRose, hunharibo, 1.x 2024).
- 128 is the performance sweet spot; 1024 wrecks save times and fps (FoozleMcDoozle, hunharibo, 2024).

Gotchas:
- A 20 m paint circle looks blocky at 128 and smooth at 512 (FoozleMcDoozle, e1.5). Weigh that against the 4 GB limit in section 5.1.
- Playable area and terrain size are separate. Cut the playable area smaller than the terrain and hide the rest under the outer mesh (gkx/TW, 2020).

Wiki: https://docs.bannerlordmodding.lt/editor/terrain/ - adds detail (wiki defines nodes, size, dimensions and Calradia's numbers, and warns about 256; the +1 rule, size tables and reasons are new).

---

## 2. Heightmaps: import, export, scale and offset

Task: bring an external height image in, or take the current terrain out.

**Import**
1. Generate flat terrain at the final node settings.
2. Prepare a **16-bit single-channel greyscale PNG**. 8-bit or JPG gives terraced steps (256 vs 65,536 levels). RGB files are rejected; an indexed PNG imported inverted (Goblin 2023). Gaea may need RAW 16-bit to avoid spikes (Klassix, e1.x).
3. Size it nodes x dimensions + 1 (e.g. 2,561). Exports already carry the +1. Other sizes import by resampling; oversized maps just waste effort (a 16K map is resampled to 4,097; npc99. 2022), and small ones are stretched. (disputed: 2021 reports said power-of-two sizes only and mixed sizes caused "cannot import weight map"; later reports favour +1.)
4. Enter Terrain mode (Alt+7), press Ctrl+A to select every node, then Import Heightmap (bottom of the panel). Importing with one node selected fills only that node.
5. Enter **Scale** and then **Offset**: scale = highest minus lowest point in metres; offset = height of the lowest point. Range -200 to +500 m means scale 700, offset -200.
6. Hover with the landscape shaper tool to read heights and re-import with adjusted values.

**Export**
1. Ctrl+A, then Export Terrain. Files land in `bin\Win64_Shipping_wEditor` (`terrain_heightmap.png`; the world-map export is named `world_map_heightmap` in 1.x).
2. Scale and offset flash briefly in red at bottom-left. Note them; export again if missed (npc99. 2022).
3. Export always covers the whole terrain. Per-node export is gone; crop externally and remove the +1 row first (npc99. 2022).

**Delta heightmap:** a regular import resets to flat first; delta adds to existing heights. Uses: river-only map to cut beds; raise land from the sea (white above water, dark grey but not pure black below it); erosion channels from an inverted flowmap with negative offset (Cuce/TW, FoozleMcDoozle, npc99., 2020-22).


Gotchas:
- Pure black (0) at edges gives terraces or a misread floor. Lift the map off zero and use a negative offset (FoozleMcDoozle, e1.5; Boggo 2026).
- "Can't import weight map": .raw file or wrong dimensions. Use 16-bit PNG and Ctrl+A first.
- Terrain too high or low: change scene water level or re-import with a different offset (npc99. 2023).
- World Machine round trips change the value range and bury settlements. Keep the original heightmap and apply only the new normal map; erosion lowers terrain (FoozleMcDoozle, npc99., e1.x).
- Do not hand-sculpt mountains (cones). Import from World Machine, Gaea, World Creator or GIS data (USGS DEMs, tangrams heightmapper, QGIS, SceneFromGIS); blur low-resolution sources first (Reynauld, FoozleMcDoozle, 2024). Free World Machine (512-1k) and Gaea (1k) are enough for a scene.

Wiki: https://docs.bannerlordmodding.lt/editor/heightmap/ - adds detail (wiki has the click path and trial-and-error scale; offset math, bit depth, +1 size, delta and export quirks are new). https://docs.bannerlordmodding.lt/editor/mesh2hmap/ - already covered (mesh to heightmap in Blender).

---

## 3. Sculpting and terrain tools

- Alt+8 is terrain elevation. For Lower use weight 0.05-0.1 and hardness under 0.5; abrupt steps expose the node mesh (npc99. 2024).
- Slope filter is in degrees, height filter in metres; they also control flora. They skip small bits, so go back over painted areas (Swan 2024).
- **LOD multiplier** (terrain properties): raise it for blocky or low-poly terrain. TW default is about 3; people use 6 on small scenes and 30-50 on large campaign maps. It inflates `terrain.bin`, does not improve paint texture resolution, and the UI may show the default after reopening though the value is saved (Klassix, Swan, Thil, 2024-26).
- **Undo and RAM:** terrain editing leaks memory. Lower Undo Stack Size (50 to 5), save often, avoid undo (can hang the PC or corrupt the next save). Fixed in e1.6.5, returned in e1.7.x (npc99., Dejan/TW).

Limits:
- No holes, overhangs, caves or second level, with no plan to add them (Muret/TW 2020; repeated to 2026). Fake caves with a trench covered by rocks or soil entities. Terrain never occludes objects; make an occluder from the heightmap in Blender (Sheepbitter 2023).
- Spikes and holes after saving, mostly after Lower Terrain or sculpting below 0, persist through 1.3-1.5, even in vanilla scenes. TW acknowledged. Smooth the spot (flatten first, then smooth, if it returns; Boggo 2024); expect it back after the next save (Snorri, Dejan, Taylor, 2025-26).

Wiki: https://docs.bannerlordmodding.lt/editor/brushes/ - new (wiki only offers brush packs; tools, filters, spikes and LOD multiplier are not on it).

---

## 4. Node resolution and resizing (when and why they corrupt)

Version history:
- e1.5-e1.7: raising node resolution crashes; lowering works one node at a time; painting afterwards fails; flora will not generate on resized nodes (FoozleMcDoozle, Klassix, 2021). Changing resolution only works with no layers on the terrain (Failware, toy boat, e1.7.2).
- 1.x: never change resolution of an individual node. It corrupts the node and later edits crash. The engine already lowers detail with distance (FoozleMcDoozle 2023).
- Growing terrain breaks layers: thin lines at node seams, "layer cannot be placed", wrong layer count on new nodes, unpaintable materials, flora layers failing on reopen. Shrinking is fine. Growth works in two directions only (Ted, Sheepbitter, FiefEvictionNotice, 2023-26).

**Safe procedure**
1. Export the heightmap. Record scale, offset and water height (it shifts).
2. Export every layer's material map. Save presets or screenshot layer settings (presets save per season; unverified).
3. Delete the terrain. This removes all layers and is cleaner than deleting layers one by one. Props and navmesh stay.
4. Generate new terrain at the correct settings.
5. Import the heightmap, rebuild layers, import material maps (section 5.3).
6. Save, restart the editor, reopen, check.

To shrink a map: export and import at a smaller scale on fewer nodes (FiefEvictionNotice, Drusk 2025).

Gotchas:
- Paint-layer resolution must equal terrain resolution. A layer defaulting to 2x with 4K textures gave shader errors in the campaign (Craig_Tea_Nelson 2024) (unverified).

Wiki: https://docs.bannerlordmodding.lt/editor/terrain/ - adds detail (wiki just links an official resizing guide).

---

## 5. Paint layers

### 5.1 Limits (reconciled)
The numbers vary because the real bottleneck is memory.
- **4 GB compression limit:** shared by the mesh and all layers. 512 dims: about 3-4 layers. 256 dims: up to about 14-16. Exceeding it crashes on save or leaves a 0-1 KB `terrain_ed.bin`, with no warning (npc99. citing Murat Ture/TW, e1.x-1.x; GourmetBean 2022).
- **Hard cap:** 16 including the default layer. Flora layers count (FoozleMcDoozle, LizardWizard, 2021-24).
- **Stable count:** about 13-14; corruption above that is reported by few users (Reynauld 2024, Skarfoze 2025).
- **Recommended:** 8-9 per node (TW devs, e1.x); a TW dev advised about 5 active layers over one piece of terrain (2025).
- **State the range:** 4-16 by dimension; plan 8-14. Create 12-14 layers up front rather than adding later (Lemmy1916, Thil 2026).

### 5.2 Creating and painting layers
1. Add a layer in the Paint tab. Fill diffuse, normal and spec in summer, then use copy-from-summer for other seasons; a new layer may not fill all season tabs. Give the default layer a real texture too.
2. Layer textures need "For Terrain" ticked in the resource browser. For custom maps use BC4 for height, BC5 for normals, DXT1 for the rest; DXT5 painted black (LizardWizard 2023) (unverified). 4K works on campaign maps; non-1K/2K/4K sizes cannot compress (Swan 2024).
4. Triplanar Mapping (Rotation group) cures stretching on steep slopes (Muret/TW).
5. Hide tiling with height blend + smooth blend (beats raising node resolution), an Area Map second texture at a different UV scale, parallax, cliff prefabs, larger layer size (Thil, FoozleMcDoozle, 2022-26). Never combine Area Map with Mesh Blend.

### 5.3 Material maps
- Greyscale only, white = painted. Not RGB.
- Create a layer first, select it, Ctrl+A, Import Materialmap. The first/default layer cannot receive one (Kysaro, Exhorris). A partial selection may lock later layers to that patch (Reynauld 2024) (unverified).
- Size matches the heightmap (+1). 512 px is enough; a 4K map spiked RAM to about 6 GB.

### 5.4 Flora-layer flag
- "Flora layer" disables the layer's own paint and uses it only to spawn flora. Tick it in every season; it is the one setting copy-from-summer does not carry (FoozleMcDoozle; brutal_panda).
- Do not use the base layer for flora; it is painted everywhere. Use a dedicated flora layer.
- "Effect Upper Layers" on a road or sand layer below a flora layer clears flora above it. It cannot be undone or re-edited later (FoozleMcDoozle, e1.x).

### 5.5 Mesh blend
- Requirements: Mesh Blend ticked on the layer (not the first layer); blend parameters on the prop (metamesh > custom mesh parameters, x value; material args xyzw); layer painted under the prefab; layer texture has a heightmap and minimum specular size (e1.x-1.x, several reporters). Not for `mesh_bend` entities. Texture-layer blending started working on meshes in e1.7.
- Status (disputed: Klassix and Reynauld 2023 call it unreliable and blamed it for never-finishing saves; MrAdrianPl 2024 says it works but is buggy; TW dev Dejan 2026 confirms it is unreliable, no ETA). Working 2026 recipe: new layer from no template, a diffuse that enables the checkbox, no height blend, no Area Map (Thil).

### 5.6 Deleting layers
- Deleting can consume 29-50 GB RAM and crash, especially on campaign maps (chAOS, npc99., 2022-23).
- Safer: untick the layer; or convert to flora-only with no flora, save a new file, then remove (Hart, Swan 2024). Disabling all four seasons in `scene.xscene` also works (brutal_panda) (unverified).

### 5.7 Campaign layers and vista
- Use "mainmap" diffuse textures that are transparent except black detail. Colour comes from a power-of-two vista texture (16K for 16 x 16 nodes; 8K looks smeared) plus a vista normal map (npc99., Lemmy1916, 2021-25).
- Custom vista: set tileset to "none", import your image, give it a plain white albedo (multiply, weight 1). Convert big PNGs to DDS first. Vista textures stay powers of two; heightmaps and material maps are +1 (npc99. 2022).
- Layer detail distance must exceed your max camera height but not be 0 (try 10000) (npc99. 2022; Le Profyteur 2024).
- Area Map textures on layers are broken in game and Custom Battle (blue/brown terrain, base layer only); albedo-factor changes are fine (ARC4N0N, Hart, 1.x 2026).

- Road Generator decals do not work in most missions and are removed at MP round start (TW acknowledged, 2026). Build roads from three layers plus height blend.

Wiki: https://docs.bannerlordmodding.lt/editor/paint_layers/ - new (wiki page only covers layer export). Dark new terrain / vista tileset: https://docs.bannerlordmodding.lt/editor/terrain/ - already covered.

---

## 6. Blue, black or garbled terrain: shader-cache fixes

Symptoms: flat, blue, red or black terrain in game or test; layers vanishing on one tile; scrambled paint on a server but fine in the editor.

Try in this order:
1. Save twice, or save under a new scene or module name (Space_Pot8to, Le Profyteur, 2021-23).
2. Delete `C:\ProgramData\Mount and Blade II Bannerlord\Shaders\TerrainShaders\<Module>\<Scene>`. The folder is hidden. You may delete `CoreShaders` and `TerrainShaders`; never the whole Shaders folder (Lemmy1916 2026).
3. Also clear `SceneObj\<scene>\ShaderCache` (disputed: Muret/TW 2020 pointed to the scene cache; 2021 users said only the ProgramData folder worked). For a modded main map also delete `terrain_shaders_header_data.bin` in `ShaderCache\D3D11` (e1.7).
4. Untick and retick a layer, switch season, tap smooth at weight 0, or paint once with radius 3000 / weight 0 (Reynauld 2024; matthewster 2026).
5. If only the base layer shows in game, save twice, clear the shader folder, raise the paint "collision" setting (both fields to 1000), and check terrain quality: low quality shows one layer (Bullero, Boggo, 2025-26) (unverified).
6. Rebuild: export height and material maps, remove terrain, re-import.

Other causes:
- Dark new terrain: vista tileset not "none", or base layer disabled (Swan 2024).
- Blue or purple only in editor missions: editor-test bug not carried into Custom Battle (e1.x, TW). TW attributed another case to too many layers.
- A snow flowmap layer caused scrambled painting on one server (Capt Cole 2024).

Wiki: https://docs.bannerlordmodding.lt/editor/editor/ - already covered (delete CoreShaders and TerrainShaders). Per-scene folder, order and secondary causes are new.

---

## 7. Recovering native terrain

Facts:
- The game reads `terrain.bin` in `SceneObj\<scene>`; the editor reads `terrain_ed.bin` in `SceneEditData\<scene>`. Layer settings sit in `scene.xscene`; weightmaps in `terrain_ed.bin` (GourmetBean, npc99., e1.7).
- About 95-98% of vanilla scenes have no usable edit data, so they open flat or at the wrong height; the game itself runs fine. A few ship it (empire_village_003, khuzait_castle_002, a sturgia town siege, sea_bandit_a, mountain_hideout_001, empire_village_e_naval, sturgia_village_k_navalraid) (2020-26).
- "Terrain deletes itself on open" almost always means the module's SceneEditData is missing (Hart 2026).

Options:
1. **HeightMapExtractor** (Nexus 4979) extracts heightmaps from any mission scene, vanilla included, not the world map. Import with scale = max minus min, offset = min. Paint maps cannot be extracted (hunharibo, npc99., 1.x 2024).
2. **bl-terrain-ed** (lewisajax, GitHub) regenerates a `terrain_ed.bin`. One user patched it for naval-raid scenes; it hung on flora loading in some DLC scenes (hunharibo, FiefEvictionNotice, 2026) (unverified: the poster had not tested it).
3. Rebuild paint by hand or from masks; no tool extracts layers from `terrain.bin` (npc99. 2022).

Wiki: https://docs.bannerlordmodding.lt/editor/terrain/ - adds detail (wiki mentions only the flat-terrain / corrupted SceneEditData case). https://docs.bannerlordmodding.lt/resources/tools/ - check whether the extractor is listed; bl-terrain-ed is new.

---

## 8. Flora

### 8.1 Choose a method
- **Layer flora** (flora meshes on a layer): best for large areas, grass, bushes, stones. GPU-instanced, scales down with foliage settings; at the lowest setting grass thins out except trees, bushes and rocks (hunharibo, Reynauld, 2023-24).
- **Flora painter:** precise, ignores low foliage settings, immune to the 1.3+ layer-flora bugs, but loads slower. Use for trees (Klassix, FiefEvictionNotice, 2020-26). Campaign-map trees can come from both systems; erase with both.

### 8.2 Layer flora steps
1. Add flora meshes to the layer for every season; copy-from-summer copies layer flora, not painter flora.
2. Tick **Flora layer** in each season.
3. Tune: density = closeness within a group; colony radius = gap between groups; colony threshold = group size (lower is bigger); weight = minimum paint intensity for flora to spawn; seed = different noise, so kinds with different seeds do not overlap.
4. Paint, then test in game. The editor caps layer flora on a grid, so density can differ from game (npc99. e1.7).
5. Place several `border_soft` prefabs (8.3).

### 8.3 Soft-border requirement (since e1.8, July 2022)
- Since e1.8, layer flora (grass and trees) does not appear in game or Custom Battle unless the scene contains `border_soft` entities. TW dev confirmed (Dejan 2022); still true through 1.x (2023-26). Flora from the Flora Tool still appeared in 2023 tests (Klassix).
- Place several inside the scene, not hard against edges. Select or delete them like any entity via scene search. Make them visible before placing; toggling visibility after can crash (2024).
- Also check the season and colony settings. Bald spots often come from colony radius (default 200; try 100) or colony settings; set both to 0 for even coverage (LizardWizard 2022; Ördek 2026).

### 8.4 Invisible-tree collision
- Layer flora can spawn invisible trees that still block movement, including in native maps (1.3+, FiefEvictionNotice, LizardWizard 2026). Causes: small tree sprouts, and overlapping layers; trees and bushes share one slot per layer, so overlaps overwrite visuals while collision stays (Faustus, Reynauld 2024).
- Fix: paint trees with the flora painter; keep forest and grass on separate layers; stay under 5-6 kinds per layer; rebuild the tree layer if it persists.
- Invisible walls around shrubs come from built-in blocking boxes on trees and big bushes in the flora editor, for brush/layer flora only (Hart 2025) (unverified).
- Painted rocks (e.g. green rock set) have no collision; hand-placed rocks need navmesh. Only trees get AI capsules; dead trees have collision without capsules (FoozleMcDoozle, Klassix, e1.x).

### 8.5 Floating or underground flora
- 1.2.x on: grass floating or flat on slopes after load; after War Sails some chunks sit a few cm underground. Editor bug; native maps unaffected; fixes hold in game but vanish on editor reopen (Reynauld, Klassix, GourmetBean, 2023-26).
- Fix: Alt+8, Smoothen, weight 0, huge brush, tap once, save (wiki "Fix grass"; Reynauld 2025 adds undo after the tap). Repeat after every reload. Only touched chunks update.
- Alternatives: untick and retick a layer; Publish Module and use that SceneObj; in the flora kind editor uncheck "Point Up" and check "align with ground" and "align with slope" (Thil 2026).
- Back up `flora.bin` (flora moved there from `terrain.bin` about e1.6.4). Saves and bakes have wiped it; restore from the scene Backups folder (Sheepbitter 2023; papaLime 2024).

### 8.6 Density, distance, wind, performance
- Trees sit on a minimum-spacing grid (density 100 maximum). Billboard LOD about 32 triangles, LOD0 about 10k (FoozleMcDoozle, e1.x). On campaign maps trees fill every grid position at density about 3 (npc99., e1.6). Wide tree spacing on custom-named world maps (1.2.12-1.3.1): copy the kinds to your own `flora_kinds.xml` and adjust flags (see wiki).
- TW's "density under 60" applies only with colonies disabled (radius 0, threshold 0). Grass above about 64 per layer hurts fps. Prefer non-shadow variants (`flora_grass_a_non_shadow`); too much flora is the usual cause of lag at capture flags (gkx/TW, fedeita, 2020-23).
- View distance is per kind in Native `ModuleData\flora_kinds.xml` (`view_distance`; -1 = never fades, used for trees). Longer costs fps (1000 cost about 20 fps in 2020). Override from a module loaded after Native, or make a new kind in the Flora Editor (Window > Module Data windows). Register a custom file in `project.mbproj` with a `type="flora_kind"` entry; existing kinds cannot be edited. "Verify files" wipes custom kinds (Reynauld 2024).
- **Wind:** strength is in Scene Options (Wind table), not only the north angle (Maxibon 2024).
- Seasons: painter and layer entries have seasonal presets; entity trees do not (one prefab per season with visibility flags, `mesh_seasonal_material`, or "not affected by season"). Only pines have winter snow variants on the main map (npc99. 2023). Since e1.8 recolouring tints trunk and leaves (Dejan 2026); a bush with a red factor gives autumn colour without red trunks (unverified).

Gotchas:
- "Flora gone after reload" is usually a render glitch. Touch the node, resave, or change the first tree's density (Cuce/TW, e1.6).
- Editor and game differ at layer edges (1.x). Save twice, change seed, or patch with the painter (toy boat, Snorri, 2026).

Wiki: https://docs.bannerlordmodding.lt/editor/flora/ - adds detail (wiki covers parameter definitions, world-map tree spacing and the grass smoothing fix; method choice, soft borders, invisible trees, view distance, wind and recovery are new).

---

Contributors: gkx, Cuce, Muret/Murat Ture, NIN3, Dejan (TaleWorlds); npc99., FoozleMcDoozle, Klassix, Reynauld, Swan, Hart, Snorri, LizardWizard, Lemmy1916, Le Profyteur, Thil, hunharibo, Space_Pot8to, Bullero, GourmetBean, Sheepbitter, FiefEvictionNotice, ARC4N0N, Kaseke, Horris, Boggo, Capt Cole, Suspicious Man, Piconi, Spartan, and others in #bl-scenes.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/09-terrain-and-flora-sources.md](sources/09-terrain-and-flora-sources.md)** (284 of 285 linked). The links only work for members of the Mount & Blade Modding server.
