using System.Runtime.InteropServices;
using Avalonia.Threading;
using Centaur.Core.Terminal;
using Centaur.Rendering;

namespace Centaur.App;

/// <summary>
/// A pane's half of the settings: the renderer it draws with, rebuilt whenever an appearance
/// setting moves, plus the two settings that are behaviour rather than layout - how much
/// scrollback to keep, and what a BEL should do.
///
/// The renderer captures the theme and the cell metrics when it is constructed, and the whole
/// grid is laid out on those metrics, so a font or theme change means a new renderer and a
/// re-measure rather than a mutation.
/// </summary>
sealed class PaneAppearance : IDisposable
{
    readonly TerminalServices services;
    readonly TerminalSurface surface;
    readonly Action markDirty;
    readonly Action reMeasure;
    readonly DispatcherTimer flashTimer;

    // Held so they can be unsubscribed; both are on objects that outlive the pane.
    readonly Action<string> settingsChanged;
    readonly Action bellRang;

    // Renderers a settings change retired. Disposing one immediately would free its Skia
    // paints and typeface while a frame already handed to the compositor might still be
    // drawing with them, so they are kept until the pane closes - a session produces a
    // handful of them at most, and only when the user is actually changing settings.
    readonly List<TerminalRenderer> retired = [];

    public PaneAppearance(
        TerminalServices services,
        TerminalSurface surface,
        TerminalRenderer renderer,
        Action markDirty,
        Action reMeasure
    )
    {
        this.services = services;
        this.surface = surface;
        this.markDirty = markDirty;
        this.reMeasure = reMeasure;
        Renderer = renderer;

        flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        flashTimer.Tick += (_, _) => EndFlash();

        settingsChanged = OnSettingsChanged;
        bellRang = OnBell;
        services.Settings.Changed += settingsChanged;
        surface.Parser.Bell += bellRang;
    }

    /// <summary>The renderer the next frame should be drawn with.</summary>
    public TerminalRenderer Renderer { get; private set; }

    /// <summary>True while a visual bell is washing the pane.</summary>
    public bool BellFlashing { get; private set; }

    void OnSettingsChanged(string id)
    {
        // A save can come from anywhere - the settings page is on the UI thread, but the
        // working-directory tracker saves from wherever the command was noticed.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSettingsChanged(id));
            return;
        }

        if (id is SettingIds.Scrollback or "")
        {
            surface.SetScrollbackLines(services.Settings.ScrollbackLines);
        }

        if (SettingIds.AffectsRendering(id))
        {
            Rebuild();
        }
    }

    void Rebuild()
    {
        var theme = services.Theme;

        retired.Add(Renderer);
        Renderer = new TerminalRenderer(
            theme,
            TerminalAppearance.From(services.Settings),
            services.Profiler
        );

        surface.UseRenderer(Renderer);
        surface.SetTheme(theme);

        // The cell size may have moved, and the grid is sized in cells, so the pane has to
        // re-measure before the new renderer draws anything.
        reMeasure();
        markDirty();
    }

    // Raised from the pty read thread, for every BEL the program emits.
    void OnBell() => Dispatcher.UIThread.Post(Ring);

    void Ring()
    {
        switch (services.Settings.Bell)
        {
            case BellMode.Sound:
                // MessageBeep rather than Console.Beep, which synthesises a tone synchronously
                // and would stall the UI thread for the length of it.
                _ = MessageBeep(messageBeepDefault);
                break;
            case BellMode.Flash:
                BellFlashing = true;
                flashTimer.Stop();
                flashTimer.Start();
                markDirty();
                break;
        }
    }

    void EndFlash()
    {
        flashTimer.Stop();
        BellFlashing = false;
        markDirty();
    }

    public void Dispose()
    {
        services.Settings.Changed -= settingsChanged;
        surface.Parser.Bell -= bellRang;
        flashTimer.Stop();

        foreach (var renderer in retired)
        {
            renderer.Dispose();
        }

        retired.Clear();
        Renderer.Dispose();
    }

    // MB_OK - the system's default notification sound.
    const uint messageBeepDefault = 0x00000000;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool MessageBeep(uint type);
}
