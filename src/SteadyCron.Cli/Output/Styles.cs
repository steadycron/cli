namespace SteadyCron.Cli.Output;

/// <summary>
/// Central Spectre.Console style names for the CLI's output style guide (see docs/style-guide.md).
/// Reference these instead of a literal color name so the palette is a one-file change, and so
/// "grey" doesn't quietly creep back in as the path of least resistance for de-emphasis.
///
/// Labels and body text intentionally have no constant here: the style guide's rule for them is
/// "no color at all" (the terminal's default foreground), so the correct call is to not wrap them
/// in a markup tag in the first place.
/// </summary>
public static class Styles
{
    /// <summary>The ✓/v success glyph.</summary>
    public const string Success = "green";

    /// <summary>The leading "?" marker on interactive prompts.</summary>
    public const string Prompt = "yellow";

    /// <summary>The !/warning glyph.</summary>
    public const string Warning = "yellow";

    /// <summary>The ✗/x error glyph.</summary>
    public const string Error = "red";

    /// <summary>A literal CLI command the user can run, e.g. `steadycron apply steadycron.yaml`.</summary>
    public const string Command = "cyan";

    /// <summary>A user-critical value: job name, email, ping URL.</summary>
    public const string Critical = "bold";

    /// <summary>
    /// The single most action-critical line in a flow (the heartbeat ping snippet to paste into a
    /// cron command) — must be the most visible line on screen, never dim.
    /// </summary>
    public const string PingSnippet = "bold green";

    /// <summary>
    /// Optional hints only: default-value hints in brackets, footnotes, and
    /// <see cref="OutputContext.Info"/>'s secondary status asides. Nothing else — see
    /// docs/style-guide.md for the full rule.
    /// </summary>
    public const string Hint = "grey";

    /// <summary>Table/rule borders and dividers. Cell and title content is never this color.</summary>
    public const string Border = "grey";
}
