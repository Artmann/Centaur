using Avalonia.Controls;
using Centaur.Core.Hosting;

namespace Centaur.App;

/// <summary>
/// The full-pane overlay a terminal can put up - reverse search - and the one fact the
/// terminal needs back from it: whether it is showing, because while it is, key presses
/// belong to the overlay and not to the shell.
///
/// The overlay is built on first use and then reused, and is parented to the panel holding
/// the terminal rather than to the terminal itself, so it can cover the whole pane. Settings
/// used to live here too; it is a window-level page now, because a pane-level settings
/// surface meant one overlay per split, each covering half the screen.
/// </summary>
public sealed class TerminalOverlays
{
    readonly Control owner;
    readonly TerminalServices services;
    readonly Action<string> commandSelected;

    ReverseSearchOverlay? reverseSearchOverlay;

    /// <param name="commandSelected">
    /// Called with the history entry the user picked out of reverse search, after the
    /// overlay has closed.
    /// </param>
    public TerminalOverlays(
        Control owner,
        TerminalServices services,
        Action<string> commandSelected
    )
    {
        this.owner = owner;
        this.services = services;
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

        // Read now rather than captured at construction, so the overlay follows a theme change.
        reverseSearchOverlay.Show(services.Theme, services.CommandHistory.GetAll());
        services.Host.Events.Publish(new ReverseSearchRequestedEvent());
    }

    void CloseReverseSearch()
    {
        reverseSearchOverlay?.Hide();
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
