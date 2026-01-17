using Avalonia.Controls;

namespace Centaur.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => terminal.Focus();
    }
}
