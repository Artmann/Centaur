using Avalonia.Controls;
using Avalonia.Input;

namespace Centaur.App.Splits;

public interface IPaneTerminal
{
    Control View { get; }
    string? WorkingDirectory { get; }
    event EventHandler<GotFocusEventArgs>? GotFocus;
    event Action? WorkingDirectoryChanged;
    bool Focus();
    void Close();
}
