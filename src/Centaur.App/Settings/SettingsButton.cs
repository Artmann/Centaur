using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>
/// The one interactive surface the settings page is built from: a <see cref="Border"/> that takes
/// keyboard focus, marks itself when the keyboard put it there, fills on hover and press, and
/// activates on click, Space or Enter.
///
/// Every affordance on the page used to be a bare Border with a PointerPressed handler. A Border
/// is not focusable in Avalonia, so none of them had a tab stop, an activation key or a focus
/// visual - twelve settings of which two could be reached without a mouse. Putting the behaviour
/// in one control rather than at each call site is what keeps the next one from being built the
/// same way.
/// </summary>
class SettingsButton : Border
{
    IBrush resting = Brushes.Transparent;
    IBrush? hover;
    IBrush? press;
    IBrush outline = Brushes.Transparent;
    bool pointerInside;
    bool pointerDown;
    bool focusedByKeyboard;

    public SettingsButton()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);

        // A transparent outline at rest, always one pixel thick. Border.Render is sealed, so the
        // focus ring has to be this border rather than something drawn over it, and thickening it
        // on focus would shift everything inside by a pixel. Only its colour ever changes.
        BorderThickness = new Thickness(1);

        // Not null: a Border with no background is not hit-testable, so a transparent one is what
        // makes the whole surface clickable rather than just the text inside it.
        Background = Brushes.Transparent;
    }

    /// <summary>Clicked, or activated with Space or Enter.</summary>
    public event Action? Activated;

    /// <summary>
    /// An arrow key while focused, as a direction: -1 for Left or Up, +1 for Right or Down, and
    /// <see cref="int.MinValue"/> / <see cref="int.MaxValue"/> for Home and End. A segmented
    /// control and a radio group are each one tab stop whose arrows move the choice, which is
    /// what the platform controls they stand in for do.
    /// </summary>
    public event Action<int>? Moved;

    /// <summary>The colour the outline takes while the keyboard holds this. Unset means none.</summary>
    public IBrush? FocusBrush { get; set; }

    /// <summary>Whether the keyboard, rather than the pointer, is what put focus here. Read by
    /// the page when a rebuild is about to replace this control and focus has to be put back the
    /// way it was found.</summary>
    public bool Ringed => focusedByKeyboard;

    /// <summary>The outline while the keyboard is elsewhere - the frame of a stepper, the edge of
    /// a chosen segment, or transparent for the surfaces that carry no outline of their own.</summary>
    public IBrush Outline
    {
        get => outline;
        set
        {
            outline = value;
            Repaint();
        }
    }

    /// <summary>
    /// Repaints for a new state or a new theme. <paramref name="hoverFill"/> and
    /// <paramref name="pressFill"/> are left null by surfaces that should not react to the
    /// pointer - a chosen segment, which clicking again would not change.
    /// </summary>
    public void SetFill(IBrush restingFill, IBrush? hoverFill = null, IBrush? pressFill = null)
    {
        resting = restingFill;
        hover = hoverFill;
        press = pressFill;
        Repaint();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        pointerInside = true;
        Repaint();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        pointerInside = false;
        pointerDown = false;
        Repaint();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        pointerDown = true;
        Repaint();
        Focus(NavigationMethod.Pointer);
        Activated?.Invoke();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        pointerDown = false;
        Repaint();
    }

    /// <summary>
    /// The mark shows for a keyboard arrival only. A pointer arrival already told the user where
    /// they are, and outlining every clicked control leaves the page covered in rings.
    /// </summary>
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        focusedByKeyboard = e.NavigationMethod != NavigationMethod.Pointer;
        Repaint();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        focusedByKeyboard = false;
        Repaint();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            Activated?.Invoke();
            return;
        }

        var direction = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            Key.Home => int.MinValue,
            Key.End => int.MaxValue,
            _ => 0,
        };

        if (direction != 0 && Moved is not null)
        {
            e.Handled = true;
            Moved(direction);
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// Keyboard focus takes the outline and the hover fill together. One of the two would do it on
    /// most of these, but the toggle's outline is already the accent when it is on and the nav
    /// entry's fill is already the chip when it is open, so neither alone survives every state.
    /// </summary>
    void Repaint()
    {
        var ringed = focusedByKeyboard && FocusBrush is not null;

        Background =
            (pointerDown ? press : null) ?? (pointerInside || ringed ? hover : null) ?? resting;

        BorderBrush = ringed ? FocusBrush : outline;
    }
}
