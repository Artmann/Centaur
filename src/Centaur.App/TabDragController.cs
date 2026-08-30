using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>
/// Drag-to-reorder for the tab strip: tracks the pointer once it has moved far enough to count
/// as a drag, drags the tab panel along with it, and shows where it would land. Owns the drop
/// indicator it adds to the overlay above the tabs.
/// </summary>
sealed class TabDragController
{
    const double dragThreshold = 5;

    readonly StackPanel tabsPanel;

    readonly Border indicator = new()
    {
        Width = 2,
        Height = 20,
        Background = TabColors.dropIndicator,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
        IsVisible = false,
        IsHitTestVisible = false,
    };

    int draggingTabId = -1;
    Point dragStartPoint;
    bool isDragging;

    public TabDragController(StackPanel tabsPanel, Panel overlay)
    {
        this.tabsPanel = tabsPanel;
        overlay.Children.Add(indicator);
    }

    /// <summary>Raised on left-press, before the drag itself starts.</summary>
    public event Action<int>? TabPressed;

    /// <summary>Raised on drop, with the tab's id and the index it landed on.</summary>
    public event Action<int, int>? TabMoved;

    public void Attach(Panel panel, int tabId)
    {
        panel.PointerPressed += (_, e) => OnPressed(panel, tabId, e);
        panel.PointerMoved += (_, e) => OnMoved(panel, tabId, e);
        panel.PointerReleased += (_, e) => OnReleased(panel, tabId, e);
    }

    void OnPressed(Panel panel, int tabId, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TabPressed?.Invoke(tabId);
        draggingTabId = tabId;
        dragStartPoint = e.GetPosition(tabsPanel);
        isDragging = false;
        e.Pointer.Capture(panel);
        e.Handled = true;
    }

    void OnMoved(Panel panel, int tabId, PointerEventArgs e)
    {
        if (draggingTabId != tabId || e.Pointer.Captured != panel)
        {
            return;
        }

        var currentPos = e.GetPosition(tabsPanel);
        var delta = currentPos - dragStartPoint;

        if (!isDragging && Math.Abs(delta.X) > dragThreshold)
        {
            isDragging = true;
        }

        if (isDragging)
        {
            panel.RenderTransform = new TranslateTransform(delta.X, 0);
            panel.Opacity = 0.7;
            panel.ZIndex = 100;
            UpdateIndicator(currentPos.X, panel);
        }
    }

    void OnReleased(Panel panel, int tabId, PointerReleasedEventArgs e)
    {
        if (draggingTabId != tabId)
        {
            return;
        }

        e.Pointer.Capture(null);

        if (isDragging)
        {
            var newIndex = DropIndex(e.GetPosition(tabsPanel).X, panel);
            panel.RenderTransform = null;
            panel.Opacity = 1;
            panel.ZIndex = 0;
            indicator.IsVisible = false;
            TabMoved?.Invoke(tabId, newIndex);
        }

        draggingTabId = -1;
        isDragging = false;
    }

    /// <summary>The index the dragged tab would take, counting the tabs it is dropped past.</summary>
    int DropIndex(double pointerX, Panel dragged)
    {
        for (var i = 0; i < tabsPanel.Children.Count; i++)
        {
            var child = tabsPanel.Children[i];
            if (child == dragged)
            {
                continue;
            }

            var bounds = child.Bounds;
            if (pointerX < bounds.X + bounds.Width / 2)
            {
                return i;
            }
        }

        return tabsPanel.Children.Count - 1;
    }

    void UpdateIndicator(double pointerX, Panel dragged)
    {
        var dropIndex = DropIndex(pointerX, dragged);

        // Nothing to show while the tab is over its own slot.
        if (dropIndex == tabsPanel.Children.IndexOf(dragged))
        {
            indicator.IsVisible = false;
            return;
        }

        indicator.RenderTransform = new TranslateTransform(IndicatorX(dropIndex, dragged) - 1, 0);
        indicator.IsVisible = true;
    }

    /// <summary>Left edge of the gap the tab would drop into.</summary>
    double IndicatorX(int dropIndex, Panel dragged)
    {
        if (dropIndex <= 0)
        {
            return 0;
        }

        var insertBefore = ChildAt(dropIndex, dragged);
        return insertBefore >= 0 ? tabsPanel.Children[insertBefore].Bounds.X : LastEdge(dragged);
    }

    /// <summary>Index of the <paramref name="position"/>th child that isn't the dragged tab,
    /// or -1 when the drop lands past the last one.</summary>
    int ChildAt(int position, Panel dragged)
    {
        var seen = 0;
        for (var i = 0; i < tabsPanel.Children.Count; i++)
        {
            if (tabsPanel.Children[i] == dragged)
            {
                continue;
            }

            if (seen == position)
            {
                return i;
            }

            seen++;
        }

        return -1;
    }

    double LastEdge(Panel dragged)
    {
        var lastChild = tabsPanel.Children[^1];
        if (lastChild == dragged && tabsPanel.Children.Count > 1)
        {
            lastChild = tabsPanel.Children[^2];
        }

        return lastChild.Bounds.Right;
    }
}
