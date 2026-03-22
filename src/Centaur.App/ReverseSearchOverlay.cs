using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

public class ReverseSearchOverlay : UserControl
{
    readonly ReverseSearchState state;
    readonly StackPanel resultsPanel;
    readonly ScrollViewer scrollViewer;
    readonly TextBox searchBox;
    readonly TextBlock matchCounter;
    readonly TextBlock placeholderText;
    readonly Border searchBarBorder;

    IReadOnlyList<string> currentCommands = [];

    SolidColorBrush? backgroundBrush;
    SolidColorBrush? foregroundBrush;
    SolidColorBrush? dimBrush;
    SolidColorBrush? accentBrush;
    SolidColorBrush? selectionBrush;
    SolidColorBrush? surfaceBrush;

    public event Action<string>? CommandSelected;
    public event Action? CloseRequested;

    static readonly FontFamily monoFont = new("JetBrains Mono, Consolas, Courier New, monospace");

    public ReverseSearchOverlay(ReverseSearchState state)
    {
        this.state = state;
        IsVisible = false;
        IsHitTestVisible = true;

        resultsPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
        };

        scrollViewer = new ScrollViewer
        {
            Content = resultsPanel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = Avalonia
                .Controls
                .Primitives
                .ScrollBarVisibility
                .Disabled,
            Padding = new Thickness(0, 8, 0, 0),
        };

