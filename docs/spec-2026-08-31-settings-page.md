# Spec: Settings Page

## Context

Settings were a 500px card floating over a dimmed backdrop, holding one section — the
starting-directory choice — because that was all `Settings` stored. Everything else a user would
reasonably change was a hardcoded constant: the theme pinned to Macchiato by a literal id, the
font size a default parameter nobody passed, the shell the string `"powershell.exe"`, scrollback
`10000`, and the bell dropped on the floor.

The overlay was also built **per terminal pane** — each `TerminalControl` constructed its own
`TerminalOverlays` and parented the card to that pane's panel. Split the window and there were two
settings overlays, each covering half the screen. That is the structural reason it read as a modal
rather than a page.

This replaces it with a full-window settings page inside `MainWindow`, driven by a setting
registry so that adding an option later is one descriptor plus one property, and search works over
everything automatically.

## Behavior

- **Ctrl+,** opens a page that covers the terminal area. The tab strip and the caption buttons
  stay live above it. **Ctrl+,** again or **Escape** closes it.
- A sidebar picks between **General** and **Appearance**. A search box spans both.
- Typing in search filters rows across *both* tabs, and each result group is headed
  `TAB · SECTION` so the tab a match came from is visible. The sidebar highlight clears while a
  search is active, because the results are no longer one tab's. A query with no matches shows
  `No settings match “…”.` followed by one chip per section, so a dead query still offers a
  way on.
- Every edit saves and applies immediately. There is no apply or cancel step.
- Every affordance is keyboard-operable. `Tab` walks the sidebar, the rows and the steppers;
  arrows move within a segmented control, a radio group or a stepper; `Space` and `Enter`
  activate; and whatever the keyboard is standing on wears a visible ring. A segmented control is
  one tab stop whose arrows move the choice, which is what the platform control it stands in for
  does.
- Closing the page returns focus to the pane that had it.
- While the page is open it swallows `Ctrl+T`, `Ctrl+W` and `Ctrl+1..9`, so a stray shortcut
  cannot close a pane behind it.

## Architecture

### The registry is the load-bearing idea

A settings page that "grows later" fails if each new option means editing a layout method. So the
page renders from data:

```csharp
sealed record SettingDescriptor(
    string Id,              // "appearance.fontSize" — stable, used by search and tests
    SettingsTab Tab,        // General | Appearance
    string Section,         // "Shell", "Terminal", "Theme", "Text", "Cursor", "Window"
    string Title,
    string Description,
    string[] Keywords,      // extra search terms: "typeface", "dpi", "zoom"
    bool FullWidth,
    Func<SettingsContext, Control> CreateEditor
);
```

`SettingsRegistry.All` is the ordered list. The page groups by `Tab` → `Section` and renders a row
per descriptor. **Adding a setting is one property on `Settings` plus one descriptor. Adding a tab
is one enum value.** Search filters the descriptor list and needs no per-setting work.

Search reuses `Centaur.Core.Terminal.FuzzyMatcher` over `Title`, `Keywords` and `Section`.
`FuzzyMatcher` is subsequence-based, so it is deliberately **not** applied to the prose
`Description`: almost any short query is a subsequence of a sentence, and matching descriptions
made every row match everything.

### New components

| File | Role |
| --- | --- |
| `Settings/SettingDescriptor.cs` | The record above, `SettingsTab`, and `SettingsContext` |
| `Settings/SettingsRegistry.cs` | The descriptor list — the file you edit to add a setting |
| `Settings/SettingsPage.cs` | Chrome: header, search box, sidebar host, scrolling content, `Show`/`Hide`, `ApplyTheme` |
| `Settings/SettingsNav.cs` | Sidebar tab list, selection visual, `TabSelected` |
| `Settings/SettingsSearch.cs` | `FuzzyMatcher` over descriptors; filtered and ranked ids |
| `Settings/SettingsControls.cs` | Section, card and row factories, plus the segmented control and switch |
| `Settings/SettingsButton.cs` | The one focusable, hoverable surface every affordance on the page is built from |
| `Settings/NumberEditor.cs` | The stepper: a typed, clamped, unit-carrying number with a step at each end |
| `Settings/StartDirectoryEditor.cs` | The old `StartDirectorySection`, reshaped as one descriptor's bespoke editor |

