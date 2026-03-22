namespace Centaur.App;

public class TabItem
{
    public required int Id { get; init; }
    public required string Title { get; set; }
    public required TerminalControl Terminal { get; init; }
}
