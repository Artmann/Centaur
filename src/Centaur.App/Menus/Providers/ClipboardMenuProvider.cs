namespace Centaur.App.Menus.Providers;

public sealed class ClipboardMenuProvider : ITerminalContextMenuProvider
{
    public int Priority => 100;

    public IEnumerable<TerminalContextMenuItem> GetItems(ITerminalContextMenuContext context)
    {
        yield return new TerminalContextMenuItem
        {
            Label = "Copy",
            Group = "clipboard",
            IsVisible = context.HasSelection,
            OnInvoke = context.Copy,
        };

        yield return new TerminalContextMenuItem
        {
            Label = "Paste",
            Group = "clipboard",
            OnInvoke = context.Paste,
        };
    }
}