`SettingsOverlay.cs` and `StartDirectorySection.cs` are deleted, and `TerminalOverlays` loses its
settings half, keeping only reverse search.

### Where the page lives

`MainWindow.axaml` gains a third sibling in the root `Panel`, **after** `contentPanel`, so XAML
fixes the z-order and `TabManager` adding new tab roots can never land on top of it.
`UpdateContentMargin()` sets `settingsHost.Margin` alongside `contentPanel.Margin`, so the page
covers the terminal area and nothing else. `Ctrl+,` moved from the per-pane shortcut table to
`MainWindow.OnPreviewKeyDown`, because the page is window-level.

Focus moves to the search box on open through
`Dispatcher.UIThread.Post(..., DispatcherPriority.Input)`. A synchronous `Focus()` right after
`IsVisible = true` does not stick.

### Settings model

`Settings` stays **flat** and keeps the existing JSON key names, so an existing
`settings.json` keeps working with no migration code. Defaults match the constants they replace,
so behaviour is unchanged until a value is touched.

| Tab | Property | Type | Default |
| --- | --- | --- | --- |
| General | `StartDirectory` / `SpecificFolder` / `LastFolder` | existing | unchanged |
| General | `ShellCommand` | `string` | `"powershell.exe"` |
| General | `ScrollbackLines` | `int` | `10000` |
| General | `Bell` | `BellMode` | `Off` |
| Appearance | `ThemeId` | `string` | `"catppuccin-macchiato"` |
| Appearance | `FontSize` | `double` | `14` |
| Appearance | `LineHeight` | `double` | `1.2` |
| Appearance | `CursorStyle` | `CursorStyle` | `Block` |
| Appearance | `CursorBlink` | `bool` | `false` |
| Appearance | `WindowOpacity` | `double` | `1.0` |
| Appearance | `ContentPadding` | `int` | `8` |

`Load()` clamps every numeric on the way in (`FontSize` 8–48, `LineHeight` 1.0–2.0,
`ScrollbackLines` 0–200000, `WindowOpacity` 0.5–1.0, `ContentPadding` 0–64) so a hand-edited or
corrupt file cannot produce an unusable window.

`Settings` raises a plain `event Action<string>? Changed` carrying the descriptor id — the codebase
uses plain C# events throughout and has no `INotifyPropertyChanged` anywhere. `SettingsExtension`
republishes it on the event bus as `SettingsChangedEvent`, and as `ThemeChangedEvent` when the
theme id changed, so extensions can follow configuration without holding a `Settings` reference.

### Applying changes to a live terminal

`TerminalRenderer`, `TerminalSurface` and `ScreenBuffer` all capture theme and font metrics in
their constructors, and `cellWidth` / `cellHeight` are get-only. So `TerminalControl` gains
`ApplyAppearance()`, called on `Settings.Changed`:

1. Dispose the old `TerminalRenderer` and construct a new one from the new theme, font size and
   line height.
2. `TerminalSurface.SetTheme(newTheme)` updates the buffer's and parser's default pen and
   **remaps** cells whose colour equals the old theme's default foreground or background, across
   both screens and the scrollback. Without that pass, text already on screen keeps the old
   palette, because `ScreenBuffer` stores resolved `uint` colours, not palette indices.
3. `InvalidateArrange()` recomputes columns and rows from the new cell metrics and resizes the PTY.
4. `frames.MarkDirty()`.

A theme change also repaints the window chrome. The chrome brushes are published once into
`App.Resources` and **mutated in place** afterwards: Avalonia's brushes are observable, so every
control already holding one repaints without reloading a dictionary.

## Error handling

- A settings file that cannot be read or written raises a toast naming the path:
  *"Could not read or write settings — `<path>`: `<reason>`. Delete or fix that file if the problem
  persists; until then your settings will not be saved."*
- An unknown `ThemeId` falls back to the default theme rather than leaving panes unpainted.
- A shell that fails to spawn reports what to do about it:
  *"Could not start 'pwsh.exe': the system cannot find the file specified. Change it in
  Settings → General → Shell."*
