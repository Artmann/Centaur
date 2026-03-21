- Don't use CONSTANT_CASE. Use camelCase or PascalCase for variables and functions.

## Formatting and Linting

- **CSharpier** for formatting: `dotnet csharpier format .`
- **Roslyn Analyzers** for linting: runs automatically during `dotnet build`
- Run `dotnet csharpier check .` to verify formatting without writing changes
- CSharpier is a local dotnet tool — run `dotnet tool restore` after cloning

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
- Extensions and providers are registered in `App.ConfigureServices()` using `Microsoft.Extensions.DependencyInjection`
- `ExtensionHost` is a singleton resolved from the DI container
- `TerminalControl` resolves the host via `App.Services.GetRequiredService<ExtensionHost>()`
- `ActivateAsync` is called on attach, `DisposeAsync` on detach
- Provider interfaces that need framework types (e.g., SkiaSharp) live in the project that owns those types (e.g., `IRenderOverlay` in Centaur.Rendering), not in Core

## Error Handling

- **Never swallow exceptions silently** — always show a toast notification to the user
- Use `INotificationService.Show(title, message, severity)` to display errors as toast notifications
- `INotificationService` is in `Centaur.Core.Hosting` (framework-agnostic), implemented by `NotificationServiceExtension` in Centaur.App using Avalonia's `WindowNotificationManager`
- Resolve via DI: `App.Services.GetRequiredService<INotificationService>()`
- Error messages must be **actionable** — tell the user what went wrong and what they can do about it
- When planning features, always consider what errors can occur and include the exact error messages in the plan
