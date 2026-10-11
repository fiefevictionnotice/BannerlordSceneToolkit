# 13. Lighting, Atmosphere and Water

Status: community-sourced, compiled 2026-10-11 from the #bl-scenes channel of the Mount & Blade Modding Discord. Claims marked (unverified) or (disputed) need testing in your game version.

> **Biggest epiphanies**
> - The atmosphere you tune in the editor is mostly ignored in game: Custom Battle and campaign scenes pick their own preset by time of day and weather. Only scenes loaded directly (main menu, interiors) keep it, and only if you pressed "Save to Scene".
> - Until 1.3.0 a scene could enforce only colour grade, north angle, wind and global fog. Lighting itself could not be forced, which is why lights vanish by day.
> - "Is Indoors" is a trap: it disables the day/night cycle, forces night, and shrinks the bake probe grid to 0.85, which breaks outdoor bakes. It is not a lighting fix.
> - Baking needs a continuous navmesh, PRT border probes and a bake area limited to the playable space. New bakes crashed on 1.3.x; roll back to 1.2.12 to bake, and old bakes still load.
> - Water changed three times: scene water level is global, 1.3 added a second visual plane about 1 m above the physical one (DLC), and from 1.4.4 there is one fixed water type.

Versions: "e1.x" = early access (2020-22), "1.x" = after the Oct 2022 renumbering. Findings are dated; the newest wins.

---

## 1. Understand what overrides your atmosphere

**What happens**
- Custom Battle ignores the scene atmosphere. It picks a preset by time of day (hardcoded `TOD_01..12_00_SemiCloudy` names); in e1.x the pick was random. No preset has rain. Fog and wind were not overwritten in e1.x. (gkx/Mylittletantan/FoozleMcDoozle 2020-21; Hart, g-spurs 2026)
- Campaign: village and mission scenes take season, weather and time of day from the campaign. Interiors entered from the map cannot keep their own atmosphere. The campaign map interpolates its own atmospheres. (Le Profyteur 2023, Secretblaze123 2024)
- Scenes loaded directly (main menu, main map, interiors) keep the saved atmosphere. (FoozleMcDoozle, poheniks 2020-21)
- In 1.4.x Custom Battle uses a pre-1.3 renderer: no cloud shadows, and water/layers can differ from the editor. Test in the real mode. (Hart, Steinmur 2026)

**Making a look stick (options)**
1. Set the atmosphere in the Atmosphere Inspector, then press **Save to Scene** at the bottom *before* saving the scene. Skipping this resets sun and fog and gives the always-night / always-noon effect. (Lord Eastwood, Justin 2023)
2. For a template, save an atmosphere XML into your own module's `Atmospheres` folder (e.g. a night variant). (Klassix 2022)
3. To force it in Custom Battle, either use poheniks' Custom Battles Atmosphere mod (Harmony patch that stops the random pick), or save a template whose name matches the `TOD_xx` preset you want to replace. Rain this way means setting fall density in that template. (poheniks, FoozleMcDoozle 2021-22)
4. "Save to Scene" with the `_atmo_w_night` tag writes a new atmosphere file; update the scene name in your custom-battle XML to match. (Zedforen, Mermen 2025) (unverified)
5. Forcing from code at mission creation works, but skybox and clouds cannot be set that way. (hunharibo, Boggo 2025-26)

**What a scene can enforce**
- Up to 1.3.0: colour grade, north direction, wind, and global fog only. (Hart, Snorri 2025-10)
- Scene Options has "Enforced Color Grade"; the colour grade manager script (on an empty entity) is needed. A MapColorGradeManager crashed one map. (npc99., Klassix 2022-23)

