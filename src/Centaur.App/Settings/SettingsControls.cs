using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>
/// The controls the settings page is built from.
///
/// They are hand-rolled out of <see cref="SettingsButton"/> and <see cref="TextBox"/> rather than
/// taken from Fluent, for the reason <see cref="OverlayTheme.StyleTextBox"/> documents: Fluent
/// styles its controls through theme resources rather than properties, so a stock ComboBox, Slider
/// or ToggleSwitch would need a dozen resource keys stuffed into it per state and would still not
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

        // Close to the card it names and far from the one above it, so the gap does the grouping
        // rather than the label having to be read to work out what it belongs to.
        header.Margin = new Thickness(2, 0, 0, 6);
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
            Margin = new Thickness(0, 0, 0, 28),
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

            // Tagged so the page can find this row again after rebuilding itself, which is how
            // the keyboard gets put back on the control it was standing on.
            Tag = setting.Id,
        };
    }

    /// <summary>
    /// A segmented control, one segment per choice, with the current one filled. Stands in for
    /// both a dropdown and a checkbox: the options are few and naming them all beats hiding them
    /// behind a popup on a page whose whole point is discoverability.
    ///
    /// The group is one tab stop and its arrow keys move the choice, which is what a radio group
    /// does. A stop per segment would make Tab walk four times through the theme picker alone.
    /// </summary>
    /// <param name="swatch">An optional mark drawn in front of each label, for choices a word
    /// alone does not distinguish - the theme names.</param>
    public static Control Choice<T>(
        OverlayTheme colors,
        IReadOnlyList<(T Value, string Label)> options,
        T current,
        Action<T> select,
        Func<T, Control>? swatch = null
    )
        where T : notnull
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var segments = new List<SettingsButton>(options.Count);
        var group = Group(colors, row);
        var selected = current;

        foreach (var (value, label) in options)
        {
            var segment = CreateSegment(label, value, swatch?.Invoke(value));
            segment.Activated += () => Choose(value);
            segments.Add(segment);
            row.Children.Add(segment);
        }

        group.Moved += direction => Choose(Step(options, selected, direction));
        PaintSegments(colors, segments, selected);
        return group;

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

        var track = new SettingsButton
        {
            Child = knob,
            Width = 38,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(3, 0),
        };

        track.Activated += () => Set(!current);

        // The arrows set the state rather than flip it, so holding one down does not oscillate;
        // Home and End arrive as the extremes and land on off and on respectively.
        track.Moved += direction => Set(direction > 0);

        PaintToggle(colors, track, knob, current);
        return track;

        void Set(bool value)
        {
            if (value == current)
            {
                return;
            }

            current = value;
            PaintToggle(colors, track, knob, current);
            commit(current);
        }
    }

    /// <summary>A single-line text setting, written through on every keystroke like the rest
    /// of the page. Stays monospace: it holds a command, not prose.</summary>
    public static TextBox Text(OverlayTheme colors, string current, Action<string> commit)
    {
        var box = OverlayControls.CreateTextBox(new Thickness(1));
        box.FontSize = 12;
        box.Padding = new Thickness(8, 5);
        box.CornerRadius = new CornerRadius(6);
        box.Text = current;
        StyleBox(colors, box);
        FocusOutline(box, () => colors);
        box.TextChanged += (_, _) => commit(box.Text ?? "");
        return box;
    }

    /// <summary>
    /// A section name the user can click to search for it, offered where a query found nothing.
    /// </summary>
    public static Control Suggestion(OverlayTheme colors, string label, Action activate)
    {
        var text = OverlayControls.CreateUiLabel(label, 12);
        text.Foreground = colors.Foreground;

        var chip = new SettingsButton
        {
            Child = text,
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(6),
            Outline = colors.Edge,
            FocusBrush = colors.Accent,
        };

        chip.SetFill(Brushes.Transparent, colors.Hover, colors.Press);
        chip.Activated += activate;
        return chip;
    }

    /// <summary>
    /// Paints a box to match the page. The overlay styling it builds on makes every border state
    /// transparent, because an overlay draws its own frame around the box; here the box is the
    /// frame, so the state resources are painted back in - without them Fluent's focused state
    /// wins and the outline vanishes the moment the box is clicked into.
    /// </summary>
    /// <param name="border">Overrides the outline, for a box that sits inside a frame of its own
    /// and would otherwise draw a second one.</param>
    public static void StyleBox(OverlayTheme colors, TextBox box, IBrush? border = null)
    {
        colors.StyleTextBox(box);
        OverlayTheme.StyleTextBox(
            box,
            Brushes.Transparent,
            colors.Foreground,
            border ?? colors.Edge,
            colors.Accent
        );
    }

    /// <summary>
    /// Marks a box as focused. Every border state above resolves to the same colour, and
    /// <see cref="OverlayControls.CreateTextBox"/> drops the stock adorner, so without this a box
    /// holding focus looks exactly like one that does not.
    ///
    /// Subscribed once per box and reading the theme through <paramref name="colors"/> rather than
    /// capturing it, because the page keeps its search box across a theme change and re-styles it
    /// in place.
    /// </summary>
    public static void FocusOutline(TextBox box, Func<OverlayTheme> colors)
    {
        box.GotFocus += (_, _) => box.BorderBrush = colors().Accent;
        box.LostFocus += (_, _) => box.BorderBrush = colors().Edge;
    }

    /// <summary>The outline the segmented control and the stepper share, so a row holding one
    /// of each reads as one family of control.</summary>
    public static Border Frame(OverlayTheme colors, Control child) =>
        new()
        {
            Child = child,
            Background = Brushes.Transparent,
            BorderBrush = colors.Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
        };

    /// <summary>The same outline, but as the single tab stop for the choices inside it.</summary>
    static SettingsButton Group(OverlayTheme colors, Control child)
    {
        var group = new SettingsButton
        {
            Child = child,
            Outline = colors.Edge,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            FocusBrush = colors.Accent,

            // The segments inside carry the hand; the frame between them is not itself a target.
            Cursor = Cursor.Default,
        };

        group.SetFill(Brushes.Transparent);
        return group;
    }

    /// <summary>The switch's two states: filled with the knob to the right when on, outlined
    /// with the knob to the left when off.</summary>
    static void PaintToggle(OverlayTheme colors, SettingsButton track, Border knob, bool on)
    {
        // An on switch still reacts to the pointer - clicking it turns it off - but its fill is
        // the accent, which the card-derived hover and press would paint over rather than tint.
        track.SetFill(
            on ? colors.Accent : Brushes.Transparent,
            on ? colors.Shade(colors.Accent, 0.14) : colors.Hover,
            on ? colors.Shade(colors.Accent, 0.28) : colors.Press
        );

        track.Outline = on ? colors.Accent : colors.Edge;
        knob.Background = on ? colors.Surface : colors.Edge;
        knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        // The ring cannot be the accent while the accent is the fill underneath it.
        track.FocusBrush = on ? colors.Foreground : colors.Accent;
    }

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

    static SettingsButton CreateSegment<T>(string label, T value, Control? swatch)
        where T : notnull
    {
        var text = OverlayControls.CreateUiLabel(label, 12);

        var segment = new SettingsButton
        {
            Padding = new Thickness(10, 3),
            CornerRadius = new CornerRadius(4),
            Tag = value,

            // The group around it is the tab stop, and its arrows move between the segments.
            Focusable = false,
        };

        if (swatch is null)
        {
            segment.Child = text;
            return segment;
        }

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(swatch);
        content.Children.Add(text);
        segment.Child = content;
        return segment;
    }

    static void PaintSegments<T>(OverlayTheme colors, List<SettingsButton> segments, T selected)
        where T : notnull
    {
        foreach (var segment in segments)
        {
            var isSelected = EqualityComparer<T>.Default.Equals((T)segment.Tag!, selected);

            // The chosen one takes no hover or press fill: clicking it again changes nothing, and
            // a surface that lights up under the pointer is promising that it would.
            segment.SetFill(
                isSelected ? colors.Chip : Brushes.Transparent,
                isSelected ? null : colors.Hover,
                isSelected ? null : colors.Press
            );

            // Outlined as well as filled: on a light palette the fill alone is barely a shade off
            // the card behind it, and the chosen segment stops reading as chosen.
            segment.Outline = isSelected ? colors.Edge : Brushes.Transparent;
            SegmentLabel(segment).Foreground = isSelected ? colors.Foreground : colors.Dim;
        }
    }

    /// <summary>The segment's label, whether or not a swatch sits in front of it.</summary>
    static TextBlock SegmentLabel(Border segment) =>
        segment.Child as TextBlock ?? (TextBlock)((StackPanel)segment.Child!).Children[^1];

    /// <summary>The choice an arrow key moves to: one along, clamped at the ends rather than
    /// wrapped, so holding a key does not cycle past the end and back to where it started.</summary>
    static T Step<T>(IReadOnlyList<(T Value, string Label)> options, T current, int direction)
        where T : notnull
    {
        var index = 0;
        for (var i = 0; i < options.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(options[i].Value, current))
            {
                index = i;
                break;
            }
        }

        var next = direction switch
        {
            int.MinValue => 0,
            int.MaxValue => options.Count - 1,
            _ => index + direction,
        };

        return options[Math.Clamp(next, 0, options.Count - 1)].Value;
    }
}
