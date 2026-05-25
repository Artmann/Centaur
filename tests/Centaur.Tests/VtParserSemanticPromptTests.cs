using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// OSC 133 — semantic prompt marks (shell integration).
///
/// Ported (a representative subset) from
/// ghostty/src/terminal/osc/parsers/semantic_prompt.zig: prompt_start (133;A),
/// end_prompt_start_input (133;B), end_input_start_output (133;C), and
/// end_command (133;D, optionally with an exit code). These marks let the
/// terminal find prompt/command/output regions for jump-to-prompt navigation
/// and command-finished notifications.
///
/// Intended API (not yet implemented):
///   enum PromptMark { None, Prompt, Command, Output }
///   ScreenBuffer: PromptMark GetMark(int row);
///   VtParser: int? LastExitCode;
/// </summary>
public class VtParserSemanticPromptTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;

    public VtParserSemanticPromptTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    [Fact]
    public void Osc133_A_MarksPromptRow()
    {
        parser.Send("\x1b]133;A\a");
        Assert.Equal(PromptMark.Prompt, buffer.GetMark(0));
    }

    [Fact]
    public void Osc133_B_MarksCommandRegion()
    {
        // A starts the prompt, B ends it / begins command input on that row.
        parser.Send("\x1b]133;A\a\x1b]133;B\a");
        Assert.Equal(PromptMark.Command, buffer.GetMark(0));
    }

    [Fact]
    public void Osc133_C_MarksOutputRow()
    {
        // Move down two rows, then mark start of command output.
        parser.Send("\n\n\x1b]133;C\a");
        Assert.Equal(PromptMark.Output, buffer.GetMark(2));
    }

    [Fact]
    public void Osc133_D_WithExitCodeZero_SetsLastExitCode()
    {
        parser.Send("\x1b]133;D;0\a");
        Assert.Equal(0, parser.LastExitCode);
    }

    [Fact]
    public void Osc133_D_WithNonzeroExitCode_SetsLastExitCode()
    {
        parser.Send("\x1b]133;D;1\a");
        Assert.Equal(1, parser.LastExitCode);
    }

    [Fact]
    public void Osc133_D_WithoutExitCode_LeavesExitCodeNull()
    {
        parser.Send("\x1b]133;D\a");
        Assert.Null(parser.LastExitCode);
    }

    [Fact]
    public void Osc133_DoesNotEmitVisibleOutput()
    {
        parser.Send("\x1b]133;A\ahi");
        Assert.Equal('h', buffer[0, 0].character);
        Assert.Equal('i', buffer[1, 0].character);
    }
}
