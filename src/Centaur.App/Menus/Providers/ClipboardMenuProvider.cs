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

        // The escape hatch for the guess Paste makes about who should handle a clipboard
        // image: this route always writes the picture out and types its path.
        yield return new TerminalContextMenuItem
        {
            Label = "Paste Image as File",
            Group = "clipboard",
            OnInvoke = context.PasteImageAsFile,
        };
    }
}
