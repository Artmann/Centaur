using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The "Starting Directory" section of the settings overlay: one row per
/// <see cref="StartDirectoryMode"/> plus the path box the specific-folder mode needs.
///
/// Writes straight through to <see cref="Settings"/> on every change, so there is no
/// apply or cancel step for the overlay above to coordinate.
/// </summary>
sealed class StartDirectorySection
{
    readonly Settings settings;
    readonly Border[] optionRows = new Border[3];
    readonly TextBox folderTextBox;
    readonly TextBlock validationText;
    readonly Panel folderInputPanel;
    readonly StackPanel panel;

    OverlayTheme? colors;

    public StartDirectorySection(Settings settings)
    {
        this.settings = settings;

        folderTextBox = OverlayControls.CreateTextBox(new Thickness(0, 0, 0, 1));
        folderTextBox.TextChanged += OnFolderTextChanged;

        validationText = OverlayControls.CreateLabel("", 11);
        validationText.IsVisible = false;
        validationText.Margin = new Thickness(0, 4, 0, 0);

        folderInputPanel = CreateFolderInput();

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

        panel = new StackPanel { Spacing = 2 };
        foreach (var row in optionRows)
        {
            panel.Children.Add(row);
        }
        panel.Children.Add(folderInputPanel);
    }

    public Control View => panel;

    public void ApplyTheme(OverlayTheme theme)
    {
        colors = theme;
        validationText.Foreground = theme.Error;
        theme.StyleTextBox(folderTextBox);
        UpdateSelectionVisual();
    }

    /// <summary>Re-reads the settings, for when the overlay is opened.</summary>
    public void Refresh()
    {
        folderTextBox.Text = settings.SpecificFolder;
        UpdateSelectionVisual();
        ValidateFolderPath();
    }

    // The path box and its validation message, shown only for the specific-folder mode.
    Panel CreateFolderInput()
    {
        var stack = new StackPanel();
        stack.Children.Add(folderTextBox);
        stack.Children.Add(validationText);

        var input = new Panel { Margin = new Thickness(32, 4, 0, 0) };
        input.Children.Add(stack);
        return input;
    }

    Border CreateOptionRow(string label, string description, StartDirectoryMode mode)
    {
        var labelText = OverlayControls.CreateLabel(label, 14);

        var descText = OverlayControls.CreateLabel(description, 11);
        descText.Opacity = 0.6;
        descText.Margin = new Thickness(0, 2, 0, 0);

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

    void UpdateSelectionVisual()
    {
        foreach (var row in optionRows)
        {
            PaintRow(row, (StartDirectoryMode)row.Tag! == settings.StartDirectory);
        }

        folderInputPanel.IsVisible = settings.StartDirectory == StartDirectoryMode.SpecificFolder;
    }

    /// <summary>Applies the selected or unselected look to one option row in place.</summary>
    void PaintRow(Border row, bool isSelected)
    {
        row.Background = isSelected ? colors?.Selection : Brushes.Transparent;
        row.BorderBrush = isSelected ? colors?.Accent : Brushes.Transparent;

        if (row.Child is not StackPanel stack)
        {
            return;
        }

        var brush = isSelected ? colors?.Foreground : colors?.Dim;
        if (stack.Children[0] is TextBlock label)
        {
            label.Foreground = brush;
        }

        if (stack.Children[1] is TextBlock description)
        {
            description.Foreground = brush;
            description.Opacity = isSelected ? 0.7 : 0.6;
        }
    }

    void OnFolderTextChanged(object? sender, TextChangedEventArgs e)
    {
        settings.SpecificFolder = folderTextBox.Text ?? "";
        settings.Save();
        ValidateFolderPath();
    }

    void ValidateFolderPath()
    {
        var missing =
            settings.StartDirectory == StartDirectoryMode.SpecificFolder
            && !string.IsNullOrEmpty(settings.SpecificFolder)
            && !Directory.Exists(settings.SpecificFolder);

        validationText.Text = "Folder does not exist. Terminal will use default directory.";
        validationText.IsVisible = missing;
    }
}
