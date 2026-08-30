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
        MinWidth = 80,
        MaxWidth = 180,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8, 0, 20, 0),
        Background = TabColors.editorBg,
        Foreground = TabColors.activeText,
        CaretBrush = TabColors.activeText,
        BorderBrush = TabColors.editorBorder,
        BorderThickness = new Thickness(1),
        IsVisible = false,
        IsHitTestVisible = true,
    };

    public TabRenameEditor(Panel panel, TextBlock label, string title, Action<string> renamed)
    {
        this.label = label;
        this.title = title;
        this.renamed = renamed;

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
    }
}
