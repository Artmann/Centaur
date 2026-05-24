using Centaur.App.Splits;

namespace Centaur.App;

public class TabItem
{
    public required int Id { get; init; }
    public required string Title { get; set; }
    public required PaneTree Panes { get; init; }
}
