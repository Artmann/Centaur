using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>The bounds a <see cref="NumberEditor"/> steps within, the precision it shows and
/// rounds to, and the unit it shows the value in.</summary>
/// <param name="Unit">Appended to the displayed value, including any space it wants before it -
/// <c>" pt"</c>, <c>"%"</c>. Without it the row's numbers are bare and the unit lives only in
/// the description, which is not where someone reading a value looks.</param>
sealed record NumberRange(
    double Minimum,
    double Maximum,
    double Step,
    int Decimals,
    string Unit = ""
);

/// <summary>
/// A number with a decrement and an increment beside it, and the value itself typed straight in.
///
/// Stepped rather than dragged on purpose: every commit here rebuilds each pane's renderer, and
/// a slider would do that once per pixel of travel. But stepping alone made the wide ranges
/// unusable - scrollback moves in thousands across 0 to 200,000, which is 190 clicks end to end -
/// so the display is an editable field, and the arrows work from the keyboard.
/// </summary>
sealed class NumberEditor
{
    readonly OverlayTheme colors;
    readonly NumberRange range;
    readonly Action<double> commit;
    readonly TextBox box;
    readonly SettingsButton down;
    readonly SettingsButton up;
    readonly Border frame;

    double current;

    public NumberEditor(OverlayTheme colors, double value, NumberRange range, Action<double> commit)
    {
        this.colors = colors;
        this.range = range;
        this.commit = commit;
        current = value;

        box = CreateBox();
        down = CreateStep("−", () => Nudge(-range.Step));
        up = CreateStep("+", () => Nudge(range.Step));

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(down);
        row.Children.Add(Sleeve());
        row.Children.Add(up);

        frame = SettingsControls.Frame(colors, row);
        UpdateBounds();
    }

    public Control View => frame;

    TextBox CreateBox()
    {
        var created = OverlayControls.CreateTextBox(new Thickness(0));
        created.FontFamily = OverlayControls.UiFont;
        created.FontSize = 12;
        created.Padding = new Thickness(6, 3);
        created.MinWidth = 56;
        created.TextAlignment = TextAlignment.Center;
        created.Text = Format(current);
        SettingsControls.StyleBox(colors, created, Brushes.Transparent);

        created.KeyDown += OnKeyDown;
        created.LostFocus += (_, _) => Commit(created.Text);

        // The frame is the box's focus visual: the field has no border of its own, so ringing it
        // would draw a second outline inside the one the stepper already has.
        created.GotFocus += (_, _) => frame.BorderBrush = colors.Accent;
        created.LostFocus += (_, _) => frame.BorderBrush = colors.Edge;
        return created;
    }

    /// <summary>The field between the two rules that separate it from the step buttons.</summary>
    Border Sleeve() =>
        new()
        {
            Child = box,
            BorderBrush = colors.Hairline,
            BorderThickness = new Thickness(1, 0),
        };

    SettingsButton CreateStep(string glyph, Action nudge)
    {
        var label = OverlayControls.CreateUiLabel(glyph, 13);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        var button = new SettingsButton
        {
            Child = label,
            Width = 24,
            CornerRadius = new CornerRadius(4),

            // The field beside it is the tab stop, and its Up and Down keys do what these do.
            // Two more stops per stepper would treble the page's tab order to no end.
            Focusable = false,
        };

        button.Activated += nudge;
        button.SetFill(Brushes.Transparent, colors.Hover, colors.Press);
        return button;
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Up => range.Step,
            Key.Down => -range.Step,
            Key.PageUp => range.Step * 10,
            Key.PageDown => -range.Step * 10,
            _ => 0,
        };

        if (step != 0)
        {
            e.Handled = true;
            Nudge(step);
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Commit(box.Text);
            return;
        }

        // Only swallows the first Escape, and only when there is an edit to abandon; the second
        // one reaches the page and closes it.
        if (e.Key == Key.Escape && box.Text != Format(current))
        {
            e.Handled = true;
            box.Text = Format(current);
        }
    }

    void Nudge(double delta) => Apply(current + delta);

    /// <summary>Takes what was typed, or puts the committed value back if it was not a number.</summary>
    void Commit(string? text)
    {
        var stripped = Strip(text);
        if (double.TryParse(stripped, NumberStyles.Number, CultureInfo.CurrentCulture, out var v))
        {
            Apply(v);
            return;
        }

        box.Text = Format(current);
    }

    void Apply(double value)
    {
        // Floating-point steps drift; rounding to the displayed precision keeps
        // 1.2 + 0.05 from becoming 1.2500000000000002 in the settings file.
        var next = Math.Round(
            Math.Clamp(value, range.Minimum, range.Maximum),
            range.Decimals,
            MidpointRounding.AwayFromZero
        );

        box.Text = Format(next);

        if (Math.Abs(next - current) < double.Epsilon)
        {
            return;
        }

        current = next;
        UpdateBounds();
        commit(next);
    }

    /// <summary>
    /// Greys out the step that would do nothing. A control that looks live and silently refuses
    /// reads as broken, and both ends of every range here are reachable.
    /// </summary>
    void UpdateBounds()
    {
        Paint(down, current > range.Minimum);
        Paint(up, current < range.Maximum);

        void Paint(SettingsButton button, bool enabled)
        {
            button.IsEnabled = enabled;
            button.Cursor = new Cursor(
                enabled ? StandardCursorType.Hand : StandardCursorType.Arrow
            );

            if (button.Child is TextBlock glyph)
            {
                glyph.Foreground = enabled ? colors.Foreground : colors.Edge;
            }
        }
    }

    string Format(double value) =>
        value.ToString("N" + range.Decimals, CultureInfo.CurrentCulture) + range.Unit;

    string Strip(string? text) =>
        (text ?? "").Replace(range.Unit, "", StringComparison.OrdinalIgnoreCase).Trim();
}
