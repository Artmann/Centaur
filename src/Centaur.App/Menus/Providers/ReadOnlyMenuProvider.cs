namespace Centaur.App.Menus.Providers;

public sealed class ReadOnlyMenuProvider : ITerminalContextMenuProvider
{
    public int Priority => 200;

    public IEnumerable<TerminalContextMenuItem> GetItems(ITerminalContextMenuContext context)
    {
        yield return new TerminalContextMenuItem
        {
            Label = "Read-Only Mode",
            Group = "clipboard",
            IsChecked = context.IsReadOnly,
            OnInvoke = context.ToggleReadOnly,
        };
    }
}
