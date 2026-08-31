# Centaur verification map

This directory is the maintained source for verifying Centaur's user-facing behaviour. Read this
index before driving the app, then use the matching feature file as the recipe. The harness and
its safety rules are described in [`../SKILL.md`](../SKILL.md).

## Baseline preconditions

- Build once, then launch an isolated instance: `& $c build` then `& $c launch`.
- The instance runs with `CENTAUR_CONFIG_DIR` set to `<run>/config`, so it starts from an empty
  session and never touches `%APPDATA%\Centaur`.
- The window is 1200x800 at screen `80,80`. Every coordinate in this map assumes that geometry.
- Each pane's shell starts in `<run>/workdir`, which the run owns and which starts empty.
- `& $c doctor` must report `configInUse: True` and `healthy: True` before anything is driven.
- Never drive an instance this run did not start. `otherInstances` naming a PID is the user's own
  Centaur and is left alone.

## Driving conventions

- Start every recipe from the baseline unless its preconditions say otherwise.
- Coordinates are client coordinates: `(0,0)` is the window's top-left pixel, title bar included.
- Prefer keyboard shortcuts over clicking chrome, and menu row order over menu pixels.
- Treat every command as literal. Keep quoting and flags unchanged.
- Give the app time: `-Settle` defaults to 250ms per input, a shell command needs ~2s, and a new
  pane needs ~3s for its shell to print a prompt.
- Restore the clipboard around any recipe that copies or pastes.
- Do not delete artifacts, `config/` or `workdir/` during cleanup.

## Proof and skip reporting

- Capture the user action and the resulting state, not only the final screen.
- Pair every screenshot with an off-screen check: a file in `<run>/workdir`, a `& $c state` dump,
  or a `& $c windows` listing.
- Persistence is only fully written on close. Check `& $c state` again after `& $c stop`.
- Record the feature ID and the entry point used with every artifact.
- Report an unreachable path with the attempted command and the unmet precondition.
- Do not report a skipped entry point as verified through a different path. Splits reached by
  keyboard do not prove the mouse path, and vice versa.

## Feature entry contract

Each feature file starts with an H1 title and one paragraph describing the user-visible
behaviour. It then uses exactly four H2 sections in this order.

1. `Sub-features` lists short IDs with one line for each behaviour.
2. `How to get to it (user POV)` lists every user entry point.
3. `Driving it with control-centaur.ps1` starts with `Preconditions:` and uses labelled bullets
   that pair each user action with an exact command and observable result.
4. `Gotchas` lists traps that can waste or invalidate a verification run.

Keep implementation details out of the map. Name only user paths, stable handles, required
state, commands, and observable proof.

## Features

- [Run a command in a pane](./run-command.md) covers typing, the ConPTY round trip, rendering of
  output and errors, and the working directory a pane starts in.
- [Tabs](./tabs.md) covers creating, switching, renaming and closing tabs, and their persistence
  across a restart.
- [Split panes](./split-panes.md) covers the four split directions, per-pane focus, closing a
  pane, and the split tree that is persisted.
- [Selection and clipboard](./selection-clipboard.md) covers drag selection, copy, paste,
  bracketed paste, and pasting an image as a file.
- [Command history](./command-history.md) covers inline suggestions and the reverse-search
  overlay, both fed by the shared history file.
- [Settings](./settings.md) covers the settings page: its two tabs, the search that spans them,
  live theme and font changes, the starting-directory choice, and what persists.

## Not yet mapped

Real user-facing surface with no feature file yet. Add one before claiming any of it is verified.

- **Scrollback**: `shift+pageup` / `shift+pagedown` and the mouse wheel.
- **Read-Only Mode** on the pane context menu.
- **Mouse reporting** to a full-screen program that grabs the pointer, and the `shift` escape
  hatch that keeps selection working inside one.
- **Overlays**: the FPS counter and the `ctrl+shift+p` profiler.
- **Tab drag-to-reorder**.
