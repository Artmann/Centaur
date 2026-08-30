using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The settings card and the dimmed backdrop behind it. The settings themselves live in
/// <see cref="StartDirectorySection"/>; this type owns the chrome, the theme and Esc.
/// </summary>
public class SettingsOverlay : UserControl
{
    readonly StartDirectorySection startDirectory;

    // Chrome that ApplyTheme repaints. Held from construction rather than re-found by
    // walking the visual tree every time the overlay opens.
    readonly Border backdrop;
    readonly Border card;
    readonly Border separator;
    readonly TextBlock sectionHeader;
    readonly TextBlock titleText;
    readonly TextBlock closeButton;

    public event Action? CloseRequested;

    public SettingsOverlay(Settings settings)
    {
        IsVisible = false;
        IsHitTestVisible = true;

        startDirectory = new StartDirectorySection(settings);

        sectionHeader = OverlayControls.CreateLabel("Starting Directory", 14, FontWeight.SemiBold);
        sectionHeader.Margin = new Thickness(0, 0, 0, 12);

        titleText = OverlayControls.CreateLabel("Settings", 16, FontWeight.Bold);
        titleText.VerticalAlignment = VerticalAlignment.Center;

        closeButton = CreateCloseButton();
        separator = new Border { Height = 1, Margin = new Thickness(20, 12, 20, 0) };
        card = CreateCard();

        // Clicking anywhere outside the card dismisses the overlay.
        backdrop = new Border();
        backdrop.PointerPressed += (_, _) => CloseRequested?.Invoke();

        var root = new Panel();
        root.Children.Add(backdrop);
        root.Children.Add(card);

        Content = root;

        KeyDown += OnOverlayKeyDown;
    }

    TextBlock CreateCloseButton()
    {
        var button = OverlayControls.CreateLabel("Esc", 11);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(0, 0, 4, 0);
        button.Cursor = new Cursor(StandardCursorType.Hand);
        button.Opacity = 0.6;
        button.PointerPressed += (_, _) => CloseRequested?.Invoke();
        return button;
    }

    Border CreateCard()
    {
        var contentStack = new StackPanel { Margin = new Thickness(20) };
        contentStack.Children.Add(sectionHeader);
        contentStack.Children.Add(startDirectory.View);

        var headerPanel = new DockPanel { Margin = new Thickness(20, 16, 20, 0) };
        DockPanel.SetDock(closeButton, Dock.Right);
        headerPanel.Children.Add(closeButton);
        headerPanel.Children.Add(titleText);

        var cardContent = new StackPanel();
        cardContent.Children.Add(headerPanel);
        cardContent.Children.Add(separator);
        cardContent.Children.Add(contentStack);

        return new Border
        {
            MaxWidth = 500,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = cardContent,
        };
    }

    public void Show(TerminalTheme theme)
    {
        ApplyTheme(theme);
        startDirectory.Refresh();

        IsVisible = true;
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    public void Hide()
    {
        IsVisible = false;
    }

    void ApplyTheme(TerminalTheme theme)
    {
        // The backdrop dims whatever is behind the overlay; the card sits on top of it
        // and stays opaque.
        var colors = new OverlayTheme(theme, backgroundOpacity: 0.85);

        backdrop.Background = colors.Background;

        card.Background = colors.Surface;
        card.BorderBrush = colors.Dim;
        card.BorderThickness = new Thickness(1);

        separator.Background = colors.Dim;

        titleText.Foreground = colors.Foreground;
        closeButton.Foreground = colors.Foreground;
        sectionHeader.Foreground = colors.Accent;

        startDirectory.ApplyTheme(colors);
    }

    void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
        }
    }
}
