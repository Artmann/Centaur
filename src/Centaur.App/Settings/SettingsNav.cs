using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>
/// The page's sidebar: one entry per <see cref="SettingsTab"/>, built from the enum so a new tab
/// needs nothing here.
///
/// The list is one tab stop and its arrow keys move between the entries, the way a sidebar behaves
/// everywhere else. Tabbing through every entry on the way to the settings themselves would make
/// the keyboard route to the page's actual content longer the more tabs the page grows.
/// </summary>
sealed class SettingsNav
{
    readonly StackPanel panel = new() { Spacing = 2 };
    readonly List<SettingsButton> entries = [];
    readonly SettingsTab[] tabs = Enum.GetValues<SettingsTab>();

    OverlayTheme colors;
    SettingsTab selected;
    bool searching;

    public SettingsNav(OverlayTheme colors)
    {
        this.colors = colors;

        foreach (var tab in tabs)
        {
            var entry = CreateEntry(tab);
            entries.Add(entry);
            panel.Children.Add(entry);
        }

        Paint();
    }

    public Control View => panel;

    public SettingsTab Selected => selected;

    /// <summary>Raised when the user picks a different tab.</summary>
    public event Action<SettingsTab>? TabSelected;

    public void Select(SettingsTab tab)
    {
        selected = tab;
        Paint();
    }

    /// <summary>
    /// Search results span every tab, so while a query is live no entry is highlighted - a
    /// highlight would claim the results all came from one tab.
    /// </summary>
    public void SetSearching(bool value)
    {
        if (searching == value)
        {
            return;
        }

        searching = value;
        Paint();
    }

    public void ApplyTheme(OverlayTheme theme)
    {
        colors = theme;
        Paint();
    }

    SettingsButton CreateEntry(SettingsTab tab)
    {
        var entry = new SettingsButton
        {
            Child = OverlayControls.CreateUiLabel(tab.Label(), 13),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            FocusBrush = colors.Accent,
            Tag = tab,

            // Only the open entry is a tab stop. Focus arriving at the sidebar lands on where the
            // user already is, and the arrows walk from there.
            Focusable = false,
        };

        entry.Activated += () => Choose(tab);
        entry.Moved += direction => Choose(Neighbour(tab, direction));
        return entry;
    }

    /// <summary>The tab an arrow key moves to, clamped at the ends of the list.</summary>
    SettingsTab Neighbour(SettingsTab from, int direction)
    {
        var index = Array.IndexOf(tabs, from);
        var next = direction switch
        {
            int.MinValue => 0,
            int.MaxValue => tabs.Length - 1,
            _ => index + direction,
        };

        return tabs[Math.Clamp(next, 0, tabs.Length - 1)];
    }

    void Choose(SettingsTab tab)
    {
        Select(tab);
        TabSelected?.Invoke(tab);

        // The stop moved with the selection, so the keyboard follows it rather than being left
        // on an entry that is no longer focusable.
        Entry(tab)?.Focus(NavigationMethod.Directional);
    }

    SettingsButton? Entry(SettingsTab tab) => entries.Find(entry => (SettingsTab)entry.Tag! == tab);

    void Paint()
    {
        foreach (var entry in entries)
        {
            var isOpen = (SettingsTab)entry.Tag! == selected;
            var isSelected = !searching && isOpen;

            entry.SetFill(
                isSelected ? colors.Chip : Brushes.Transparent,
                isSelected ? null : colors.Hover,
                isSelected ? null : colors.Press
            );

            entry.FocusBrush = colors.Accent;

            // The stop follows the open tab even while a search is hiding the highlight, so
            // Shift+Tab out of the search box always lands somewhere.
            entry.Focusable = isOpen;

            if (entry.Child is TextBlock label)
            {
                label.Foreground = isSelected ? colors.Foreground : colors.Dim;
            }
        }
    }
}