        searchBox = new TextBox
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 8),
            MinHeight = 0,
            FontSize = 13,
            FontFamily = monoFont,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(0),
        };
        searchBox.FocusAdorner = null;

        placeholderText = new TextBlock
        {
            Text = "Type to search...",
            FontSize = 13,
            FontFamily = monoFont,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(8, 0, 0, 0),
        };

        matchCounter = new TextBlock
        {
            FontSize = 11,
            FontFamily = monoFont,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 4, 0),
            Opacity = 0.6,
        };

        var searchInputArea = new Panel();
        searchInputArea.Children.Add(placeholderText);
        searchInputArea.Children.Add(searchBox);

        var searchBarContent = new DockPanel();
        DockPanel.SetDock(matchCounter, Dock.Right);
        searchBarContent.Children.Add(matchCounter);
        searchBarContent.Children.Add(searchInputArea);

        searchBarBorder = new Border
        {
            Padding = new Thickness(8, 3),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = searchBarContent,
        };

        var root = new DockPanel();
        DockPanel.SetDock(searchBarBorder, Dock.Bottom);
        root.Children.Add(searchBarBorder);
        root.Children.Add(scrollViewer);

        Content = root;

        searchBox.TextChanged += OnSearchTextChanged;
        searchBox.KeyDown += OnSearchKeyDown;
    }

    public void Show(TerminalTheme theme, IReadOnlyList<string> commands)
    {
        currentCommands = commands;
        ApplyTheme(theme);

        state.Reset();
        searchBox.Text = "";
        state.UpdateQuery(commands, "");
        RebuildResults();
        UpdateMatchCounter();

        IsVisible = true;
        Dispatcher.UIThread.Post(() => searchBox.Focus(), DispatcherPriority.Input);
    }

    public void Hide()
    {
        IsVisible = false;
        state.Reset();
        searchBox.Text = "";
    }

    void ApplyTheme(TerminalTheme theme)
    {
        backgroundBrush = BrushFromUint(theme.Background);
        foregroundBrush = BrushFromUint(theme.Foreground);
        dimBrush = BrushFromUint(theme.Palette[8]); // Bright Black / Surface2 — more readable
        accentBrush = BrushFromUint(theme.Palette[4]); // Blue
        selectionBrush = BrushFromUint(theme.Selection);
        surfaceBrush = BrushFromUint(theme.Palette[0]); // Surface1

        Background = backgroundBrush;

        // Search bar
        searchBarBorder.Background = Brushes.Transparent;
        searchBox.Foreground = foregroundBrush;
        searchBox.CaretBrush = accentBrush;

        // Override Fluent TextBox theme resources for all states
        var transparent = Brushes.Transparent;
        // Override all Fluent TextBox/TextControl resource keys for every state
        foreach (
            var key in new[]
            {
                "TextBoxBackground",
                "TextBoxBackgroundPointerOver",
                "TextBoxBackgroundFocused",
                "TextBoxBorderBrush",
                "TextBoxBorderBrushPointerOver",
                "TextBoxBorderBrushFocused",
                "TextControlBackground",
                "TextControlBackgroundPointerOver",
                "TextControlBackgroundFocused",
                "TextControlBorderBrush",
                "TextControlBorderBrushPointerOver",
                "TextControlBorderBrushFocused",
            }
        )
        {
            searchBox.Resources[key] = transparent;
        }
        foreach (
            var key in new[]
            {
                "TextBoxForeground",
                "TextBoxForegroundPointerOver",
                "TextBoxForegroundFocused",
                "TextControlForeground",
                "TextControlForegroundPointerOver",
                "TextControlForegroundFocused",
            }
        )
        {
            searchBox.Resources[key] = foregroundBrush;
        }
        placeholderText.Foreground = BrushFromUint(theme.Foreground, 0.5);

        matchCounter.Foreground = dimBrush;
        searchBarBorder.BorderBrush = dimBrush;
    }

    void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = searchBox.Text ?? "";
        placeholderText.IsVisible = string.IsNullOrEmpty(query);
        state.UpdateQuery(currentCommands, query);
        RebuildResults();
        UpdateMatchCounter();
    }

    void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                state.MoveSelection(-1);
                UpdateSelectionVisual();
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Down:
                state.MoveSelection(1);
                UpdateSelectionVisual();
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Enter:
                if (state.SelectedCommand != null)
                {
                    CommandSelected?.Invoke(state.SelectedCommand.Command);
                }
                e.Handled = true;
                break;
            case Key.Escape:
                CloseRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    void RebuildResults()
    {
        resultsPanel.Children.Clear();

        if (state.TotalCount == 0)
        {
            resultsPanel.Children.Add(CreateEmptyMessage("No command history yet"));
            return;
        }

        if (state.FilteredResults.Count == 0)
        {
            resultsPanel.Children.Add(CreateEmptyMessage("No matches"));
            return;
        }

        for (int i = 0; i < state.FilteredResults.Count; i++)
        {
            var item = state.FilteredResults[i];
            var row = CreateResultRow(item, i == state.SelectedIndex);
            resultsPanel.Children.Add(row);
        }

        ScrollToSelected();
    }

    Border CreateResultRow(FilteredCommand item, bool isSelected)
    {
        var textBlock = new TextBlock
        {
            FontSize = 14,
            FontFamily = monoFont,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (item.MatchResult != null)
        {
            var matchedSet = new HashSet<int>(item.MatchResult.MatchedIndices);
            for (int i = 0; i < item.Command.Length; i++)
            {
                var run = new Run(item.Command[i].ToString());
                if (matchedSet.Contains(i))
                {
                    run.Foreground = accentBrush;
                    run.FontWeight = FontWeight.Bold;
                }
                else
                {
                    run.Foreground = isSelected ? foregroundBrush : dimBrush;
                }
                textBlock.Inlines!.Add(run);
            }
        }
        else
        {
            textBlock.Text = item.Command;
            textBlock.Foreground = isSelected ? foregroundBrush : dimBrush;
        }

        return new Border
        {
            Padding = new Thickness(16, 6),
            Background = isSelected ? selectionBrush : Brushes.Transparent,
            BorderBrush = isSelected ? accentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Child = textBlock,
        };
    }

    TextBlock CreateEmptyMessage(string message)
    {
        return new TextBlock
        {
            Text = message,
            Foreground = dimBrush,
            FontSize = 14,
            FontFamily = monoFont,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 40),
        };
    }

    void UpdateSelectionVisual()
    {
        for (int i = 0; i < resultsPanel.Children.Count; i++)
        {
            if (resultsPanel.Children[i] is not Border border)
            {
                continue;
            }

            var isSelected = i == state.SelectedIndex;
            border.Background = isSelected ? selectionBrush : Brushes.Transparent;
            border.BorderBrush = isSelected ? accentBrush : Brushes.Transparent;

            if (border.Child is TextBlock tb)
            {
                if (tb.Inlines != null && tb.Inlines.Count > 0)
                {
                    foreach (var inline in tb.Inlines)
                    {
                        if (inline is Run run && run.FontWeight != FontWeight.Bold)
                        {
                            run.Foreground = isSelected ? foregroundBrush : dimBrush;
                        }
                    }
                }
                else
                {
                    tb.Foreground = isSelected ? foregroundBrush : dimBrush;
                }
            }
        }
    }

    void UpdateMatchCounter()
    {
        matchCounter.Text = $"{state.FilteredResults.Count} / {state.TotalCount}";
    }

    void ScrollToSelected()
    {
        if (state.SelectedIndex < 0 || state.SelectedIndex >= resultsPanel.Children.Count)
        {
            return;
        }

        var child = resultsPanel.Children[state.SelectedIndex];
        if (child is Control control)
        {
            control.BringIntoView();
        }
    }

    static SolidColorBrush BrushFromUint(uint color, double opacity = 1.0)
    {
        var c = Color.FromUInt32(color);
        if (opacity < 1.0)
        {
            c = Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B);
        }
        return new SolidColorBrush(c);
    }
}
