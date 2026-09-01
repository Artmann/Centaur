# Settings

A full-window settings page that covers the terminal area while the tab strip and the caption
buttons stay live above it. A full-height sidebar holds the way out, a search box and the
**General** / **Appearance** tabs; the page beside it is a heading and a stack of cards, one card
per section, one row per setting. Every edit saves and applies immediately - there is no apply or
cancel step. Closing the page returns focus to the pane that had it.

Every affordance on the page is keyboard-operable: `Tab` walks the sidebar, the rows and the
steppers, arrows move within a segmented control or a radio group, `Space` and `Enter` activate,
and whatever the keyboard is standing on wears a visible ring. Nothing on the page needs a mouse.

## Sub-features

- `settings-open` opens the page with `Ctrl+,` and closes it with `Ctrl+,`, `Escape`, or the
  sidebar's `← Back`.
- `settings-nav` switches between General and Appearance from the sidebar.
- `settings-search` filters rows across *both* tabs from one query, and offers the section names
  as a way on when nothing matches.
- `settings-theme` switches the palette and repaints the panes and the window chrome live.
- `settings-font` changes font size and line height, and the grid re-measures.
- `settings-start-dir` chooses where a new pane opens: last used, home, or a specific folder.
- `settings-persist` writes every edit to `settings.json` and restores it after a restart.
- `settings-keyboard` reaches and operates every setting without a pointer, and keeps focus
  inside the page across the rebuild a theme change causes.

## How to get to it (user POV)

- Press `Ctrl+,` to open the page, and `Escape`, `Ctrl+,` again, or `← Back` to close it.
- Click **General** or **Appearance** in the left sidebar.
- Type in the sidebar's search box to filter; clear it to go back to the selected tab.
- Click a segment of a segmented control (theme, bell, cursor style) to choose that value.
- Click the switch on a row that is on or off (cursor blink).
- Click a radio row to choose a starting directory.
- Click `−` or `+` on a stepper, or click into its number and type one (scrollback, size, line
  height, opacity, padding). Each carries its unit - `10,000 lines`, `14 pt`, `1.20×`, `100%`,
  `8 px` - and greys out the step that would do nothing at a bound.
- Type into the Shell box or the specific-folder box; it saves as you type.

From the keyboard alone:

- `Tab` from the search box reaches the sidebar tabs; `Up`/`Down` move between them.
- `Tab` again enters the rows. A segmented control, a radio group and the theme picker are each
  **one** tab stop: `Left`/`Right` (or `Up`/`Down`) change the choice, `Home`/`End` jump to the
  first or last.
