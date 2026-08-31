# Settings

A full-window settings page that covers the terminal area while the tab strip and the caption
buttons stay live above it. A sidebar picks between a **General** and an **Appearance** tab, a
search box spans both, and every edit saves and applies immediately - there is no apply or cancel
step. Closing the page returns focus to the pane that had it.

## Sub-features

- `settings-open` opens the page with `Ctrl+,` and closes it with `Ctrl+,` or `Escape`.
- `settings-nav` switches between General and Appearance from the sidebar.
- `settings-search` filters rows across *both* tabs from one query.
- `settings-theme` switches the palette and repaints the panes and the window chrome live.
- `settings-font` changes font size and line height, and the grid re-measures.
- `settings-start-dir` chooses where a new pane opens: last used, home, or a specific folder.
- `settings-persist` writes every edit to `settings.json` and restores it after a restart.

## How to get to it (user POV)

- Press `Ctrl+,` to open the page, and `Escape` or `Ctrl+,` again to close it.
- Click **General** or **Appearance** in the left sidebar.
- Type in the search box at the top to filter; clear it to go back to the selected tab.
- Click a pill (theme, bell, cursor style, cursor blink) to choose that value.
- Click `-` or `+` on a stepper (scrollback, font size, line height, opacity, padding).
- Type into the Shell box or the specific-folder box; the value saves as you leave the field.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True` and `configInUse: True`.
- The window is at its launch size of 1200x800. Every coordinate below is a client coordinate at
  that size and moves if the window is resized.

- **Open the page.** Run `& $c key -Combo ctrl+comma`, wait ~1s, then
  `& $c shot -Name 01-settings-general`. The page fills the terminal area: `Settings` at
  `~75,75`, the search box at `~274,75`, `Esc to close` at `~464,75`, and the sidebar with
  `General` at `~97,132` (highlighted) and `Appearance` at `~97,166`.
- **Read the General tab.** The `SHELL` section shows the Shell box at `~1043,185` and the
  starting-directory rows at `~700,290` (last used), `~700,342` (home) and `~700,395`
  (specific), with the chosen one carrying a left accent bar. The `TERMINAL` section shows the
  scrollback stepper (`-` at `~1029,540`, value at `~1088,540`, `+` at `~1148,540`) and the bell
  pills `Off` `~1004,591`, `Sound` `~1063,591`, `Flash` `~1128,591`.
- **Search across both tabs.** With General selected, run `& $c type -Text "cursor"` then
  `& $c shot -Name 02-search-filtered`. The rows collapse to a single group headed
  `APPEARANCE · CURSOR` even though General is still the selected tab, and the sidebar
  highlight clears. Clear the box with `& $c key -Combo ctrl+a` then `& $c key -Combo delete`
  to return to the tab's own rows.
- **Switch tabs.** Run `& $c click -X 97 -Y 166` then `& $c shot -Name 03-settings-appearance`.
  Four sections appear: `THEME` (pills `Latte` `~899,182`, `Frappe` `~967,182`, `Macchiato`
  `~1049,182`, `Mocha` `~1128,182`), `TEXT` (font size stepper at `~1029/1148,283`, line height
  at `~1029/1148,335`), `CURSOR` (`Block` `~982,431`, `Underline` `~1061,431`, `Bar`
  `~1134,431`; `Steady` `~1038,486`, `Blinking` `~1117,486`) and `WINDOW` (opacity at
  `~1029/1148,587`, padding at `~1029/1148,639`).
- **Switch the theme live.** Run `& $c click -X 899 -Y 182`, wait ~1s, then
  `& $c shot -Name 04-theme-latte-live`. The page, the sidebar, the pills, the tab strip and the
  caption buttons all repaint to the light palette in the same frame.
- **Prove the panes repainted too.** Run `& $c key -Combo escape` then
  `& $c shot -Name 05-back-to-terminal`. Text that was already on screen under the old theme is
  now drawn in the new one - the recolour pass covers the existing cells, not just new output.
- **Prove the pane took focus back.** Run
  `& $c type -Text 'Set-Content -Path marker.txt -Value "pane took keys after settings closed"'`
  and `& $c key -Combo enter`, wait ~2s. `<run>/workdir/marker.txt` holds that line.
- **Prove the tab strip stays live above the page.** Run `& $c key -Combo ctrl+t`, wait ~2s,
  `& $c key -Combo ctrl+comma`, then `& $c shot -Name 06-tabstrip-above-page`. Both tab labels
  and the `+` button are visible above the page, which starts below the strip.
- **Prove window opacity composites.** With the page open, click the opacity `-` six times
  (`& $c click -X 1029 -Y 587`), press `Escape`, then run
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
- A single tab shows no tab strip (see `tabs.md`), so "the strip is missing above the page" at
  baseline is the strip's own auto-hide, not the page covering it. Open a second tab first.
- The nav highlight clears while a search is active, because the results are no longer one tab's.
  That is deliberate, not a lost selection.
- Every coordinate here is tied to the 1200x800 launch size. Re-screenshot after any resize
  rather than reusing them.
