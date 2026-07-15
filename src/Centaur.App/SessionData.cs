using Avalonia.Layout;

namespace Centaur.App;

public class SessionData
{
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveTabIndex { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
}

public class SessionTab
{
    public string Title { get; set; } = "";
    public SessionNode Root { get; set; } = null!;
}

public class SessionNode
{
    public bool IsSplit { get; set; }

    // Leaf only
    public string? WorkingDirectory { get; set; }

    // Split only
    public Orientation Orientation { get; set; }
    public double Ratio { get; set; } = 0.5;
    public SessionNode? First { get; set; }
    public SessionNode? Second { get; set; }
}
