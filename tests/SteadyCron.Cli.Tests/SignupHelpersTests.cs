using System.Net;
using System.Text;
using Spectre.Console.Testing;
using SteadyCron.Cli.Api;
using SteadyCron.Cli.Commands;
using SteadyCron.Cli.Output;
using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// Drives the interactive verify-code loop (<see cref="SignupHelpers.VerifyLoopAsync"/>) through
/// a real <see cref="TestConsole"/> — scripted keystrokes in, rendered output asserted on — same
/// idea as <see cref="SteadyCronClientTests"/>'s <c>StubHandler</c> but for the prompt layer.
/// </summary>
public sealed class SignupHelpersTests
{
    private static SteadyCronClient ClientWith(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new HttpClient(new StubHandler(handler)), "https://api.steadycron.com", apiKey: null);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task VerifyLoopAsync_wrongCodeThenCorrect_retriesAndSucceeds()
    {
        var attempt = 0;
        var client = ClientWith((_, _) =>
        {
            attempt++;
            return Task.FromResult(attempt == 1
                ? Json(HttpStatusCode.BadRequest,
                    """{"v":1,"status":"error","error":"invalid_code","message":"Incorrect code. 4 attempt(s) remaining."}""")
                : Json(HttpStatusCode.OK,
                    """{"v":1,"status":"ok","data":{"provisioning_token":"scpt_abc","expires_in":300}}"""));
        });

        var console = new TestConsole();
        console.Input.PushTextWithEnter("000000");
        console.Input.PushTextWithEnter("482913");
        var output = new OutputContext(json: false, quiet: false, console, console);

        var result = await SignupHelpers.VerifyLoopAsync(client, Guid.NewGuid(), output, CancellationToken.None);

        Assert.Equal("scpt_abc", result.ProvisioningToken);
        Assert.Equal(2, attempt);
        Assert.Contains("Incorrect code", console.Output);
    }

    [Fact]
    public async Task VerifyLoopAsync_lockedThenResend_sendsResendAndSucceedsOnNewCode()
    {
        var calls = new List<string>();
        var client = ClientWith((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            calls.Add(path);

            if (path.EndsWith("/resend", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """{"v":1,"status":"ok","data":{"dev_code":"111111"}}"""));
            }

            // verify: first call locked, second call (after resend) succeeds.
            return Task.FromResult(calls.Count(c => c.EndsWith("/verify", StringComparison.Ordinal)) == 1
                ? Json(HttpStatusCode.BadRequest,
                    """{"v":1,"status":"error","error":"code_expired_or_locked","message":"Too many incorrect attempts. Request a new code."}""")
                : Json(HttpStatusCode.OK,
                    """{"v":1,"status":"ok","data":{"provisioning_token":"scpt_xyz","expires_in":300}}"""));
        });

        var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("000000"); // locked
        console.Input.PushKey(ConsoleKey.Enter); // selection prompt: "Resend code" (first choice)
        console.Input.PushTextWithEnter("111111"); // succeeds after resend
        var output = new OutputContext(json: false, quiet: false, console, console);

        var result = await SignupHelpers.VerifyLoopAsync(client, Guid.NewGuid(), output, CancellationToken.None);

        Assert.Equal("scpt_xyz", result.ProvisioningToken);
        Assert.Contains(calls, c => c.EndsWith("/resend", StringComparison.Ordinal));
        Assert.Contains("new code has been sent", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyLoopAsync_lockedThenQuit_throwsCancelledCliException()
    {
        var client = ClientWith((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"v":1,"status":"error","error":"code_expired_or_locked","message":"Locked."}""")));

        var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("000000");
        console.Input.PushKey(ConsoleKey.DownArrow); // move to "Quit"
        console.Input.PushKey(ConsoleKey.Enter);
        var output = new OutputContext(json: false, quiet: false, console, console);

        var ex = await Assert.ThrowsAsync<CliException>(
            () => SignupHelpers.VerifyLoopAsync(client, Guid.NewGuid(), output, CancellationToken.None));

        Assert.Equal(ExitCodes.Cancelled, ex.ExitCode);
    }

    [Fact]
    public void ExtractSignupId_parsesDataSignupId()
    {
        var ex = new SteadyCronApiException(
            HttpStatusCode.Forbidden, "POST", "api/auth/cli/login", "email_not_verified", "Verify your email.",
            """{"v":1,"status":"error","error":"email_not_verified","message":"Verify your email.","data":{"signup_id":"0192a4e0-0000-7000-8000-000000000001"}}""");

        var signupId = SignupHelpers.ExtractSignupId(ex);

        Assert.Equal(Guid.Parse("0192a4e0-0000-7000-8000-000000000001"), signupId);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
