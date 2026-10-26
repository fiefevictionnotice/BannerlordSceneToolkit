# Panel size and panel position

> **Not yet in the in-editor docs.** These features shipped in v0.8.0 (size) and v0.8.1
> (centre key), but no Documentation-button topic covers them yet. This page is the
> draft for that topic. When it is built into `DocumentationVM.cs`, delete this file: the
> exported `in-editor/USER-GUIDE.md` will carry it from then on.

## Panel size: one size for every panel

All toolkit panels share **one** size. With any panel focused (click it first):

| Keys | Effect |
|------|--------|
| Ctrl + Minus | Smaller, 10% per press |
| Ctrl + Equals (the unshifted `+` key) | Bigger, 10% per press |
| Ctrl + 0 | Back to the default, 80% |

- **Number row only.** The numpad belongs to Grow Selection (Ctrl+Numpad+), so Ctrl+Numpad
  keys never resize a panel.
- **Range 50% to 150%.** Panels open at 80% until you change it.
- **Applies everywhere, live.** A change made in one panel resizes every panel that's
  already open, and every panel you open later.
- **Remembered between sessions** in
  `Documents\Mount and Blade II Bannerlord\MaterialSwapTool\PanelScales.json` (a single
  number). A hand-edited value outside 50-150% is clamped back into range. Delete the file
  to return to the default.
- **Not inside Grow Selection.** While the Grow Selection panel is open, its own keys step
  the radius, and the size keys are ignored there. Resize from any other panel.
- **Shift must be up.** Ctrl+Shift+0 is the centre key below, not a size reset.

## Panel position: drag, saved per panel

- **Drag any panel by its title row.** Where you drop it is saved for that panel and
  restored next time it opens, in
  `Documents\Mount and Blade II Bannerlord\<Tool>\PanelPositions.json`, one file per tool
  (`MaterialSwapTool`, `PrefabSwapperTool`, `PrefabCreatorTool`).
- **Never fully off-screen.** A panel's centre is kept at least 150 pixels inside the
  screen edge. This matters after a resize: a panel that grew could otherwise open with
  its title row and Close button out of reach.

## Centre all open panels

**Ctrl + Alt + 0**, or **Ctrl + Shift + Numpad 0**, moves every open toolkit panel to the
middle of the screen and saves that as its position, as if you'd dragged it there.

- Works whether or not a panel has focus, and can't be switched off.
- Panels that are closed at the time keep their saved positions.
- **Ctrl + Shift + 0** on the number row also works if Windows lets it through. A default
  Windows install reserves that combo for switching input language and swallows it, hence
  the two alternatives.

(The Keyboard shortcuts topic in the in-editor User Guide already mentions the centre key;
this page collects it with the size and drag behaviour it depends on.)
