using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Centaur.App;

public class TabBar : Control
{
    static readonly IBrush activeBg = SolidColorBrush.Parse("#363A4F");
    static readonly IBrush inactiveBg = Brushes.Transparent;
    static readonly IBrush hoverBg = SolidColorBrush.Parse("#2E3248");
    static readonly IBrush activeText = SolidColorBrush.Parse("#CAD3F5");
    static readonly IBrush inactiveText = SolidColorBrush.Parse("#7F849C");
    static readonly IBrush closeBg = SolidColorBrush.Parse("#494D64");
    static readonly IBrush closeHoverBg = SolidColorBrush.Parse("#ED8796");

    readonly DockPanel container;
    readonly StackPanel tabsPanel;
    readonly ScrollViewer scrollViewer;

    public event Action<int>? TabSelected;
    public event Action? NewTabRequested;
    public event Action<int>? TabClosed;
    public event Action<int, string>? TabRenamed;

    public TabBar()
    {
        tabsPanel = new StackPanel { Orientation = Orientation.Horizontal };

        scrollViewer = new ScrollViewer
        {
            Content = tabsPanel,
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
            var isActive = tab.Id == activeId;
            var tabButton = CreateTabButton(tab, isActive);
            tabsPanel.Children.Add(tabButton);
        }
    }

    Panel CreateTabButton(TabItem tab, bool isActive)
    {
        var panel = new Panel
        {
            Height = 28,
            MinWidth = 100,
            MaxWidth = 200,
            Background = isActive ? activeBg : inactiveBg,
        };

        var label = new TextBlock
        {
            Text = tab.Title,
            FontSize = 12,
            Foreground = isActive ? activeText : inactiveText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 0, 20, 0),
            IsHitTestVisible = false,
        };
        panel.Children.Add(label);

        var closeButton = new Button
        {
            Content = "\u00d7",
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
            Foreground = inactiveText,
            Opacity = 0,
        };
        panel.Children.Add(closeButton);

        var tabId = tab.Id;

        var renameBox = new TextBox
        {
            FontSize = 12,
            Height = 22,
            MinWidth = 80,
            MaxWidth = 180,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 0, 20, 0),
            Background = SolidColorBrush.Parse("#1E2030"),
            Foreground = activeText,
            CaretBrush = activeText,
            BorderBrush = SolidColorBrush.Parse("#494D64"),
            BorderThickness = new Thickness(1),
            IsVisible = false,
            IsHitTestVisible = true,
        };
        panel.Children.Add(renameBox);

        void CommitRename()
        {
            if (!renameBox.IsVisible)
            {
                return;
            }

            var newName = renameBox.Text?.Trim();
            renameBox.IsVisible = false;
            label.IsVisible = true;
            if (!string.IsNullOrEmpty(newName))
            {
                TabRenamed?.Invoke(tabId, newName);
            }
        }

        void StartRename()
        {
            renameBox.Text = tab.Title;
            label.IsVisible = false;
            renameBox.IsVisible = true;
            renameBox.Focus();
            renameBox.SelectAll();
        }

        renameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitRename();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                renameBox.IsVisible = false;
                label.IsVisible = true;
                e.Handled = true;
            }
        };

        renameBox.LostFocus += (_, _) => CommitRename();

        var renameMenuItem = new MenuItem { Header = "Rename Tab" };
        renameMenuItem.Click += (_, _) => StartRename();

        var closeMenuItem = new MenuItem { Header = "Close Tab" };
        closeMenuItem.Click += (_, _) => TabClosed?.Invoke(tabId);

        panel.ContextMenu = new ContextMenu { Items = { renameMenuItem, closeMenuItem } };

        panel.PointerEntered += (_, _) =>
        {
            if (!isActive)
            {
                panel.Background = hoverBg;
            }
            closeButton.Opacity = 1;
        };
        panel.PointerExited += (_, _) =>
        {
            if (!isActive)
            {
                panel.Background = inactiveBg;
            }
            closeButton.Opacity = 0;
        };

        panel.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed)
            {
                TabSelected?.Invoke(tabId);
                e.Handled = true;
            }
        };

        closeButton.PointerEntered += (_, _) =>
        {
            closeButton.Background = closeHoverBg;
            closeButton.Foreground = SolidColorBrush.Parse("#24273A");
        };
        closeButton.PointerExited += (_, _) =>
        {
            closeButton.Background = Brushes.Transparent;
            closeButton.Foreground = inactiveText;
        };

        closeButton.Click += (_, e) =>
        {
            TabClosed?.Invoke(tabId);
            e.Handled = true;
        };

        return panel;
    }

    Button CreateAddButton()
    {
        var button = new Button
        {
            Content = "+",
            FontSize = 14,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = inactiveText,
        };

        button.PointerEntered += (_, _) =>
        {
            button.Background = hoverBg;
            button.Foreground = activeText;
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.Foreground = inactiveText;
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
