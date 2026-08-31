using Centaur.Core.Terminal;

namespace Centaur.Core.Hosting;

public record TerminalReadyEvent;

public record TerminalShutdownEvent;

public record BufferChangedEvent;

public record BufferResizedEvent(int Columns, int Rows);

public record ThemeChangedEvent(TerminalTheme NewTheme);

public record PtyDataReceivedEvent(ReadOnlyMemory<byte> Data);

public record PtyExitedEvent(int ExitCode);

public record CommandSubmittedEvent(string Command);

public record ReverseSearchRequestedEvent;

public record SettingsRequestedEvent;

/// <summary>One setting was saved, named by its <see cref="SettingIds"/> id - or the empty
/// string for a bulk save. Lets an extension follow the user's configuration without holding a
/// <see cref="Settings"/> reference of its own.</summary>
public record SettingsChangedEvent(string Id);
