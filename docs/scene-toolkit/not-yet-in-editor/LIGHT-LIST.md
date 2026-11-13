> Built into the in-editor documentation in v0.9.0 (2026-11-12). Kept here as the source draft.

# Light List

Every light in the open scene in one list, with the ones most likely to cost performance
or to be mistakes at the top. Read-only: it does not edit lights.

Open it with **F7 -> Lights** (bottom row, next to Close). It is the Lights tab of the
Scene Statistics panel, so the same Refresh, Show More, Write Report and Close apply. If
the Statistics panel is already open, the button just switches it to this tab.

## What a row shows

`[!] radius 25   SHADOW   intensity 1000   rgb 1.00 0.60 0.24   'flame_loop' in 'torch_wall_a'   - shadow-casting with a large radius - likely expensive`

- **radius**, **SHADOW / no shadow**, **intensity** and **color** as the engine reports
  them for the live scene.
- The **owner**: the entity that carries the light, and the top-level prefab it sits in
  when it is a child (lights usually are: the flame inside a torch).
- `(hidden)` when the owner, or a parent, is currently invisible.
- **Go** jumps to the owner's position and selects the entity there. That is normally the
  owner; if its parent sits at exactly the same spot, the parent may be picked instead.

The header gives the count: how many lights, how many cast shadows, how many are flagged.

## Sorting

- **Shadow cost** (default): shadow-casting lights first, largest radius first, then the
  rest by radius. A shadow-casting light has to re-render what it can reach, and how much
  it reaches grows with its radius, so this order puts the expensive ones on top.
- **Radius**: largest first, shadows or not.

The button at the top right switches between the two and names the order it will switch to.

## Warnings

A flagged row starts with `[!]`, is tinted, and says why:

| Flag | Meaning |
|------|---------|
| shadow-casting with a large radius - likely expensive | Shadows on and radius 15 or more. Native torches sit around 6 to 10. |
| very large radius | Radius 40 or more, without shadows. |
| zero radius / zero intensity - lights nothing | The light exists but cannot light anything. Usually a leftover. |
| N lights at this position | Two or more lights within 5 cm of each other: almost always a duplicated torch stacked on itself, doubling the cost for no visible gain. |
| +N more light component(s) on this entity, not listed | The entity carries several lights; only the first can be read. |
| could not be read | The engine did not return the light. Listed so the count still adds up. |

The thresholds are judgement calls for "worth a look", not engine limits. A large
shadow-casting light can be exactly what a scene needs; a dozen of them in one courtyard
usually is not.

## Limits

- **Read-only in this version.** Change radius, intensity and shadows in the editor's own
  inspector; then press Refresh here.
- Only whether shadows are **on** is shown. The engine does not expose whether a light's
  shadow is static or dynamic.
- Radius is the light's own value and does not account for a scaled parent.
- The sun and sky lighting come from the atmosphere, not from light entities, and are not
  in this list.
- The list shows 60 rows at first; **Show More** adds 60 at a time. **Write Report** saves
  all of them, with positions.
