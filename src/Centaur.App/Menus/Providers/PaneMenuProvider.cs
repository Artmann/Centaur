using Centaur.App.Splits;

namespace Centaur.App.Menus.Providers;

public sealed class PaneMenuProvider : ITerminalContextMenuProvider
{
    public int Priority => 300;

    public IEnumerable<TerminalContextMenuItem> GetItems(ITerminalContextMenuContext context)
    {
        yield return new TerminalContextMenuItem
        {
            Label = "Split Right",
            Group = "pane",
            OnInvoke = () => context.Split(SplitDirection.Right),
        };

        yield return new TerminalContextMenuItem
        {
            Label = "Split Left",
            Group = "pane",
            OnInvoke = () => context.Split(SplitDirection.Left),
        };

        yield return new TerminalContextMenuItem
        {
            Label = "Split Down",
            Group = "pane",
            OnInvoke = () => context.Split(SplitDirection.Down),
        };

        yield return new TerminalContextMenuItem
        {
            Label = "Split Up",
            Group = "pane",
            OnInvoke = () => context.Split(SplitDirection.Up),
        };

        yield return new TerminalContextMenuItem
        {
            Label = "Close Pane",
            Group = "pane",
            OnInvoke = context.Close,
        };
    }
}
