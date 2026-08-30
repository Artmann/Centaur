namespace Centaur.App;

/// <summary>
/// Infers the shell's new working directory from a submitted command line.
///
/// The shell does not tell us where it is, so we watch for the handful of commands that
/// change directory and resolve the argument ourselves. This is best-effort: anything we
/// cannot resolve to an existing directory is reported as "no change" rather than guessed.
/// </summary>
public static class WorkingDirectoryTracker
{
    // Both cmd- and PowerShell-flavoured spellings, including PowerShell's `sl` alias.
    static readonly string[] prefixes = ["cd ", "Set-Location ", "pushd ", "chdir ", "sl "];

    /// <summary>
    /// Returns the directory <paramref name="command"/> would move the shell into, or null
    /// when it is not a directory change or the target does not exist.
    /// </summary>
    /// <param name="baseDirectory">Directory a relative target is resolved against.</param>
    public static string? Resolve(string command, string? baseDirectory)
    {
        var target = ExtractTarget(command);
        if (target == null)
        {
            return null;
        }

        target = ExpandHome(target);

        if (!Path.IsPathRooted(target) && !string.IsNullOrEmpty(baseDirectory))
        {
            target = Path.GetFullPath(Path.Combine(baseDirectory, target));
        }

        return Directory.Exists(target) ? target : null;
    }

    static string? ExtractTarget(string command)
    {
        foreach (var prefix in prefixes)
        {
            if (command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return command[prefix.Length..].Trim().Trim('"', '\'');
            }
        }

        return null;
    }

    static string ExpandHome(string target)
    {
        var isHome =
            target == "~"
            || target.StartsWith("~/", StringComparison.Ordinal)
            || target.StartsWith("~\\", StringComparison.Ordinal);

        if (!isHome)
        {
            return target;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            target[1..].TrimStart('/', '\\')
        );
    }
}
