# Selection and clipboard

The user drags across terminal text to select it, copies with `Ctrl+C`, and pastes with
`Ctrl+V` or `Shift+Insert`. Pasted text is wrapped for bracketed paste so a shell can tell it
from typing, and an image on the clipboard is written to a file whose path is pasted instead.

## Sub-features

- `clip-select` selects a range of cells by dragging, and highlights it.
- `clip-copy` copies the selection on `Ctrl+C`.
- `clip-paste` inserts clipboard text on `Ctrl+V` and on `Shift+Insert`.
- `clip-bracketed` wraps a paste in bracketed-paste markers when the program asked for them.
- `clip-image` writes a clipboard image to a file and pastes that path.
- `clip-menu` offers Copy, Paste and Paste Image as File on the pane context menu.

## How to get to it (user POV)

- Press and drag across text in a pane, then press `Ctrl+C`.
- Press `Ctrl+V` or `Shift+Insert` in a pane.
- Right-click a pane and choose `Copy`, `Paste` or `Paste Image as File`.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True`.
- The pane shows at least one line of text to select - run any command first.
- **Save the user's clipboard** and restore it when the recipe ends. Everything below reads and
  writes the real system clipboard:

  ```powershell
  $saved = Get-Clipboard -Raw
  try { <recipe> } finally { if ($null -ne $saved) { Set-Clipboard -Value $saved } }
  ```

- **Select a range.** Drag across one line. Run
  `& $c drag -X 613 -Y 436 -ToX 900 -ToY 436` then `& $c shot -Name 30-selection`. The dragged
  cells are drawn with a highlight background; the rest of the line is not.
- **Copy it.** Press `Ctrl+C`. Run `& $c key -Combo ctrl+c`, then `Get-Clipboard -Raw`. The
  clipboard holds exactly the highlighted text, trimmed to the dragged columns - the off-screen
  proof that the selection was real and not just painted.
- **Paste it back.** Put a known value on the clipboard and paste. Run
  `Set-Clipboard -Value "'clip-verified' | Set-Content clip.txt"`, then
  `& $c key -Combo ctrl+v`, `& $c shot -Name 31-pasted`, `& $c key -Combo enter`, wait ~2s.
  The pasted line appears at the prompt and `<run>/workdir/clip.txt` contains `clip-verified`.
- **Paste via the other binding.** Run `& $c key -Combo shift+insert` and
  `& $c shot -Name 32-shift-insert`. The same text appears at the prompt.
- **Paste through the menu.** Right-click the pane and choose `Paste`. Run
  `& $c click -X 400 -Y 300 -Button right`, `& $c windows` to confirm the menu opened,
  `& $c key -Combo down -Count 1` (`Paste` is the first row with no selection, the second with
  one), `& $c key -Combo enter`, then `& $c shot -Name 33-menu-paste`.
- **Paste an image as a file.** Put an image on the clipboard, then choose `Paste Image as File`
  (`down -Count 2` with no selection). The pane receives a file path, and a file exists at that
  path. Read it back to confirm it is a real image and not an empty stub.
- **Proof set.** `30-selection.png` through `33-menu-paste.png`, the `Get-Clipboard` output
  after the copy, and `<run>/workdir/clip.txt`.

## Gotchas

- **The clipboard is global and shared with the user.** Every recipe here overwrites what the
  user had copied. Save and restore it, and never leave the run's test string sitting there.
- `Get-Clipboard`/`Set-Clipboard` handle text only. If the user's clipboard held an image or a
  file list, restoring it as text loses it - check before assuming the save round-trips.
- `Ctrl+C` is overloaded. With a selection it copies; with none it sends `0x03` to the shell.
  A recipe that expects an interrupt must clear the selection first, and one that expects a copy
  must confirm the selection is still live - clicking anywhere drops it.
- The copy is trimmed to the dragged columns, not to the line. Dragging from mid-word gives a
  mid-word string; compare against what you dragged, not against the visible line.
- Drag coordinates are cell-quantised. A one-pixel difference can move the boundary by a whole
  character, so assert on the copied text rather than on an exact substring index.
- Bracketed paste is only wrapped when the running program has enabled it. Under a plain prompt
  the markers are absent and their absence is correct.
- A pasted multi-line string executes every line. Keep pastes to a single line unless the recipe
  is specifically about multi-line paste.
