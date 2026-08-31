---
name: verify-centaur
description: Drive the Centaur terminal emulator (an Avalonia desktop GUI on Windows) with real keystrokes, real mouse input and real screenshots, and capture evidence that a feature works. Use when verifying or demonstrating Centaur behaviour end to end, reproducing a GUI bug, or proving a change before handing it over.
---

# Verify Centaur

Centaur is a Windows desktop terminal emulator: Avalonia 11 over ANGLE/Skia, ConPTY underneath,
no scripting port and no test hook. The only honest way to verify it is the way a user drives
it - keystrokes into the real window, clicks at real coordinates, pixels off the real GPU
surface - so this skill talks to it through Win32 (`SendInput`, `PrintWindow`) via one helper
script.

Every run is isolated. `CENTAUR_CONFIG_DIR` points the instance at its own config directory, so
it starts from an empty session and never reads or writes the tabs, history and settings of the
Centaur the user already has open. **You can run this while the user is using Centaur.** The
harness only ever signals the PID it started.

The feature map in [`features/`](./features/) is the maintained list of what to drive and how.
Read [`features/README.md`](./features/README.md) before driving anything.

## Launch

Everything goes through one script. Set the location first, then use its absolute path - the
helper resolves the repo from its own location, but a relative invocation breaks the moment
your shell's working directory is not the repo root.

```powershell
Set-Location C:\Users\Artga\Code\Centaur
$c = "C:\Users\Artga\Code\Centaur\.claude\skills\verify-centaur\control-centaur.ps1"

& $c build     # dotnet build src/Centaur.App -c Debug
& $c launch    # start an isolated instance, 1200x800 at 80,80, and click into its first pane
```

`launch` prints the run identity - `runId`, `processId`, `hwnd`, `workDir`, `artifacts`. Every
later command finds the newest run on its own; pass `-Run <runId>` to target an older one.

Readiness is not a log line (the app writes none). `launch` returns only after all of:

1. the process has a `MainWindowHandle`, within 30s;
2. the window has been moved to a fixed 1200x800 at 80,80, so map coordinates mean something;
3. `<run>/config/session.json` exists, within a further 10s.

Step 3 is the isolation proof. The app debounces its session save ~400ms after a layout change,
so the resize in step 2 is what forces the first write. If that file never appears the instance
is writing `%APPDATA%\Centaur` instead - the user's own state - and `launch` closes it and
fails rather than driving it. The usual cause is an app build older than
`src/Centaur.Core/Terminal/ConfigPaths.cs`; run `build` and launch again.

