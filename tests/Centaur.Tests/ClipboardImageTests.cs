using Centaur.App;
using SkiaSharp;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// The half of image paste that does not need a window: turning whatever bytes the clipboard
/// held into a PNG on disk, and quoting the path so it survives a shell that splits on spaces.
/// </summary>
public class ClipboardImageTests : TempDirectory
{
    static byte[] SamplePng(int width = 4, int height = 3)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(0x40, 0xA0, 0x60));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A bare 32-bpp top-down BITMAPINFOHEADER DIB, which is the shape CF_DIB takes on
    /// the Windows clipboard: a BMP with the 14-byte file header stripped off. Built by hand
    /// because Skia decodes BMP but will not encode it.</summary>
    static byte[] SampleDib(int width = 4, int height = 3)
    {
        var pixels = width * height * 4;
        var dib = new byte[40 + pixels];
        BitConverter.TryWriteBytes(dib.AsSpan(0), 40); // biSize
        BitConverter.TryWriteBytes(dib.AsSpan(4), width); // biWidth
        BitConverter.TryWriteBytes(dib.AsSpan(8), -height); // biHeight, negative = top-down
        BitConverter.TryWriteBytes(dib.AsSpan(12), (short)1); // biPlanes
        BitConverter.TryWriteBytes(dib.AsSpan(14), (short)32); // biBitCount
        BitConverter.TryWriteBytes(dib.AsSpan(20), pixels); // biSizeImage

        for (var i = 40; i < dib.Length; i += 4)
        {
            dib[i] = 0x60; // blue
            dib[i + 1] = 0xA0; // green
            dib[i + 2] = 0x40; // red
            dib[i + 3] = 0xFF; // alpha
        }

        return dib;
    }

    [Fact]
    public void Save_WritesPngToTheGivenDirectory()
    {
        var path = ClipboardImage.Save(SamplePng(), TempDir);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(".png", Path.GetExtension(path));
        Assert.Equal(TempDir, Path.GetDirectoryName(path));
    }

    [Fact]
    public void Save_ProducesADecodablePngOfTheOriginalSize()
    {
        var path = ClipboardImage.Save(SamplePng(width: 7, height: 5), TempDir);

        using var written = SKBitmap.Decode(path);
        Assert.NotNull(written);
        Assert.Equal(7, written.Width);
        Assert.Equal(5, written.Height);
    }

    [Fact]
    public void Save_AcceptsARawDibFromTheWindowsClipboard()
    {
        var path = ClipboardImage.Save(SampleDib(), TempDir);

        Assert.NotNull(path);
        using var written = SKBitmap.Decode(path);
        Assert.NotNull(written);
        Assert.Equal(4, written.Width);
    }

    [Fact]
    public void Save_NamesFilesSoTwoPastesDoNotCollide()
    {
        var first = ClipboardImage.Save(SamplePng(), TempDir);
        var second = ClipboardImage.Save(SamplePng(), TempDir);

        Assert.NotEqual(first, second);
        Assert.StartsWith("paste-", Path.GetFileName(first!), StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(TempDir, "*.png").Length);
    }

    [Fact]
    public void Save_ReturnsNullWhenTheBytesAreNotAnImage()
    {
        Assert.Null(ClipboardImage.Save([1, 2, 3, 4], TempDir));
    }

    [Fact]
    public void Save_LeavesNoPartialFileWhenTheBytesAreNotAnImage()
    {
        ClipboardImage.Save([1, 2, 3, 4], TempDir);
        Assert.Empty(Directory.GetFiles(TempDir));
    }

    [Fact]
    public void Quote_WrapsPathsSoASpaceDoesNotSplitTheArgument()
    {
        // Windows filenames cannot contain a double quote, so wrapping is all that is needed.
        Assert.Equal(
            "\"C:\\Users\\Ada Lovelace\\a.png\"",
            ClipboardImage.Quote("C:\\Users\\Ada Lovelace\\a.png")
        );
    }

    [Fact]
    public void Save_CreatesTheDirectoryItWasPointedAt()
    {
        var fresh = Path.Combine(TempDir, "Centaur");
        var path = ClipboardImage.Save(SamplePng(), fresh);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_SurfacesAWriteFailureRatherThanSwallowingIt()
    {
        // A file sitting where the directory should be: the caller has to hear about this
        // so it can say why the paste produced nothing.
        var blocked = TempFile("blocked");
        File.WriteAllText(blocked, "not a directory");

        Assert.ThrowsAny<IOException>(() => ClipboardImage.Save(SamplePng(), blocked));
    }
}
