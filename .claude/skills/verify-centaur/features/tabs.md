# Tabs

Each tab holds one pane tree and appears as a labelled strip along the top of the window. The
user creates tabs, switches between them by keyboard or click, renames one by double-clicking
its label, and closes them. The strip and the active tab survive a restart.

## Sub-features

- `tabs-create` opens a new tab with a fresh shell, titled `Terminal N`.
- `tabs-switch` activates a tab by keyboard, by index, or by clicking its label.
- `tabs-rename` edits a tab title in place on double-click.
- `tabs-close` closes the focused pane, and with it the tab when it was the last pane.
- `tabs-persist` restores the tab list and active index after a restart.

## How to get to it (user POV)

- Press `Ctrl+T` to create a tab.
- Click the `+` at the right of the tab strip to create a tab.
- Press `Ctrl+Tab` to move to the next tab, or `Ctrl+1`..`Ctrl+9` to jump to one by position.
- Click a tab's label to activate it.
- Double-click a tab's label to rename it, then press Enter.
- Press `Ctrl+W` to close the focused pane.

## Driving it with control-centaur.ps1

Preconditions:

- `& $c doctor` reports `healthy: True`.
- The window has exactly one tab, `Terminal 1`, which is the state after `launch`.

- **Create a tab.** Press `Ctrl+T`. Run `& $c key -Combo ctrl+t`, wait ~2s, then
  `& $c shot -Name 10-two-tabs`. Two labels appear, `Terminal 1` at client `~46,17` and
  `Terminal 2` at `~146,17`, with `Terminal 2` highlighted and its pane showing a fresh
  PowerShell banner.
- **Prove the new tab has its own shell.** Run
  `& $c type -Text "'tab-two' | Set-Content tab2.txt"` and `& $c key -Combo enter`, wait ~2s.
  `<run>/workdir/tab2.txt` contains `tab-two`.
- **Switch by keyboard.** Press `Ctrl+1`. Run `& $c key -Combo ctrl+1` then
  `& $c shot -Name 11-tab-one-active`. `Terminal 1` is highlighted and its pane shows the
  first tab's scrollback, not the second's.
- **Switch by click.** Click the second label. Run `& $c click -X 146 -Y 17` then
  `& $c shot -Name 12-tab-two-clicked`. `Terminal 2` is active again.
- **Cycle.** Press `Ctrl+Tab`. Run `& $c key -Combo ctrl+tab`. The active tab advances by one
  and wraps at the end of the strip.
- **Rename.** Double-click the active label and type. Run `& $c click -X 146 -Y 17 -Count 2`,
  `& $c type -Text "renamed"`, `& $c key -Combo enter`, then
  `& $c shot -Name 13-tab-renamed`. The label reads `renamed`.
- **Prove persistence.** Run `& $c stop`, then `& $c state`. The `session.json` dump lists both
  tabs in order with `"Title": "renamed"` on the second and `"ActiveTabIndex": 1`. The dump must
  come after `stop`: the app flushes its final session in the `Closed` handler.
- **Proof set.** `10-two-tabs.png` through `13-tab-renamed.png`, the contents of
  `<run>/workdir/tab2.txt`, and the post-`stop` `state` dump.

## Gotchas

- **A single tab shows no tab strip.** After `launch` the window has one tab and no labels at
  all - the strip appears with the second tab. An absent `Terminal 1` label at baseline is
  correct, not a failure to render.
- A new tab's shell takes a couple of seconds to print its banner. Screenshotting sooner
  captures an empty pane and reads as a broken tab.
- A new tab does not inherit keyboard focus from the old one in every path. If typing lands
  nowhere, `focus -X 400 -Y 300` the new pane before continuing.
- `Ctrl+W` closes the focused **pane**, not the tab. In a split tab it removes one pane and
  leaves the tab open; only the last pane's close takes the tab with it.
- Tab labels shift left as tabs are added and removed, so a coordinate captured for
  `Terminal 2` is wrong once a tab before it closes. Re-screenshot before clicking, or use
  `Ctrl+1`..`Ctrl+9`.
- Double-click puts the label into an edit box that swallows shortcuts. Commit with Enter before
  sending anything else.
- `session.json` is written on a ~400ms debounce after a layout change and flushed on close.
  Reading it mid-run can show the state before your last action.