Teardown is `& $c cleanup` (see [Cleanup](#cleanup)).

## Doctor

```powershell
& $c doctor
```

Read-only. Run it after `launch`, and again any time the app stops responding the way the map
says it should. It exits non-zero and lists problems if any of these fail:

- the PID from `run.json` is still running, and is still `Centaur.App` started at the recorded
  time (so a reused PID cannot masquerade as our instance);
- the window handle still exists and is visible;
- `Centaur.App.exe` has not been rebuilt since launch - a rebuild means the window on screen is
  the **old** build and anything you prove with it is a lie;
- the run's scratch `workdir` exists;
- `<run>/config/session.json` exists, i.e. the config override is still in force;
- `%APPDATA%\Centaur\session.json` has not changed since launch *unless* another Centaur is
  running, which legitimately writes it.

Healthy output ends with `Healthy: this instance is worth driving.` and reports
`configInUse: True`, `otherInstances`, and the window geometry. `otherInstances` naming a PID is
normal and fine - that is the user's own Centaur, and this run leaves it alone.

## Drive

Coordinates are **client coordinates**, relative to the window's top-left. Centaur draws its own
title bar into the client area, so client `(0,0)` is the window's top-left pixel and the map's
numbers hold for the standard 1200x800 window.

```powershell
& $c focus                                   # foreground + click the pane at 400,300
& $c focus -X 900 -Y 400                     # focus a specific pane after a split
& $c type -Text "Get-ChildItem"              # literal text, one Unicode keystroke per char
& $c key -Combo enter                        # also: ctrl+t, ctrl+tab, ctrl+r, ctrl+comma, escape, down
& $c key -Combo down -Count 4                # -Count repeats the combo, 80ms apart
& $c click -X 146 -Y 17                      # left click; -Button right, -Count 2 for double
& $c drag -X 613 -Y 436 -ToX 900 -ToY 436    # press, move in steps, release - text selection
& $c windows                                 # top-level windows: proves a popup menu is open
& $c shot -Name 01-prompt                    # PNG into <run>/artifacts
& $c state                                   # dump the run's session/settings/history JSON
& $c workdir                                 # path the panes' shells start in
```

Prefer stable handles over coordinates wherever one exists:

- **Keyboard first.** Tabs, overlays and the pane all have real shortcuts (`ctrl+t`, `ctrl+tab`,
  `ctrl+1`..`ctrl+9`, `ctrl+w`, `ctrl+r`, `ctrl+comma`, `escape`, `tab`, `shift+pageup`). Use
  them instead of clicking chrome.
- **Menu items by position in the list, not by pixel.** Splits exist only on the pane's context
  menu. Right-click the pane, then walk the menu with `down` and commit with `enter`. The order
  is fixed by the providers in `src/Centaur.App/Menus/Providers/`: Copy (only with a selection),
  Paste, Paste Image as File, Read-Only Mode, Split Right, Split Left, Split Down, Split Up,
  Close Pane. Count the rows for the state you are in - Copy is absent when nothing is selected.
- **The shell is the most stable handle of all.** To prove a keystroke reached the PTY, have it
  write a file in `workdir` and read the file.

## Evidence

Artifacts go to `<run>/artifacts/` (`& $c artifacts` prints the path). Name them in order -
`01-`, `02-` - so the sequence reads as a sequence.

`shot` uses `PrintWindow` with `PW_RENDERFULLCONTENT`, which asks the window to redraw itself
into a bitmap. It reaches the ANGLE/Skia surface and works **even when the window is occluded**,
so a screenshot never depends on what happens to be on top. Use `-Screen` only for popup menus,
which are separate windows the main window cannot render (see [Gotchas](#gotchas-that-invalidate-a-run)).

What a proof must contain:

- **The real user path.** Type into the window, click the window. Never call an internal setter,
  never write the config JSON to fake a state, never drive the ConPTY directly. The point is
  that the GUI carries the input.
- **The action and the resulting state, not just the end screen.** Capture before, during and
  after: the command typed at the prompt, then the prompt after it ran.
- **A side effect, not only pixels.** A screenshot of text proves rendering; it does not prove
  the bytes reached the shell. Pair every visual with something checkable off-screen:
  - filesystem - run `'value' | Set-Content marker.txt` in the pane, then read
    `<run>/workdir/marker.txt`;
  - persisted state - `& $c state` dumps the run's own `session.json`, `settings.json` and
    `command-history.json`, which is how you prove tabs, splits and history were recorded;
  - window topology - `& $c windows` lists the popup windows, which is how you prove a context
    menu opened without reading a screenshot.
- **Proof that survives the teardown.** `cleanup` keeps `artifacts/`, `config/` and `workdir/`.

The full session flush only happens on close: `stop` uses `CloseMainWindow`, so the app runs its
`Closed` handler and writes the final session. Check persistence **after** `stop`, not before.

Nothing here is mocked. The instance runs a real `powershell.exe` under a real ConPTY in a
scratch directory the run owns; there is no external system to stub.

## Cleanup

```powershell
& $c cleanup
```

`cleanup` calls `stop` and nothing else. `stop` looks up the PID recorded in `run.json`, checks
it is still the process this run started - name **and** start time, so a recycled PID is not
mistaken for ours - and sends `CloseMainWindow()`, falling back to `Kill()` after 10s.

**Never kill by process name.** `Get-Process Centaur.App | Stop-Process` would take the user's
own terminal with it. If other instances are running, `cleanup` says so and leaves them alone.

Cleanup deletes nothing. `artifacts/`, `config/` and `workdir/` all stay under
`.verify/<runId>/` - the artifacts are the evidence, and the config and workdir *are* evidence
of what the run persisted and wrote. `.verify/` is git-ignored. Old run directories are the
user's to delete.

Run `cleanup` after a failed attempt too, before retrying, so a broken run does not leave a
window on the user's screen.

## Helpers

[`control-centaur.ps1`](./control-centaur.ps1) is the only helper. PowerShell 7+, invoked as
shown throughout this file:

```powershell
& "C:\Users\Artga\Code\Centaur\.claude\skills\verify-centaur\control-centaur.ps1" <verb> [-Args]
```

Verbs: `build`, `launch`, `doctor`, `focus`, `type`, `key`, `click`, `drag`, `shot`, `pixel`,
`windows`, `state`, `workdir`, `artifacts`, `stop`, `cleanup`. `-Run <runId>` targets a specific
run; `-Settle <ms>` changes the pause after an input (default 250).

## Gotchas that invalidate a run

- **Foreground is not focus.** Avalonia gives `TerminalControl` keyboard focus on pointer press
  only. A window that has never been clicked swallows everything typed at it. `launch` clicks
  the first pane for you; after a split or a tab switch, `focus -X -Y` the pane you mean.
- **Every input needs the window in front.** `type`, `key`, `click` and `drag` refuse to fire
  unless the target window holds the foreground, because `SendInput` goes wherever focus is -
  otherwise the recipe gets typed into whatever the user is doing.
- **A context menu is its own window.** `shot` renders the main window, so the menu is simply
  absent from the PNG. Use `& $c windows` to prove it opened, and `shot -Screen` if you need to
  see it. Note that `-Screen` photographs the desktop, so it captures whatever else is on the
  user's screen - prefer `windows` unless you specifically need the menu's contents.
- **`-Screen` and open menus fight over the foreground.** A menu light-dismisses when anything
  activates its owner. `shot -Screen` skips the foreground grab when the foreground already
  belongs to our process, which is what keeps the menu on screen.
- **Rebuild after launch and the window is stale.** The running window keeps the old build until
  you relaunch. `doctor` catches this; heed it.
- **The clipboard is global.** `ctrl+c`/`ctrl+v` in a pane reads and writes the user's real
  clipboard. Save it with `Get-Clipboard -Raw` and restore it with `Set-Clipboard` around any
  recipe that touches copy or paste.
- **The pane's `ctrl+c` is overloaded.** With a selection it copies; without one it sends
  `0x03` to the shell. Clear the selection before using it as an interrupt, and vice versa.

## Keeping the map honest

The feature map is the source of truth for what is verified. When the app grows a feature or
moves a shortcut, update `features/` in the same change. `/maintain-verification-skill` walks
that loop.
