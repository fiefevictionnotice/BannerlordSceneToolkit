> Built into the in-editor documentation in v0.9.0 (2026-11-12). Kept here as the source draft.

# Scene Diff

Scene Diff answers "what changed between these two saves?" It compares two `scene.xscene`
files for the open scene and lists every entity that was added, removed, moved, rotated,
scaled, renamed or otherwise changed.

Open it with **F7 -> Scene Diff** (bottom row, next to Close).

## It reads saved files, not the editor

The diff reads files on disk. Anything you have changed since your last save is in
**neither** file and will not show up. Save first if you want the current state compared.
The panel's status line says how long ago the scene was last saved.

## Picking the two states

The list shows the **saved file** on top, then every backup of this scene, newest first.
Each backup row shows when it was taken, why (`auto-4min`, `before-apply`, `manual`, ...),
which save it is a copy of, and its size.

- **From** marks the older state (purple). **To** marks the newer one (green).
- Any two rows can be compared, including two backups.
- **Default pair:** To = the saved file, From = the newest backup that is *not* a copy of
  that same save. The newest backup is often identical to the saved file (a backup is a
  copy of the save), and comparing those can only say "no differences". Rows marked
  `= identical to the saved file` are those copies.
- If From turns out to be the newer save, the diff still runs; it just reads backwards
  (Added means "gone since"). The status line says so.
- At most 60 backups are listed. The default retention keeps 50.

Press **Compare**. Both files are read in the background, so the editor stays responsive;
a 13,000-entity scene took about a fifth of a second outside the editor. The result opens
in the results window on the left.

## Reading the result

The first line is the summary, for example:

`+12 added, -1 removed, 40 moved, 5 rotated/scaled, 2 renamed, 1 prefab ref, 7 material, 3 color, 9 other  (13,112 -> 13,556 entities)`

Below it, one section per kind of change:

| Section | What it lists |
|---------|---------------|
| Removed | In From, not in To. Position is where it used to be. |
| Added | In To, not in From. |
| Prefab reference changed | Swapped to a different prefab in place, or a prefab link broken (baked into the scene) or restored. |
| Renamed | Same entity, different name. |
| Moved | Position changed. Shows the distance, farthest first, and the old position. |
| Rotated / scaled in place | Position unchanged, rotation or scale changed. |
| Material changed | A mesh's material differs, shown as `old -> new (n meshes)`. |
| Color changed | A mesh's color factor differs, shown as `#AARRGGBB -> #AARRGGBB`. |
| Other changes | Tags (`+added -removed`), scripts, physics, light, components, mesh arguments, flags/visibility, prefab child overrides. |

An entity appears once, in the first section that applies to it, with everything else that
changed on it listed on the same line. The summary counts every kind separately, so one
entity that moved and changed material counts in both numbers.

- **Added and removed rows are top-most only.** A removed prefab with 36 children is one
  row, `(+36 child entities)`, not 37.
- **Go** jumps the camera to the row's position and selects the entity there if the live
  scene has one. For a removed row the camera goes to where it used to be. Positions come
  from the To file; if you compared two backups, the live scene may have moved on.
- **Children** show their top-level parent's position (that is where Go takes you) plus
  their own local position as text.
- A note appears when scene-level data outside the entity list (atmosphere, water, terrain
  layers, paths) also differs. It is not itemized.

Each section shows a share of the row limit. Raise **Show first** at the bottom of the
results window to see more, or use **Write Report** for everything.

## Write Report

**Write Report** on the Scene Diff panel saves the complete, untruncated result of the last
comparison as a text file in:

`Documents\Mount and Blade II Bannerlord\MaterialSwapTool\Reports\scene-diff_<scene>_<date>_<time>.txt`

The status line shows the full path.

## How entities are matched, and where that can be wrong

A scene file has no entity IDs. An entity has a name and/or a prefab, a transform, and a
place in the parent/child tree, and duplicate names are normal (hundreds of the same
rock). So Scene Diff has to *match* entities between the two files:

1. It works one sibling group at a time: top-level entities first, then the children of
   each matched pair.
2. Within a group, entities are grouped by what they were made from (prefab, or the prefab
   they were baked from, or their name) and paired in passes, most certain first:
   identical transform; same position; nearest remaining position; same place but renamed;
   same transform but a different prefab.
3. Whatever is left over is Removed or Added.

Limits worth knowing:

- **Moved vs. deleted-and-replaced.** If you delete one barrel and place another of the
  same prefab elsewhere, the file looks exactly like "that barrel moved". Scene Diff
  reports a move and shows the distance, so an implausible 300 m move is easy to spot.
- **Which duplicate moved is a guess.** Among identical entities, the closest pairs are
  matched first. Counts and positions are right; the pairing may not be the one you made.
- **Changed name *and* moved** shows as one Removed plus one Added.
- **Reparenting** (dragging an entity under another) shows as Removed plus Added.
- **Rotation** is compared as an orientation, not as three numbers. The editor rewrites
  the angles of upright objects on every save, and comparing the numbers would report
  dozens of rotations that never happened. Rotations under about a quarter of a degree are
  ignored. If one file stores a rotation as angles and the other as a matrix, the row says
  "rotation rewritten" because the two cannot be compared reliably.
- **Position** changes under 1.5 mm are ignored (the file stores three decimals).
- Terrain, flora and nav mesh data live in other files and are not compared.
