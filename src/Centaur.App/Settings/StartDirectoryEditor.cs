using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The starting-directory editor: one row per <see cref="StartDirectoryMode"/> plus the path box
/// the specific-folder mode needs.
///
/// The only setting with an editor of its own - three modes where one of them carries a value is
/// more than a pill row can say. It writes straight through on every change, like the rest of the
/// page, so there is no apply or cancel step for the page above to coordinate.
/// </summary>
sealed class StartDirectoryEditor
{
    readonly Settings settings;
    readonly OverlayTheme colors;
    readonly Border[] optionRows;
    readonly TextBox folderTextBox;
    readonly TextBlock validationText;
    readonly Panel folderInputPanel;
    readonly StackPanel panel;

    public StartDirectoryEditor(SettingsContext context)
    {
        settings = context.Settings;
        colors = context.Colors;

        folderTextBox = OverlayControls.CreateTextBox(new Thickness(0, 0, 0, 1));
        folderTextBox.Text = settings.SpecificFolder;
        folderTextBox.TextChanged += OnFolderTextChanged;
        colors.StyleTextBox(folderTextBox);

        validationText = OverlayControls.CreateLabel("", 11);
        validationText.Foreground = colors.Error;
        validationText.IsVisible = false;
        validationText.Margin = new Thickness(0, 4, 0, 0);

        folderInputPanel = CreateFolderInput();
        optionRows = CreateOptionRows();

        panel = new StackPanel { Spacing = 2 };
        foreach (var row in optionRows)
        {
            panel.Children.Add(row);
        }

        panel.Children.Add(folderInputPanel);

        UpdateSelectionVisual();
        ValidateFolderPath();
    }

    public Control View => panel;

    /// <summary>One row per <see cref="StartDirectoryMode"/>, in the order they are offered.</summary>
    Border[] CreateOptionRows() =>
        [
            CreateOptionRow(
                "Last used folder",
                "Restores the directory from your previous session",
                StartDirectoryMode.LastFolder
            ),
            CreateOptionRow(
                "Home folder",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                StartDirectoryMode.HomeFolder
            ),
            CreateOptionRow(
                "Specific folder",
                "Always start in a chosen directory",
                StartDirectoryMode.SpecificFolder
            ),
        ];

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
        var labelText = OverlayControls.CreateLabel(label, 13);

        var descText = OverlayControls.CreateLabel(description, 11);
        descText.Opacity = 0.6;
        descText.Margin = new Thickness(0, 2, 0, 0);

        var stack = new StackPanel();
        stack.Children.Add(labelText);
        stack.Children.Add(descText);

        var border = new Border
        {
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 4, 4, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = stack,
            Tag = mode,
        };

        border.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            SelectOption(mode);
        };

        return border;
    }

    void SelectOption(StartDirectoryMode mode)
    {
        settings.StartDirectory = mode;
        settings.Save(SettingIds.StartDirectory);
        UpdateSelectionVisual();
        ValidateFolderPath();

        if (mode == StartDirectoryMode.SpecificFolder)
        {
            // The box has only just been made visible; focusing it synchronously does not
            // stick, so the focus waits for the layout pass that reveals it.
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
        row.Background = isSelected ? colors.Selection : Brushes.Transparent;
        row.BorderBrush = isSelected ? colors.Accent : Brushes.Transparent;

        if (row.Child is not StackPanel stack)
        {
            return;
        }

        var brush = isSelected ? colors.Foreground : colors.Dim;
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
        settings.Save(SettingIds.StartDirectory);
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
