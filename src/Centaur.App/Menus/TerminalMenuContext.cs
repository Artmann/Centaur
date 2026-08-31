using Centaur.App.Splits;

namespace Centaur.App.Menus;

/// <summary>
/// What a context-menu provider is allowed to see of, and do to, the pane the menu was
/// opened on.
///
/// Built from delegates rather than from the pane itself, so a provider gets exactly these
/// eight capabilities and no way to reach the rest of the control. The state getters are
/// called when the menu opens, not when this is constructed.
/// </summary>
public sealed class TerminalMenuContext : ITerminalContextMenuContext
{
    public required Func<bool> SelectionPresent { get; init; }
    public required Func<bool> ReadOnly { get; init; }
    public required Action ToggleReadOnlyRequested { get; init; }
    public required Action CopyRequested { get; init; }
    public required Action PasteRequested { get; init; }
    public required Action PasteImageAsFileRequested { get; init; }
    public required Action<SplitDirection> SplitRequested { get; init; }
    public required Action CloseRequested { get; init; }

    bool ITerminalContextMenuContext.HasSelection => SelectionPresent();

    bool ITerminalContextMenuContext.IsReadOnly => ReadOnly();

    void ITerminalContextMenuContext.ToggleReadOnly() => ToggleReadOnlyRequested();

    void ITerminalContextMenuContext.Copy() => CopyRequested();

    void ITerminalContextMenuContext.Paste() => PasteRequested();

    void ITerminalContextMenuContext.PasteImageAsFile() => PasteImageAsFileRequested();

    void ITerminalContextMenuContext.Split(SplitDirection direction) => SplitRequested(direction);

    void ITerminalContextMenuContext.Close() => CloseRequested();
}
