namespace Centaur.Core.Terminal;

public readonly record struct Lab(double L, double a, double b);

public static class PaletteGenerator
{
    public static uint[] GenerateFullPalette(uint[] base16, uint background, uint foreground)
    {
        var palette = new uint[256];

        // 0-15: copy base16
        Array.Copy(base16, palette, 16);

        // 16-231: 6x6x6 color cube via trilinear CIELAB interpolation
        var bgLab = RgbToLab(background);
        var fgLab = RgbToLab(foreground);

        // Map base16 ANSI colors to cube corners
        var cornerLabs = new Lab[8];
        cornerLabs[0] = bgLab; // (0,0,0) = background
        cornerLabs[1] = RgbToLab(base16[1]); // (5,0,0) = red
        cornerLabs[2] = RgbToLab(base16[2]); // (0,5,0) = green
        cornerLabs[3] = RgbToLab(base16[3]); // (5,5,0) = yellow
        cornerLabs[4] = RgbToLab(base16[4]); // (0,0,5) = blue
        cornerLabs[5] = RgbToLab(base16[5]); // (5,0,5) = magenta
        cornerLabs[6] = RgbToLab(base16[6]); // (0,5,5) = cyan
        cornerLabs[7] = fgLab; // (5,5,5) = foreground

        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    var index = 16 + (36 * r) + (6 * g) + b;
                    var tr = r / 5.0;
                    var tg = g / 5.0;
                    var tb = b / 5.0;
                    palette[index] = LabToRgb(TrilinearInterp(cornerLabs, tr, tg, tb));
                }
            }
        }

        // 232-255: 24-step grayscale ramp from background to foreground
        for (int i = 0; i < 24; i++)
        {
            var t = i / 23.0;
            palette[232 + i] = LabToRgb(LerpLab(t, bgLab, fgLab));
        }

        return palette;
    }

    static Lab TrilinearInterp(Lab[] corners, double tr, double tg, double tb)
    {
        // corners: [0]=000, [1]=R00, [2]=0G0, [3]=RG0, [4]=00B, [5]=R0B, [6]=0GB, [7]=RGB
        var c00 = LerpLab(tr, corners[0], corners[1]);
        var c10 = LerpLab(tr, corners[2], corners[3]);
        var c01 = LerpLab(tr, corners[4], corners[5]);
        var c11 = LerpLab(tr, corners[6], corners[7]);

        var c0 = LerpLab(tg, c00, c10);
        var c1 = LerpLab(tg, c01, c11);

        return LerpLab(tb, c0, c1);
    }

    static Lab LerpLab(double t, Lab a, Lab b)
    {
        return new Lab(a.L + t * (b.L - a.L), a.a + t * (b.a - a.a), a.b + t * (b.b - a.b));
    }

    public static Lab RgbToLab(uint argb)
    {
        var r = ((argb >> 16) & 0xFF) / 255.0;
        var g = ((argb >> 8) & 0xFF) / 255.0;
        var b = (argb & 0xFF) / 255.0;

        // sRGB to linear
        r = r > 0.04045 ? Math.Pow((r + 0.055) / 1.055, 2.4) : r / 12.92;
        g = g > 0.04045 ? Math.Pow((g + 0.055) / 1.055, 2.4) : g / 12.92;
        b = b > 0.04045 ? Math.Pow((b + 0.055) / 1.055, 2.4) : b / 12.92;

        // Linear RGB to XYZ (D65)
        var x = r * 0.4124564 + g * 0.3575761 + b * 0.1804375;
        var y = r * 0.2126729 + g * 0.7151522 + b * 0.0721750;
        var z = r * 0.0193339 + g * 0.1191920 + b * 0.9503041;

        // XYZ to Lab (D65 white point)
        x /= 0.95047;
        y /= 1.00000;
        z /= 1.08883;

        x = x > 0.008856 ? Math.Cbrt(x) : 7.787 * x + 16.0 / 116.0;
        y = y > 0.008856 ? Math.Cbrt(y) : 7.787 * y + 16.0 / 116.0;
        z = z > 0.008856 ? Math.Cbrt(z) : 7.787 * z + 16.0 / 116.0;

        return new Lab(116.0 * y - 16.0, 500.0 * (x - y), 200.0 * (y - z));
    }

    public static uint LabToRgb(Lab lab)
    {
        var y = (lab.L + 16.0) / 116.0;
        var x = lab.a / 500.0 + y;
        var z = y - lab.b / 200.0;

        var x3 = x * x * x;
        var y3 = y * y * y;
        var z3 = z * z * z;

        x = (x3 > 0.008856 ? x3 : (x - 16.0 / 116.0) / 7.787) * 0.95047;
        y = y3 > 0.008856 ? y3 : (y - 16.0 / 116.0) / 7.787;
        z = (z3 > 0.008856 ? z3 : (z - 16.0 / 116.0) / 7.787) * 1.08883;

        // XYZ to linear RGB
        var r = x * 3.2404542 + y * -1.5371385 + z * -0.4985314;
        var g = x * -0.9692660 + y * 1.8760108 + z * 0.0415560;
        var b = x * 0.0556434 + y * -0.2040259 + z * 1.0572252;

        // Linear to sRGB
        r = r > 0.0031308 ? 1.055 * Math.Pow(r, 1.0 / 2.4) - 0.055 : 12.92 * r;
        g = g > 0.0031308 ? 1.055 * Math.Pow(g, 1.0 / 2.4) - 0.055 : 12.92 * g;
        b = b > 0.0031308 ? 1.055 * Math.Pow(b, 1.0 / 2.4) - 0.055 : 12.92 * b;

        var ri = (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255);
        var gi = (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255);
        var bi = (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255);

        return 0xFF000000u | ((uint)ri << 16) | ((uint)gi << 8) | bi;
    }
}
