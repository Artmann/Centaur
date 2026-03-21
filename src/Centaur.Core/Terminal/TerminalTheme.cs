namespace Centaur.Core.Terminal;

public class TerminalTheme
{
    public uint Foreground { get; }
    public uint Background { get; }
    public uint Cursor { get; }
    public uint Selection { get; }
    public uint[] Palette { get; }
    public uint[] FullPalette { get; }

    public TerminalTheme(
        uint foreground,
        uint background,
        uint cursor,
        uint selection,
        uint[] palette
    )
    {
        Foreground = foreground;
        Background = background;
        Cursor = cursor;
        Selection = selection;
        Palette = palette;
        FullPalette = PaletteGenerator.GenerateFullPalette(palette, background, foreground);
    }

    public uint GetColor(int index) =>
        index >= 0 && index < FullPalette.Length ? FullPalette[index] : Foreground;
}

public static class CatppuccinThemes
{
    public static TerminalTheme Latte { get; } =
        new(
            foreground: 0xFF4C4F69, // Text
            background: 0xFFEFF1F5, // Base
            cursor: 0xFFDC8A78, // Rosewater
            selection: 0xFFCCD0DA, // Surface0
            palette: new uint[]
            {
                0xFF5C5F77, // 0  Black    - Subtext1
                0xFFD20F39, // 1  Red
                0xFF40A02B, // 2  Green
                0xFFDF8E1D, // 3  Yellow
                0xFF1E66F5, // 4  Blue
                0xFFEA76CB, // 5  Magenta  - Pink
                0xFF179299, // 6  Cyan     - Teal
                0xFFACB0BE, // 7  White    - Surface2
                0xFF6C6F85, // 8  Bright Black  - Subtext0
                0xFFD20F39, // 9  Bright Red
                0xFF40A02B, // 10 Bright Green
                0xFFDF8E1D, // 11 Bright Yellow
                0xFF1E66F5, // 12 Bright Blue
                0xFFEA76CB, // 13 Bright Magenta
                0xFF179299, // 14 Bright Cyan
                0xFF7C7F93, // 15 Bright White  - Overlay2
            }
        );

    public static TerminalTheme Frappe { get; } =
        new(
            foreground: 0xFFC6D0F5, // Text
            background: 0xFF303446, // Base
            cursor: 0xFFF2D5CF, // Rosewater
            selection: 0xFF414559, // Surface0
            palette: new uint[]
            {
                0xFF51576D, // 0  Black    - Surface1
                0xFFE78284, // 1  Red
                0xFFA6D189, // 2  Green
                0xFFE5C890, // 3  Yellow
                0xFF8CAAEE, // 4  Blue
                0xFFF4B8E4, // 5  Magenta  - Pink
                0xFF81C8BE, // 6  Cyan     - Teal
                0xFFB5BFE2, // 7  White    - Subtext1
                0xFF626880, // 8  Bright Black  - Surface2
                0xFFE78284, // 9  Bright Red
                0xFFA6D189, // 10 Bright Green
                0xFFE5C890, // 11 Bright Yellow
                0xFF8CAAEE, // 12 Bright Blue
                0xFFF4B8E4, // 13 Bright Magenta
                0xFF81C8BE, // 14 Bright Cyan
                0xFFA5ADCE, // 15 Bright White  - Subtext0
            }
        );

    public static TerminalTheme Macchiato { get; } =
        new(
            foreground: 0xFFCAD3F5, // Text
            background: 0xFF24273A, // Base
            cursor: 0xFFF4DBD6, // Rosewater
            selection: 0xFF363A4F, // Surface0
            palette: new uint[]
            {
                0xFF494D64, // 0  Black    - Surface1
                0xFFED8796, // 1  Red
                0xFFA6DA95, // 2  Green
                0xFFEED49F, // 3  Yellow
                0xFF8AADF4, // 4  Blue
                0xFFF5BDE6, // 5  Magenta  - Pink
                0xFF8BD5CA, // 6  Cyan     - Teal
                0xFFB8C0E0, // 7  White    - Subtext1
                0xFF5B6078, // 8  Bright Black  - Surface2
                0xFFED8796, // 9  Bright Red
                0xFFA6DA95, // 10 Bright Green
                0xFFEED49F, // 11 Bright Yellow
                0xFF8AADF4, // 12 Bright Blue
                0xFFF5BDE6, // 13 Bright Magenta
                0xFF8BD5CA, // 14 Bright Cyan
                0xFFA5ADCB, // 15 Bright White  - Subtext0
            }
        );

    public static TerminalTheme Mocha { get; } =
        new(
            foreground: 0xFFCDD6F4, // Text
            background: 0xFF1E1E2E, // Base
            cursor: 0xFFF5E0DC, // Rosewater
            selection: 0xFF313244, // Surface0
            palette: new uint[]
            {
                0xFF45475A, // 0  Black    - Surface1
                0xFFF38BA8, // 1  Red
                0xFFA6E3A1, // 2  Green
                0xFFF9E2AF, // 3  Yellow
                0xFF89B4FA, // 4  Blue
                0xFFF5C2E7, // 5  Magenta  - Pink
                0xFF94E2D5, // 6  Cyan     - Teal
                0xFFBAC2DE, // 7  White    - Subtext1
                0xFF585B70, // 8  Bright Black  - Surface2
                0xFFF38BA8, // 9  Bright Red
                0xFFA6E3A1, // 10 Bright Green
                0xFFF9E2AF, // 11 Bright Yellow
                0xFF89B4FA, // 12 Bright Blue
                0xFFF5C2E7, // 13 Bright Magenta
                0xFF94E2D5, // 14 Bright Cyan
                0xFFA6ADC8, // 15 Bright White  - Subtext0
            }
        );
}
