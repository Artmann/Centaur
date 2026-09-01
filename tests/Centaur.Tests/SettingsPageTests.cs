using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Centaur.App;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// The settings page driven through the controls a user actually touches: the sidebar, the
/// search box and the option segments. The rows are built from
/// <see cref="SettingsRegistry"/>, so these assertions hold for settings added later too.
/// </summary>
public class SettingsPageTests : TempDirectory
{
    [AvaloniaFact]
    public void Opening_shows_the_general_tab()
    {
        var (page, _, _) = CreatePage();

        page.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(page.IsOpen);
        Assert.Contains("Shell", Headings(page));
        Assert.DoesNotContain("Cursor", Headings(page));
    }

    [AvaloniaFact]
    public void Clicking_a_sidebar_entry_switches_tabs()
    {
        var (page, _, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();

        Click(NavEntry(page, "Appearance"), window);

        Assert.Contains("Cursor", Headings(page));
        Assert.DoesNotContain("Shell", Headings(page));
    }

    [AvaloniaFact]
    public void Searching_filters_rows_across_both_tabs()
    {
        var (page, _, _) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();

        SearchBox(page).Text = "s";
        Dispatcher.UIThread.RunJobs();

        // The General tab is the one open, so an Appearance heading can only have come from
        // the search spanning both.
        var headings = Headings(page);
        Assert.Contains(headings, h => h.StartsWith("Appearance", StringComparison.Ordinal));
        Assert.Contains(headings, h => h.StartsWith("General", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void A_query_that_matches_nothing_says_so()
    {
        var (page, _, _) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();

        SearchBox(page).Text = "qqzzxwvj";
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(Headings(page));
        Assert.Contains(
            Labels(page),
            t => t.Contains("No settings match", StringComparison.Ordinal)
        );
    }

    [AvaloniaFact]
    public void Escape_asks_to_be_closed()
    {
        var (page, _, _) = CreatePage();
        var closed = 0;
        page.CloseRequested += () => closed++;
        page.Show();
        Dispatcher.UIThread.RunJobs();

        page.RaiseEvent(
            new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }
        );

        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void Picking_a_theme_writes_it_through_and_announces_it()
    {
        var (page, settings, window) = CreatePage();
        var changes = new List<string>();
        settings.Changed += changes.Add;

        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        Click(Segment(page, "Latte"), window);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("catppuccin-latte", settings.ThemeId);
        Assert.Contains(SettingIds.Theme, changes);
    }

    [AvaloniaFact]
    public void Picking_a_cursor_style_writes_it_through()
    {
        var (page, settings, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        Click(Segment(page, "Underline"), window);

        Assert.Equal(CursorStyle.Underline, settings.CursorStyle);
    }

    /// <summary>A page in a shown window, so its controls are laid out and hit-testable.</summary>
    (SettingsPage Page, Settings Settings, Window Window) CreatePage()
    {
        var settings = new Settings(TempFile("settings.json"));
        var host = new ExtensionHost();
        host.RegisterProvider<IThemeProvider>(new CatppuccinThemeProvider());

        var services = new TerminalServices
        {
            Host = host,
            Notifications = new SilentNotifications(),
            Suggestions = new SuggestionState(),
            CommandHistory = new CommandHistory(TempFile("history.json")),
            ReverseSearch = new ReverseSearchState(),
            Settings = settings,
            Profiler = new RenderProfiler(),
            FpsOverlay = new FpsOverlayExtension(),
        };
        services.WatchSettings();

        var page = new SettingsPage(services);
        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = page,
        };
        window.Show();

        return (page, settings, window);
    }

    static TextBox SearchBox(SettingsPage page) =>
        page.GetVisualDescendants().OfType<TextBox>().First(b => b.Watermark == "Search settings");

    /// <summary>The section headings currently rendered, which is what the page shows of its
    /// filtering. Found by tag rather than by font metrics, which a restyle moves.</summary>
    static string[] Headings(SettingsPage page) =>
        page.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => (t.Tag as string) == SettingsControls.SectionHeaderTag)
            .Select(t => t.Text ?? "")
            .ToArray();

    static string[] Labels(SettingsPage page) =>
        page.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    static Border NavEntry(SettingsPage page, string name) => Chip(page, name);

    static Border Segment(SettingsPage page, string label) => Chip(page, label);

    /// <summary>The Border whose only child is a label reading <paramref name="text"/>. The nav
    /// entries and the option segments share that shape, so they are found the same way.</summary>
    static Border Chip(SettingsPage page, string text)
    {
        var matches = page.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Child is TextBlock label && label.Text == text)
            .ToArray();

        Assert.NotEmpty(matches);
        return matches[0];
    }

    static void Click(Border target, Window window)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed
        );

        target.RaiseEvent(
            new PointerPressedEventArgs(
                target,
                pointer,
                window,
                ((Visual)target).TranslatePoint(new Point(2, 2), window) ?? default,
                timestamp: 0,
                properties,
                KeyModifiers.None,
                clickCount: 1
            )
        );

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The page never reports anything of its own; this exists only to satisfy the
    /// services bundle.</summary>
    sealed class SilentNotifications : INotificationService
    {
        public void Show(string title, string message, NotificationSeverity severity) { }
    }
}
