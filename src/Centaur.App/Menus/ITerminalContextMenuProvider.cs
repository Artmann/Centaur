using Centaur.App.Splits;
using Centaur.Core.Hosting;

namespace Centaur.App.Menus;

public interface ITerminalContextMenuProvider : IProvider
{
    IEnumerable<TerminalContextMenuItem> GetItems(ITerminalContextMenuContext context);
}

public sealed class TerminalContextMenuItem
{
    public required string Label { get; init; }
    public required Action OnInvoke { get; init; }
    public string Group { get; init; } = "default";
    public bool IsVisible { get; init; } = true;
    public bool? IsChecked { get; init; }
}

public interface ITerminalContextMenuContext
{
    bool HasSelection { get; }
    bool IsReadOnly { get; }
    void ToggleReadOnly();
    void Copy();
    void Paste();
    void Split(SplitDirection direction);
    void Close();
}
