using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>The bounds a <see cref="SettingsControls.Number"/> editor steps within, and the
/// precision it shows and rounds to.</summary>
sealed record NumberRange(double Minimum, double Maximum, double Step, int Decimals);

/// <summary>
/// The controls the settings page is built from.
///
/// They are hand-rolled out of <see cref="Border"/> and <see cref="TextBox"/> rather than taken
/// from Fluent, for the reason <see cref="OverlayTheme.StyleTextBox"/> documents: Fluent styles
/// its controls through theme resources rather than properties, so a stock ComboBox or Slider
/// would need a dozen resource keys stuffed into it per state and would still not match the
/// terminal's own look. A pill and a stepper are less code than that, and they theme from the
/// palette like everything else here.
/// </summary>
static class SettingsControls
{
    /// <summary>A group heading within a tab.</summary>
    public static TextBlock SectionHeader(string text, OverlayTheme colors)
    {
        var header = OverlayControls.CreateLabel(text.ToUpperInvariant(), 11, FontWeight.Bold);
        header.Foreground = colors.Accent;
        header.Margin = new Thickness(0, 24, 0, 8);
        header.Opacity = 0.9;
        return header;
    }

    /// <summary>Title and description on the left, the editor on the right.</summary>
    public static Control Row(SettingDescriptor setting, Control editor, OverlayTheme colors)
    {
        var title = OverlayControls.CreateLabel(setting.Title, 13);
        title.Foreground = colors.Foreground;

        var description = OverlayControls.CreateLabel(setting.Description, 11);
        description.Foreground = colors.Dim;
        description.Margin = new Thickness(0, 3, 0, 0);
        description.TextWrapping = TextWrapping.Wrap;

        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(description);

        // A tall editor - the start-directory list - reads better beneath its label than
        // squeezed into a right-hand column.
        if (setting.FullWidth)
        {
            editor.HorizontalAlignment = HorizontalAlignment.Stretch;
            editor.Margin = new Thickness(0, 10, 0, 0);

            var stacked = new StackPanel { Margin = new Thickness(0, 10) };
            stacked.Children.Add(text);
            stacked.Children.Add(editor);
            return stacked;
        }

        text.Margin = new Thickness(0, 0, 24, 0);
        editor.HorizontalAlignment = HorizontalAlignment.Right;
        editor.VerticalAlignment = VerticalAlignment.Top;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 10),
        };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(text);
        grid.Children.Add(editor);
        return grid;
    }

    /// <summary>
    /// A row of pills, one per choice, with the current one filled. Stands in for both a
    /// dropdown and a checkbox: the options are few and naming them all beats hiding them
    /// behind a popup on a page whose whole point is discoverability.
    /// </summary>
    public static Control Choice<T>(
        OverlayTheme colors,
        IReadOnlyList<(T Value, string Label)> options,
        T current,
        Action<T> select
    )
        where T : notnull
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var pills = new List<Border>(options.Count);
        var selected = current;

        foreach (var (value, label) in options)
        {
            var pill = CreatePill(label, value);
            pill.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                Choose(value);
            };

            pills.Add(pill);
            row.Children.Add(pill);
        }

        PaintPills(colors, pills, selected);
        return row;

        void Choose(T chosen)
        {
            if (EqualityComparer<T>.Default.Equals(selected, chosen))
            {
                return;
            }

            // Repainted before the write, because the write may not come back: only a theme
            // change rebuilds the page, and every other setting leaves these pills as they are.
            selected = chosen;
            PaintPills(colors, pills, selected);
            select(chosen);
        }
    }

    /// <summary>
    /// A number with a decrement and an increment beside it. Stepped rather than dragged on
    /// purpose: every commit here rebuilds each pane's renderer, and a slider would do that
    /// once per pixel of travel.
    /// </summary>
    public static Control Number(
        OverlayTheme colors,
        double current,
        NumberRange range,
        Action<double> commit
    )
    {
        var value = OverlayControls.CreateLabel(Format(current, range.Decimals), 13);
        value.Foreground = colors.Foreground;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(CreateStep(colors, "−", () => Nudge(-range.Step)));
        row.Children.Add(CreateDisplay(colors, value));
        row.Children.Add(CreateStep(colors, "+", () => Nudge(range.Step)));
        return row;

        void Nudge(double delta)
        {
            // Floating-point steps drift; rounding to the displayed precision keeps
            // 1.2 + 0.05 from becoming 1.2500000000000002 in the settings file.
            var next = Math.Round(
                Math.Clamp(current + delta, range.Minimum, range.Maximum),
                range.Decimals
            );

            if (Math.Abs(next - current) < double.Epsilon)
            {
                return;
            }

            current = next;
            value.Text = Format(next, range.Decimals);
            commit(next);
        }
    }

    /// <summary>A single-line text setting, written through on every keystroke like the rest
    /// of the page.</summary>
    public static TextBox Text(OverlayTheme colors, string current, Action<string> commit)
    {
        var box = OverlayControls.CreateTextBox(new Thickness(1));
        box.Width = 240;
        box.Text = current;
        colors.StyleTextBox(box);
        box.TextChanged += (_, _) => commit(box.Text ?? "");
        return box;
    }

    static Border CreatePill<T>(string label, T value)
        where T : notnull =>
        new()
        {
            Child = OverlayControls.CreateLabel(label, 12),
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = value,
        };

    static void PaintPills<T>(OverlayTheme colors, List<Border> pills, T selected)
        where T : notnull
    {
        foreach (var pill in pills)
        {
            var isSelected = EqualityComparer<T>.Default.Equals((T)pill.Tag!, selected);
            pill.Background = isSelected ? colors.Selection : Brushes.Transparent;
            pill.BorderBrush = isSelected ? colors.Accent : colors.Dim;

            if (pill.Child is TextBlock label)
            {
                label.Foreground = isSelected ? colors.Foreground : colors.Dim;
            }
        }
    }

    static Border CreateDisplay(OverlayTheme colors, TextBlock value) =>
        new()
        {
            Child = value,
            MinWidth = 78,
            Padding = new Thickness(10, 6),
            Background = colors.Selection,
            BorderBrush = colors.Dim,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

    static Border CreateStep(OverlayTheme colors, string glyph, Action nudge)
    {
        var label = OverlayControls.CreateLabel(glyph, 14);
        label.Foreground = colors.Foreground;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        var button = new Border
        {
            Child = label,
            Width = 30,
            Padding = new Thickness(0, 6),
            Background = Brushes.Transparent,
            BorderBrush = colors.Dim,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        button.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            nudge();
        };

        return button;
    }

    static string Format(double value, int decimals) =>
        value.ToString("F" + decimals, CultureInfo.CurrentCulture);
}
