> Built into the in-editor documentation in v0.9.0 (2026-11-12). Kept here as the source draft.

# Scene Statistics

A read-only overview of what the open scene is made of: totals, the prefabs you used most,
and the meshes that cost the most triangles. Nothing on this panel changes the scene.

Open it with **F7 -> Statistics** (bottom row, next to Close). The same panel also holds
the light list; see the Light List page.

## It reads the live scene, on demand

Unlike Scene Diff, this reads the scene as it is in the editor right now, unsaved changes
included. It scans once when the panel opens and again when you press **Refresh**. It does
not update by itself, so press Refresh after you add, delete or swap things. The status
line shows how long the scan took.

The panel stays open when you close F7, so you can use its Go and Select buttons with the
viewport clear. It closes on Esc, on its own Close button, and when the scene changes.

## Totals

The block at the top:

- **Entities**, split into top-level and children.
- **Prefabs**: how many different ones, on how many entities, and how many of those are
  still live prefab links versus baked copies (link broken). Also how many entities carry
  no prefab name at all.
- **Meshes**: mesh components and how many entities have one; cloth simulators.
- **Lights** (with how many cast shadows and how many are flagged), **particle systems**,
  **decals**.
- **Scripts**: script components and how many entities carry one.
- **Triangles**: appears once the Heaviest Meshes tab has been opened.

## Most-Used Prefabs

Every prefab name in the scene with its instance count, most used first. A row reads
`412 x  rock_a`, followed by `(baked)` or `(400 linked, 12 baked)` when not every instance
is a live link.

- **Select** selects every instance of that prefab in the editor.
- **Go** jumps to the first instance.

Entities are counted by the prefab they link to, or, if the link was broken, the prefab
they were baked from. Children inside a baked prefab that came from prefabs themselves are
counted too.

## Heaviest Meshes

Every distinct mesh with `total triangles = instances x triangles each`, heaviest total
first. This is the tab for "what is eating my triangle budget": a 40,000-triangle statue
used once and a 900-triangle crate used 600 times both show up near the top.

- Triangles are **full detail (LOD0) only**. What the engine draws at a distance is less.
- The count is per mesh, not per prefab. A building made of eight parts is eight rows.
- **Select** selects every entity that uses the mesh. **Go** jumps to the first.
- This tab runs a second, heavier scan the first time you open it, and again on Refresh
  while it is showing.
- Terrain and painted flora are not entities and are not counted.
- If a mesh reads as 0 triangles, the engine did not expose its face count; the header
  says how many did.

## Rows, Show More and the report

Each table shows 60 rows at first. **Show More** adds 60 at a time, up to 600. The header
line always gives the real total.

**Write Report** saves the totals and all three tables in full (no row limit) to:

`Documents\Mount and Blade II Bannerlord\MaterialSwapTool\Reports\scene-stats_<scene>_<date>_<time>.txt`

The status line shows the full path. The Heaviest Meshes table is only in the report if
that tab has been opened since the last Refresh.

## Select and the short delay

Select uses the same delayed selection as every other select-in-editor button in the
toolkit: the selection lands a couple of frames after the click. Selecting several
thousand entities at once can make the editor pause briefly.