- The shell command applies to panes opened from now on, not to ones already running. The row's
  description says so, so a user does not read the unchanged pane as a failure.
- If no theme provider is registered, the theme row renders *"No themes are registered."* instead
  of an empty control.

## Verification

Unit tests cover the model (defaults, clamping at both bounds, an old three-key file still
loading, an unknown theme id), the registry (ids unique, every descriptor has a title and a known
tab, search by title, keyword and section, a nonsense query returning nothing) and the page under
`[AvaloniaFact]` (both tabs render, nav switches content, search filters across both tabs, Escape
raises `CloseRequested`).

GUI behaviour is proved by driving the real window with the `verify-centaur` skill — see
[`features/settings.md`](../.claude/skills/verify-centaur/features/settings.md) for the recipe and
the traps.

`AppServiceGraphTests` resolves the application's service graph on a background thread with a
deadline. A cycle in that graph does not throw: the container cannot see through a factory lambda,
so a service whose factory resolves back into one already being constructed deadlocks on the
container's own cache, and the app then starts, stays alive, and never shows a window with nothing
on stderr to say why. This spec's first build hit exactly that.

## Notes

**Window opacity composites under ANGLE.** `Win32RenderingMode.AngleEgl` was the open risk — the
plan was to ship the control disabled if alpha did not reach the desktop. Driven with a real
screen capture at `WindowOpacity = 0.7`, the windows behind Centaur read clearly through the pane
background while the terminal's own glyphs stay opaque. The control ships enabled.

**Fluent styles controls through theme resources, not properties.** `OverlayTheme.StyleTextBox`
has to stuff 21 resource keys onto each box for this reason, the last three being the watermark
brush. `ComboBox`, `Slider` and `ToggleSwitch` would each need equivalent treatment, so the page
uses a hand-rolled segmented control, numeric stepper and switch instead — each is a `Border`
and a `TextBlock`, and each takes its colours from `OverlayTheme` directly.

**A page has to stop looking like the terminal.** The first cut rendered every label in the
terminal's monospace, which made a settings page read as terminal output. The labels moved to
`OverlayControls.UiFont`, and only the two boxes that hold code — the shell command and the folder
path — stayed monospace. The rows then grouped into one rounded card per section, ruled between
rows and headed by a small dim sentence-case label, which is the shape desktop settings pages have
settled on. `OverlayTheme.Card` and `Hairline` are blended from the background towards the
foreground rather than picked from the palette, so the same formula lightens the card on a dark
theme and darkens it on a light one.

**One affordance, not twelve call sites.** Every control on the first cut was a bare `Border` with
a `PointerPressed` handler. A `Border` is not focusable in Avalonia, so none of them had a tab
stop, an activation key or a focus visual — twelve settings of which two could be reached without
a mouse. `SettingsButton` puts focus, hover, press and key handling in one place, which is what
keeps the next control from being built the same way. `Border.Render` is sealed, so the focus ring
cannot be drawn over the border: it *is* the border, permanently one pixel thick and transparent
at rest, with only its colour changing, so focusing something never shifts the layout by a pixel.

**A theme change rebuilds the page under the keyboard.** Picking a theme replaces every control on
the page, including the one the user is standing on — driving it revealed the next arrow key
landing on the window's *minimise button*. The page now reads the focused control before the
rebuild and puts focus back on the same setting afterwards, re-ringing it only if the keyboard,
rather than the pointer, was what put it there.

**Dim text failed AA on three of the four palettes.** The secondary text took
`theme.Palette[8]`, which is a *text* role on Latte (Subtext0) but a *surface* role on Frappé,
Macchiato and Mocha (Surface2) — 2.9:1 against the page on Mocha. No single blend factor fixes
all four (4.5:1 needs 0.86 on Latte and 0.62 on Mocha), so `OverlayTheme` scans the blend towards
the foreground and takes the first amount that passes. Contrast is monotonic in that amount, so
the first passing one is the minimum passing one: the text is exactly as quiet as it can be while
still being readable.
