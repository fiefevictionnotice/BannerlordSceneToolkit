> Built into the in-editor documentation in v0.9.0 (2026-11-12). Kept here as the source draft.

# Tooltips and unsaved-change warnings

Draft User Guide text for features added 2026-11-12 that do not have an in-editor topic yet.

## Tooltips

Rest the mouse on a toolkit button for about half a second and a small box explains what the
button does: what it acts on (your selection, the rules list, the whole scene, files on disk),
whether it asks before acting, and whether it can be undone.

- The tooltip goes away when you move off the button or click it. After a click it stays away
  until the mouse moves to another button.
- Some text fields have a tooltip too; hover the dark box around the field.
- Tooltips follow the panel size you set with Ctrl+Minus / Ctrl+Equals.
- To switch them off: F9 > Keyboard Shortcuts > "Tooltips: ON/OFF". The setting is saved.

Tooltips never take a click or a keystroke. They are drawn on their own layer that ignores
all input.

## Unsaved-change warnings

The Interior Whitelist, Category Editor and Culture Editor only write your edits when you press
Save. If you press Close or Esc with edits that have not been saved, the toolkit now asks:

- **Discard changes** closes the panel and drops the edits.
- **Keep editing** leaves the panel open so you can press Save.

There is no question when nothing changed. Panels that are closed for you (their parent panel
closing, or a scene switch) still close without asking.

Delete Interior Entities (F7) also warns when the Interior Whitelist is open with unsaved
edits, because it would act on the saved list, not on what you see in the panel.

## "Simplify for Export"

The old name "Solidify" is gone from the confirmation dialog, its button, and the status and
log messages. The button, the dialog and the messages now all say "Simplify for Export".
Files written by earlier versions (`solidified_*.jsonl`) keep their names.
