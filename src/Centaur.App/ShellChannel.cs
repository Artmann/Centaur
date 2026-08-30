using System.Buffers;
using Avalonia.Threading;
using Centaur.Core.Hosting;
using Centaur.Core.Pty;
using Centaur.Core.Terminal;
using Centaur.Pty.Windows;

namespace Centaur.App;

/// <summary>
/// The pane's half of the conversation with its shell: starting the child process, writing to
/// it, and tearing it down. Output goes straight back out through the callback the pane
/// supplies - this type never touches the screen buffer.
///
/// It also tracks the working directory, because the only evidence of it is the command line
/// the user submitted, which arrives through the same channel.
/// </summary>
public sealed class ShellChannel
{
    readonly INotificationService notifications;
    readonly Settings settings;
    readonly Action<ReadOnlySequence<byte>> onOutput;

    // Raised before every user-initiated write, so the pane can jump the view back to the
    // live edge. Protocol replies (Respond) deliberately don't fire it.
    readonly Action onUserInput;

    readonly string? preferredDirectory;
    PtySession? session;
    bool started;

    public ShellChannel(
        INotificationService notifications,
        Settings settings,
        string? preferredDirectory,
        Action<ReadOnlySequence<byte>> onOutput,
        Action onUserInput
    )
    {
        this.notifications = notifications;
        this.settings = settings;
        this.preferredDirectory = preferredDirectory;
        this.onOutput = onOutput;
        this.onUserInput = onUserInput;
        WorkingDirectory = preferredDirectory;
    }

    /// <summary>Raised on the UI thread once the child process has gone away.</summary>
    public event Action? Exited;

    public event Action? WorkingDirectoryChanged;

    /// <summary>Where the shell is believed to be, as far as submitted commands reveal.</summary>
    public string? WorkingDirectory { get; private set; }

    /// <summary>Drops user input on the floor while leaving protocol replies working, so a
    /// pane can be parked next to a long-running command without being typed into.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>True once the shell is up and writes will reach it.</summary>
    public bool IsConnected => session != null;

    /// <summary>
    /// Spawns the shell at the given grid size. Idempotent: the pane calls this from its
    /// layout pass, which runs repeatedly, but only the first call starts anything.
    /// </summary>
    public async void Start(int columns, int rows)
    {
        if (started)
        {
            return;
        }
        started = true;

        try
        {
            WorkingDirectory = ResolveStartingDirectory();
            var options = new PtyOptions(
                executable: "powershell.exe",
                columns: columns,
                rows: rows,
                workingDirectory: WorkingDirectory
            );

            session = await PtySession.StartAsync(
                options,
                onOutput,
                () => Dispatcher.UIThread.Post(() => Exited?.Invoke())
            );
        }
        catch (Exception ex)
        {
            notifications.Show("PTY Error", ex.Message, NotificationSeverity.Error);
        }
    }

    public void Resize(int columns, int rows)
    {
        session?.Resize(columns, rows);
    }

    public async void Stop()
    {
        var stopping = session;
        session = null;
        if (stopping != null)
        {
            await stopping.DisposeAsync();
        }
    }

    /// <summary>Sends user input, unless the pane is read-only.</summary>
    public async void Send(byte[] data)
    {
        var target = session;
        if (target == null || IsReadOnly)
        {
            return;
        }

        onUserInput();

        try
        {
            await target.WriteAsync(data);
        }
        catch (Exception ex)
        {
            notifications.Show("Input Error", ex.Message, NotificationSeverity.Error);
        }
    }

    /// <summary>
    /// Writes a parser-generated protocol reply (Device Attributes, DECRQM, OSC color or
    /// clipboard). Unlike <see cref="Send"/> this ignores the read-only gate and does not
    /// move the view: these are answers to the program's own questions, not user input, and
    /// a program that never gets them stalls on its startup timeouts before it will echo.
    /// Called from the PTY read thread, but it writes to the input pipe, so nothing contends.
    /// </summary>
    public async void Respond(byte[] data)
    {
        var target = session;
        if (target == null)
        {
            return;
        }

        try
        {
            await target.WriteAsync(data);
        }
        catch (Exception ex)
        {
            notifications.Show("Terminal Error", ex.Message, NotificationSeverity.Error);
        }
    }

    /// <summary>
    /// The shell never tells us where it is, so we read each submitted command line and
    /// follow directory changes ourselves. See <see cref="WorkingDirectoryTracker"/>.
    /// </summary>
    public void NoteCommandSubmitted(string command)
    {
        var target = WorkingDirectoryTracker.Resolve(command, settings.LastFolder);
        if (target == null)
        {
            return;
        }

        settings.UpdateLastFolder(target);
        WorkingDirectory = target;
        WorkingDirectoryChanged?.Invoke();
    }

    string? ResolveStartingDirectory()
    {
        var directory = preferredDirectory ?? settings.GetStartingDirectory();
        if (directory == null || Directory.Exists(directory))
        {
            return directory;
        }

        notifications.Show(
            "Starting Directory",
            $"Directory \"{directory}\" not found. Using default instead.",
            NotificationSeverity.Warning
        );
        return null;
    }
}
