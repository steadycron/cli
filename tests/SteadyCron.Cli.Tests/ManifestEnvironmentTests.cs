using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class ManifestEnvironmentTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFile(string dir, string name, string content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Enforces_env_file_when_required_placeholder_present()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "m.yaml", "version: 2\nchannels:\n  - name: c\n    kind: slack\n    config:\n      webhook_url: ${SC_C_WEBHOOK_URL}\n");

        var ex = Assert.Throws<ManifestException>(() =>
            ManifestEnvironment.Build([dir], [], allowProcessEnv: false, enforceEnvFile: true));

        Assert.Contains("--env-file", ex.Message);
        Assert.Contains("SC_C_WEBHOOK_URL", ex.Message);
    }

    [Fact]
    public void Allows_process_env_bypass()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "m.yaml", "url: ${NEEDED}\n");

        // Should not throw the guardrail when explicitly allowed.
        var getVar = ManifestEnvironment.Build([dir], [], allowProcessEnv: true, enforceEnvFile: true);
        Assert.NotNull(getVar);
    }

    [Fact]
    public void No_guardrail_when_only_defaulted_placeholders()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "m.yaml", "region: ${REGION:-us}\n");

        var getVar = ManifestEnvironment.Build([dir], [], allowProcessEnv: false, enforceEnvFile: true);
        Assert.NotNull(getVar);
    }

    [Fact]
    public void Passing_env_file_satisfies_guardrail_and_resolves()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "m.yaml", "url: ${NEEDED}\n");
        var envPath = WriteFile(dir, "secrets.env", "NEEDED=from-file\n");

        var getVar = ManifestEnvironment.Build([dir], [envPath], allowProcessEnv: false, enforceEnvFile: true);
        Assert.Equal("from-file", getVar("NEEDED"));
    }

    [Fact]
    public void Env_file_takes_precedence_over_process_env()
    {
        var dir = CreateTempDir();
        var envPath = WriteFile(dir, "secrets.env", "PRECEDENCE_TEST_VAR=from-file\n");
        Environment.SetEnvironmentVariable("PRECEDENCE_TEST_VAR", "from-process");
        try
        {
            var getVar = ManifestEnvironment.Build([dir], [envPath], allowProcessEnv: false, enforceEnvFile: true);
            Assert.Equal("from-file", getVar("PRECEDENCE_TEST_VAR"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRECEDENCE_TEST_VAR", null);
        }
    }

    [Fact]
    public void Falls_back_to_process_env_when_not_in_file()
    {
        Environment.SetEnvironmentVariable("FALLBACK_TEST_VAR", "from-process");
        try
        {
            var getVar = ManifestEnvironment.Build([], [], allowProcessEnv: true, enforceEnvFile: false);
            Assert.Equal("from-process", getVar("FALLBACK_TEST_VAR"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FALLBACK_TEST_VAR", null);
        }
    }

    [Fact]
    public void Validate_style_call_does_not_enforce()
    {
        var dir = CreateTempDir();
        WriteFile(dir, "m.yaml", "url: ${NEEDED}\n");

        // enforceEnvFile:false mirrors how `validate` calls it — no guardrail.
        var getVar = ManifestEnvironment.Build([dir], [], allowProcessEnv: true, enforceEnvFile: false);
        Assert.NotNull(getVar);
    }

    // ── ResolveEnvFiles (default secrets-file auto-detection) ────────────────────

    [Fact]
    public void ResolveEnvFiles_explicitFlag_alwaysWins_evenWhenDefaultFileExists()
    {
        var dir = CreateTempDir();
        WriteFile(dir, ManifestEnvironment.DefaultSecretsFile, "IGNORED=x\n");

        var result = ManifestEnvironment.ResolveEnvFiles(["custom.env"], dir);

        Assert.Equal(["custom.env"], result);
    }

    [Fact]
    public void ResolveEnvFiles_noExplicitFlag_usesDefaultFile_whenItExists()
    {
        var dir = CreateTempDir();
        WriteFile(dir, ManifestEnvironment.DefaultSecretsFile, "X=1\n");

        var result = ManifestEnvironment.ResolveEnvFiles([], dir);

        Assert.Equal([ManifestEnvironment.DefaultSecretsFile], result);
    }

    [Fact]
    public void ResolveEnvFiles_noExplicitFlag_emptyResult_whenDefaultFileAbsent()
    {
        var dir = CreateTempDir();

        var result = ManifestEnvironment.ResolveEnvFiles([], dir);

        Assert.Empty(result);
    }
}
