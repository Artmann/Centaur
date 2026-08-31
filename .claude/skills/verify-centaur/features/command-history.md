# Command history

Commands submitted in any pane are recorded in one shared history file. Two features read it: an
inline ghost suggestion that completes what the user is typing and is accepted with `Tab`, and a
reverse-search overlay opened with `Ctrl+R` that filters history as the user types and runs the
chosen command.

## Sub-features

- `hist-record` appends a submitted command to the shared history.
- `hist-share` makes a command typed in one pane visible to every other pane and tab.
- `hist-suggest` shows the rest of a matching past command as ghost text while typing.
- `hist-accept` completes the line on `Tab`.
- `hist-search` opens the reverse-search overlay on `Ctrl+R` and filters it by query.
- `hist-run` runs the selected entry on Enter and closes the overlay.

## How to get to it (user POV)

- Start typing a command that was run before, and press `Tab` to accept the ghost completion.
- Press `Ctrl+R` in a pane, type to filter, move with `Up`/`Down`, press Enter to run or `Esc`
  to dismiss.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True`.
- At least one command has been submitted this run, so history is not empty. The
  [run a command](./run-command.md) recipe leaves
  `'centaur-verified' | Set-Content marker.txt` in history.
- `<run>/config/command-history.json` is the run's own file, not the user's.

- **Confirm the command was recorded.** Run `& $c state`. The `command-history.json` dump lists
  the submitted command.
- **Open reverse search from another pane.** Switch tabs so the query cannot be answered by the
  pane's own scrollback: `& $c key -Combo ctrl+t`, wait ~2s, `& $c focus`, then
  `& $c key -Combo ctrl+r`, and `& $c shot -Name 40-reverse-search`. An overlay fills the bottom
  of the window: the matching command on a highlighted row near client y `732`, a
  `Type to search...` input at y `772`, and a `1 / 1` counter at the right. Seeing the first
  tab's command in the second tab is the proof that history is shared, not per-pane.
- **Filter it.** Run `& $c type -Text "marker"` and `& $c shot -Name 41-search-filtered`. The
  counter still shows a match and the row is still listed.
- **Filter it to nothing.** Run `& $c type -Text "zzzz"` and
  `& $c shot -Name 42-search-empty`. The counter reads `0 / 1` and no rows are listed.
- **Dismiss.** Run `& $c key -Combo escape` then `& $c shot -Name 43-search-closed`. The overlay
  is gone and the prompt is visible again.
- **Run an entry from history.** Reopen and commit: `& $c key -Combo ctrl+r`,
  `& $c type -Text "marker"`, `& $c key -Combo enter`, wait ~2s, then
  `& $c shot -Name 44-history-run`. Enter **runs** the selected command, it does not merely
  insert it - the overlay closes and the command is echoed at the prompt and executed. Delete
  `<run>/workdir/marker.txt` first so its reappearance is the off-screen proof.
- **Accept a ghost suggestion.** Type a prefix of a past command: `& $c type -Text "'centaur"`,
  then `& $c shot -Name 45-ghost`. The remainder of the past command is drawn dimmed after the
  caret. Run `& $c key -Combo tab` and `& $c shot -Name 46-ghost-accepted`; the dimmed text
  becomes real input on the line.
- **Proof set.** `40-reverse-search.png` through `46-ghost-accepted.png`, the `state` dump of
  `command-history.json`, and the re-created `<run>/workdir/marker.txt`.

## Gotchas

- History is flushed like the rest of the run's state - a command submitted seconds ago may not
  be in the JSON yet. Re-read `& $c state` after `& $c stop` for the complete file.
- The overlay takes the keystrokes while it is open. `type` goes into the search box, not the
  shell, and `Up`/`Down` move the selection rather than recalling shell history.
- Enter in the overlay executes immediately. Never leave a destructive command in this run's
  history and then drive reverse search over it.
- `Tab` accepts the ghost suggestion before the shell ever sees it, so `Tab` does not do the
  shell's own completion while a suggestion is showing.
- The suggestion only appears for a genuine prefix match of a recorded command. A typo in the
  prefix means no ghost text, which is correct behaviour and not a failure.
- The run's history file starts empty. A recipe that assumes the user's history is present is
  testing the wrong file and its isolation has failed - check `& $c doctor`.
