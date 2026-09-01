# Settings

A full-window settings page that covers the terminal area while the tab strip and the caption
buttons stay live above it. A full-height sidebar holds the way out, a search box and the
**General** / **Appearance** tabs; the page beside it is a heading and a stack of cards, one card
per section, one row per setting. Every edit saves and applies immediately - there is no apply or
cancel step. Closing the page returns focus to the pane that had it.

## Sub-features

- `settings-open` opens the page with `Ctrl+,` and closes it with `Ctrl+,`, `Escape`, or the
  sidebar's `← Back`.
- `settings-nav` switches between General and Appearance from the sidebar.
- `settings-search` filters rows across *both* tabs from one query.
- `settings-theme` switches the palette and repaints the panes and the window chrome live.
- `settings-font` changes font size and line height, and the grid re-measures.
- `settings-start-dir` chooses where a new pane opens: last used, home, or a specific folder.
- `settings-persist` writes every edit to `settings.json` and restores it after a restart.

## How to get to it (user POV)

- Press `Ctrl+,` to open the page, and `Escape`, `Ctrl+,` again, or `← Back` to close it.
- Click **General** or **Appearance** in the left sidebar.
- Type in the sidebar's search box to filter; clear it to go back to the selected tab.
- Click a segment of a segmented control (theme, bell, cursor style) to choose that value.
- Click the switch on a row that is on or off (cursor blink).
- Click a radio row to choose a starting directory.
- Click `−` or `+` on a stepper (scrollback, font size, line height, opacity, padding).
- Type into the Shell box or the specific-folder box; it saves as you type.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True` and `configInUse: True`.
- The window is at its launch size of 1200x800. Every coordinate below is a client coordinate at
  that size and moves if the window is resized.

- **Open the page.** Run `& $c key -Combo ctrl+comma`, wait ~1s, then
  `& $c shot -Name 01-settings-general`. The page fills the terminal area. The sidebar holds
  `← Back` at `~50,67` with its `Esc` hint at `~180,67`, the search box at `~107,106`, and the
  tabs `General` `~100,149` (highlighted) and `Appearance` `~100,181`. The page heading names the
  open tab: `General` at `~375,81`.
- **Read the General tab.** The dim `Shell` label at `~355,123` sits above a card holding the
  Shell box at `~934,177` and the starting-directory radios at `~370,297` (last used),
  `~370,349` (home) and `~370,401` (specific), with the chosen one carrying a filled dot; the
  specific-folder box follows at `~715,446`. The `Terminal` label at `~364,501` heads a second
  card with the scrollback stepper (`−` at `~954,547`, value at `~992,547`, `+` at `~1030,547`)
  and the bell segments `Off` `~915,606`, `Sound` `~964,606`, `Flash` `~1017,606`.
- **Search across both tabs.** Click the search box (`& $c click -X 107 -Y 106`), run
  `& $c type -Text "cursor"` then `& $c shot -Name 02-search-filtered`. The heading changes to
  `Search results`, the rows collapse to one card headed `Appearance · Cursor` even though
  General is still the selected tab, and the sidebar highlight clears. Clear the box with
  `& $c key -Combo ctrl+a` then `& $c key -Combo delete` to return to the tab's own rows.
- **Switch tabs.** Run `& $c click -X 100 -Y 181` then `& $c shot -Name 03-settings-appearance`.
  Four cards appear: `Theme` (segments `Latte` `~820,169`, `Frappe` `~875,169`, `Macchiato`
  `~943,169`, `Mocha` `~1012,169`), `Text` (font size stepper at `~954/1030,275`, line height at
  `~954/1030,334`), `Cursor` (`Block` `~899,440`, `Underline` `~963,440`, `Bar` `~1017,440`, and
  the cursor-blink switch at `~1025,499`) and `Window` (opacity at `~954/1030,604`, padding at
  `~954/1030,664`).
- **Flip a switch.** Run `& $c click -X 1025 -Y 499`. The track fills with the accent and the
  knob moves to the right, and `settings.json` gains `"CursorBlink": true`.
- **Switch the theme live.** Run `& $c click -X 820 -Y 169`, wait ~1s, then
  `& $c shot -Name 04-theme-latte-live`. The page, the sidebar, the cards, the segments, the tab
  strip and the caption buttons all repaint to the light palette in the same frame.
- **Prove the panes repainted too.** Run `& $c key -Combo escape` then
  `& $c shot -Name 05-back-to-terminal`. Text that was already on screen under the old theme is
  now drawn in the new one - the recolour pass covers the existing cells, not just new output.
- **Prove the pane took focus back.** Run
  `& $c type -Text 'Set-Content -Path marker.txt -Value "pane took keys after settings closed"'`
  and `& $c key -Combo enter`, wait ~2s. `<run>/workdir/marker.txt` holds that line.
- **Prove the tab strip stays live above the page.** Run `& $c key -Combo ctrl+t`, wait ~2s,
  `& $c key -Combo ctrl+comma`, then `& $c shot -Name 06-tabstrip-above-page`. Both tab labels
  and the `+` button are visible above the page, which starts below the strip.
- **Prove window opacity composites.** With the page open, click the opacity `−` six times
  (`& $c click -X 954 -Y 604`), press `Escape`, then run
  `& $c shot -Name 09-opacity-70-screen -Screen`. Whatever sits behind Centaur shows through the
  pane background while the terminal's own glyphs stay fully opaque. A plain `shot` cannot show
  this - see the gotchas.
- **Prove persistence.** Run `& $c state` mid-run and again after `& $c stop`. Both dumps show
  `settings.json` carrying the edits, e.g. `"ThemeId": "catppuccin-latte"` and
  `"WindowOpacity": 0.7`.
- **Proof set.** `01-settings-general.png` through `06-tabstrip-above-page.png` plus
  `09-opacity-70-screen.png`, the contents of `<run>/workdir/marker.txt`, and the post-`stop`
  `state` dump.

## Gotchas

- **A theme change repaints the panes behind the page, not just the page.** A screenshot of the
  page alone does not prove the switch worked; the proof needs a second shot after `Escape`
  showing pre-existing terminal text in the new palette.
- **The page swallows `Ctrl+W` and `Ctrl+T` while it is open**, so a stray shortcut cannot close
  a pane behind it. Only `Ctrl+,` and `Escape` reach the window. Close the page before driving
  any tab or pane shortcut.
- **`settings.json` is written on every edit**, so `& $c state` mid-run is meaningful here -
  unlike `session.json`, which is debounced and only flushed on close.
- **`shot` cannot photograph window opacity.** `PrintWindow` renders the window into its own
  bitmap with nothing behind it, so a translucent window comes back looking flat grey. Use
  `shot -Screen`, which grabs the real screen and shows what actually composites.
- The page is in the shell's UI font, not the terminal's monospace. Two boxes stay monospace on
  purpose - the Shell command and the specific-folder path - because they hold code, not prose.
  A screenshot showing those two in a different face from their labels is correct.
- A single tab shows no tab strip (see `tabs.md`), so "the strip is missing above the page" at
  baseline is the strip's own auto-hide, not the page covering it. Open a second tab first.
- The nav highlight clears while a search is active, because the results are no longer one tab's.
  That is deliberate, not a lost selection.
- Every coordinate here is tied to the 1200x800 launch size. Re-screenshot after any resize
  rather than reusing them.
