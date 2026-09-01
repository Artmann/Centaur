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
/// its controls through theme resources rather than properties, so a stock ComboBox, Slider or
/// ToggleSwitch would need a dozen resource keys stuffed into it per state and would still not
/// theme from the terminal's palette. A segment, a stepper and a switch are less code than that.
///
/// The shape they make is the one every desktop settings page makes: a dim label naming a
/// section, then a rounded card holding that section's rows, each row a title and a description
/// on the left and a small control on the right.
/// </summary>
static class SettingsControls
{
    /// <summary>
    /// Marks a section heading. The tests find headings by this rather than by font metrics,
    /// which any restyle moves.
    /// </summary>
    public const string SectionHeaderTag = "settings-section-header";

    /// <summary>The dim label naming the card beneath it.</summary>
    public static TextBlock SectionHeader(string text, OverlayTheme colors)
    {
        var header = OverlayControls.CreateUiLabel(text, 12);
        header.Foreground = colors.Dim;
        header.Margin = new Thickness(2, 2, 0, 8);
        header.Tag = SectionHeaderTag;
        return header;
    }

    /// <summary>Groups one section's rows into a single card, ruled between the rows.</summary>
    public static Control Card(IReadOnlyList<Control> rows, OverlayTheme colors)
    {
        var stack = new StackPanel();

        foreach (var row in rows)
        {
            // The rule between rows is drawn as the row's own top border, so the card needs no
            // separator elements interleaved with its children.
            if (stack.Children.Count > 0 && row is Border bordered)
            {
                bordered.BorderBrush = colors.Hairline;
                bordered.BorderThickness = new Thickness(0, 1, 0, 0);
            }

            stack.Children.Add(row);
        }

        return new Border
        {
            Child = stack,
            Background = colors.Card,
            BorderBrush = colors.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 20),
        };
    }

    /// <summary>Title and description on the left, the editor on the right.</summary>
    public static Border Row(SettingDescriptor setting, Control editor, OverlayTheme colors)
    {
        var title = OverlayControls.CreateUiLabel(setting.Title, 13);
        title.Foreground = colors.Foreground;

        var description = OverlayControls.CreateUiLabel(setting.Description, 12);
        description.Foreground = colors.Dim;
        description.Margin = new Thickness(0, 2, 0, 0);
        description.TextWrapping = TextWrapping.Wrap;

        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(description);

        return new Border
        {
            Padding = new Thickness(14, 11),
            Child = setting.FullWidth ? Stacked(text, editor) : SideBySide(text, editor),
        };
    }

    /// <summary>
    /// A segmented control, one segment per choice, with the current one filled. Stands in for
    /// both a dropdown and a checkbox: the options are few and naming them all beats hiding them
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
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var segments = new List<Border>(options.Count);
        var selected = current;

        foreach (var (value, label) in options)
        {
            var segment = CreateSegment(label, value);
            segment.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                Choose(value);
            };

            segments.Add(segment);
            row.Children.Add(segment);
        }

        PaintSegments(colors, segments, selected);
        return Frame(colors, row);

        void Choose(T chosen)
        {
            if (EqualityComparer<T>.Default.Equals(selected, chosen))
            {
                return;
            }

            // Repainted before the write, because the write may not come back: only a theme
            // change rebuilds the page, and every other setting leaves these as they are.
            selected = chosen;
            PaintSegments(colors, segments, selected);
            select(chosen);
        }
    }

    /// <summary>An on/off switch, for the settings where naming both states would say less than
    /// the switch itself does.</summary>
    public static Control Toggle(OverlayTheme colors, bool current, Action<bool> commit)
    {
        var knob = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var track = new Border
        {
            Child = knob,
            Width = 38,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        track.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            current = !current;
            PaintToggle(colors, track, knob, current);
            commit(current);
        };

        PaintToggle(colors, track, knob, current);
        return track;
    }

    /// <summary>The switch's two states: filled with the knob to the right when on, outlined
    /// with the knob to the left when off.</summary>
    static void PaintToggle(OverlayTheme colors, Border track, Border knob, bool on)
    {
        track.Background = on ? colors.Accent : Brushes.Transparent;
        track.BorderBrush = on ? colors.Accent : colors.Dim;
        knob.Background = on ? colors.Surface : colors.Dim;
        knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
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
        var value = OverlayControls.CreateUiLabel(Format(current, range.Decimals), 12);
        value.Foreground = colors.Foreground;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CreateStep(colors, "−", () => Nudge(-range.Step)));
        row.Children.Add(CreateDisplay(colors, value));
        row.Children.Add(CreateStep(colors, "+", () => Nudge(range.Step)));
        return Frame(colors, row);

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
    /// of the page. Stays monospace: it holds a command, not prose.</summary>
    public static TextBox Text(OverlayTheme colors, string current, Action<string> commit)
    {
        var box = OverlayControls.CreateTextBox(new Thickness(1));
        box.Width = 220;
        box.FontSize = 12;
        box.Padding = new Thickness(8, 4);
        box.CornerRadius = new CornerRadius(6);
        box.Text = current;
        StyleBox(colors, box);
        box.TextChanged += (_, _) => commit(box.Text ?? "");
        return box;
    }

    /// <summary>
    /// Paints a box to match the page. The overlay styling it builds on makes every border state
    /// transparent, because an overlay draws its own frame around the box; here the box is the
    /// frame, so the state resources are painted back in - without them Fluent's focused state
    /// wins and the outline vanishes the moment the box is clicked into.
    /// </summary>
    public static void StyleBox(OverlayTheme colors, TextBox box)
    {
        colors.StyleTextBox(box);
        OverlayTheme.StyleTextBox(
            box,
            Brushes.Transparent,
            colors.Foreground,
            colors.Hairline,
            colors.Accent
        );
    }

    /// <summary>The outline the segmented control and the stepper share, so a row holding one
    /// of each reads as one family of control.</summary>
    static Border Frame(OverlayTheme colors, Control child) =>
        new()
        {
            Child = child,
            Background = Brushes.Transparent,
            BorderBrush = colors.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
        };

    static StackPanel Stacked(Control text, Control editor)
    {
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.Margin = new Thickness(0, 10, 0, 0);

        var stacked = new StackPanel();
        stacked.Children.Add(text);
        stacked.Children.Add(editor);
        return stacked;
    }

    static Grid SideBySide(Control text, Control editor)
    {
        text.Margin = new Thickness(0, 0, 20, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        editor.HorizontalAlignment = HorizontalAlignment.Right;
        editor.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(text);
        grid.Children.Add(editor);
        return grid;
    }

    static Border CreateSegment<T>(string label, T value)
        where T : notnull =>
        new()
        {
            Child = OverlayControls.CreateUiLabel(label, 12),
            Padding = new Thickness(10, 3),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = value,
        };

    static void PaintSegments<T>(OverlayTheme colors, List<Border> segments, T selected)
        where T : notnull
    {
        foreach (var segment in segments)
        {
            var isSelected = EqualityComparer<T>.Default.Equals((T)segment.Tag!, selected);
            segment.Background = isSelected ? colors.Selection : Brushes.Transparent;

            // Outlined as well as filled: on a light palette the selection tint alone is barely
            // a shade off the card behind it, and the chosen segment stops reading as chosen.
            segment.BorderBrush = isSelected ? colors.Hairline : Brushes.Transparent;

            if (segment.Child is TextBlock label)
            {
                label.Foreground = isSelected ? colors.Foreground : colors.Dim;
            }
        }
    }

    static Border CreateDisplay(OverlayTheme colors, TextBlock value) =>
        new()
        {
            Child = value,
            MinWidth = 52,
            Padding = new Thickness(6, 3),
            BorderBrush = colors.Hairline,
            BorderThickness = new Thickness(1, 0),
        };

    static Border CreateStep(OverlayTheme colors, string glyph, Action nudge)
    {
        var label = OverlayControls.CreateUiLabel(glyph, 13);
        label.Foreground = colors.Foreground;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        var button = new Border
        {
            Child = label,
            Width = 24,
            Background = Brushes.Transparent,
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
