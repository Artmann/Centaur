# Architecture: Extensions & Providers

How Centaur composes features. This document describes the code as it exists in
`src/Centaur.Core/Hosting` and its consumers — it is a reference, not a proposal.
Design proposals and feature designs live alongside it in [`docs/`](README.md).

## 1. Why

Centaur is a terminal, but almost nothing beyond "read the PTY, parse VT, draw cells"
is core. FPS counters, render profilers, ghost-text suggestions, reverse search,
context menu entries, themes and toasts are all *additions* to that loop. Wiring them
directly into `TerminalControl` would turn one class into the whole app.

Instead the app is assembled from two kinds of parts, both managed by a single
`ExtensionHost`:

| Concept       | Interface    | Has lifecycle? | Answers                                  |
| ------------- | ------------ | -------------- | ---------------------------------------- |
| **Extension** | `IExtension` | Yes            | "Do something when the terminal starts." |
| **Provider**  | `IProvider`  | No             | "Here is a capability, use it if asked." |

Extensions *act*. Providers are *asked*. A type may be both.

## 2. Layout

```
Centaur.Core            Framework-agnostic. Hosting, VT parsing, buffers, themes.
  Hosting/              ExtensionHost, IExtension, IProvider, event bus, hooks.
Centaur.Rendering       SkiaSharp rendering. IRenderOverlay lives here.
Centaur.Pty.Windows     ConPTY implementation of IPtyConnection.
Centaur.App             Avalonia app. DI wiring, controls, app-level extensions.
```

Rule of thumb: a provider interface lives in the project that owns the types in its
signature. `IRenderOverlay` takes an `SKCanvas`, so it lives in `Centaur.Rendering`,
not in Core. `ITerminalContextMenuProvider` deals in Avalonia-facing menu items, so it
lives in `Centaur.App/Menus`. Core stays free of framework references.

```
                     ┌───────────────────────────┐
                     │       ExtensionHost       │
                     │  (IExtensionContext)      │
                     │                           │
  MainWindow ──────▶ │  extensions: IExtension[] │
  ActivateAsync()    │  providers:  IProvider[]  │
  DisposeAsync()     │  events:     TerminalEventBus
                     └─────┬───────────────┬─────┘
                           │               │
             ActivateAsync │               │ GetProvider<T>() / GetProviders<T>()
                           ▼               ▼
                    ┌─────────────┐  ┌──────────────────┐
                    │ Extensions  │  │    Providers     │
                    │ Suggestion  │  │ IThemeProvider   │
                    │ Settings    │  │ IRenderOverlay   │
                    │ ReverseSrch │  │ ISuggestionProv. │
                    │ FpsOverlay  │  │ IContextMenuProv.│
                    └──────┬──────┘  └──────────────────┘
                           │  Publish / Subscribe
                           ▼
                    ┌─────────────┐
                    │  Event bus  │◀──── TerminalControl publishes
                    └─────────────┘      (CommandSubmitted, ReverseSearchRequested, …)
```

## 3. Extensions

```csharp
public interface IExtension : IAsyncDisposable
{
    Task ActivateAsync(IExtensionContext context);
}
```

An extension gets exactly one hook into startup and one into shutdown.

- `ActivateAsync` receives an `IExtensionContext` — the host itself. Use it to look up
  providers and to subscribe to events. Keep it fast; activation is awaited in sequence
  on the UI thread during `MainWindow.Loaded`.
- Store every `IDisposable` returned by `Subscribe<T>` and dispose it in `DisposeAsync`.
- Extensions are disposed in **reverse registration order**, so an extension can rely on
  the ones registered before it still being alive while it tears down.

