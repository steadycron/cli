namespace SteadyCron.Cli.Output;

/// <summary>Applies the style guide's yellow "?" marker to an interactive prompt's title.</summary>
public static class PromptFormatting
{
    public static string Marker(string title) => $"[{Styles.Prompt}]?[/] {title}";
}
