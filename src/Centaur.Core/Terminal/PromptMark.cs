namespace Centaur.Core.Terminal;

/// <summary>
/// Semantic-prompt region a row belongs to, set by OSC 133 marks (A/B/C).
/// Powers jump-to-prompt navigation and command-region detection.
/// </summary>
public enum PromptMark
{
    None,
    Prompt,
    Command,
    Output,
}
