using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Centaur.App;

/// <summary>
/// The in-place rename box for one tab: swaps itself for the tab's label while editing, and
/// reports the new name on Enter or focus loss. Escape leaves the name alone.
/// </summary>
sealed class TabRenameEditor
{
    readonly TextBlock label;
    readonly string title;
    readonly Action<string> renamed;

    readonly TextBox editor = new()
    {
        FontSize = 12,
        Height = 22,
        MinHeight = 0,
        MinWidth = 80,
        MaxWidth = 180,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(6, 0),
        Padding = new Thickness(4, 0),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        IsVisible = false,
        IsHitTestVisible = true,
    };

    /// <summary>Raised as editing starts and ends, so the tab can get its close button out
    /// of the way and hand the whole chip over to the box.</summary>
    public event Action<bool>? EditingChanged;

    public TabRenameEditor(Panel panel, TextBlock label, string title, Action<string> renamed)
    {
        this.label = label;
        this.title = title;
        this.renamed = renamed;

        // Fluent paints the box through theme resources rather than properties, and with no
        // theme variant set those resolve light — hence the white fill this fixes.
        OverlayTheme.StyleTextBox(
            editor,
            TabColors.editorBg,
            TabColors.activeText,
            TabColors.editorBorder,
            TabColors.activeText
        );
        editor.SelectionBrush = TabColors.editorSelection;
        editor.SelectionForegroundBrush = TabColors.activeText;
        editor.FocusAdorner = null;

        panel.Children.Add(editor);
        editor.KeyDown += (_, e) => OnKeyDown(e);
        editor.LostFocus += (_, _) => Commit();
    }

    public void Start()
    {
        editor.Text = title;
        label.IsVisible = false;
        editor.IsVisible = true;
        editor.Focus();
        editor.SelectAll();
        EditingChanged?.Invoke(true);
    }

    void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    void Commit()
    {
        if (!editor.IsVisible)
        {
            return;
        }

        var newName = editor.Text?.Trim();
        Cancel();
        if (!string.IsNullOrEmpty(newName))
        {
            renamed(newName);
        }
    }

    void Cancel()
    {
        editor.IsVisible = false;
        label.IsVisible = true;
        EditingChanged?.Invoke(false);
    }
}
