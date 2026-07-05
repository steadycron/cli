# CLI output style guide

Every command renders through [`OutputContext`](../src/SteadyCron.Cli/Output/OutputContext.cs). This
guide covers the two rules that keep that output consistent and readable everywhere the CLI runs,
including legacy Windows consoles (`cmd.exe`) that can't render most Unicode glyphs or a low-contrast
grey.

## 1. Never hardcode a status glyph — use `Glyphs`

Legacy `cmd.exe` (the classic Command Prompt, not the modern Windows Terminal app) uses a raster font
with very limited Unicode glyph coverage. A literal `✓`, `✗`, `●`, `→`, `·`, `—`, `×`, or `─` in a
markup string prints as a `?` box on that terminal, regardless of code page.

[`Output/Glyphs.cs`](../src/SteadyCron.Cli/Output/Glyphs.cs) resolves each of these to an ASCII
fallback automatically, based on the console's detected Unicode support
(`IAnsiConsole.Profile.Capabilities.Unicode`):

| Glyph | Unicode | ASCII fallback |
|---|---|---|
| `Glyphs.Success` | `✓` | `v` |
| `Glyphs.Error` | `✗` | `x` |
| `Glyphs.Dot` | `●` | `*` |
| `Glyphs.Arrow` | `→` | `->` |
| `Glyphs.Bullet` | `·` | `-` |
| `Glyphs.NoValue` | `—` | `-` |
| `Glyphs.Times` | `×` | `x` |
| `Glyphs.Rule` (a `char`, for `new string(glyphs.Rule, n)`) | `─` | `-` |

Every `OutputContext` exposes its resolved set as `output.Glyphs`. A static top-level exception
handler that runs before any `OutputContext` exists (e.g. `Program.cs`'s `SetExceptionHandler`, or a
command that doesn't take a `CliSettings`/API dependency) can resolve the same thing directly:
`Glyphs.For(AnsiConsole.Console)`.

A shared static formatter that doesn't already receive an `OutputContext` (e.g.
`LogbookFormatting.SeverityDot`) takes a `Glyphs` parameter instead — thread it through rather than
falling back to a literal character.

`!` (the warning glyph) is already ASCII-safe and doesn't need this treatment.

## 2. One style set, no ad-hoc `grey`

[`Output/Styles.cs`](../src/SteadyCron.Cli/Output/Styles.cs) is the single source of truth for output
color. Reference its constants instead of a literal color name in markup:

| Element | Style | Rule |
|---|---|---|
| Success marker (`Glyphs.Success`) | `Styles.Success` (green) | |
| Prompt marker `?` | `Styles.Prompt` (yellow) | Every `TextPrompt`/`SelectionPrompt`/`Confirm` title goes through `PromptFormatting.Marker(title)`, which prepends this. |
| Labels / body text | *(no markup tag — the terminal's default foreground)* | Never grey for content the user must read. This covers field labels, table cell content, job names, error messages, arrows/bullets joining values, etc. |
| Commands to run | `Styles.Command` (cyan) | e.g. `steadycron apply steadycron.yaml`. |
| User-critical values (job name, email, ping URL) | `Styles.Critical` (bold) | |
| The heartbeat ping snippet | `Styles.PingSnippet` (bold green) | The single most action-critical line in a flow — must be the most visible, never dim. |
| Optional hints only | `Styles.Hint` (grey) | Default-value hints in brackets (`[*/15 * * * *]`), footnotes (e.g. the login command's key-hygiene tip), and `OutputContext.Info()`'s secondary status asides (a row count, "Aborted.", "Stopped."). Nothing else. |
| Table/rule borders | `Styles.Border` (grey) | The border itself only — column headers and cell content are never this color. |

If you're tempted to reach for `[grey]` or `[dim]` for anything not in that "optional hints" row,
don't — it's very likely a label or body-text case that should carry no color tag at all.

## Where this came from

A user reported that `logstream`'s success dot rendered as a `?` on Windows `cmd.exe`, which led to
an audit of every hardcoded glyph and `grey` usage across the CLI — several genuinely important
values (job names, failure details, plan diffs) were being dimmed to grey with no fallback for
non-Unicode terminals. `Glyphs` and `Styles` exist to make both mistakes structurally harder to
reintroduce.
