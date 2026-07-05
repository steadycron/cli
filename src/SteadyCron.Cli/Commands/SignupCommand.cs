using System.Net.Mail;
using Spectre.Console;
using Spectre.Console.Cli;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Configuration;
using SteadyCron.Cli.Infrastructure;
using SteadyCron.Cli.Output;

namespace SteadyCron.Cli.Commands;

public sealed class SignupSettings : CliSettings
{
    [System.ComponentModel.Description("Overwrite an existing, still-working config without prompting.")]
    [CommandOption("--force")]
    public bool Force { get; set; }
}

/// <summary>
/// `steadycron signup` — account creation, email verification, and API key provisioning,
/// entirely in-terminal (SPEC-20b). Never touches the dashboard.
/// </summary>
public sealed class SignupCommand : SteadyCronCommandBase<SignupSettings>
{
    public SignupCommand(ConfigResolver resolver, SteadyCronClientFactory clientFactory, CancellationProvider cancellation)
        : base(resolver, clientFactory, cancellation)
    {
    }

    protected override async Task<int> RunAsync(SignupSettings settings, OutputContext output, CancellationToken ct)
    {
        if (!TerminalHelper.IsInteractive())
        {
            throw new CliException("signup requires an interactive terminal.", ExitCodes.Error);
        }

        var config = ResolveConfig(settings);
        if (!settings.Force)
        {
            await WarnIfExistingKeyWorksAsync(config, output, ct);
        }

        var client = ClientFactory.CreateUnauthenticated(config);

        var email = PromptEmail(output);
        var password = PromptPassword(output);

        var signup = await SignupHelpers.SignupOrSuggestLoginAsync(client, email, password, ct);

        output.Success($"Account created. Verification code sent to {email}.");

        var provisioning = await SignupHelpers.VerifyLoopAsync(client, signup.SignupId, output, ct);

        output.Success("Email verified.");

        await SignupHelpers.ProvisionAndSaveAsync(client, config, settings, provisioning.ProvisioningToken, output, ct);

        output.Success($"A default email alert channel was set up for {email}.");
        output.Line();
        output.Markup("Next: [cyan]steadycron init[/] — create your first monitored job.");

        return ExitCodes.Ok;
    }

    private static string PromptEmail(OutputContext output)
    {
        while (true)
        {
            var email = output.Out.Prompt(new TextPrompt<string>(PromptFormatting.Marker("Email:")));
            if (MailAddress.TryCreate(email, out _))
            {
                return email;
            }

            output.Warn($"'{email}' doesn't look like a valid email address.");
        }
    }

    private static string PromptPassword(OutputContext output)
    {
        while (true)
        {
            var password = output.Out.Prompt(new TextPrompt<string>(PromptFormatting.Marker("Password:")).Secret());
            if (password.Length >= 12)
            {
                return password;
            }

            output.Warn("Password must be at least 12 characters.");
        }
    }

    private async Task WarnIfExistingKeyWorksAsync(CliConfiguration config, OutputContext output, CancellationToken ct)
    {
        if (!config.HasApiKey)
        {
            return;
        }

        try
        {
            var probeClient = ClientFactory.Create(config);
            await probeClient.ListJobsAsync(page: 1, pageSize: 1, ct: ct);
        }
        catch (SteadyCronApiException ex) when (ex.IsAuthError)
        {
            return; // the configured key doesn't actually work — nothing to protect, proceed.
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return; // couldn't reach the API to check — don't block signup on that.
        }

        throw new CliException(
            "A working API key is already configured. Pass --force to overwrite it with a new account.",
            ExitCodes.Error);
    }
}
