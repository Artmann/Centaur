- Don't use CONSTANT_CASE. Use camelCase or PascalCase for variables and functions.
- Don't include Claude in the commits

## Formatting and Linting

- **CSharpier** for formatting: `dotnet csharpier format .`
- **Roslyn Analyzers** for linting: runs automatically during `dotnet build`
- Run `dotnet csharpier check .` to verify formatting without writing changes
- **roe** for codebase intelligence: `dotnet roe .` (dead code, duplication, health) - exits non-zero on findings and gates CI
- Suppress roe false positives in `roe.json`; prefer scoped `deadCode.ignore` over top-level `ignore`
- `roe.json` raises `health.maxTypeMembers` to 40 for `TerminalControl` (Avalonia overrides) and `VtParser` (sequence dispatch); every other health threshold stays at its default
- CSharpier is a local dotnet tool — run `dotnet tool restore` after cloning

## Commit Messages

- Don't include Claude as the author in commit messages
- Use [Conventional Commits](https://www.conventionalcommits.org/) - release-please derives the
  version bump and the changelog from them, so the prefix is load-bearing
- Format is `<type>(<optional scope>): <subject>`. Keep the existing prose subject style after the
  prefix: `feat(tabs): give the tab strip room, and a double-click to rename`
- Types in use: `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `build`, `ci`, `chore`
- `feat` bumps the minor, `fix` and `perf` bump the patch, and a `!` after the type or a
  `BREAKING CHANGE:` footer bumps the major
- A commit with no recognised prefix is skipped entirely - it neither bumps the version nor appears
  in the changelog. Nothing in CI catches this, so it is on you to get the prefix right

## Verifying the App

- The app has no scripting port. To prove GUI behaviour, drive the real window with the
  `verify-centaur` skill (`.claude/skills/verify-centaur/`) rather than reasoning from the code
- `.claude/skills/verify-centaur/features/` is the maintained map of user-facing features and how
  to drive each one. When a feature changes or a shortcut moves, update the matching feature file
  in the same commit
- Runs land in `.verify/<runId>/` (git-ignored): `artifacts/` for screenshots, `config/` for the
  instance's persisted JSON, `workdir/` for the scratch directory its shells start in

### Config isolation

- `ConfigPaths` (`Centaur.Core.Terminal`) resolves where session, settings and history JSON live.
  It is `%APPDATA%\Centaur` unless `CENTAUR_CONFIG_DIR` overrides it
- That override is what lets a verification instance run **while the user has Centaur open**
  without inheriting - and then overwriting - their tabs, history and settings
- Redirecting the `APPDATA` environment variable does not work: Windows resolves
  `SpecialFolder.ApplicationData` through `SHGetKnownFolderPath`, which reads the user's profile
  rather than the environment. Route every new state file through `ConfigPaths`, never through
  `Environment.GetFolderPath` directly
- Never stop Centaur by process name - that takes the user's own terminal with it. Signal only
  the PID the run started

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

