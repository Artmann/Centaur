- Always use bracers for if statements and other control structures, even for single-line blocks.
- Don't use CONSTANT_CASE. Use camelCase or PascalCase for variables and functions.

## Commit Messages

- Don't include Claude as the author in commit messages

## Avalonia Rendering

- Avalonia uses **immediate-mode rendering** - each frame receives a fresh canvas
- The canvas is NOT preserved between frames (starts cleared/transparent)
- Incremental/dirty-region rendering doesn't work with `ICustomDrawOperation.Render()` because unchanged areas won't be redrawn
- Always do full redraws in custom draw operations
- Timer-based update coalescing (batching multiple updates into one render) is still useful for reducing flicker from rapid PTY output

## Extension & Provider Pattern

The codebase uses an **ExtensionHost** (`Centaur.Core.Hosting`) to manage component lifecycle and extensibility.

### Extensions (activate/dispose)
- Implement `IExtension` (`ActivateAsync` + `IAsyncDisposable`)
- During activation, extensions receive an `IExtensionContext` to query providers and subscribe to events
- Subscribe to typed events via `context.Events.Subscribe<TEvent>()` — returns `IDisposable` for cleanup
- Extensions are disposed in reverse registration order
- Example: `FpsOverlayExtension` in Centaur.Rendering

### Providers
- Implement `IProvider` (with `Priority` for ordering) and a domain-specific interface (e.g., `IThemeProvider`, `IRenderOverlay`)
- Providers are passive — they supply data/capabilities, no lifecycle needed
- An extension can also be a provider (auto-registered as both)
- Query via `host.GetProvider<T>()` (highest priority) or `host.GetProviders<T>()` (all)
- Example: `CatppuccinThemeProvider` implements `IThemeProvider`

### Events
- Defined as record types in `TerminalHooks.cs` (e.g., `TerminalReadyEvent`, `ThemeChangedEvent`)
- Published via `events.Publish<T>()` (sync) or `events.PublishAsync<T>()` (async)
- Adding a new hook = adding a new record type, no enums or string keys

### Wiring
- `TerminalControl` creates the `ExtensionHost`, registers providers/extensions, calls `ActivateAsync` on attach and `DisposeAsync` on detach
- Provider interfaces that need framework types (e.g., SkiaSharp) live in the project that owns those types (e.g., `IRenderOverlay` in Centaur.Rendering), not in Core
