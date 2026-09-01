using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>
/// The page's sidebar: one entry per <see cref="SettingsTab"/>, built from the enum so a new tab
/// needs nothing here.
/// </summary>
sealed class SettingsNav
{
    readonly StackPanel panel = new() { Spacing = 2 };
    readonly List<Border> entries = [];

    OverlayTheme colors;
    SettingsTab selected;
    bool searching;

    public SettingsNav(OverlayTheme colors)
    {
        this.colors = colors;

        foreach (var tab in Enum.GetValues<SettingsTab>())
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

    Border CreateEntry(SettingsTab tab)
    {
        var label = OverlayControls.CreateUiLabel(tab.ToString(), 13);

        var entry = new Border
        {
            Child = label,
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = tab,
        };

        entry.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Select(tab);
            TabSelected?.Invoke(tab);
        };

        return entry;
    }

    void Paint()
    {
        foreach (var entry in entries)
        {
            var isSelected = !searching && (SettingsTab)entry.Tag! == selected;
            entry.Background = isSelected ? colors.Selection : Brushes.Transparent;

            if (entry.Child is TextBlock label)
            {
                label.Foreground = isSelected ? colors.Foreground : colors.Dim;
            }
        }
    }
}
