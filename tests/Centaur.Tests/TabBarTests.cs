using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Centaur.App;
using Xunit;
using TabItem = Centaur.App.TabItem;

namespace Centaur.Tests;

/// <summary>The tab strip's pointer behaviour: selecting on a click, renaming on a
/// double-click, and the editor's commit and cancel keys.</summary>
public class TabBarTests
{
    [AvaloniaFact]
    public void Double_click_opens_the_rename_editor()
    {
        var (bar, window) = CreateBar();

        Press(bar, window, "Terminal 1", clickCount: 2);

        var chip = Chip(bar, "Terminal 1");
        Assert.True(Editor(chip).IsVisible);
        Assert.False(Label(chip, "Terminal 1").IsVisible);
    }

    [AvaloniaFact]
    public void Single_click_selects_the_tab_without_editing()
    {
        var (bar, window) = CreateBar();
        var selected = new List<int>();
        bar.TabSelected += id => selected.Add(id);

        Press(bar, window, "Terminal 2", clickCount: 1);

        Assert.Equal([2], selected);
        Assert.False(Editor(Chip(bar, "Terminal 2")).IsVisible);
    }

    [AvaloniaFact]
    public void Enter_commits_the_new_title()
    {
        var (_, editor, renames) = StartRename();

        editor.Text = "Comrade Mayor";
        PressKey(editor, Key.Enter);

        Assert.Equal([(1, "Comrade Mayor")], renames);
    }

    [AvaloniaFact]
    public void Escape_leaves_the_title_alone()
    {
        var (chip, editor, renames) = StartRename();

        editor.Text = "Discarded";
        PressKey(editor, Key.Escape);

        Assert.Empty(renames);
        Assert.False(editor.IsVisible);
        Assert.True(Label(chip, "Terminal 1").IsVisible);
    }

    /// <summary>A strip with the first tab's editor already open, plus the list the renames
    /// it reports land in.</summary>
    static (Panel Chip, TextBox Editor, List<(int Id, string Title)> Renames) StartRename()
    {
        var (bar, window) = CreateBar();
        var renames = new List<(int Id, string Title)>();
        bar.TabRenamed += (id, title) => renames.Add((id, title));

        Press(bar, window, "Terminal 1", clickCount: 2);

        var chip = Chip(bar, "Terminal 1");
        return (chip, Editor(chip), renames);
    }

    /// <summary>A shown two-tab strip. The strip hides itself below two tabs, and the
    /// ScrollViewer only builds its template once the window has laid out.</summary>
    static (TabBar Bar, Window Window) CreateBar()
    {
        var bar = new TabBar();
        var window = new Window
        {
            Width = 400,
            Height = 40,
            Content = bar,
        };
        window.Show();

        bar.Update(
            [
                new TabItem
                {
                    Id = 1,
                    Title = "Terminal 1",
                    Panes = FakePaneTerminal.BuildTree().tree,
                },
                new TabItem
                {
                    Id = 2,
                    Title = "Terminal 2",
                    Panes = FakePaneTerminal.BuildTree().tree,
                },
            ],
            activeId: 1
        );
        Dispatcher.UIThread.RunJobs();

        return (bar, window);
    }

    static void Press(TabBar bar, Window window, string title, int clickCount)
    {
        var chip = Chip(bar, title);
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed
        );

        chip.RaiseEvent(
            new PointerPressedEventArgs(
                chip,
                pointer,
                window,
                chip.TranslatePoint(new Point(4, 4), window) ?? default,
                timestamp: 0,
                properties,
                KeyModifiers.None,
                clickCount
            )
        );
    }

    static void PressKey(TextBox editor, Key key) =>
        editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    /// <summary>The panel for one tab, found through its label so the close button's own
    /// "×" TextBlock can't be mistaken for it.</summary>
    static Panel Chip(TabBar bar, string title) => (Panel)FindLabel(bar, title).GetVisualParent()!;

    static TextBlock Label(Panel chip, string title) =>
        chip.Children.OfType<TextBlock>().First(t => t.Text == title);

    static TextBox Editor(Panel chip) => chip.Children.OfType<TextBox>().Single();

    static TextBlock FindLabel(TabBar bar, string title) =>
        bar.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == title);
}
