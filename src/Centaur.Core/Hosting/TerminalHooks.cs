using Centaur.Core.Terminal;

namespace Centaur.Core.Hosting;

public record TerminalReadyEvent;

public record TerminalShutdownEvent;

public record BufferChangedEvent;

public record BufferResizedEvent(int Columns, int Rows);

public record ThemeChangedEvent(TerminalTheme NewTheme);

public record PtyDataReceivedEvent(ReadOnlyMemory<byte> Data);

public record PtyExitedEvent(int ExitCode);
