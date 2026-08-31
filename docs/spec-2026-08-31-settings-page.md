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
  `No settings match “…”.`
- Every edit saves and applies immediately. There is no apply or cancel step.
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
| `Settings/SettingsControls.cs` | Row and section factories, the pill picker and the numeric stepper |
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
uses a hand-rolled pill picker and numeric stepper instead — both are a `Border` and a
`TextBlock`, and both take their colours from `OverlayTheme` directly.