- `Space` or `Enter` flips a switch or activates `← Back`.
- On a stepper, `Up`/`Down` step by one, `PageUp`/`PageDown` by ten, `Enter` commits a typed
  value and `Escape` reverts it.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True` and `configInUse: True`.
- The window is at its launch size of 1200x800. Every coordinate below is a client coordinate at
  that size and moves if the window is resized.

- **Open the page.** Run `& $c key -Combo ctrl+comma`, wait ~1s, then
  `& $c shot -Name 01-settings-general`. The page fills the terminal area. The sidebar holds
  `← Back` at `~50,68` with its `Esc` hint at `~180,68`, the search box at `~107,108`, and the
  tabs `General` `~50,152` (highlighted) and `Appearance` `~63,186`. The page heading names the
  open tab: `General` at `~275,81`.
- **Read the General tab.** The dim `Shell` label at `~254,122` heads a card holding the
  `Command` row (box at `~600,206`) and the `Starting directory` radios at `~271,314` (last
  used), `~271,368` (home) and `~271,423` (specific), with the chosen one carrying a filled dot;
  the specific-folder box follows at `~615,469`. The `Terminal` label at `~264,530` heads a
  second card with the scrollback stepper (`−` at `~829,574`, value at `~879,574`, `+` at
  `~930,574`) and the bell segments `Off` `~814,633`, `Sound` `~863,633`, `Flash` `~918,633`.
- **Search across both tabs.** Click the search box (`& $c click -X 107 -Y 108`), wait ~400ms,
  run `& $c type -Text "cursor"` then `& $c shot -Name 02-search-filtered`. The heading changes
  to `Search results`, the rows collapse to one card headed `Appearance · Cursor` holding both
  `Blink` and `Style` even though General is still the selected tab, and the sidebar highlight
  clears. Clear the box with `& $c key -Combo ctrl+a` then `& $c key -Combo backspace` to return
  to the tab's own rows.
- **See the empty state.** With the box focused, `& $c type -Text "zzzz"` then
  `& $c shot -Name 03-search-empty`. `No settings match "zzzz".` is followed by
  `The page covers:` and one chip per section - `Shell` `~265,186`, `Terminal` `~331,186`,
  `Theme` `~403,186`, `Text` `~463,186`, `Cursor` `~523,186`, `Window` `~594,186` - each of which
  runs that section as a fresh query.
- **Switch tabs.** Run `& $c click -X 63 -Y 186` then `& $c shot -Name 04-settings-appearance`.
  The `Theme` row comes first with **no** section heading above it - a section holding one row
  drops the heading that would only repeat the row's own title. Its segments carry a colour
  swatch each: `Latte` `~657,151`, `Frappé` `~731,151`, `Macchiato` `~818,151`, `Mocha`
  `~903,151`. Then `Text` (`Size` stepper `−`/value/`+` at `~847`/`~888`/`~929,269`, `Line
  height` at the same columns on `,328`), `Cursor` (`Block` `~800,438`, `Underline` `~863,438`,
  `Bar` `~921,438`, blink switch `~925,497`) and `Window` (`Opacity` on `,607`, `Padding` on
  `,666`).
- **Flip a switch.** Run `& $c click -X 925 -Y 497`. The track fills with the accent and the knob
  moves to the right, and `settings.json` gains `"CursorBlink": true`.
- **Type a number and watch it clamp.** Double-click the size value (`& $c click -X 888 -Y 269
  -Count 2`), then `& $c key -Combo ctrl+a`, `& $c type -Text "99"`, `& $c key -Combo enter`,
  then `& $c shot -Name 05-stepper-clamped`. The field reads `48 pt`, the `+` glyph greys out
  because 48 is the ceiling, the `−` stays lit, and `settings.json` holds `"FontSize": 48`.
- **Switch the theme live.** Run `& $c click -X 657 -Y 151`, wait ~1s, then
  `& $c shot -Name 06-theme-latte-live`. The page, the sidebar, the cards, the segments, the tab
  strip and the caption buttons all repaint to the light palette in the same frame.
- **Drive the whole page from the keyboard.** With the page freshly opened:
  `& $c key -Combo tab` (sidebar), `& $c key -Combo down` (Appearance),
  `& $c key -Combo tab` (into the theme picker), `& $c key -Combo home`,
  `& $c shot -Name 07-theme-latte-keyboard`, `& $c key -Combo right`,
  `& $c shot -Name 08-theme-frappe-keyboard`. `Home` selects Latte and repaints the page light;
  `Right` moves to Frappé and repaints it dark **with the ring still on the Frappé segment**. A
  ring on a caption button in that second shot is the focus-restore regression, not a pass.
- **Check the wide layout.** Click the maximise caption button (`& $c click -X 1130 -Y 17`) and
  `& $c shot -Name 09-maximised`. The content column stays capped and left-aligned against the
  sidebar rather than drifting to the middle of the screen.
- **Prove the panes repainted too.** Run `& $c key -Combo escape` then
  `& $c shot -Name 10-back-to-terminal`. Text that was already on screen under the old theme is
  now drawn in the new one, at the new size - the recolour pass covers the existing cells, not
  just new output.
- **Prove the pane took focus back.** Run
  `& $c type -Text "Set-Content marker.txt pane-alive-after-settings"` and
  `& $c key -Combo enter`, wait ~2s. `<run>/workdir/marker.txt` holds that line.
- **Prove the tab strip stays live above the page.** Run `& $c key -Combo ctrl+t`, wait ~2s,
  `& $c key -Combo ctrl+comma`, then `& $c shot -Name 11-tabstrip-above-page`. Both tab labels
  and the `+` button are visible above the page, which starts below the strip.
- **Prove window opacity composites.** With the page open, click the opacity `−` six times
  (`& $c click -X 847 -Y 607`), press `Escape`, then run
  `& $c shot -Name 12-opacity-70-screen -Screen`. Whatever sits behind Centaur shows through the
  pane background while the terminal's own glyphs stay fully opaque. A plain `shot` cannot show
  this - see the gotchas.
- **Prove persistence.** Run `& $c state` mid-run and again after `& $c stop`. Both dumps show
  `settings.json` carrying the edits, e.g. `"ThemeId": "catppuccin-frappe"`, `"FontSize": 48`
  and `"CursorBlink": true`.
- **Proof set.** `01-settings-general.png` through `12-opacity-70-screen.png`, the contents of
  `<run>/workdir/marker.txt`, and the post-`stop` `state` dump.

## Gotchas

- **A click or a keystroke in the first few hundred milliseconds after `Ctrl+,` is lost.** The
  page moves focus to the search box on a posted dispatcher callback, which lands *after* an
  immediate click and re-selects the box, swallowing what was typed. Wait ~400ms after opening
  before driving anything, and re-shoot if a `type` appears to have done nothing.
- **A theme change rebuilds the page under the keyboard.** The page reads which control was
  focused before the rebuild and puts focus back on the same setting afterwards, and only
  re-rings it if the keyboard - not the pointer - was what put it there. A shot after a keyboard
  theme change must show the ring on the picker; if it is on a caption button, the restore broke.
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
- **A section holding one row shows no heading.** `Theme` on Appearance and any single-row group
  a search turns up render as a bare card. "The heading is missing" is the design, not a
  rendering fault.
- **A segmented control is one tab stop, not one per segment.** `Tab` past a theme picker lands
  on the next *row*; the arrows are what move between Latte and Mocha. Driving it by pressing
  `Tab` four times walks off the row entirely.
- The page is in the shell's UI font, not the terminal's monospace. Two boxes stay monospace on
  purpose - the Shell command and the specific-folder path - because they hold code, not prose.
  A screenshot showing those two in a different face from their labels is correct.
- A single tab shows no tab strip (see `tabs.md`), so "the strip is missing above the page" at
  baseline is the strip's own auto-hide, not the page covering it. Open a second tab first.
- The nav highlight clears while a search is active, because the results are no longer one tab's.
  That is deliberate, not a lost selection.
- Every coordinate here is tied to the 1200x800 launch size. Re-screenshot after any resize
  rather than reusing them.
