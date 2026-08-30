using Avalonia.Controls;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The two full-pane overlays a terminal can put up - reverse search and settings - and the
/// one fact the terminal needs back from them: whether either is showing, because while one
/// is, key presses belong to the overlay and not to the shell.
///
/// Each overlay is built on first use and then reused, and is parented to the panel holding
/// the terminal rather than to the terminal itself, so it can cover the whole pane.
/// </summary>
public sealed class TerminalOverlays
{
    readonly Control owner;
    readonly TerminalServices services;
    readonly TerminalTheme theme;
    readonly Action<string> commandSelected;

    ReverseSearchOverlay? reverseSearchOverlay;
    SettingsOverlay? settingsOverlay;

    /// <param name="commandSelected">
    /// Called with the history entry the user picked out of reverse search, after the
    /// overlay has closed.
    /// </param>
    public TerminalOverlays(
        Control owner,
        TerminalServices services,
        TerminalTheme theme,
        Action<string> commandSelected
    )
    {
        this.owner = owner;
        this.services = services;
        this.theme = theme;
        this.commandSelected = commandSelected;
    }

    /// <summary>True while an overlay is up and owns the keyboard.</summary>
    public bool AnyOpen { get; private set; }

    public void OpenReverseSearch()
    {
        if (AnyOpen)
        {
            return;
        }

        AnyOpen = true;

        if (reverseSearchOverlay == null)
        {
            reverseSearchOverlay = new ReverseSearchOverlay(services.ReverseSearch);
            reverseSearchOverlay.CommandSelected += command =>
            {
                CloseReverseSearch();
                commandSelected(command);
            };
            reverseSearchOverlay.CloseRequested += CloseReverseSearch;
            Attach(reverseSearchOverlay);
        }

        reverseSearchOverlay.Show(theme, services.CommandHistory.GetAll());
        services.Host.Events.Publish(new ReverseSearchRequestedEvent());
    }

    public void OpenSettings()
    {
        if (AnyOpen)
        {
            return;
        }

        AnyOpen = true;

        if (settingsOverlay == null)
        {
            settingsOverlay = new SettingsOverlay(services.Settings);
            settingsOverlay.CloseRequested += CloseSettings;
            Attach(settingsOverlay);
        }

        settingsOverlay.Show(theme);
        services.Host.Events.Publish(new SettingsRequestedEvent());
    }

    void CloseReverseSearch()
    {
        reverseSearchOverlay?.Hide();
        Closed();
    }

    void CloseSettings()
    {
        settingsOverlay?.Hide();
        Closed();
    }

    void Attach(Control overlay)
    {
        if (owner.Parent is Panel panel)
        {
            panel.Children.Add(overlay);
        }
    }

    // Focus goes back to the terminal, otherwise the shell stops receiving keys entirely.
    void Closed()
    {
        AnyOpen = false;
        owner.Focus();
    }
}
