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

    SolidColorBrush? backgroundBrush;
    SolidColorBrush? foregroundBrush;
    SolidColorBrush? dimBrush;
    SolidColorBrush? accentBrush;
    SolidColorBrush? selectionBrush;
    SolidColorBrush? surfaceBrush;

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
        var sectionHeader = new TextBlock
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
        var titleText = new TextBlock
        {
            Text = "Settings",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            FontFamily = monoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var closeButton = new TextBlock
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

        var separator = new Border { Height = 1, Margin = new Thickness(20, 12, 20, 0) };

        // Card
        var cardContent = new StackPanel();
        cardContent.Children.Add(headerPanel);
        cardContent.Children.Add(separator);
        cardContent.Children.Add(contentStack);

        var card = new Border
        {
            MaxWidth = 500,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = cardContent,
        };

        // Background overlay (click to close)
        var backdrop = new Border();
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
        backgroundBrush = BrushFromUint(theme.Background, 0.85);
        foregroundBrush = BrushFromUint(theme.Foreground);
        dimBrush = BrushFromUint(theme.Palette[8]);
        accentBrush = BrushFromUint(theme.Palette[4]);
        selectionBrush = BrushFromUint(theme.Selection);
        surfaceBrush = BrushFromUint(theme.Palette[0]);

        // Backdrop
        if (Content is Panel root && root.Children[0] is Border backdrop)
        {
            backdrop.Background = backgroundBrush;
        }

        // Card
        if (Content is Panel root2 && root2.Children[1] is Border card)
        {
            card.Background = BrushFromUint(theme.Background);
            card.BorderBrush = dimBrush;
            card.BorderThickness = new Thickness(1);
        }

        // Apply to all text in the overlay
        ApplyForegroundRecursive(this, foregroundBrush);

        // Section header accent
        var contentPanel = FindDescendant<StackPanel>(this, 3);
        if (contentPanel?.Children[0] is TextBlock sectionHeader)
        {
            sectionHeader.Foreground = accentBrush;
        }

        // Separator
        foreach (var child in FindAllDescendants<Border>(this))
        {
            if (child.Height == 1)
            {
                child.Background = dimBrush;
            }
        }

        // Validation text
        validationText.Foreground = BrushFromUint(theme.Palette[1]); // Red

        // TextBox styling
        folderTextBox.Foreground = foregroundBrush;
        folderTextBox.CaretBrush = accentBrush;
        folderTextBox.BorderBrush = dimBrush;
        foreach (
            var key in new[]
            {
                "TextBoxBackground",
                "TextBoxBackgroundPointerOver",
                "TextBoxBackgroundFocused",
                "TextBoxBorderBrush",
                "TextBoxBorderBrushPointerOver",
                "TextBoxBorderBrushFocused",
                "TextControlBackground",
                "TextControlBackgroundPointerOver",
                "TextControlBackgroundFocused",
                "TextControlBorderBrush",
                "TextControlBorderBrushPointerOver",
                "TextControlBorderBrushFocused",
            }
        )
        {
            folderTextBox.Resources[key] = Brushes.Transparent;
        }
        foreach (
            var key in new[]
            {
                "TextBoxForeground",
                "TextBoxForegroundPointerOver",
                "TextBoxForegroundFocused",
                "TextControlForeground",
                "TextControlForegroundPointerOver",
                "TextControlForegroundFocused",
            }
        )
        {
            folderTextBox.Resources[key] = foregroundBrush;
        }

        UpdateSelectionVisual();
    }

    void UpdateSelectionVisual()
    {
        for (int i = 0; i < optionRows.Length; i++)
        {
            var row = optionRows[i];
            var mode = (StartDirectoryMode)row.Tag!;
            var isSelected = mode == settings.StartDirectory;

            row.Background = isSelected ? selectionBrush : Brushes.Transparent;
            row.BorderBrush = isSelected ? accentBrush : Brushes.Transparent;

            if (row.Child is StackPanel stack)
            {
                if (stack.Children[0] is TextBlock label)
                {
                    label.Foreground = isSelected ? foregroundBrush : dimBrush;
                }
                if (stack.Children[1] is TextBlock desc)
                {
                    desc.Foreground = isSelected ? foregroundBrush : dimBrush;
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

    static void ApplyForegroundRecursive(Control control, IBrush brush)
    {
        if (control is TextBlock tb)
        {
            tb.Foreground = brush;
        }

        if (control is Decorator decorator && decorator.Child is Control child)
        {
            ApplyForegroundRecursive(child, brush);
        }

        if (control is Panel panel)
        {
            foreach (var c in panel.Children)
            {
                if (c is Control ctrl)
                {
                    ApplyForegroundRecursive(ctrl, brush);
                }
            }
        }

        if (control is ContentControl cc && cc.Content is Control content)
        {
            ApplyForegroundRecursive(content, brush);
        }
    }

    static T? FindDescendant<T>(Control control, int depth)
        where T : class
    {
        if (depth <= 0)
        {
            return control as T;
        }

        if (control is ContentControl cc && cc.Content is Control content)
        {
            return FindDescendant<T>(content, depth - 1);
        }

        if (control is Decorator d && d.Child is Control child)
        {
            return FindDescendant<T>(child, depth - 1);
        }

        if (control is Panel p)
        {
            foreach (var c in p.Children)
            {
                if (c is Control ctrl)
                {
                    var result = FindDescendant<T>(ctrl, depth - 1);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
        }

        return null;
    }

    static IEnumerable<T> FindAllDescendants<T>(Control control)
        where T : class
    {
        if (control is T match)
        {
            yield return match;
        }

        if (control is ContentControl cc && cc.Content is Control content)
        {
            foreach (var item in FindAllDescendants<T>(content))
            {
                yield return item;
            }
        }

        if (control is Decorator d && d.Child is Control child)
        {
            foreach (var item in FindAllDescendants<T>(child))
            {
                yield return item;
            }
        }

        if (control is Panel p)
        {
            foreach (var c in p.Children)
            {
                if (c is Control ctrl)
                {
                    foreach (var item in FindAllDescendants<T>(ctrl))
                    {
                        yield return item;
                    }
                }
            }
        }
    }

    static SolidColorBrush BrushFromUint(uint color, double opacity = 1.0)
    {
        var c = Color.FromUInt32(color);
        if (opacity < 1.0)
        {
            c = Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B);
        }
        return new SolidColorBrush(c);
    }
}
