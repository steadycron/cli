using Spectre.Console;
using Spectre.Console.Rendering;
using SteadyCron.Cli.Api;

namespace SteadyCron.Cli.Output;

/// <summary>
/// Centralizes all console output: human-readable messages and tables on stdout, diagnostics on
/// stderr, and a clean <c>--json</c> mode where stdout carries only JSON. Honours <c>--quiet</c>
/// and <c>--no-color</c>.
/// </summary>
public sealed class OutputContext
{
    public OutputContext(bool json, bool quiet, bool noColor)
    {
        Json = json;
        Quiet = quiet;
        var colors = noColor ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect;

        Out = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Out),
            ColorSystem = colors,
        });
        Err = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error),
            ColorSystem = colors,
        });
    }

    public bool Json { get; }

    public bool Quiet { get; }

    /// <summary>Primary console (stdout).</summary>
    public IAnsiConsole Out { get; }

    /// <summary>Diagnostics console (stderr).</summary>
    public IAnsiConsole Err { get; }

    /// <summary>Writes a JSON document to stdout verbatim (no markup interpretation). Always emitted.</summary>
    public void WriteJson<T>(T value) => Out.WriteLine(OutputJson.Serialize(value));

    /// <summary>
    /// Writes a plain line of primary output to stdout. Suppressed only in <c>--json</c> mode
    /// (where stdout is reserved for JSON); <c>--quiet</c> does not suppress primary data.
    /// </summary>
    public void Line(string text = "")
    {
        if (Json)
        {
            return;
        }

        Out.WriteLine(text);
    }

    /// <summary>Writes a markup line to stdout (suppressed by <c>--quiet</c> / <c>--json</c>).</summary>
    public void Markup(string markup)
    {
        if (Quiet || Json)
        {
            return;
        }

        Out.MarkupLine(markup);
    }

    public void Info(string message)
    {
        if (Quiet || Json)
        {
            return;
        }

        Out.MarkupLine($"[grey]{Escape(message)}[/]");
    }

    public void Success(string message)
    {
        if (Quiet || Json)
        {
            return;
        }

        Out.MarkupLine($"[green]✓[/] {Escape(message)}");
    }

    /// <summary>Warnings always go to stderr so stdout (data) stays clean.</summary>
    public void Warn(string message) =>
        Err.MarkupLine($"[yellow]![/] {Escape(message)}");

    /// <summary>Errors always go to stderr.</summary>
    public void Error(string message) =>
        Err.MarkupLine($"[red]✗[/] {Escape(message)}");

    public void ApiError(SteadyCronApiException ex)
    {
        var code = ex.ErrorCode is null ? string.Empty : $" [grey]({Escape(ex.ErrorCode)})[/]";
        Err.MarkupLine($"[red]✗[/] {Escape(ex.Message)}{code}");
    }

    /// <summary>Renders a table/tree etc. as primary output. Suppressed only in <c>--json</c> mode.</summary>
    public void Render(IRenderable renderable)
    {
        if (Json)
        {
            return;
        }

        Out.Write(renderable);
    }

    public static string Escape(string text) => Spectre.Console.Markup.Escape(text);
}
