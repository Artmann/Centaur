using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The settings page: a full-width surface over the terminal area, with a sidebar of tabs, a
/// search box that spans them, and a row per setting.
///
/// It is a page rather than the modal card it replaces because it belongs to the window, not to
/// a pane. The old overlay was built inside every <see cref="TerminalControl"/>, so a split
/// window had one settings card per pane, each covering half the screen.
///
/// The layout is the one desktop settings pages have settled on - a full-height sidebar holding
/// the way out, the search and the tabs, and a scrolling column of grouped cards beside it - so
/// that it reads as an application surface rather than as terminal output.
/// </summary>
sealed class SettingsPage : UserControl
{
    readonly TerminalServices services;
    readonly SettingsNav nav;
    readonly TextBox searchBox;
    readonly StackPanel content = new();
    readonly TextBlock title;
    readonly TextBlock backLabel;
    readonly TextBlock backArrow;
    readonly TextBlock hint;
    readonly SettingsButton backButton = new();
    readonly Border sidebar;

    OverlayTheme colors;

    public SettingsPage(TerminalServices services)
    {
        this.services = services;
        colors = new OverlayTheme(services.Theme);

        title = OverlayControls.CreateUiLabel("General", 20, FontWeight.SemiBold);
        title.Margin = new Thickness(0, 0, 0, 20);

        backArrow = OverlayControls.CreateUiLabel("←", 13);
        backLabel = OverlayControls.CreateUiLabel("Back", 13);
        hint = OverlayControls.CreateUiLabel("Esc", 11);

        searchBox = CreateSearchBox();
        SettingsControls.FocusOutline(searchBox, () => colors);

        nav = new SettingsNav(colors);
        nav.TabSelected += _ => Rebuild();

        sidebar = new Border
        {
            Child = BuildSidebar(),
            Width = 200,
            Padding = new Thickness(10, 12),
            BorderThickness = new Thickness(0, 0, 1, 0),
        };

        Content = BuildLayout();
        Focusable = true;
        IsVisible = false;
        ApplyTheme();

        // Only a theme change alters what an already-built row looks like; every other setting
        // is written through by an editor that repaints itself, and rebuilding on those would
        // pull the search box out from under the user mid-word.
        services.Settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised when the page wants to be dismissed - Escape, or the close affordance.</summary>
    public event Action? CloseRequested;

    public bool IsOpen => IsVisible;

    public void Show()
    {
        searchBox.Text = "";
        Rebuild();
        IsVisible = true;

        // The box has only just been made visible; focusing it synchronously does not stick.
        Dispatcher.UIThread.Post(() => searchBox.Focus(), DispatcherPriority.Input);
    }

    public void Hide() => IsVisible = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>The search field, in the shell's own typeface: it takes prose, not commands.</summary>
    TextBox CreateSearchBox()
    {
        var box = OverlayControls.CreateTextBox(new Thickness(1));
        box.Watermark = "Search settings";
        box.FontFamily = OverlayControls.UiFont;
        box.FontSize = 12;
        box.Padding = new Thickness(8, 5);
        box.CornerRadius = new CornerRadius(6);
        box.Margin = new Thickness(0, 10, 0, 14);
        box.TextChanged += (_, _) => Rebuild();
        return box;
    }

    /// <summary>The way out, the search and the tabs, stacked down the left the way every
    /// settings page puts them.</summary>
    StackPanel BuildSidebar()
    {
        var back = new DockPanel { Margin = new Thickness(2, 0) };
        var arrow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        arrow.Children.Add(backArrow);
        arrow.Children.Add(backLabel);

        hint.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hint, Dock.Right);
        back.Children.Add(hint);
        back.Children.Add(arrow);

        backButton.Child = back;
        backButton.Padding = new Thickness(6, 6);
        backButton.CornerRadius = new CornerRadius(6);
        backButton.Activated += () => CloseRequested?.Invoke();

        var panel = new StackPanel();
        panel.Children.Add(backButton);
        panel.Children.Add(searchBox);
        panel.Children.Add(nav.View);
        return panel;
    }