```csharp
public class SuggestionExtension : IExtension, ISuggestionProvider
{
    readonly CommandHistory history;
    readonly INotificationService notifications;
    IDisposable? commandSub;

    public int Priority => 100;

    public Task ActivateAsync(IExtensionContext context)
    {
        try
        {
            history.Load();
        }
        catch (Exception ex)
        {
            notifications.Show(
                "History Error",
                $"Could not load command history: {ex.Message}",
                NotificationSeverity.Warning
            );
        }

        commandSub = context.Events.Subscribe<CommandSubmittedEvent>(e => RecordCommand(e.Command));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        commandSub?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

Note the shape: it is an extension (it subscribes to commands being submitted) *and* a
provider (`ISuggestionProvider`, which `TerminalControl` queries to draw ghost text).
Registering it as an extension auto-registers it as a provider too — `ExtensionHost`
checks `extension is IProvider` and adds it to both lists.

## 4. Providers

```csharp
public interface IProvider
{
    int Priority => 1000;
}
```

Providers are passive: no activation, no disposal, no state machine. They implement a
domain interface that derives from `IProvider` and are looked up on demand.

```csharp
public interface IThemeProvider : IProvider
{
    IReadOnlyList<ThemeInfo> GetThemes();
}
```

Lookup happens through the host (or the `IExtensionContext` handed to an extension):

```csharp
var themeProvider = host.GetProvider<IThemeProvider>();   // best single match, or null
var overlays = host.GetProviders<IRenderOverlay>();       // all matches, ordered
```

**Priority ordering.** Both lookups sort ascending by `Priority`, so **a lower number
wins**: `SuggestionExtension` at `100` is consulted before anything left at the default
`1000`. `GetProvider<T>()` returns the first of that ordering, `GetProviders<T>()`
returns all of them in it. Pick a number in the same spirit as the existing ones rather
than inventing a new scale:

| Priority | Meaning                                                       |
| -------- | ------------------------------------------------------------- |
| < 1000   | Should be preferred over the defaults (`SuggestionExtension` = 100, `SettingsExtension` = 200) |
| 1000     | Default — overlays, themes, notifications, most providers      |
| > 1000   | Fallbacks that should only be used if nothing else applies     |

Provider interfaces in the codebase today:

| Interface                      | Project            | Consumed by                                      |
| ------------------------------ | ------------------ | ------------------------------------------------ |
| `IThemeProvider`               | Centaur.Core       | `TerminalControl` — resolves the active theme     |
| `ISuggestionProvider`          | Centaur.Core       | `TerminalControl` — ghost-text suggestions        |
| `IRenderOverlay`               | Centaur.Rendering  | `TerminalControl.Render` — draws over the grid    |
| `ITerminalContextMenuProvider` | Centaur.App        | `TerminalControl` — builds the right-click menu   |

`INotificationService` is deliberately *not* a provider: there is one implementation,
resolved straight from DI, so it needs no priority or fan-out.

## 5. Events

Extensions talk to the rest of the app through a typed event bus rather than by holding
references to each other.

```csharp
public interface ITerminalEvents
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
    IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler);
    void Publish<TEvent>(TEvent evt);
    Task PublishAsync<TEvent>(TEvent evt);
}
```

Events are plain records in `Centaur.Core/Hosting/TerminalHooks.cs`. **Adding a hook is
adding a record** — no enums, no string keys, no registration:

```csharp
public record CommandSubmittedEvent(string Command);
```

Current hooks and where they are raised:

| Event                        | Published by                           |
| ---------------------------- | -------------------------------------- |
| `TerminalReadyEvent`         | `ExtensionHost.ActivateAsync`, after all extensions activate |
| `TerminalShutdownEvent`      | `ExtensionHost.DisposeAsync`, before extensions are disposed |
| `CommandSubmittedEvent`      | `TerminalControl` on Enter / on running a history entry |
| `ReverseSearchRequestedEvent`| `TerminalControl.OnKeyDown` (Ctrl+R)   |
| `SettingsRequestedEvent`     | `TerminalControl.OnKeyDown`            |
| `BufferChangedEvent`, `BufferResizedEvent`, `ThemeChangedEvent`, `PtyDataReceivedEvent`, `PtyExitedEvent` | Declared but not yet published — reserved hook points |

Behaviour worth knowing before you rely on it:

- Dispatch is **synchronous and in-order**. `Publish` invokes async handlers with
  `GetAwaiter().GetResult()`; use `PublishAsync` when handlers actually need to await.
- Handlers are copied before dispatch, so subscribing or unsubscribing from inside a
  handler is safe.
- `TerminalEventBus` is **not thread-safe** — it is a plain dictionary of handler lists.
  Publish and subscribe from the UI thread. PTY output arrives on a background thread, so
  marshal to the UI thread before publishing from there.
- A handler that throws propagates to the publisher. Catch inside the handler and report
  through `INotificationService` rather than letting it escape into a render or input path.

## 6. Lifecycle

```
App.OnFrameworkInitializationCompleted
  └── ConfigureServices() ................ registers extensions & providers in DI
