using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

public class SettingsOverlay : UserControl
{
    readonly Settings settings;
    readonly Border[] optionRows = new Border[3];
    readonly TextBox folderTextBox;
    readonly TextBlock validationText;
    readonly Panel folderInputPanel;

    // Chrome that ApplyTheme repaints. Held from construction rather than re-found by
    // walking the visual tree every time the overlay opens.
    readonly Border backdrop;
    readonly Border card;
    readonly Border separator;
    readonly TextBlock sectionHeader;
    readonly TextBlock titleText;
    readonly TextBlock closeButton;

    OverlayTheme? colors;

    public event Action? CloseRequested;

    static readonly FontFamily monoFont = new("JetBrains Mono, Consolas, Courier New, monospace");

    public SettingsOverlay(Settings settings)
    {
        this.settings = settings;
        IsVisible = false;
        IsHitTestVisible = true;

        // Folder path text box
        folderTextBox = new TextBox
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 8),
            MinHeight = 0,
            FontSize = 13,
            FontFamily = monoFont,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(0),
        };
        folderTextBox.FocusAdorner = null;
        folderTextBox.TextChanged += OnFolderTextChanged;

        validationText = new TextBlock
        {
            FontSize = 11,
            FontFamily = monoFont,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0),
        };

        folderInputPanel = new Panel { Margin = new Thickness(32, 4, 0, 0) };
        var folderStack = new StackPanel();
        folderStack.Children.Add(folderTextBox);
        folderStack.Children.Add(validationText);
        folderInputPanel.Children.Add(folderStack);

        // Build option rows
        optionRows[0] = CreateOptionRow(
            "Last used folder",
            "Restores the directory from your previous session",
            StartDirectoryMode.LastFolder
        );
        optionRows[1] = CreateOptionRow(
            "Home folder",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            StartDirectoryMode.HomeFolder
        );
        optionRows[2] = CreateOptionRow(
            "Specific folder",
            "Always start in a chosen directory",
            StartDirectoryMode.SpecificFolder
        );

        var optionsPanel = new StackPanel { Spacing = 2 };
        foreach (var row in optionRows)
        {
            optionsPanel.Children.Add(row);
        }
        optionsPanel.Children.Add(folderInputPanel);

        // Section header
        sectionHeader = new TextBlock
        {
            Text = "Starting Directory",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            FontFamily = monoFont,
            Margin = new Thickness(0, 0, 0, 12),
        };

        // Content area
        var contentStack = new StackPanel { Margin = new Thickness(20) };
        contentStack.Children.Add(sectionHeader);
        contentStack.Children.Add(optionsPanel);

        // Header with title and close button
        titleText = new TextBlock
        {
            Text = "Settings",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            FontFamily = monoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };

        closeButton = new TextBlock
        {
            Text = "Esc",
            FontSize = 11,
            FontFamily = monoFont,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Opacity = 0.6,
        };
        closeButton.PointerPressed += (_, _) => CloseRequested?.Invoke();

        var headerPanel = new DockPanel { Margin = new Thickness(20, 16, 20, 0) };
        DockPanel.SetDock(closeButton, Dock.Right);
        headerPanel.Children.Add(closeButton);
        headerPanel.Children.Add(titleText);

        separator = new Border { Height = 1, Margin = new Thickness(20, 12, 20, 0) };

        // Card
        var cardContent = new StackPanel();
        cardContent.Children.Add(headerPanel);
        cardContent.Children.Add(separator);
        cardContent.Children.Add(contentStack);

        card = new Border
        {
            MaxWidth = 500,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = cardContent,
        };

        // Background overlay (click to close)
        backdrop = new Border();
        backdrop.PointerPressed += (_, _) => CloseRequested?.Invoke();

        var root = new Panel();
        root.Children.Add(backdrop);
        root.Children.Add(card);

        Content = root;

        KeyDown += OnOverlayKeyDown;
    }

    Border CreateOptionRow(string label, string description, StartDirectoryMode mode)
    {
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontFamily = monoFont,
        };

        var descText = new TextBlock
        {
            Text = description,
            FontSize = 11,
            FontFamily = monoFont,
            Opacity = 0.6,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var stack = new StackPanel();
        stack.Children.Add(labelText);
        stack.Children.Add(descText);

        var border = new Border
        {
            Padding = new Thickness(16, 10),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack,
            Tag = mode,
        };

        border.PointerPressed += (_, _) => SelectOption(mode);

        return border;
    }

    void SelectOption(StartDirectoryMode mode)
    {
        settings.StartDirectory = mode;
        settings.Save();
        UpdateSelectionVisual();

        if (mode == StartDirectoryMode.SpecificFolder)
        {
            Dispatcher.UIThread.Post(() => folderTextBox.Focus(), DispatcherPriority.Input);
        }
    }

    public void Show(TerminalTheme theme)
    {
        ApplyTheme(theme);
        folderTextBox.Text = settings.SpecificFolder;
        UpdateSelectionVisual();
        ValidateFolderPath();

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
        colors = new OverlayTheme(theme, backgroundOpacity: 0.85);

        backdrop.Background = colors.Background;

        card.Background = colors.Surface;
        card.BorderBrush = colors.Dim;
        card.BorderThickness = new Thickness(1);

        separator.Background = colors.Dim;

        titleText.Foreground = colors.Foreground;
        closeButton.Foreground = colors.Foreground;
        sectionHeader.Foreground = colors.Accent;
        validationText.Foreground = colors.Error;

        colors.StyleTextBox(folderTextBox);

        UpdateSelectionVisual();
    }

    void UpdateSelectionVisual()
    {
        for (int i = 0; i < optionRows.Length; i++)
        {
            var row = optionRows[i];
            var mode = (StartDirectoryMode)row.Tag!;
            var isSelected = mode == settings.StartDirectory;

            row.Background = isSelected ? colors?.Selection : Brushes.Transparent;
            row.BorderBrush = isSelected ? colors?.Accent : Brushes.Transparent;

            if (row.Child is StackPanel stack)
            {
                if (stack.Children[0] is TextBlock label)
                {
                    label.Foreground = isSelected ? colors?.Foreground : colors?.Dim;
                }
                if (stack.Children[1] is TextBlock desc)
                {
                    desc.Foreground = isSelected ? colors?.Foreground : colors?.Dim;
                    desc.Opacity = isSelected ? 0.7 : 0.6;
                }
            }
        }

        folderInputPanel.IsVisible = settings.StartDirectory == StartDirectoryMode.SpecificFolder;
    }

    void OnFolderTextChanged(object? sender, TextChangedEventArgs e)
    {
        settings.SpecificFolder = folderTextBox.Text ?? "";
        settings.Save();
        ValidateFolderPath();
    }

    void ValidateFolderPath()
    {
        var path = settings.SpecificFolder;
        if (
            settings.StartDirectory == StartDirectoryMode.SpecificFolder
            && !string.IsNullOrEmpty(path)
            && !Directory.Exists(path)
        )
        {
            validationText.Text = "Folder does not exist. Terminal will use default directory.";
            validationText.IsVisible = true;
        }
        else
        {
            validationText.IsVisible = false;
        }
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