**Gotchas**
- Save the scene before "Start mission as", or atmosphere edits reset. (poheniks 2020) (unverified)
- Check the atmosphere XML actually saved; fog forcing was inconsistent. (Kysaro 2021)
- Custom atmospheres saved into Native cause startup warnings on every launch; save to your own module. (Thil 2026) (unverified)
- Interpolated atmosphere XMLs reportedly had to live in `Native/Atmospheres/Interpolated`, so modules could not add them. (hunharibo 2023) Module templates for scene use did work; treat module-added interpolated sets as unreliable.
- A forced custom atmosphere may sever the weather rotation link and so change paint-layer/season visibility. (disputed: Kysaro's theory, unproven)
- Editing the vanilla TOD presets changes every scene.

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ - new (wiki mentions atmosphere only in a scene-size warning).

---

## 2. Time of day, weather, sky and sun

**Steps**
1. Open the Atmosphere Inspector. Pick the "selected atmosphere" (this is how you preview night). Sun angle, fog, season and weather dropdowns are in the same window. (FoozleMcDoozle, gkx 2020)
2. Rain is the *fall density* parameter. Vanilla presets are 0. Since 1.2.0 rain starts at about 0.7; below that surfaces only get wet. An e1.x test found nothing above 0.2 looked different except lightning and wetness. (order_without_power 2022, 2023) (disputed: thresholds differ between reports, check on your version)
3. Sun too big: lower bloom and sun scatter strength in Fog & scatter. Adding sun shaft reduces sun reflection brightness. (Space_Pot8to 2023; joseph ballin 2026)
4. Shadow strength: Weather > sun > shadow opacity. Changing `shadow_opacity` was reported to break GI. (Capt Cole 2024; Klassix 2023)
5. North direction: Scene Options > Wind Direction tab > North Angle. (Yurek 2022)
6. Custom skybox: import the texture with the "for skybox background" flag. Sun and cloud textures use "for skybox sun" / "for skybox cloud". The skybox is the `__skybox__` entity material and is tied to the atmosphere. Multiple suns need skybox edits. (FoozleMcDoozle 2020-22)
8. Campaign map: 24 hourly cubemaps and scattermaps (BC6H, not exportable), so the editor cannot show real campaign visuals. Cloud plane height is a `cloud_plane` entity; precipitation particles are code-spawned. (npc99. 2021-24)

**Gotchas**
- Atmosphere is not meant for runtime change; GI probes relight only on scene load, and the world map uses no GI. (gkx 2020)
- Winter does not freeze water. The "Not affected by season" flag avoids snow variants but does not render in the editor, only in game; one user saw it fail in Custom Battle (disputed: Foozle saw it work). (Hart 2026, hwyl 2022)
- Cloud shadows work on the campaign, not in Custom Battle; 1.3 changed their behaviour. (joseph ballin, Bullero 2025-26)

Wiki: https://docs.bannerlordmodding.lt/editor/weather_effects/ - adds detail (wiki covers season/snow paint only); the rest is new.

---

## 3. Fog

**Options**
- Global fog in the Atmosphere Inspector (falloff height, min fog). It often did not show in game in e1.x (Servius 2021). From 1.3 "fog" is one of the enforceable settings.
- Ground fog/swamp: place an empty entity with a `psys_env*` or `dust_env` particle (thicker than `ground_fog_a`), lower the emitter box height, and scale the bound entity to scale the particle. (npc99., Snorri 2020-25)

**Gotchas**
- A fog-less circle around the camera comes from a `fog_start_distance` baked into the scene. (order_without_power 2022) (unverified)

Wiki: https://docs.bannerlordmodding.lt/editor/weather_effects/ - new.

---

## 4. Colour grade

**Custom grade (1.4.x, newest)**
1. Make a LUT strip of 256x16 pixels (16 squares of 16x16). Editing an exported one in GIMP works.
2. Import with Do Not Generate Mips, Do Not Compress, Don't Degrade and **For Colorgrade** ticked, or you get green/purple tints. (Thil, Kysaro 2026)
3. Select it on the scene or atmosphere, with the colour grade manager script present.

**Campaign map grades**
- Copy `Native/Atmospheres/Interpolated/campaign_map.xml` into your module. Time-of-day grades are there; the red channel of `worldmap_colourgrade_grid` maps to `worldmap_color_grades.xml`. The engine merges regional and time grades. Needs xslt/project.mbproj loading; may revert if the vanilla XML is not wiped. Does not affect mission scenes. (npc99. 2023-24)
- Base campaign atmosphere is in a `worldmap_base_atmosphere` XML. (Zedforen 2025)

**Gotchas**

Wiki: https://docs.bannerlordmodding.lt/editor/colorgrade/ - already covered for campaign grades; the scene-level LUT format adds detail.

---

## 5. Light entities and interior lighting

**Day/night switching**
1. Put the `light_cycle` script on the light. For particle plus light, put it on a parent empty entity. (FoozleMcDoozle 2020)
2. "Always on" / `AlwaysBurn` keeps lights lit by day. Some torch prefabs have duplicate LightCycle scripts on child entities. (Snorri, Klassix 2026)
3. Candles by day: raise intensity into the thousands (3000-5000) in unbaked scenes; baked values drop to 80-180. Too high turns candles into suns; a point light with the flicker script is better. (Reynauld, Lord Eastwood 2026) (unverified)

**Making interiors dark or lit**
- Cheap: `_dark_light` / `_dark_ambient_light` entities, or the negative-light prefab (scale it inside the script). (Suspicious Man 2023; Hart 2026)
- Local `envmap_probe` (reflection capture global off, low ambient multiplier) darkens a cubic room and fixes reflections but is performance-expensive. Without local probes all reflections come from the global one. (Wayfaring Knight 2023; Hart 2026)
- Env probes brighten ambient only; they do not light light sources. Move the probe nearer the lights or raise ambient. (Snorri 2026)
- Light leaks through thin or floating models: thicker walls, a duplicated flipped wall, or a local probe. `editor_plane` blocks light on one side only; meshes blocking light must be 2-sided. (Hart, Thil 2024-26)
- Sun through ceilings: hide a solid `editor_plane` in walls facing the sun. An "outside light plane" fakes outdoors seen through windows. (Hart, LizardWizard 2025)
- Rain/snow inside: roofs are unreliable; set interior entities to not affected by season or hide an invisible awning. (Snorri 2024-25)

**Gotchas**
- Lights only show when they hit geometry; sky-high lights do nothing. (Snorri 2026)
- The "Indoor scene" checkbox just disables the day/night cycle and forces night. (Hart 2026) Set the interior atmosphere preset and save it to the scene instead. (Exhorris, Hart 2024)

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ - new.

---

## 6. Environment probes and reflection

**Steps**
1. Keep exactly one global envmap probe ("is global" ticked), in the open, about 5-20 m above ground, in the middle of the map and above the water. It drives reflections and ambient. Place it where the scene's average colour is. (Foozle, Le Profyteur, Lord Eastwood 2020-24)
2. Interiors in daylight cycles: global probe outside, local envmap probes inside. Use several small local probes for non-box rooms with little overlap. (NIN3, Snorri 2021-24)
3. The water probe attaches to the water automatically (see section 9).

**Gotchas**
- Too many probes cost fps and caused crashes. (beefbacon 2020)
- A probe near foliage or strongly coloured ground tints all shadows (green battle maps); move it or put a neutral surface under it. (Snorri 2025)
- Water reflection looks distorted without a global probe. (Foozle 2020)
- Black squares in reflections/sky/water: turn off "indoor" in the atmosphere tab or cut the number of lights. (Emma, Snorri 2026) (unverified)

Wiki: https://docs.bannerlordmodding.lt/editor/water/ - adds detail (wiki covers only the water envmap probe).

---

## 7. Baked lighting (PRT / GI)

Bake is optional. Native battle scenes are rarely baked; main maps need none. Many modders consider it worth it only for mixed interior/exterior scenes. A bake lowers light cost (stored as texture) and softens harsh shadows. (Kaseke 2025; fedeita 2024)

**Steps**
1. Finish and furnish the scene first; bake last.
2. Build a continuous navmesh. Probes only activate near it, and it identifies interiors. It need not be precise (may cross tables and lamps, just not walls). Editing the navmesh after a bake removes the PRT. (Hart 2025; Foozle 2021)
3. Place PRT border probes (`prt_border_probe`) at the lowest and highest corners. Bake the playable area only.
4. Keep the global env probe outside the bake borders. Set a *fallback probe* to control lighting outside the baked area and on outer terrain.
5. Uncheck "Is Indoors" in **both** Scene Options and the Atmosphere.
6. Raise the grid size (about 4x4 for open scenes; 5x5 low density when testing). A big scene at default grid can exceed the 2048 visibility-probe limit and crash.
7. Run "full bake single level" (enough for MP). "All levels" is for SP castles and far slower.
8. Fix leaks: left-alt click the leak and Ctrl+C to toggle probe visibility; move probes (select, T, G); Ctrl+X twice rebakes. Dark-light entities help interiors.
9. Save to local and load from local every session (data goes to `C:\ProgramData\Mount and Blade II Bannerlord\PrtEditData`). `prt_data.bin` in `SceneObj` is what ships (small); the multi-GB edit data stays in `SceneEditData`.

**Gotchas**
- **Is Indoors pitfall:** it locks the grid to 0.85 (docs suggest about 1.5x2) and breaks outdoor bakes. (LizardWizard, Kaseke 2023-26)
- Black boxes and flicker: probes inside or too close to geometry, or overlapping. Move or deactivate them. (Kaseke, Zedforen 2025)
- Changing sun altitude beyond the baked path (max about 82 degrees) gives artefacts; after changing a light, rebake nearby probes. Lights must be very intense to show in daytime bakes. (fedeita 2024)
- A bake can corrupt `prt_data` and crash the scene; delete or move the file from `SceneObj` to recover (the navmesh can stay). Adjusting a probe after baking can crash unless you load/save local. (Le Profyteur, Hart 2022-25)
- Versions: GI was broken from about e1.6.4 until e1.7.2; bakes crashed after 1.2.8; new bakes crash on 1.3.x and War Sails-era editors (existing ones load). Roll back to 1.2.12 to bake; some got it working on 1.4.5, others not (disputed: mixed reports; Dejan said "fixed next patch" in 2026-02). Raise the pagefile; RAM use climbs steeply with scene size.
- Whether a bake helps MP performance is disputed (LizardWizard yes, Ordek no).

Wiki: https://docs.bannerlordmodding.lt/editor/navmesh/ - new (baking is not covered).

---

## 8. Shadows and shader-cache glitches

**Steps**
- Purple/blue/pink materials, black boxes, or a dark scene: delete the shader cache in `C:\ProgramData\Mount and Blade II Bannerlord\Shaders` (especially `TerrainShaders`) and the `ShaderCache` in the scene's `SceneObj`. Saving to a new scene file also fixed corruption; delete `compressed_shader_sack` in the module if scenes come in black. Delete again when the paint layer count changes after a server test. (Bullero, Le Profyteur, Haterade, FiefEvictionNotice 2023-26)
- Broken terrain shadows after opening a scene in 1.2.x: modify the terrain (smooth, raise 0) to restore. Buggy versions save a smaller `terrain.bin`. (Byako 2024)
- Square patterns on terrain: a layer uses the editor plane specular map; give every layer a proper specular. (Foozle 2021)

**Gotchas**
- Shadow type "Static and dynamic" crashed a server map with custom buildings; camera dynamic shadows make square areas. (Tiburon, papaLime 2024-25) (unverified)
- Occluders cull objects (an object must be fully inside) and do not affect lighting; terrain does not occlude. (Foozle 2021; Hart 2025)

Wiki: https://docs.bannerlordmodding.lt/editor/shaders/ - new.

---

## 9. Water planes and rivers

**Scene water (large lakes/sea)**
1. Scene Options > Water (hidden by default; Window menu). Raise the level and carve beds. The level is global per scene and can reset when running maps; re-set it. (Foozle, papaLime 2020-24)
2. Select a `wat_*`/`water_*` material.

**Local pools**
1. Place an `editor_plane` (or a water pool prefab) with a water material. Scaling tiles the texture. (Foozle 2020)
2. Add physics override material Water for sound; remove the physics object to swim. Planes do not slow players.
3. To slow players and splash: put a visual plane (no physics) over a lower plane with the water override and `water_splash`; a "has steps" body flag slows. Splash is horses-only. (LizardWizard, Foozle, Carolina Warlord 2022-23)

**Rivers**
1. Draw a flat path (Hermite, not linear). Name it `river_...`.
2. Add an empty entity with `road_instance`; tick river mode; set the path name and a water material. Lower the whole path by selecting its points. (Muret, Foozle 2020-22)
3. Tune tile, speed, normal. Export the mesh as OBJ for tweaks if needed.
4. For foam use `prt_shore_foam` or FX. Flow direction cannot be controlled. (Swan, Klassix 2024)

**Waterfalls**: about three particle systems (the fall, a vertical-velocity splash mist, radial surface foam), desynced and angled differently, plus an animated plane behind big ones. Cost depends on emission rate, not count. Waterfalls fall straight down only. (ARC4N0N, Kaseke, Hart 2025)

**1.3 / 1.4 changes**
- With the DLC on, a water plane gains a visual plane about 1 m above the physical one, so you swim under the surface; DLC scenes built on old flat water are flooded. Level about -1.5 matches both. (Bullero 2025-26)
- Since 1.4.4 the water type selector does nothing (one fixed type). A per-scene water material override arrived Oct 2026 for non-naval scenes. Use `water_plane` plus `water_plane_pool_a`, or a fake plane. War Sails waves are several metres high at high strength; reduce strength. (Snorri, Sly 2026)
- Non-DLC fallback: a flat water plane recoloured with a black second diffuse, scaled huge. (Bullero 2026) (unverified)
- Campaign-map water in 1.3: copy `<water_properties>` from the native world map, set `water_exists` true and a `water_material` (e.g. `wat_open_ocean_g`), adjust `water_level` (try 0 if the sea does not show). Vanilla water is "dead" (no waves) in 1.3.2. (Artem, Litauen 2025)

**Gotchas**
- Choosing "canal dark blue global" (or other canal waters) as scene water crashes the editor without the DLC. (toy boat 2025)
- The `wat_` material list crashed the road script on e1.8.0; early e1.x crashed on path-name entry. (Foozle, Muret 2020-22)
- Upside-down path points make water visible only from below. (Wizcas Chen 2024)
- River decals linger after deletion until reload. The world-map river mask is flipped on the north-south axis. `river_generator` is unreliable and regular scenes cannot use its flowmaps. (Foozle, npc99. 2020-24)
- Water simulation can cost about 14% on a map. (Carolina Warlord 2023)

Wiki: https://docs.bannerlordmodding.lt/editor/paths/ - already covered (river steps); https://docs.bannerlordmodding.lt/editor/water/ - adds detail (probe only on wiki); 1.3+ DLC water, pools and waterfalls are new.

---

## 10. Performance costs of lights

- Dense or overlapping point lights cause pixelated checker/speckle artefacts and crashes (worse on AMD and on reflective surfaces). Limit radius and spacing, avoid many spotlights, use the tiled light overdraw tool. Make flame-only lights tiny with no shadow; let 1-2 per cluster emit real light. (Feli, Hart, Snorri 2024-26)
- Local env probes are costly; `_dark_light` entities are cheap.
- Road decals are major FPS killers. (Bullero 2023)
- The `rain_splash` entity once caused huge lag after 1.2.8; deleting it took one scene from 60 to 144 fps. (Justin 2024) (unverified)
- Baking lowers light cost (section 7).

Wiki: https://docs.bannerlordmodding.lt/editor/scenes/ - new.

---

Contributors: FoozleMcDoozle, gkx (TW), Muret (TW), poheniks, NIN3, Klassix, Le Profyteur, LizardWizard, Hart, Snorri, Bullero, npc99., Reynauld, Kaseke, fedeita, Suspicious Man, Lord Eastwood, order_without_power, Zedforen, Thil, Kysaro, toy boat, Sly, ARC4N0N, Steinmur, hunharibo, FiefEvictionNotice, Capt Cole, beefbacon, Byako, and others named inline.

## Sources

Every finding in this guide, with its author, date, confidence and a jump link to the original Discord message: **[sources/13-lighting-atmosphere-and-water-sources.md](sources/13-lighting-atmosphere-and-water-sources.md)** (128 of 131 linked). The links only work for members of the Mount & Blade Modding server.
