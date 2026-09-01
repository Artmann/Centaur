using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The starting-directory editor: one row per <see cref="StartDirectoryMode"/> plus the path box
/// the specific-folder mode needs.
///
/// The only setting with an editor of its own - three modes where one of them carries a value is
/// more than a segmented control can say. It writes straight through on every change, like the
/// rest of the page, so there is no apply or cancel step for the page above to coordinate.
/// </summary>
sealed class StartDirectoryEditor
{
    readonly Settings settings;
    readonly OverlayTheme colors;
    readonly StartDirectoryMode[] modes = Enum.GetValues<StartDirectoryMode>();
    readonly SettingsButton[] optionRows;
    readonly TextBox folderTextBox;
    readonly TextBlock validationText;
    readonly Panel folderInputPanel;
    readonly StackPanel panel;

    public StartDirectoryEditor(SettingsContext context)
    {
        settings = context.Settings;
        colors = context.Colors;

        folderTextBox = OverlayControls.CreateTextBox(new Thickness(1));
        folderTextBox.FontSize = 12;
        folderTextBox.Padding = new Thickness(8, 4);
        folderTextBox.CornerRadius = new CornerRadius(6);
        folderTextBox.Text = settings.SpecificFolder;
        folderTextBox.TextChanged += OnFolderTextChanged;
        SettingsControls.StyleBox(colors, folderTextBox);
        SettingsControls.FocusOutline(folderTextBox, () => colors);

        validationText = OverlayControls.CreateUiLabel("", 12);
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
    SettingsButton[] CreateOptionRows() =>
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

        var input = new Panel { Margin = new Thickness(30, 6, 0, 0) };
        input.Children.Add(stack);
        return input;
    }

    SettingsButton CreateOptionRow(string label, string description, StartDirectoryMode mode)
    {
        var labelText = OverlayControls.CreateUiLabel(label, 13);
        labelText.Foreground = colors.Foreground;

        var descText = OverlayControls.CreateUiLabel(description, 12);
        descText.Foreground = colors.Dim;
        descText.Margin = new Thickness(0, 2, 0, 0);

        var text = new StackPanel();
        text.Children.Add(labelText);
        text.Children.Add(descText);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(CreateRadio());
        content.Children.Add(text);

        var row = new SettingsButton
        {
            Padding = new Thickness(8, 7),
            CornerRadius = new CornerRadius(6),
            Child = content,
            FocusBrush = colors.Accent,
            Tag = mode,

            // The three rows are one radio group and so one tab stop: the stop sits on the
            // chosen mode and the arrows move between them. <see cref="PaintRow"/> moves it.
            Focusable = false,
        };

        row.Activated += () => SelectOption(mode);
        row.Moved += direction => SelectOption(Neighbour(mode, direction));
        return row;
    }

    /// <summary>The mode an arrow key moves to, clamped at the ends of the list.</summary>
    StartDirectoryMode Neighbour(StartDirectoryMode from, int direction)
    {
        var next = direction switch
        {
            int.MinValue => 0,
            int.MaxValue => modes.Length - 1,
            _ => Array.IndexOf(modes, from) + direction,
        };

        return modes[Math.Clamp(next, 0, modes.Length - 1)];
    }

    /// <summary>
    /// The dot that says which mode is chosen - three modes where exactly one holds is what a
    /// radio says, and it says it without the row having to look like a button to be readable.
    /// <see cref="PaintRow"/> colours it.
    /// </summary>
    static Border CreateRadio() =>
        new()
        {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    void SelectOption(StartDirectoryMode mode)
    {
        if (mode == settings.StartDirectory)
        {
            return;
        }

        settings.StartDirectory = mode;
        settings.Save(SettingIds.StartDirectory);
        UpdateSelectionVisual();
        ValidateFolderPath();

        if (mode == StartDirectoryMode.SpecificFolder)
        {
            // The box has only just been made visible; focusing it synchronously does not
            // stick, so the focus waits for the layout pass that reveals it.
            Dispatcher.UIThread.Post(() => folderTextBox.Focus(), DispatcherPriority.Input);
            return;
        }

        // The stop moved with the choice, so the keyboard follows it rather than being left on
        // a row that is no longer focusable.
        Row(mode)?.Focus(NavigationMethod.Directional);
    }

    SettingsButton? Row(StartDirectoryMode mode) =>
        Array.Find(optionRows, row => (StartDirectoryMode)row.Tag! == mode);

    void UpdateSelectionVisual()
    {
        foreach (var row in optionRows)
        {
            PaintRow(row, (StartDirectoryMode)row.Tag! == settings.StartDirectory);
        }

        folderInputPanel.IsVisible = settings.StartDirectory == StartDirectoryMode.SpecificFolder;
    }

    /// <summary>Applies the selected or unselected look to one option row in place.</summary>
    void PaintRow(SettingsButton row, bool isSelected)
    {
        row.SetFill(Brushes.Transparent, colors.Hover, colors.Press);
        row.Focusable = isSelected;

        if (row.Child is not StackPanel content || content.Children[0] is not Border radio)
        {
            return;
        }

        // The ring is a control boundary, not text, so it takes the boundary colour rather than
        // the body one - on Latte the dim it used to take was a shade off the card.
        radio.BorderBrush = isSelected ? colors.Accent : colors.Edge;

        if (radio.Child is Border dot)
        {
            dot.Background = colors.Accent;
            dot.IsVisible = isSelected;
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
