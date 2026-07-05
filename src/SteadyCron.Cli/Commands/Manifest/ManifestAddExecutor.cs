using Spectre.Console;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Manifest;
using SteadyCron.Cli.Manifest.Generators;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands.Manifest;

/// <summary>
/// The shared tail of every <c>manifest add &lt;resource&gt;</c> command (SPEC-21 §3.1 steps
/// 1, 4-6): resolve/create the target file, check for a duplicate, honor <c>--dry-run</c>, and
/// perform the validated write. Resource-specific flag/prompt collection and block rendering stay
/// in each command — this only owns the file-level mechanics every resource shares identically.
/// </summary>
internal static class ManifestAddExecutor
{
    public static async Task<int> ExecuteAsync(
        OutputContext output,
        ManifestAddSettingsBase settings,
        ManifestSection section,
        string block,
        Func<IManifestFileEditor, string, bool> isDuplicate,
        string duplicateMessage,
        string resourceDescription)
    {
        var path = settings.TargetPath;

        if (Directory.Exists(path))
        {
            output.Error($"'{path}' is a directory. Pass -f/--file with a manifest file path.");
            return ExitCodes.Error;
        }

        if (settings.DryRun)
        {
            Console.Write(block);
            return ExitCodes.Ok;
        }

        IManifestFileEditor editor = new YamlSectionAppendEditor();
        string existingContent;

        if (File.Exists(path))
        {
            existingContent = await File.ReadAllTextAsync(path);
        }
        else
        {
            // Non-interactive (e.g. CI/scripts): proceed without asking — this is purely additive,
            // and flags-only invocations must never block on a prompt that can't be answered.
            if (TerminalHelper.IsInteractive() && !AnsiConsole.Confirm(PromptFormatting.Marker($"{path} doesn't exist. Create it?")))
            {
                output.Info("Aborted.");
                return ExitCodes.Ok;
            }

            existingContent = editor.EmptyFileHeader();
        }

        string candidate;
        try
        {
            if (isDuplicate(editor, existingContent))
            {
                output.Error(duplicateMessage);
                return ExitCodes.ManifestError;
            }

            var writer = new ManifestAddWriter(editor, new ManifestLoader(), new ManifestValidator());
            var (built, errors) = writer.BuildAndValidate(existingContent, section, block);

            if (built is null)
            {
                foreach (var error in errors)
                {
                    output.Error(error);
                }

                return ExitCodes.ManifestError;
            }

            candidate = built;
        }
        catch (ManifestEditException ex)
        {
            output.Error(ex.Message);
            return ExitCodes.ManifestError;
        }

        await File.WriteAllTextAsync(path, candidate);
        output.Success($"Added {resourceDescription} to {path}");
        output.Markup($"Preview: [cyan]steadycron plan {path}[/]");
        return ExitCodes.Ok;
    }
}
