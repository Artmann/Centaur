# Reverse Search (Ctrl+R) — Design Spec

## Context

Centaur already tracks command history (up to 1000 commands, persisted to JSON) and offers prefix-based ghost-text suggestions. However, there is no way to interactively search through history. Power users expect fzf-style Ctrl+R reverse search — a full-screen overlay with fuzzy filtering and keyboard navigation.

## Behavior

- **Ctrl+R** opens a full-screen overlay covering the terminal area.
- The overlay has two regions:
  - **Results list** — fills the main area, most recent commands at the bottom (closest to the input).
  - **Search input bar** — at the bottom, with a prompt indicator and a match counter ("8 / 142").
- Typing in the input fuzzy-filters the command list. Matched characters are shown in **bold + theme accent color**.
- **Up/Down arrows** navigate the results. The selected item has a left accent border and highlighted background.
- **Enter** executes the selected command immediately (writes it to the PTY followed by a newline).
- **Escape** closes the overlay and returns focus to the terminal.
- All colors come from the active `TerminalTheme` (background, foreground, accent = ANSI blue from palette).

## Architecture

### New Components

**`Centaur.Core/Terminal/FuzzyMatcher.cs`** — Pure static class.
- `Match(string pattern, string candidate)` returns `FuzzyMatchResult?` (score + matched indices).
- Scoring: consecutive matches score higher, matches at word boundaries score higher, earlier matches score higher.
- Case-insensitive by default.

**`Centaur.Core/Terminal/ReverseSearchState.cs`** — Mutable state holder.
- Properties: `Query`, `FilteredResults` (list of command + match result), `SelectedIndex`.
- Methods: `UpdateQuery(commands, query)` — re-filters and resets selection. `MoveSelection(delta)` — navigates with wraparound.

**`Centaur.App/ReverseSearchOverlay.cs`** — Avalonia `UserControl`.
- Contains a `ListBox` (or `ItemsRepeater`) for results and a `TextBox` for the search input.
- Binds to `ReverseSearchState` for display.
- Styled programmatically using theme colors from `TerminalTheme`.
- Uses the same monospace font as the terminal (JetBrains Mono).

**`Centaur.App/ReverseSearchExtension.cs`** — `IExtension`.
- On activation: subscribes to `ReverseSearchRequestedEvent`.
- Creates and manages the `ReverseSearchOverlay` and `ReverseSearchState`.
- On Ctrl+R: shows overlay, loads commands from `CommandHistory.GetAll()`, focuses input.
- On Enter: gets selected command, writes to PTY, hides overlay.
- On Esc: hides overlay, returns focus to terminal.

### New Events

**`ReverseSearchRequestedEvent`** — Published by `TerminalControl.OnKeyDown` when Ctrl+R is pressed.

### Integration Points

- `TerminalControl.OnKeyDown`: Intercept Ctrl+R, publish `ReverseSearchRequestedEvent`, mark event as handled.
- `CommandHistory.GetAll()`: Already exists, returns all stored commands.
- PTY write: Use existing mechanism to send selected command text + Enter to the shell.
- DI registration in `App.ConfigureServices()`: Register `ReverseSearchExtension` as `IExtension`, `ReverseSearchState` as singleton, `ReverseSearchOverlay` as singleton.

### Data Flow

1. User presses Ctrl+R → `TerminalControl` publishes `ReverseSearchRequestedEvent`
2. `ReverseSearchExtension` receives event → shows overlay with all commands
3. User types → `FuzzyMatcher.Match` filters commands → `ReverseSearchState` updated → overlay re-renders
4. User presses Enter → selected command sent to PTY → overlay hidden
5. User presses Esc → overlay hidden, terminal focused

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Empty history | Show "No command history yet" centered in the overlay |
| No fuzzy matches | Show "No matches" message, keep input active |
| PTY write failure | Toast notification via `INotificationService`: "Could not execute command: {message}" |
| History load failure | Already handled by `SuggestionExtension`; overlay gets empty list |

## Testing

- **`FuzzyMatcherTests`**: Exact matches, partial matches, non-contiguous matches ("gcp" → "git cherry-pick"), no matches, empty inputs, scoring order, case insensitivity.
- **`ReverseSearchStateTests`**: Filtering, selection navigation (up/down/wraparound), clearing search, empty state.

## Files to Create

- `src/Centaur.Core/Terminal/FuzzyMatcher.cs`
- `src/Centaur.Core/Terminal/ReverseSearchState.cs`
- `src/Centaur.App/ReverseSearchOverlay.cs`
- `src/Centaur.App/ReverseSearchExtension.cs`
- `tests/Centaur.Tests/FuzzyMatcherTests.cs`
- `tests/Centaur.Tests/ReverseSearchStateTests.cs`

## Files to Modify

- `src/Centaur.Core/Hosting/TerminalHooks.cs` — Add `ReverseSearchRequestedEvent` record
- `src/Centaur.App/TerminalControl.cs` — Add Ctrl+R handler in `OnKeyDown`
- `src/Centaur.App/App.axaml.cs` — Register new services in DI
