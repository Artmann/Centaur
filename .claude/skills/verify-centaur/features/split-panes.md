# Split panes

A tab can be divided into several panes, each running its own shell. The user splits the focused
pane right, left, down or up from its context menu; the tab becomes a tree of panes with a
draggable divider, each pane taking focus on click. The tree is persisted per tab.

## Sub-features

- `split-right`, `split-left`, `split-down`, `split-up` divide the focused pane in the named
  direction, each new pane starting its own shell.
- `split-focus` moves keyboard focus to whichever pane is clicked.
- `split-close` removes the focused pane and gives its space back to its sibling.
- `split-persist` records the pane tree, its orientation and its ratio in the session.

## How to get to it (user POV)

- Right-click a pane and choose `Split Right`, `Split Left`, `Split Down` or `Split Up`.
- Right-click a pane and choose `Close Pane`, or press `Ctrl+W`.
- Click a pane to give it the keyboard.

There is no keyboard shortcut for splitting. The context menu is the only entry point.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True`.
- The active tab holds a single pane.
- Nothing is selected in the pane - a selection adds a `Copy` row and shifts the menu.

- **Open the pane menu.** Right-click the pane. Run `& $c click -X 400 -Y 300 -Button right`,
  wait ~1s, then `& $c windows`. A second top-level window appears, `164x243`, its origin at the
  click point - that listing, not a screenshot, is the proof the menu opened.
- **Choose Split Right.** Walk down to the fifth row and commit. Run
  `& $c key -Combo down -Count 4` then `& $c key -Combo enter`, and wait ~3s. Rows with no
  selection are Paste, Paste Image as File, Read-Only Mode, `Split Right`, Split Left, Split
  Down, Split Up, Close Pane.
- **Confirm the split.** Run `& $c shot -Name 20-split-right`. Two panes sit side by side with a
  divider near client x `600`, each showing its own PowerShell banner and prompt.
- **Focus the new pane.** Click it. Run `& $c focus -X 900 -Y 400`.
- **Prove it is a separate shell.** Run
  `& $c type -Text "'split-pane-verified' | Set-Content split.txt"` and
  `& $c key -Combo enter`, wait ~2s. `<run>/workdir/split.txt` contains `split-pane-verified`,
  written by a process the first pane never ran.
- **Confirm the persisted tree.** Run `& $c state`. The active tab's `Root` has
  `"IsSplit": true`, `"Orientation": "Horizontal"`, `"Ratio": 0.5`, and both `First` and
  `Second` are leaves whose `WorkingDirectory` is `<run>\workdir`.
- **Close a pane.** Press `Ctrl+W` in the focused pane. Run `& $c key -Combo ctrl+w`, wait ~1s,
  `& $c shot -Name 21-pane-closed`. One pane fills the tab again and the tab is still open.
- **Proof set.** The `windows` listing with the menu open, `20-split-right.png`,
  `21-pane-closed.png`, `<run>/workdir/split.txt`, and the `state` dump showing the split tree.

## Gotchas

- The context menu is its own top-level window. `shot` renders only the main window, so the menu
  is **absent** from an ordinary screenshot - it has not failed to open. Prove it with
  `& $c windows`; use `shot -Screen` only when you need to read the menu's contents, and
  remember that photographs the user's whole desktop region.
- The row count depends on state. A live selection adds `Copy` at the top and every split moves
  down one. Check for a selection before counting rows.
- Send arrow keys one at a time. A burst arrives while the menu is still animating open and
  leaves the highlight one row down instead of four - which silently commits the wrong item.
  `-Count` already spaces them 80ms apart; do not replace it with a single batched press.
- Committing the wrong row is not harmless: `Paste` pastes the user's real clipboard into the
  shell and Enter runs it. Screenshot the armed menu with `-Screen` before pressing Enter if
  the row count is at all uncertain.
- A right-click that a full-screen program has grabbed the mouse for never reaches the menu; the
  pane forwards it instead. `Shift`+right-click is the user's escape hatch.
- A new pane needs ~3s before its shell prints a prompt, and does not take keyboard focus by
  itself. Click it.
