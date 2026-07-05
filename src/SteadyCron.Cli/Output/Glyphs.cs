using Spectre.Console;

namespace SteadyCron.Cli.Output;

/// <summary>
/// Status/decoration glyphs resolved once against a console's detected Unicode support.
/// Legacy Windows consoles (cmd.exe with the default raster font) can't render most Unicode
/// glyphs and print literal "?" boxes instead, so call sites ask for a glyph here rather than
/// hardcoding a Unicode character — this class picks the ASCII fallback automatically.
/// </summary>
public sealed class Glyphs
{
    public static readonly Glyphs Unicode = new(unicode: true);
    public static readonly Glyphs Ascii = new(unicode: false);

    private Glyphs(bool unicode)
    {
        Success = unicode ? "✓" : "v";
        Error = unicode ? "✗" : "x";
        Dot = unicode ? "●" : "*";
        Arrow = unicode ? "→" : "->";
        Bullet = unicode ? "·" : "-";
        NoValue = unicode ? "—" : "-";
        Times = unicode ? "×" : "x";
        Rule = unicode ? '─' : '-';
    }

    public string Success { get; }
    public string Error { get; }
    public string Dot { get; }
    public string Arrow { get; }
    public string Bullet { get; }
    public string NoValue { get; }
    public string Times { get; }
    public char Rule { get; }

    public static Glyphs For(IAnsiConsole console) =>
        console.Profile.Capabilities.Unicode ? Unicode : Ascii;
}
