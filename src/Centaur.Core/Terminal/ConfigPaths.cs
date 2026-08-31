namespace Centaur.Core.Terminal;

/// <summary>
/// Resolves where the app keeps its JSON state - the session layout, the settings and the
/// command history.
///
/// Normally that is %APPDATA%\Centaur. Setting <c>CENTAUR_CONFIG_DIR</c> moves the whole set
/// somewhere else, which is what lets a second instance run without inheriting - and then
/// overwriting - the tabs, history and settings belonging to the one the user already has
/// open. Redirecting the APPDATA environment variable does not work for this: Windows
/// resolves ApplicationData through SHGetKnownFolderPath, which reads the user's profile
/// rather than the environment.
/// </summary>
public static class ConfigPaths
{
    const string overrideVariable = "CENTAUR_CONFIG_DIR";

    /// <param name="readEnvironment">
    /// How to look a variable up. Defaults to the process environment; tests pass their own
    /// so they do not have to mutate process-global state to cover both branches.
    /// </param>
    public static string Root(Func<string, string?>? readEnvironment = null)
    {
        var read = readEnvironment ?? Environment.GetEnvironmentVariable;
        var overridden = read(overrideVariable);

        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Centaur"
        );
    }

    public static string For(string fileName, Func<string, string?>? readEnvironment = null) =>
        Path.Combine(Root(readEnvironment), fileName);
}
