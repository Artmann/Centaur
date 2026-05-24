using Avalonia.Controls;
using Avalonia.Input;

namespace Centaur.App.Splits;

public interface IPaneTerminal
{
    Control View { get; }
    event EventHandler<GotFocusEventArgs>? GotFocus;
    bool Focus();
    void Close();
}
