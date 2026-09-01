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

    [AvaloniaFact]
    public void Picking_a_theme_keeps_the_keyboard_on_the_picker()
    {
        var (page, settings, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        var picker = Group(page, "Latte");
        picker.Focus(NavigationMethod.Directional);
        Press(picker, Key.End);

        // A theme change is the one edit that rebuilds the page, so the control the keyboard was
        // standing on is thrown away mid-keystroke. Without putting it back, the next arrow key
        // walks out of the page and into the window's caption buttons.
        Assert.Equal("catppuccin-mocha", settings.ThemeId);
        Assert.Same(Group(page, "Latte"), window.FocusManager?.GetFocusedElement());
    }

    [AvaloniaFact]
    public void A_section_with_one_row_drops_the_heading_that_would_repeat_it()
    {
        var (page, _, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        // Theme is the tab's only theme row, so a "Theme" heading would sit directly on top of a
        // row titled "Theme". Cursor has two rows and so still needs naming.
        Assert.DoesNotContain("Theme", Headings(page));
        Assert.Contains("Cursor", Headings(page));
    }

    [AvaloniaFact]
    public void Tab_from_the_search_box_reaches_the_tabs()
    {
        var (page, _, _) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();

        var next = KeyboardNavigationHandler.GetNext(SearchBox(page), NavigationDirection.Next);

        Assert.Same(NavEntry(page, "General"), next);
    }

    [AvaloniaFact]
    public void Arrows_move_a_segmented_choice()
    {
        var (page, settings, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        // The group is the tab stop, not the segments: one press of Right moves off Block.
        Press(Group(page, "Block"), Key.Right);

        Assert.Equal(CursorStyle.Underline, settings.CursorStyle);
    }

    [AvaloniaFact]
    public void Space_flips_a_switch()
    {
        var (page, settings, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        Assert.False(settings.CursorBlink);
        Press(Switch(page), Key.Space);

        Assert.True(settings.CursorBlink);
    }

    [AvaloniaFact]
    public void A_number_can_be_typed_and_is_clamped_to_its_range()
    {
        var (page, settings, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        var field = Stepper(page, " pt");
        field.Text = "999";
        Press(field, Key.Enter);

        Assert.Equal(48, settings.FontSize);
        Assert.Equal("48 pt", field.Text);
    }

    [AvaloniaFact]
    public void A_stepper_disables_the_step_that_would_do_nothing()
    {
        var (page, _, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();
        Click(NavEntry(page, "Appearance"), window);

        var field = Stepper(page, " pt");
        field.Text = "8";
        Press(field, Key.Enter);

        var (down, up) = Steps(field);
        Assert.False(down.IsEnabled);
        Assert.True(up.IsEnabled);
    }

    [AvaloniaFact]
    public void A_query_that_matches_nothing_offers_a_way_on()
    {
        var (page, _, window) = CreatePage();
        page.Show();
        Dispatcher.UIThread.RunJobs();

        SearchBox(page).Text = "qqzzxwvj";
        Dispatcher.UIThread.RunJobs();

        Click(Chip(page, "Cursor"), window);

        Assert.Equal("Cursor", SearchBox(page).Text);
        Assert.NotEmpty(Headings(page));
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

    static SettingsButton NavEntry(SettingsPage page, string name) => Chip(page, name);

    static SettingsButton Segment(SettingsPage page, string label) => Chip(page, label);

    /// <summary>The frame around the segment reading <paramref name="label"/> - the one tab stop
    /// the whole segmented control gets, and so the thing the arrow keys are sent to.</summary>
    static SettingsButton Group(SettingsPage page, string label) => Chips(page, label)[0];

    /// <summary>The switch, found by its knob: it is the one affordance on the page whose child
    /// is a shape rather than a word, which is the whole reason it needs a row title to read.</summary>
    static SettingsButton Switch(SettingsPage page) =>
        page.GetVisualDescendants().OfType<SettingsButton>().First(b => b.Child is Border);

    /// <summary>The editable value of the stepper showing <paramref name="unit"/>.</summary>
    static TextBox Stepper(SettingsPage page, string unit) =>
        page.GetVisualDescendants()
            .OfType<TextBox>()
            .First(b => (b.Text ?? "").EndsWith(unit, StringComparison.Ordinal));

    /// <summary>The decrement and increment flanking a stepper's field.</summary>
    static (SettingsButton Down, SettingsButton Up) Steps(TextBox field)
    {
        var row = field.GetVisualAncestors().OfType<StackPanel>().First();
        return ((SettingsButton)row.Children[0], (SettingsButton)row.Children[2]);
    }

    /// <summary>
    /// The innermost affordance holding a label reading <paramref name="text"/>. Descendants
    /// rather than the direct child, because a theme segment holds a swatch beside its label;
    /// innermost, because a segment sits inside the group that frames it and both match.
    /// </summary>
    static SettingsButton Chip(SettingsPage page, string text) => Chips(page, text)[^1];

    static SettingsButton[] Chips(SettingsPage page, string text)
    {
        var matches = page.GetVisualDescendants()
            .OfType<SettingsButton>()
            .Where(b =>
                b.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == text)
            )
            .ToArray();

        Assert.NotEmpty(matches);
        return matches;
    }

    static void Press(InputElement target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

        Dispatcher.UIThread.RunJobs();
    }

    static void Click(Control target, Window window)
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
