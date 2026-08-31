using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Centaur.App;

public class TabBar : Control
{
    readonly DockPanel container;
    readonly StackPanel tabsPanel;
    readonly ScrollViewer scrollViewer;
    readonly TabDragController drag;

    public event Action<int>? TabSelected;
    public event Action? NewTabRequested;
    public event Action<int>? TabClosed;
    public event Action<int, string>? TabRenamed;
    public event Action<int, int>? TabMoved;

    public TabBar()
    {
        tabsPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var tabsOverlay = new Panel();
        tabsOverlay.Children.Add(tabsPanel);

        drag = new TabDragController(tabsPanel, tabsOverlay);
        drag.TabPressed += id => TabSelected?.Invoke(id);
        drag.TabMoved += (id, index) => TabMoved?.Invoke(id, index);

        scrollViewer = new ScrollViewer
        {
            Content = tabsOverlay,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        scrollViewer.PointerWheelChanged += (_, e) =>
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X - e.Delta.Y * 50, 0);
            e.Handled = true;
        };

        var addButton = CreateAddButton();
        DockPanel.SetDock(addButton, Dock.Right);

        container = new DockPanel { LastChildFill = true };
        container.Children.Add(addButton);
        container.Children.Add(scrollViewer);

        VisualChildren.Add(container);
        LogicalChildren.Add(container);
    }

    public void Update(IReadOnlyList<TabItem> tabs, int activeId)
    {
        IsVisible = tabs.Count > 1;

        tabsPanel.Children.Clear();

        foreach (var tab in tabs)
        {
            tabsPanel.Children.Add(CreateTabButton(tab, tab.Id == activeId));
        }
    }

    Panel CreateTabButton(TabItem tab, bool isActive)
    {
        var panel = new Panel
        {
            Height = 28,
            MinWidth = 100,
            MaxWidth = 200,
            VerticalAlignment = VerticalAlignment.Center,
            Background = isActive ? TabColors.activeBg : TabColors.inactiveBg,
        };

        var label = new TextBlock
        {
            Text = tab.Title,
            FontSize = 12,
            Foreground = isActive ? TabColors.activeText : TabColors.inactiveText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 0, 20, 0),
            IsHitTestVisible = false,
        };
        panel.Children.Add(label);

        var tabId = tab.Id;
        var closeButton = CreateCloseButton(tabId);
        panel.Children.Add(closeButton);

        panel.ContextMenu = CreateContextMenu(tabId, AttachRename(panel, label, tab, closeButton));

        AttachHover(panel, closeButton, isActive);
        drag.Attach(panel, tabId);

        return panel;
    }

    Button CreateCloseButton(int tabId)
    {
        var closeButton = new Button
        {
            Content = "×",
            FontSize = 14,
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TabColors.inactiveText,
            Opacity = 0,
        };

        closeButton.PointerEntered += (_, _) =>
        {
            closeButton.Background = TabColors.closeHoverBg;
            closeButton.Foreground = TabColors.closeHoverText;
        };
        closeButton.PointerExited += (_, _) =>
        {
            closeButton.Background = Brushes.Transparent;
            closeButton.Foreground = TabColors.inactiveText;
        };

        closeButton.Click += (_, e) =>
        {
            TabClosed?.Invoke(tabId);
            e.Handled = true;
        };

        return closeButton;
    }

    ContextMenu CreateContextMenu(int tabId, TabRenameEditor rename)
    {
        var renameMenuItem = new MenuItem { Header = "Rename Tab" };
        renameMenuItem.Click += (_, _) => rename.Start();

        var closeMenuItem = new MenuItem { Header = "Close Tab" };
        closeMenuItem.Click += (_, _) => TabClosed?.Invoke(tabId);

        return new ContextMenu { Items = { renameMenuItem, closeMenuItem } };
    }

    /// <summary>
    /// The tab's inline editor and the double-click that opens it. The close button steps
    /// aside while editing so the box gets the whole tab.
    ///
    /// The press handler goes on ahead of the drag controller, which handles every left
    /// press: a handled press stops the handlers behind it, so the second click of a
    /// double-click edits instead of starting a drag.
    /// </summary>
    TabRenameEditor AttachRename(Panel panel, TextBlock label, TabItem tab, Button closeButton)
    {
        var tabId = tab.Id;
        var rename = new TabRenameEditor(
            panel,
            label,
            tab.Title,
            newName => TabRenamed?.Invoke(tabId, newName)
        );
        rename.EditingChanged += editing => closeButton.IsVisible = !editing;

        panel.PointerPressed += (_, e) =>
        {
            if (e.ClickCount == 2 && e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed)
            {
                rename.Start();
                e.Handled = true;
            }
        };

        return rename;
    }

    /// <summary>Lights the tab up under the pointer and reveals its close button. The active
    /// tab keeps its own background, so only the close button reacts there.</summary>
    static void AttachHover(Panel panel, Button closeButton, bool isActive)
    {
        panel.PointerEntered += (_, _) =>
        {
            if (!isActive)
            {
                panel.Background = TabColors.hoverBg;
            }
            closeButton.Opacity = 1;
        };
        panel.PointerExited += (_, _) =>
        {
            if (!isActive)
            {
                panel.Background = TabColors.inactiveBg;
            }
            closeButton.Opacity = 0;
        };
    }

    Button CreateAddButton()
    {
        var button = new Button
        {
            Content = "+",
            FontSize = 14,
            Width = 28,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TabColors.inactiveText,
        };

        button.PointerEntered += (_, _) =>
        {
            button.Background = TabColors.hoverBg;
            button.Foreground = TabColors.activeText;
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.Foreground = TabColors.inactiveText;
        };

        button.Click += (_, _) => NewTabRequested?.Invoke();

        return button;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        container.Measure(availableSize);
        return new Size(availableSize.Width, container.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        container.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
