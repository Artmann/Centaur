# Run a command in a pane

A pane is a live `powershell.exe` behind a ConPTY. The user types into the window, presses
Enter, and sees the command echoed with syntax highlighting, then its output, then a fresh
prompt. This is the feature every other one is built on: if a keystroke does not reach the
shell, nothing else in this map means anything.

## Sub-features

- `run-type` renders typed characters at the prompt with the shell's own highlighting.
- `run-submit` sends the line on Enter and the shell executes it.
- `run-output` renders stdout, stderr and a returned prompt.
- `run-error` renders PowerShell's error formatting without corrupting the screen.
- `run-cwd` starts each pane's shell in the directory the settings name.
- `run-interrupt` sends `0x03` on `ctrl+c` when nothing is selected.

## How to get to it (user POV)

- Click a pane, type, and press Enter. This is the only entry point.
- The first pane of a freshly launched window is already the target; `launch` clicks it.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True` and `configInUse: True`.
- `<run>/workdir` is empty.

- **Focus the pane.** Click into it. Run `& $c focus`. The caret in the pane blinks; the window
  is foreground.
- **Confirm the working directory.** Read the prompt. Run `& $c shot -Name 01-prompt`. The
  prompt reads `PS <run>\workdir>`, matching the path printed by `& $c workdir`.
- **Type a command.** Run
  `& $c type -Text "'centaur-verified' | Set-Content marker.txt"`, then
  `& $c shot -Name 02-command-typed`. The line appears after the prompt, with the quoted string
  and `Set-Content` in different colours - proof the PTY is feeding the parser, not just a
  glyph buffer.
- **Submit it.** Run `& $c key -Combo enter`, wait ~2s, then `& $c shot -Name 03-command-run`.
  The command stays echoed above a fresh prompt and no error text appears.
- **Prove the bytes reached the shell.** Read the file the command wrote:
  `Get-Content (Join-Path (& $c workdir) 'marker.txt')` prints `centaur-verified`. A screenshot
  alone would only prove rendering.
- **Prove error output renders.** Run `& $c type -Text "Get-ChildItem /no/such/path"` then
  `& $c key -Combo enter`, wait ~2s, `& $c shot -Name 04-error`. The screenshot shows
  PowerShell's red `ItemNotFoundException` block and a usable prompt underneath it.
- **Prove the interrupt path.** With no selection active, run `& $c key -Combo ctrl+c` and
  `& $c shot -Name 05-interrupt`. The pane shows `^C` and returns to a fresh prompt.
- **Proof set.** `01-prompt.png` through `05-interrupt.png`, plus the contents of
  `<run>/workdir/marker.txt`.

## Gotchas

- Foreground is not focus. A window that has never been clicked renders normally and drops every
  keystroke silently. Run `focus` first; after a split or tab switch, `focus -X -Y` the pane.
- `type` sends one Unicode keystroke per character with no shell quoting applied. What you pass
  is what the shell sees, so quote for PowerShell, not for the harness.
- A shell command needs time. Screenshotting immediately after `enter` captures the line before
  the output arrives, which reads as a failure that is really a race.
- `ctrl+c` copies instead of interrupting when a selection is active. Clear it first.
- A multi-line paste or an unterminated quote leaves PowerShell at a `>>` continuation prompt.
  The pane looks idle but is not; send `escape` or `ctrl+c` before the next recipe.
- Nothing in this recipe writes outside `<run>/workdir`. Keep it that way - a command with an
  absolute path elsewhere breaks the run's isolation.