MainWindow.Loaded
  └── host.ActivateAsync()
        ├── providers.Sort(by Priority)
        ├── foreach extension: await ActivateAsync(context)   (registration order)
        └── publish TerminalReadyEvent
… app runs; TerminalControl queries providers and publishes events …
MainWindow.Closed
  └── host.DisposeAsync()
        ├── publish TerminalShutdownEvent
        └── foreach extension in reverse: await DisposeAsync()
```

`ExtensionHost` is a DI singleton. `MainWindow` owns its lifecycle; `TerminalControl`
resolves the same instance via `App.Services.GetRequiredService<ExtensionHost>()` and only
uses it (never activates it), since several controls share one host. Registration after
activation throws `InvalidOperationException` — the set of parts is fixed once the app is up.

## 7. Wiring

Everything is registered in `App.ConfigureServices()`. The idiom for a type that is both an
extension and something else is to register the concrete type once, then map each role onto
that same instance so all roles share state:

```csharp
services.AddSingleton<ExtensionHost>();

// Provider only
services.AddSingleton<IThemeProvider, CatppuccinThemeProvider>();

// Extension only
services.AddSingleton<SettingsExtension>();
services.AddSingleton<IExtension>(sp => sp.GetRequiredService<SettingsExtension>());

// Extension + provider + service, one instance
services.AddSingleton<NotificationServiceExtension>();
services.AddSingleton<IExtension>(sp => sp.GetRequiredService<NotificationServiceExtension>());
services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationServiceExtension>());
```

`ExtensionHost`'s constructor takes `IEnumerable<IProvider>` and `IEnumerable<IExtension>`,
so DI hands it every registration of either kind. Registration order in
`ConfigureServices` is activation order.

Two ways a provider reaches the host:

1. Registered as `IProvider` (or a subinterface bound to `IProvider`) — e.g. the context
   menu providers, `SuggestionOverlay`.
2. Registered as `IExtension` while also implementing `IProvider` — the host adds it to
   both lists itself. `FpsOverlayExtension` (an `IRenderOverlay`) works this way.

## 8. Adding a feature

**A capability others query → provider.**

1. Define `IMyThingProvider : IProvider` in the project that owns the types in its signature.
2. Implement it; set `Priority` only if ordering matters.
3. `services.AddSingleton<IProvider, MyThingProvider>();`
4. Consume with `host.GetProvider<IMyThingProvider>()` / `GetProviders<…>()`.

**Something that reacts to the terminal → extension.**

1. Implement `IExtension`; subscribe in `ActivateAsync`, dispose subscriptions in `DisposeAsync`.
2. Add a hook record to `TerminalHooks.cs` if no existing event fits, and publish it from
   wherever the thing happens (usually `TerminalControl`).
3. Register the concrete type plus an `IExtension` mapping to it.

**Both** — the common case for UI features (see `SuggestionExtension`, `FpsOverlayExtension`):
implement `IExtension` and the provider interface on one class, register the concrete type and
map each role onto it.

Whatever the shape, failures are user-visible: never swallow an exception, show an
actionable toast through `INotificationService` instead (see [CLAUDE.md](../CLAUDE.md)).

## 9. Testing

`ExtensionHost` has no framework dependencies, so extensions and providers are testable
without an Avalonia app — construct a host, register fakes, activate, assert. See
`tests/Centaur.Tests/ExtensionHostTests.cs` for the registration, priority ordering,
activation and disposal cases.

```csharp
var host = new ExtensionHost();
host.RegisterExtension(new MyExtension());
await host.ActivateAsync();
// … assert …
await host.DisposeAsync();
```
