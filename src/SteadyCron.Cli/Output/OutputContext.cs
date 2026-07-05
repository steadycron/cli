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
        Glyphs = Glyphs.For(Out);
    }

    /// <summary>Test seam: inject consoles directly (e.g. Spectre.Console.Testing's <c>TestConsole</c>)
    /// instead of binding to the real <see cref="Console"/> streams.</summary>
    public OutputContext(bool json, bool quiet, IAnsiConsole @out, IAnsiConsole err)
    {
        Json = json;
        Quiet = quiet;
        Out = @out;
        Err = err;
        Glyphs = Glyphs.For(Out);
    }

    public bool Json { get; }

    public bool Quiet { get; }

    /// <summary>Primary console (stdout).</summary>
    public IAnsiConsole Out { get; }

    /// <summary>Diagnostics console (stderr).</summary>
    public IAnsiConsole Err { get; }

    /// <summary>Status glyphs, resolved against <see cref="Out"/>'s detected Unicode support.</summary>
    public Glyphs Glyphs { get; }

    /// <summary>
    /// Writes a JSON document to stdout verbatim (no markup interpretation). Always emitted.
    /// Bypasses <see cref="Out"/>/Spectre.Console entirely: <c>IAnsiConsole.WriteLine</c> renders
    /// plain text through the same word-wrapping pipeline as markup, so a long unbroken string
    /// (a URL, an error message, ...) gets a literal newline inserted at the console width —
    /// silently producing invalid JSON. Writing straight to <see cref="Console.Out"/> sends the
    /// same bytes to the same stream without going through that renderer.
    /// </summary>
    public void WriteJson<T>(T value) => Console.Out.WriteLine(OutputJson.Serialize(value));

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

    /// <summary>
    /// Writes a line of primary output verbatim, bypassing Spectre's word-wrap (and markup
    /// parsing) entirely — for content the user needs to copy byte-for-byte (a URL, a markdown
    /// snippet) where a wrapped line could read as broken, or corrupt the copy in a terminal that
    /// doesn't track soft-wrapped lines. Suppressed by <c>--quiet</c>/<c>--json</c> like <see cref="Line"/>.
    /// </summary>
    public void RawLine(string text)
    {
        if (Quiet || Json)
        {
            return;
        }

        Console.Out.WriteLine(text);
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

    /// <summary>
    /// A secondary status aside — a row count, "Aborted.", "Stopped." — never the primary answer
    /// to the command, just context around it. This is the one deliberate use of
    /// <see cref="Styles.Hint"/> outside default-value hints and footnotes; see docs/style-guide.md.
    /// </summary>
    public void Info(string message)
    {
        if (Quiet || Json)
        {
            return;
        }

        Out.MarkupLine($"[{Styles.Hint}]{Escape(message)}[/]");
    }

    public void Success(string message)
    {
        if (Quiet || Json)
        {
            return;
        }

        Out.MarkupLine($"[{Styles.Success}]{Glyphs.Success}[/] {Escape(message)}");
    }

    /// <summary>Warnings always go to stderr so stdout (data) stays clean.</summary>
    public void Warn(string message) =>
        Err.MarkupLine($"[{Styles.Warning}]![/] {Escape(message)}");

    /// <summary>Errors always go to stderr.</summary>
    public void Error(string message) =>
        Err.MarkupLine($"[{Styles.Error}]{Glyphs.Error}[/] {Escape(message)}");

    public void ApiError(SteadyCronApiException ex)
    {
        var code = ex.ErrorCode is null ? string.Empty : $" ({Escape(ex.ErrorCode)})";
        Err.MarkupLine($"[{Styles.Error}]{Glyphs.Error}[/] {Escape(ex.Message)}{code}");
    }

    /// <summary>
    /// The exact "no credentials configured" block from SPEC-20b §3.4 — deliberately
    /// undecorated (no ✗/! glyph, unlike <see cref="Error"/>/<see cref="Warn"/>) since none
    /// appears in the spec's literal text.
    /// </summary>
    public void AuthRequiredMessage()
    {
        Err.WriteLine("No credentials found.");
        Err.WriteLine(string.Empty);
        Err.WriteLine("  New here?           steadycron signup");
        Err.WriteLine("  Already registered? steadycron login");
        Err.WriteLine(string.Empty);
        Err.WriteLine("(exit 4)");
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
