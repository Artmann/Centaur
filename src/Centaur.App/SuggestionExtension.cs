using Centaur.Core.Hosting;
using Centaur.Core.Terminal;

namespace Centaur.App;

public class SuggestionExtension : IExtension, ISuggestionProvider
{
    readonly CommandHistory history;
    readonly INotificationService notifications;
    IDisposable? commandSub;

    public int Priority => 100;

    public SuggestionExtension(INotificationService notifications)
    {
        var historyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Centaur"
        );
        var historyFile = Path.Combine(historyDir, "command-history.json");
        history = new CommandHistory(historyFile);
        this.notifications = notifications;
    }

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

        commandSub = context.Events.Subscribe<CommandSubmittedEvent>(e =>
        {
            RecordCommand(e.Command);
        });

        return Task.CompletedTask;
    }

    public string? GetSuggestion(string currentInput)
    {
        if (string.IsNullOrWhiteSpace(currentInput))
        {
            return null;
        }

        return history.FindMatch(currentInput);
    }

    public void RecordCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        history.Add(command.Trim());

        try
        {
            history.Save();
        }
        catch (Exception ex)
        {
            notifications.Show(
                "History Error",
                $"Could not save command history: {ex.Message}",
                NotificationSeverity.Warning
            );
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        commandSub?.Dispose();
        return ValueTask.CompletedTask;
    }
}