    DockPanel BuildLayout()
    {
        // Capped rather than left to fill: a row whose description runs the full width of a
        // maximised window is a paragraph, not a caption.
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        column.Children.Add(title);
        column.Children.Add(content);

        var host = new Border { Child = column, Padding = new Thickness(32, 26, 32, 36) };

        // Measured rather than expressed as Stretch plus MaxWidth, which centres the column on a
        // wide screen: the page's content would drift away from the sidebar it belongs to, and
        // the heading would stop lining up with anything. Sizing the column explicitly holds it
        // against the left edge and still stops it growing into a paragraph.
        host.SizeChanged += (_, e) => column.Width = Math.Clamp(e.NewSize.Width - 64, 0, 720);

        var scroller = new ScrollViewer
        {
            Content = host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var layout = new DockPanel();
        DockPanel.SetDock(sidebar, Dock.Left);
        layout.Children.Add(sidebar);
        layout.Children.Add(scroller);
        return layout;
    }

    void OnSettingsChanged(string id)
    {
        if (id != SettingIds.Theme)
        {
            return;
        }

        // Read now, while the control the user is standing on still exists. A theme picked from
        // the keyboard used to leave focus nowhere: the rebuild threw the picker away and the
        // next arrow key walked into the window's caption buttons.
        var focused = FocusedButton();

        // Posted rather than run inline: the change arrives from inside a segment's own pointer
        // handler, and that segment is about to be replaced by the rebuild.
        Dispatcher.UIThread.Post(() =>
        {
            colors = new OverlayTheme(services.Theme);
            nav.ApplyTheme(colors);
            ApplyTheme();
            Rebuild();

            if (focused is not null)
            {
                // Put back the way it was found, so a theme clicked with the mouse does not come
                // back wearing a focus ring the user never asked for.
                FocusSetting(
                    id,
                    focused.Ringed ? NavigationMethod.Directional : NavigationMethod.Pointer
                );
            }
        });
    }

    /// <summary>The page's own affordance holding focus, if one is.</summary>
    SettingsButton? FocusedButton() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as SettingsButton;

    /// <summary>Puts the keyboard back on a setting's control after the page rebuilt under it.</summary>
    void FocusSetting(string id, NavigationMethod method)
    {
        var row = content
            .GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(candidate => (candidate.Tag as string) == id);

        row?.GetVisualDescendants()
            .OfType<SettingsButton>()
            .FirstOrDefault(button => button.Focusable)
            ?.Focus(method);
    }

    void ApplyTheme()
    {
        Background = colors.Surface;
        title.Foreground = colors.Foreground;
        backArrow.Foreground = colors.Dim;
        backLabel.Foreground = colors.Foreground;
        hint.Foreground = colors.Dim;
        sidebar.Background = colors.Card;
        sidebar.BorderBrush = colors.Hairline;
        backButton.FocusBrush = colors.Accent;
        backButton.SetFill(Brushes.Transparent, colors.Hover, colors.Press);
        SettingsControls.StyleBox(colors, searchBox);
    }

    /// <summary>
    /// Rebuilds the rows from the registry. Cheap enough to do wholesale on every keystroke -
    /// there are a dozen settings, and rebuilding avoids each editor having to know how to
    /// re-read a value it may not own.
    /// </summary>
    void Rebuild()
    {
        var query = searchBox.Text ?? "";
        var searching = query.Trim().Length > 0;
        nav.SetSearching(searching);
        title.Text = searching ? "Search results" : nav.Selected.Label();

        // A query searches every tab. Restricting it to the open one would hide the match the
        // user is looking for behind a tab they have no reason to suspect.
        var source = searching ? SettingsRegistry.All : SettingsRegistry.ForTab(nav.Selected);
        var results = SettingsSearch.Filter(source, query);

        content.Children.Clear();

        if (results.Count == 0)
        {
            content.Children.Add(EmptyMessage(query.Trim()));
            return;
        }

        var context = new SettingsContext
        {
            Settings = services.Settings,
            Themes = services.Themes,
            Colors = colors,
        };

        foreach (var group in results.GroupBy(setting => (setting.Tab, setting.Section)))
        {
            RenderGroup(group, context, searching);
        }
    }

    /// <summary>One section heading and the card of rows beneath it.</summary>
    void RenderGroup(
        IGrouping<(SettingsTab Tab, string Section), SettingDescriptor> group,
        SettingsContext context,
        bool searching
    )
    {
        var rows = group
            .Select(setting =>
                (Control)SettingsControls.Row(setting, setting.CreateEditor(context), colors)
            )
            .ToArray();

        // A heading above a lone row says the row's own title twice - "Theme" over "Theme". While
        // searching it stays, because there it carries the tab the match came from, which nothing
        // else on the row does.
        if (searching)
        {
            var heading = $"{group.Key.Tab.Label()} · {group.Key.Section}";
            content.Children.Add(SettingsControls.SectionHeader(heading, colors));
        }
        else if (rows.Length > 1)
        {
            content.Children.Add(SettingsControls.SectionHeader(group.Key.Section, colors));
        }

        content.Children.Add(SettingsControls.Card(rows, colors));
    }

    /// <summary>
    /// What a query that found nothing offers instead. A dead end here is a dead end on the whole
    /// page: the search spans every tab, so there is nowhere else for the user to go looking.
    /// The sections are the shortest honest list of what the page does hold.
    /// </summary>
    StackPanel EmptyMessage(string query)
    {
        var empty = OverlayControls.CreateUiLabel($"No settings match “{query}”.", 13);
        empty.Foreground = colors.Foreground;

        var lead = OverlayControls.CreateUiLabel("The page covers:", 12);
        lead.Foreground = colors.Dim;
        lead.Margin = new Thickness(0, 14, 0, 8);

        var sections = new WrapPanel();
        foreach (var section in SettingsRegistry.All.Select(s => s.Section).Distinct())
        {
            var chip = SettingsControls.Suggestion(colors, section, () => Search(section));

            // Spaced by the chips themselves: WrapPanel has no gap of its own, and a Spacing on
            // the panel would not separate the wrapped lines from each other anyway.
            chip.Margin = new Thickness(0, 0, 8, 8);
            sections.Children.Add(chip);
        }

        var panel = new StackPanel { Margin = new Thickness(2, 4, 0, 0) };
        panel.Children.Add(empty);
        panel.Children.Add(lead);
        panel.Children.Add(sections);
        return panel;
    }

    /// <summary>Puts a term in the search box, as if the user had typed it.</summary>
    void Search(string query)
    {
        searchBox.Text = query;
        searchBox.CaretIndex = query.Length;
        searchBox.Focus();
    }
}
