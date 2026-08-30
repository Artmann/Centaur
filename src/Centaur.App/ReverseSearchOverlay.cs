using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// Ctrl+R history search: a search bar pinned over the prompt with the matching
/// commands listed above it. <see cref="ReverseSearchResultsList"/> owns the list.
/// </summary>
public class ReverseSearchOverlay : UserControl
{
    readonly ReverseSearchState state;
    readonly ReverseSearchResultsList results;
    readonly TextBox searchBox;
    readonly TextBlock matchCounter;
    readonly TextBlock placeholderText;
    readonly Border searchBarBorder;

    IReadOnlyList<string> currentCommands = [];

    public event Action<string>? CommandSelected;
    public event Action? CloseRequested;

    public ReverseSearchOverlay(ReverseSearchState state)
    {
        this.state = state;
        IsVisible = false;
        IsHitTestVisible = true;

        results = new ReverseSearchResultsList(state);
        searchBox = OverlayControls.CreateTextBox(new Thickness(0));

        // Sits behind the TextBox rather than using its Watermark so it can be styled
        // independently; hidden as soon as the query is non-empty.
        placeholderText = OverlayControls.CreateLabel("Type to search...", 13);
        placeholderText.VerticalAlignment = VerticalAlignment.Center;
        placeholderText.IsHitTestVisible = false;
        placeholderText.Margin = new Thickness(8, 0, 0, 0);

        matchCounter = OverlayControls.CreateLabel("", 11);
        matchCounter.VerticalAlignment = VerticalAlignment.Center;
        matchCounter.Margin = new Thickness(12, 0, 4, 0);
        matchCounter.Opacity = 0.6;

        searchBarBorder = CreateSearchBar();

        // Results fill the overlay; the search bar is pinned to the bottom, where the
        // prompt the user was typing at is.
        var root = new DockPanel();
        DockPanel.SetDock(searchBarBorder, Dock.Bottom);
        root.Children.Add(searchBarBorder);
        root.Children.Add(results.View);

        Content = root;

        searchBox.TextChanged += OnSearchTextChanged;
        searchBox.KeyDown += OnSearchKeyDown;
    }

    Border CreateSearchBar()
    {
        var inputArea = new Panel();
        inputArea.Children.Add(placeholderText);
        inputArea.Children.Add(searchBox);

        var content = new DockPanel();
        DockPanel.SetDock(matchCounter, Dock.Right);
        content.Children.Add(matchCounter);
        content.Children.Add(inputArea);

        return new Border
        {
            Padding = new Thickness(8, 3),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = content,
        };
    }

    public void Show(TerminalTheme theme, IReadOnlyList<string> commands)
    {
        currentCommands = commands;
        ApplyTheme(theme);

        state.Reset();
        searchBox.Text = "";
        state.UpdateQuery(commands, "");
        results.Rebuild();
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
        var colors = new OverlayTheme(theme);

        Background = colors.Background;

        searchBarBorder.Background = Brushes.Transparent;
        searchBarBorder.BorderBrush = colors.Dim;
        colors.StyleTextBox(searchBox);

        placeholderText.Foreground = colors.Placeholder;
        matchCounter.Foreground = colors.Dim;

        results.ApplyTheme(colors);
    }

    void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = searchBox.Text ?? "";
        placeholderText.IsVisible = string.IsNullOrEmpty(query);
        state.UpdateQuery(currentCommands, query);
        results.Rebuild();
        UpdateMatchCounter();
    }

    void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                state.MoveSelection(-1);
                results.UpdateSelection();
                e.Handled = true;
                break;
            case Key.Down:
                state.MoveSelection(1);
                results.UpdateSelection();
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

    void UpdateMatchCounter()
    {
        matchCounter.Text = $"{state.FilteredResults.Count} / {state.TotalCount}";
    }
}
