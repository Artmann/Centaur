using Avalonia.Input.Platform;
using SkiaSharp;

namespace Centaur.App;

/// <summary>
/// The image half of paste: finding a picture on the system clipboard and putting it somewhere
/// the shell can name.
///
/// Windows offers the same picture under several formats at once - a screenshot from Win+Shift+S
/// arrives as registered "PNG" alongside the predefined CF_DIB and CF_DIBV5 - so the clipboard is
/// probed in preference order rather than asked for one thing. Whatever comes back is re-encoded
/// as PNG, which is the only format every consumer of a pasted path agrees on.
/// </summary>
public static class ClipboardImage
{
    /// <summary>
    /// Clipboard format names that carry a picture, best first. PNG needs no repair; the DIB
    /// forms are a BMP with its file header cut off and are reassembled below. Avalonia's Win32
    /// backend names predefined formats it has no mapping for as Unknown_Format_&lt;id&gt;, which is
    /// how CF_DIB (8) and CF_DIBV5 (17) surface.
    /// </summary>
    static readonly string[] imageFormats =
    [
        "PNG",
        "image/png",
        "Unknown_Format_17",
        "Unknown_Format_8",
        "DeviceIndependentBitmap",
        "image/bmp",
        "image/jpeg",
        "JFIF",
        "GIF",
    ];

    /// <summary>True when the clipboard is offering something this type can turn into a PNG.</summary>
    public static async Task<byte[]?> ReadAsync(IClipboard clipboard)
    {
        var available = await clipboard.GetFormatsAsync();
        foreach (var format in imageFormats)
        {
            if (!available.Contains(format, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await clipboard.GetDataAsync(format) is byte[] { Length: > 0 } bytes)
            {
                return bytes;
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes clipboard bytes and writes them to <paramref name="directory"/> as a PNG,
    /// returning the full path - or null when the bytes are not a picture after all.
    /// Failures to write are thrown, not swallowed: the caller has to be able to say why the
    /// paste produced nothing.
    /// </summary>
    public static string? Save(byte[] bytes, string directory)
    {
        using var bitmap = Decode(bytes);
        if (bitmap == null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded == null)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        var path = UnusedPath(directory);

        // Written through a temp file, so an interrupted save cannot leave a truncated PNG
        // behind under a name the user has already been handed.
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, encoded.ToArray());
        File.Move(tempPath, path);
        return path;
    }

    /// <summary>Wraps a path so a space in it does not split the shell's argument. Windows
    /// filenames cannot contain a double quote, so wrapping is the whole job.</summary>
    public static string Quote(string path) => '"' + path + '"';

    static string UnusedPath(string directory)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", null);
        for (var suffix = 0; ; suffix++)
        {
            var name = suffix == 0 ? $"paste-{stamp}.png" : $"paste-{stamp}-{suffix}.png";
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    static SKBitmap? Decode(byte[] bytes)
    {
        var decoded = TryDecode(bytes);
        if (decoded != null)
        {
            return decoded;
        }

        // Not a self-describing image, so it is most likely a bare DIB off the Windows
        // clipboard: a BMP whose 14-byte file header was stripped. Put one back.
        var repaired = RestoreBitmapFileHeader(bytes);
        return repaired == null ? null : TryDecode(repaired);
    }

    /// <summary>Decodes, or gives up quietly. Asking for the codec first keeps unrecognized
    /// bytes - which is the whole point of probing - from arriving as an exception.</summary>
    static SKBitmap? TryDecode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return codec == null ? null : SKBitmap.Decode(codec);
    }

    /// <summary>
    /// Rebuilds the BITMAPFILEHEADER in front of a CF_DIB payload. The only field that takes any
    /// thought is where the pixels start: past the DIB header, past the colour table, and past
    /// the three channel masks a BI_BITFIELDS image keeps outside its header.
    /// </summary>
    static byte[]? RestoreBitmapFileHeader(byte[] dib)
    {
        const int fileHeaderSize = 14;
        if (dib.Length < 40)
        {
            return null;
        }

        var headerSize = BitConverter.ToInt32(dib, 0);
        if (headerSize is < 40 or > 124 || headerSize > dib.Length)
        {
            return null;
        }

        var bitCount = BitConverter.ToInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var paletteEntries = BitConverter.ToInt32(dib, 32);
        if (paletteEntries == 0 && bitCount <= 8)
        {
            paletteEntries = 1 << bitCount;
        }

        // BI_BITFIELDS masks follow a plain BITMAPINFOHEADER; V4 and V5 headers hold them
        // inside the header instead, so they are already counted.
        var maskBytes = compression == 3 && headerSize == 40 ? 12 : 0;
        var pixelOffset = fileHeaderSize + headerSize + maskBytes + paletteEntries * 4;
        if (pixelOffset > fileHeaderSize + dib.Length)
        {
            return null;
        }

        var file = new byte[fileHeaderSize + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.TryWriteBytes(file.AsSpan(2), file.Length);
        BitConverter.TryWriteBytes(file.AsSpan(10), pixelOffset);
        dib.CopyTo(file, fileHeaderSize);
        return file;
    }
}
