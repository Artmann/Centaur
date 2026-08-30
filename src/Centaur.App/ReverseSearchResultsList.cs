using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The scrolling list of history matches inside the reverse-search overlay.
///
/// Owns the rows and their selected/unselected look; the overlay above it owns the
/// search bar and the key handling, and drives this through Rebuild and UpdateSelection.
/// </summary>
sealed class ReverseSearchResultsList
{
    readonly ReverseSearchState state;
    readonly StackPanel panel;
    readonly ScrollViewer scroller;

    OverlayTheme? colors;

    public ReverseSearchResultsList(ReverseSearchState state)
    {
        this.state = state;

        // Bottom-aligned: the newest, best matches sit next to the search bar.
        panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
        };

        scroller = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 8, 0, 0),
        };
    }

    public Control View => scroller;

    public void ApplyTheme(OverlayTheme theme)
    {
        colors = theme;
    }

    /// <summary>Rebuilds every row from the current filter results.</summary>
    public void Rebuild()
    {
        panel.Children.Clear();

        if (state.TotalCount == 0)
        {
            panel.Children.Add(EmptyMessage("No command history yet"));
            return;
        }

        if (state.FilteredResults.Count == 0)
        {
            panel.Children.Add(EmptyMessage("No matches"));
            return;
        }

        for (int i = 0; i < state.FilteredResults.Count; i++)
        {
            panel.Children.Add(CreateRow(state.FilteredResults[i], i == state.SelectedIndex));
        }

        ScrollToSelected();
    }

    /// <summary>Repaints the existing rows after the selection moved.</summary>
    public void UpdateSelection()
    {
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is Border row)
            {
                PaintRow(row, i == state.SelectedIndex);
            }
        }

        ScrollToSelected();
    }

    Border CreateRow(FilteredCommand item, bool isSelected)
    {
        var text = new TextBlock
        {
            FontSize = 14,
            FontFamily = OverlayControls.MonoFont,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (item.MatchResult == null)
        {
            text.Text = item.Command;
        }
        else
        {
            // One Run per character so the fuzzy-matched ones can be picked out; their
            // bold weight is also how PaintRow knows to leave their colour alone.
            var matched = new HashSet<int>(item.MatchResult.MatchedIndices);
            for (int i = 0; i < item.Command.Length; i++)
            {
                var run = new Run(item.Command[i].ToString());
                if (matched.Contains(i))
                {
                    run.Foreground = colors?.Accent;
                    run.FontWeight = FontWeight.Bold;
                }
                text.Inlines!.Add(run);
            }
        }

        var row = new Border
        {
            Padding = new Thickness(16, 6),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Child = text,
        };

        PaintRow(row, isSelected);
        return row;
    }

    /// <summary>Applies the selected or unselected look to one row in place.</summary>
    void PaintRow(Border row, bool isSelected)
    {
        row.Background = isSelected ? colors?.Selection : Brushes.Transparent;
        row.BorderBrush = isSelected ? colors?.Accent : Brushes.Transparent;

        if (row.Child is not TextBlock text)
        {
            return;
        }

        var brush = isSelected ? colors?.Foreground : colors?.Dim;
        if (text.Inlines is not { Count: > 0 } inlines)
        {
            text.Foreground = brush;
            return;
        }

        foreach (var inline in inlines)
        {
            if (inline is Run run && run.FontWeight != FontWeight.Bold)
            {
                run.Foreground = brush;
            }
        }
    }

    TextBlock EmptyMessage(string message) =>
        new()
        {
            Text = message,
            Foreground = colors?.Dim,
            FontSize = 14,
            FontFamily = OverlayControls.MonoFont,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 40),
        };

    void ScrollToSelected()
    {
        if (state.SelectedIndex < 0 || state.SelectedIndex >= panel.Children.Count)
        {
            return;
        }

        if (panel.Children[state.SelectedIndex] is Control control)
        {
            control.BringIntoView();
        }
    }
}
