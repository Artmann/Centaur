namespace Centaur.Tests;

/// <summary>
/// A throwaway directory for tests that touch the file system, removed on dispose.
/// Derive from this instead of hand-rolling a temp directory per test class.
/// </summary>
public abstract class TempDirectory : IDisposable
{
    protected string TempDir { get; }

    protected TempDirectory()
    {
        TempDir = Path.Combine(Path.GetTempPath(), "centaur-test-" + Guid.NewGuid());
        Directory.CreateDirectory(TempDir);
    }

    protected string TempFile(string name) => Path.Combine(TempDir, name);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(TempDir))
        {
            Directory.Delete(TempDir, true);
        }
    }
}
