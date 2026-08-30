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

    OverlayTheme? colors;

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
        colors = new OverlayTheme(theme);

        Background = colors.Background;

        searchBarBorder.Background = Brushes.Transparent;
        searchBarBorder.BorderBrush = colors.Dim;
        colors.StyleTextBox(searchBox);

        placeholderText.Foreground = colors.Placeholder;
        matchCounter.Foreground = colors.Dim;
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
                    run.Foreground = colors?.Accent;
                    run.FontWeight = FontWeight.Bold;
                }
                else
                {
                    run.Foreground = isSelected ? colors?.Foreground : colors?.Dim;
                }
                textBlock.Inlines!.Add(run);
            }
        }
        else
        {
            textBlock.Text = item.Command;
            textBlock.Foreground = isSelected ? colors?.Foreground : colors?.Dim;
        }

        return new Border
        {
            Padding = new Thickness(16, 6),
            Background = isSelected ? colors?.Selection : Brushes.Transparent,
            BorderBrush = isSelected ? colors?.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Child = textBlock,
        };
    }

    TextBlock CreateEmptyMessage(string message)
    {
        return new TextBlock
        {
            Text = message,
            Foreground = colors?.Dim,
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
            border.Background = isSelected ? colors?.Selection : Brushes.Transparent;
            border.BorderBrush = isSelected ? colors?.Accent : Brushes.Transparent;

            if (border.Child is TextBlock tb)
            {
                if (tb.Inlines != null && tb.Inlines.Count > 0)
                {
                    foreach (var inline in tb.Inlines)
                    {
                        if (inline is Run run && run.FontWeight != FontWeight.Bold)
                        {
                            run.Foreground = isSelected ? colors?.Foreground : colors?.Dim;
                        }
                    }
                }
                else
                {
                    tb.Foreground = isSelected ? colors?.Foreground : colors?.Dim;
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
}
