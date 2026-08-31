using Centaur.App;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// The one judgment call in paste: an image goes to the running program as Ctrl+V when a
/// full-screen program is up and can read the clipboard itself, and is written out as a file
/// otherwise. "Paste Image as File" forces the second route from the context menu, which is the
/// way back when the program on the other end ignored Ctrl+V.
/// </summary>
public class PasteRouteTests
{
    [Fact]
    public void Text_WinsOverAnImageOnTheSameClipboard()
    {
        var route = TerminalClipboard.Route(
            hasText: true,
            hasImage: true,
            alternateScreen: true,
            asFile: false
        );

        Assert.Equal(PasteRoute.Text, route);
    }

    [Fact]
    public void Image_OnTheAlternateScreen_GoesToTheProgramAsCtrlV()
    {
        var route = TerminalClipboard.Route(
            hasText: false,
            hasImage: true,
            alternateScreen: true,
            asFile: false
        );

        Assert.Equal(PasteRoute.ClipboardKey, route);
    }

    [Fact]
    public void Image_AtAShellPrompt_IsWrittenOutAsAFile()
    {
        var route = TerminalClipboard.Route(
            hasText: false,
            hasImage: true,
            alternateScreen: false,
            asFile: false
        );

        Assert.Equal(PasteRoute.ImageFile, route);
    }

    [Fact]
    public void PasteImageAsFile_TakesTheFileRouteEvenOnTheAlternateScreen()
    {
        var route = TerminalClipboard.Route(
            hasText: false,
            hasImage: true,
            alternateScreen: true,
            asFile: true
        );

        Assert.Equal(PasteRoute.ImageFile, route);
    }

    [Fact]
    public void EmptyClipboard_DoesNothing()
    {
        var route = TerminalClipboard.Route(
            hasText: false,
            hasImage: false,
            alternateScreen: false,
            asFile: false
        );

        Assert.Equal(PasteRoute.Nothing, route);
    }

    [Fact]
    public void PasteImageAsFile_WithNoImage_DoesNothingRatherThanTypingTheText()
    {
        var route = TerminalClipboard.Route(
            hasText: true,
            hasImage: false,
            alternateScreen: false,
            asFile: true
        );

        Assert.Equal(PasteRoute.Nothing, route);
    }
}
