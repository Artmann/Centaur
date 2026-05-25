namespace Centaur.Core.Terminal;

/// <summary>
/// An OSC 52 clipboard write/clear request. <see cref="Selection"/> is the
/// target ('c' = clipboard, 's'/'p' = primary selection). <see cref="Data"/> is
/// the base64-encoded payload to set, or empty to clear.
/// </summary>
public record ClipboardRequest(char Selection, string Data);
