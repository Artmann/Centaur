using Avalonia.Controls;
using Centaur.Core.Hosting;

namespace Centaur.App.Menus;

/// <summary>
/// Builds a pane's context menu from whatever <see cref="ITerminalContextMenuProvider"/>s
/// the extension host has registered.
///
/// The menu is rebuilt every time it opens rather than once up front, because visibility and
/// check state - "Copy" needs a selection, "Read-only" is a toggle - are only true of the
/// moment the user right-clicked.
/// </summary>
public static class TerminalContextMenuBuilder
{
    public static ContextMenu Create(ExtensionHost host, ITerminalContextMenuContext context)
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) => Populate(menu, host, context);
        return menu;
    }

    static void Populate(ContextMenu menu, ExtensionHost host, ITerminalContextMenuContext context)
    {
        menu.Items.Clear();

        var items = host.GetProviders<ITerminalContextMenuProvider>()
            .SelectMany(p => p.GetItems(context))
            .Where(i => i.IsVisible);

        string? lastGroup = null;
        foreach (var item in items)
        {
            // Providers group their own items; a change of group is where a separator goes.
            if (lastGroup != null && item.Group != lastGroup)
            {
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(CreateItem(item));
            lastGroup = item.Group;
        }
    }

    static MenuItem CreateItem(TerminalContextMenuItem item)
    {
        var menuItem = new MenuItem { Header = item.Label };

        if (item.IsChecked.HasValue)
        {
            menuItem.ToggleType = MenuItemToggleType.CheckBox;
            menuItem.IsChecked = item.IsChecked.Value;
        }

        var onInvoke = item.OnInvoke;
        menuItem.Click += (_, _) => onInvoke();
        return menuItem;
    }
}
