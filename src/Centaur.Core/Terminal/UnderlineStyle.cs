namespace Centaur.Core.Terminal;

/// <summary>
/// Underline rendering style, selected by SGR 4 / 21 and the colon
/// sub-parameter form ESC[4:Nm. Numeric values match the colon sub-parameter
/// codes so a code in range can be cast directly.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Single/Double are the standard underline-style names from the VT/SGR spec."
)]
public enum UnderlineStyle
{
    None = 0,
    Single = 1,
    Double = 2,
    Curly = 3,
    Dotted = 4,
    Dashed = 5,
}
