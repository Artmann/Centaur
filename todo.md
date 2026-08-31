# Centaur → Ghostty Feature-Parity TODO

A prioritized roadmap to close the gap with Ghostty (github.com/ghostty-org/ghostty).

**Framing:** This is a _feature_ roadmap, not a port. Ghostty is macOS/Linux only;
Centaur is Windows/Avalonia. We target _behavioral_ parity. Platform-native items
(SwiftUI/GTK chrome, Metal, systemd, X11 primary selection, AppleScript, IPC) are
out of scope — see bottom.

---

## Phase 1 — Emulation correctness (make real apps render right)

> Highest leverage. Without these, vim/htop/git/ls look wrong. Core parser + renderer.

- [x] **Render SGR text styles** — store bold/faint/italic/underline/strikethrough/inverse/invisible/blink in the cell (`ScreenBuffer.cs`); draw in `TerminalRenderer.cs` (bold+italic typeface selection, inverse fg/bg swap). _Already parsed, just dropped._
- [x] **Underline variants** — single/double/curly/dotted/dashed + underline color (`SGR 4:x`, `58;…`).
- [ ] **Mouse reporting** — modes 1000/1002/1003/1006 (+1004 focus, 1007 alt-scroll); translate Avalonia pointer events in `TerminalControl.cs` to SGR reports; Shift overrides to local selection.
- [x] **Bracketed paste** — wrap paste in `CSI 200~ … 201~` when mode 2004 is on.
- [ ] **OSC dispatcher** (stop discarding OSC in `VtParser.cs`):
    - [ ] OSC 0/1/2 → window/tab title (`TitleChangedEvent` → tab).
    - [ ] OSC 7 → working-directory tracking (feeds Phase 2 + split inheritance).
    - [ ] OSC 52 → clipboard read/write (with paste-safety guard).
    - [ ] OSC 4/10/11 → color palette set/query.
- [ ] **Device/mode queries** — DA1/DA2, DECRQM responses (TUIs gate features on these).
- [ ] Parser unit tests in `tests/Centaur.Tests` for each new sequence (TDD).
- [ ] Manual matrix: vim, htop, lazygit, git log, ls --color, btop.

### Known emulation defects

> Found while tracking down the green blocks left behind by diff rendering. That one was
> `CursorRegisters` dropping the SGR style flags across DECSC/DECRC, which is fixed; these
> are the other real defects the same search turned up, none of them yet covered by tests.

- [ ] **Torn frames** — `TerminalSurface.Snapshot` hands back the shared, reused
  `snapshotBuffer` under the lock on the UI thread, but `TerminalDrawOperation.Render` walks
  it later on the render thread with no lock. The next frame can overwrite the array
  mid-draw, mixing rows from two states.
- [ ] **Region-unaware line editing** — `ScreenOps.InsertLines`/`DeleteLines` shift rows
  across the whole screen, ignoring `Region.Top`/`Bottom`, and `SU`/`SD` go through
  `ScrollRegion.ScrollUp`/`ScrollDown`, which are hardcoded to the full screen. IL should be
  `ScrollDownIn(n, cursorY, Region.Bottom)` and DL `ScrollUpIn(n, cursorY, Region.Bottom)`,
  both ignored when the cursor is outside the region. Note `ScrollUpIn` feeds scrollback
  whenever the region is the whole screen, so DL needs an opt-out that `LineFeed` and `SU`
  do not take.
- [ ] **Stale selection** — the selection is cleared on scroll and on copy, never when new
  output rewrites the rows it covers, so it stays pinned to fixed screen rows and keeps
  painting them swapped.
- [ ] **No wcwidth** — `ScreenOps.Write` advances one cell per `char`, so a wide CJK glyph
  takes one column instead of two and an astral emoji becomes two cells each holding half a
  surrogate pair. Column accounting then drifts against what the program believes.
- [ ] **Dropped resize** — `ShellChannel.Start` is `async void` and sets `started` before it
  assigns `session`, so a `Resize` arriving during the await hits a null session and is
  silently dropped, leaving the grid and the PTY disagreeing on width.

## Phase 2 — Shell integration (Ghostty's signature productivity layer)

> Builds on OSC 7 + a new OSC 133 handler.

- [ ] **OSC 133 semantic prompts** — mark prompt/command/output regions (row/cell metadata).
- [ ] **Shell-integration injection** for PowerShell + pwsh (optionally bash/zsh under WSL/Git-Bash) via profile hooks.
- [ ] **Jump-to-prompt** navigation (keybinding to scroll between marks).
- [ ] **CWD-aware new tab/split** — open panes in tracked dir (`PaneTree.cs`/`TabManager.cs`).
- [ ] **Command-finished notification / exit status** via `INotificationService`.

## Phase 3 — UX & configuration (daily-driver friendly)

> App-layer work on existing provider/overlay infrastructure.

- [ ] **Real config file** — expand `Settings.cs` (theme, font family/size, scrollback limit, keybindings, padding, cursor style, opacity). Decide JSON vs Ghostty-style `key = value`.
- [ ] **Hot-reload** — FileSystemWatcher → `ConfigChangedEvent` (theme/font already flow through providers).
- [ ] **Configurable keybindings** — replace hardcoded keys in `TerminalControl.cs` with a keymap + action registry.
- [ ] **Runtime theme switching** — surface all providers (not hardcoded Macchiato) + follow-OS light/dark.
- [ ] **Command palette** — fuzzy action launcher (reuse `FuzzyMatcher`); actions via a provider.
- [ ] **Scrollback text search** — in-buffer find with match highlighting (distinct from history reverse-search).
- [ ] **Finish stubs** — wire up `ReverseSearchExtension` + settings overlay (font/theme controls).
- [ ] **URL detection + click** — regex-detect URLs, render clickable, open in browser; honor OSC 8 links.
- [ ] **Window title** — with only one tab there is no window title; show a centered title defaulting to "Centaur", editable like a tab title.
- [ ] _(optional)_ Quick-terminal dropdown — global hotkey + slide-in window.

## Phase 4 — Advanced protocols & typography (depth / signature features)

> Largest, riskiest items. Each independently shippable.

- [ ] **Ligatures & font features** — HarfBuzz feature flags + `font-feature`/`font-variation` config in `TerminalRenderer.cs`.
- [ ] **Kitty keyboard protocol** — disambiguate/report-events flag stack; richer key encoding.
- [ ] **Kitty graphics protocol** — image transmit/placement/storage + Skia overlay rendering; scrollback image limits.
- [ ] **Custom shaders** — Skia/SkSL post-process overlay (use the `IRenderOverlay` hook).

---

## Out of scope (platform-native, no Windows analogue)

SwiftUI/GTK chrome, AppleScript/Shortcuts, systemd/cgroup isolation, X11 primary
selection, GTK custom CSS, single-instance IPC / XDG-terminal-exec, libghostty
embeddable library, Sixel (Ghostty omits it too).

## Sizing & order

| Phase | Theme                 | Size      | Risk    |
| ----- | --------------------- | --------- | ------- |
| 1     | Emulation correctness | Med-large | Low     |
| 2     | Shell integration     | Med       | Low-med |
| 3     | UX & config           | Med       | Low     |
| 4     | Advanced protocols    | Large     | High    |

**Do Phase 1 first** — it unblocks correct rendering and several items (OSC 7, SGR
styles) are prerequisites for Phases 2–3. Run `dotnet csharpier check .` + `dotnet
build` before each merge.
