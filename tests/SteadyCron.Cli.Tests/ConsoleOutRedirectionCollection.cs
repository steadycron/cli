using Xunit;

namespace SteadyCron.Cli.Tests;

/// <summary>
/// Groups every test that temporarily swaps the process-wide <see cref="Console.Out"/> (to
/// capture output written via <c>OutputContext.RawLine</c>/<c>WriteJson</c>, which deliberately
/// bypass the injected <c>IAnsiConsole</c> — see their doc comments). xUnit runs different test
/// collections in parallel by default; tests within the same collection never run concurrently
/// with each other. Without this, two such tests racing on the same global caused
/// <c>OutputContextTests.WriteJson_does_not_wrap_long_unbroken_strings</c> to intermittently fail.
/// </summary>
[CollectionDefinition("ConsoleOutRedirection")]
public sealed class ConsoleOutRedirectionCollection;
