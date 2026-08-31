using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The settings page: a full-width surface over the terminal area, with a sidebar of tabs, a
/// search box that spans them, and a row per setting.
///
/// It is a page rather than the modal card it replaces because it belongs to the window, not to
/// a pane. The old overlay was built inside every <see cref="TerminalControl"/>, so a split
/// window had one settings card per pane, each covering half the screen.
/// </summary>
sealed class SettingsPage : UserControl
{
    readonly TerminalServices services;
    readonly SettingsNav nav;
    readonly TextBox searchBox;
    readonly StackPanel content = new();
    readonly TextBlock title;
    readonly TextBlock hint;
    readonly Border sidebar;

    OverlayTheme colors;

    public SettingsPage(TerminalServices services)
    {
        this.services = services;
        colors = new OverlayTheme(services.Theme);

        title = OverlayControls.CreateLabel("Settings", 20, FontWeight.Bold);
        hint = OverlayControls.CreateLabel("Esc to close", 11);
        hint.VerticalAlignment = VerticalAlignment.Center;

        searchBox = OverlayControls.CreateTextBox(new Thickness(0, 0, 0, 1));
        searchBox.Watermark = "Search settings";
        searchBox.Width = 260;
        searchBox.Margin = new Thickness(24, 0);
        searchBox.HorizontalAlignment = HorizontalAlignment.Left;
        searchBox.TextChanged += (_, _) => Rebuild();

        nav = new SettingsNav(colors);
        nav.TabSelected += _ => Rebuild();

        sidebar = new Border
        {
            Child = nav.View,
            Width = 180,
            Padding = new Thickness(12, 8),
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

    DockPanel BuildLayout()
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(24, 20, 24, 16),
        };
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        header.Children.Add(searchBox);
        header.Children.Add(hint);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var scroller = new ScrollViewer
        {
            Content = new Border { Child = content, Padding = new Thickness(28, 4, 28, 32) },
            HorizontalScrollBarVisibility = Avalonia
                .Controls
                .Primitives
                .ScrollBarVisibility
                .Disabled,
        };
        Grid.SetColumn(scroller, 1);
        body.Children.Add(sidebar);
        body.Children.Add(scroller);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(body);
        return layout;
    }

    void OnSettingsChanged(string id)
    {
        if (id != SettingIds.Theme)
        {
            return;
        }

        // Posted rather than run inline: the change arrives from inside a pill's own pointer
        // handler, and that pill is about to be replaced by the rebuild.
        Dispatcher.UIThread.Post(() =>
        {
            colors = new OverlayTheme(services.Theme);
            nav.ApplyTheme(colors);
            ApplyTheme();
            Rebuild();
        });
    }

    void ApplyTheme()
    {
        Background = colors.Surface;
        title.Foreground = colors.Foreground;
        hint.Foreground = colors.Dim;
        sidebar.BorderBrush = colors.Dim;
        colors.StyleTextBox(searchBox);
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

    /// <summary>One section heading and the rows beneath it.</summary>
    void RenderGroup(
        IGrouping<(SettingsTab Tab, string Section), SettingDescriptor> group,
        SettingsContext context,
        bool searching
    )
    {
        // While searching the results span both tabs, so the heading has to say which one each
        // group came from.
        var heading = searching ? $"{group.Key.Tab} · {group.Key.Section}" : group.Key.Section;
        content.Children.Add(SettingsControls.SectionHeader(heading, colors));

        foreach (var setting in group)
        {
            content.Children.Add(
                SettingsControls.Row(setting, setting.CreateEditor(context), colors)
            );
        }
    }

    TextBlock EmptyMessage(string query)
    {
        var empty = OverlayControls.CreateLabel($"No settings match “{query}”.", 13);
        empty.Foreground = colors.Dim;
        empty.Margin = new Thickness(0, 24, 0, 0);
        return empty;
    }
}
