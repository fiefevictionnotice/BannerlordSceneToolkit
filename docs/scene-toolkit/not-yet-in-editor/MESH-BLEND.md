> Built into the in-editor documentation in v0.9.0 (2026-11-12). Kept here as the source draft.

# Mesh Blend

Terrain mesh blend is the effect where the ground's texture creeps a little way up the base
of a rock, wall or building, so the object looks set into the terrain instead of placed on
top of it. This panel does two things: it tells you why the effect is not showing on the
entity you selected, and it lets you tune how the blend looks.

Open it with **F8 -> Mesh Blend** (second row, next to Continuous Recolor). The panel stays
open when you close F8. It closes on Esc, on its own Close button, and when the scene
changes.

Nothing on this panel runs by itself. Every result comes from a button press, and it always
works on what is selected in the editor at that moment.

## What mesh blend needs

All of these must be true at the same time, for the part of the object you are looking at:

1. **The material uses a standard or metallic shader.** In practice `pbr_shading` (the
   standard one) or `pbr_metallic`. Flora, decals, faces, the world-map shaders and
   `pbr_dithered_color` never blend. Skinned (animated) meshes never blend.
2. **Shader quality is not Low.** Low shader quality switches the effect off for the whole
   game.
3. **The object is stationary.** Things that move or are driven by a script (doors, siege
   engines, destructible pieces) are usually not.
4. **The terrain under it has a mesh blend layer painted there.** A terrain layer has a
   Mesh Blend tick box in its settings. The layer must not be the first layer in the list:
   the first (base) layer never blends. The more of that layer is painted at the spot, the
   stronger the blend.
5. **The base of the object is close to the ground.** By default the blend reaches from
   0.1 m below the terrain to about 1 m above it. Anything higher gets no terrain color.

## Check

Select the entity (up to four are checked per press) and press **Check**. You get one line
per condition, colored by result:

- **OK** (green): this condition is met.
- **FAIL** (red): this is a reason the blend is not showing.
- **UNKNOWN** (amber): the toolkit cannot read this from the editor. The line says what to
  look at by hand.
- The last line (blue) names the most likely cause.

What each line means:

- **Shader quality**: reads the graphics option. The toolkit assumes the lowest setting is
  the one that switches the blend off.
- **Terrain layers**: lists the layers that have Mesh Blend ticked. This is read from the
  **last saved** scene file, so tick the box, save, then check again. Unsaved layer changes
  are not seen.
- **Paint under the entity**: always UNKNOWN. The toolkit cannot read how much of a layer is
  painted at a point. Look at the ground under the object and make sure one of the listed
  layers is actually painted there.
- **Material**: how many of the entity's meshes (children and all LODs included) use a
  shader that can blend, with examples. Meshes that cannot blend are listed with the
  reason.
- **Stationary**: shows the entity's mobility setting. Stationary is reported as UNKNOWN
  (probably fine), because the engine's own flag cannot be read. Dynamic is reported as a
  likely FAIL.
- **Blend values**: the tuning values currently on the first tunable mesh.
- **Height**: compares the entity's bounding box with the terrain height under its center,
  its corners and its origin. This is a box test. A long object on a slope, or one with an
  overhanging part, can pass here and still be too high at the place you are looking at.

If nothing fails and it still does not blend, check the UNKNOWN lines by hand, then try
**Recalculate**, then save and reload the scene.

Two things the check cannot look at, which are worth knowing:

- The editor appears to expect blend layer textures to be 2048x2048, with every blend layer
  in a scene using the same diffuse resolution. This comes from text inside the editor and
  is not confirmed.
- A terrain layer change may need a save and reload before the blend picks it up.

## Recalculate

Runs the editor's own `recalculate_mesh_blend` console command for the open scene and shows
what the editor answered. The toolkit looks the command up in the editor's command list
first; if the editor does not list it, nothing is run and the status line says so.

The toolkit cannot undo this, and what exactly the command rebuilds is up to the editor.

## Tuning

Tuning writes values to the meshes of **all selected entities**, including their children
and every LOD. It only touches meshes whose material uses `pbr_shading`, `pbr_metallic` or
`terrain_mesh_blend_shader`. Other shaders (flags, bows, ropes, lightmapped materials) use
the same per-mesh values for their own effects, so those meshes are skipped and counted in
the status line. A standard material with texture scaling, animated texture coordinates or
tableau blending switched on is skipped for the same reason.

| Field | What it does | Default | Allowed |
| --- | --- | --- | --- |
| Blend height | How far up from the ground the terrain color reaches, in meters | 1 | 0.05 to 20 |
| Softness | Where the fade-out starts. Lower = starts nearer the ground, longer and softer fade. Higher = harder edge | 0 | -0.5 to 0.49 |
| Noise amount | How much a random pattern raises the top edge. -0.5 gives a straight edge | 0 | -0.5 to 10 |
| Noise scale | Size of that pattern. Higher = finer | 0 | -15 to 200 |

**Advanced** (toggle) shows three more:

| Field | What it does | Default | Allowed |
| --- | --- | --- | --- |
| Heightmap start | How far up the blend the material's height map starts to push the terrain color back | 0 | -0.5 to 0.49 |
| Heightmap power | How strongly it does that. -1 switches it off | 0 | -1 to 10 |
| Normal bend | How far up the blend the surface shading is bent toward the terrain's | 0 | -0.19 to 0.8 |

The two heightmap values only matter on materials that have a separate displacement map.

How the fields work:

- **An empty field means "leave this value as it is on each mesh".** Fill in only what you
  want to change.
- The **-** and **+** buttons change the number in the field. Nothing changes in the scene
  until you press Apply to Selection.
- You can type a comma or a dot as the decimal separator. Text that is not a number, or a
  number outside the allowed range, is refused with a message and nothing is changed.

Buttons:

- **Read from Selection** fills all fields from the first tunable mesh of the selection.
- **Apply to Selection** writes the filled-in fields. The advanced fields are written only
  while Advanced is ON.
- **Reset to Default** writes the default values (all zeros, which is a 1 m blend height)
  to all seven values.
- **Undo Last Change** puts back what the last Apply or Reset replaced. Up to 20 steps. The
  steps are kept while the scene stays open, even if you close the panel; they are dropped
  when the scene closes. This is separate from Batch History and from the editor's own
  undo.

The status line says how many meshes on how many entities were changed.

**Save the scene to keep the result.** The values are stored per mesh in the scene file.

A typical session: select a rock, press Check, fix what it reports, press Read from
Selection, press + on Blend height a few times, Apply, look, repeat, then save.

## Limits

- Paint weight, the engine's stationary flag and texture formats cannot be read; those
  lines are UNKNOWN or missing by design.
- The terrain layer list is the last save, not the live scene.
- Noise scale: if the status line warns that it "did not read back as written", the editor
  does not report that value. In that case always type a Noise scale (0 for the default)
  instead of leaving the field empty, and do not rely on Undo for it.
- Tuning is not recorded in Batch History.
- Only four entities are checked per press. Tuning has no such limit.
