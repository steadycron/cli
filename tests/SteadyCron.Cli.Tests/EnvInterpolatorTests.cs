using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class EnvInterpolatorTests
{
    private static string Apply(string text, Dictionary<string, string>? env = null)
    {
        env ??= [];
        return EnvInterpolator.Apply(text, "<test>", name => env.TryGetValue(name, out var v) ? v : null);
    }

    [Fact]
    public void Replaces_set_variable()
    {
        var result = Apply("url: ${API_URL}", new() { ["API_URL"] = "https://example.com" });
        Assert.Equal("url: https://example.com", result);
    }

    [Fact]
    public void Uses_default_when_variable_unset()
    {
        var result = Apply("timeout: ${TIMEOUT:-60}");
        Assert.Equal("timeout: 60", result);
    }

    [Fact]
    public void Set_variable_overrides_default()
    {
        var result = Apply("timeout: ${TIMEOUT:-60}", new() { ["TIMEOUT"] = "120" });
        Assert.Equal("timeout: 120", result);
    }

    [Fact]
    public void Empty_default_is_valid()
    {
        var result = Apply("body: ${BODY:-}");
        Assert.Equal("body: ", result);
    }

    [Fact]
    public void Throws_when_variable_unset_and_no_default()
    {
        var ex = Assert.Throws<ManifestException>(() =>
            Apply("url: ${MISSING_VAR}"));
        Assert.Contains("MISSING_VAR", ex.Message);
        Assert.Contains("undefined environment variable", ex.Message);
    }

    [Fact]
    public void Reports_all_missing_variables_at_once()
    {
        var ex = Assert.Throws<ManifestException>(() =>
            Apply("${A} ${B} ${C}"));
        Assert.Contains("3 undefined", ex.Message);
        Assert.Contains("A", ex.Message);
        Assert.Contains("B", ex.Message);
        Assert.Contains("C", ex.Message);
    }

    [Fact]
    public void Passes_through_server_template_vars_untouched()
    {
        var result = Apply("header: Bearer {{token}}", new() { ["token"] = "should-not-match" });
        Assert.Equal("header: Bearer {{token}}", result);
    }

    [Fact]
    public void Multiple_substitutions_in_one_line()
    {
        var result = Apply(
            "Authorization: Bearer ${KEY} X-Tenant: ${TENANT}",
            new() { ["KEY"] = "abc", ["TENANT"] = "acme" });
        Assert.Equal("Authorization: Bearer abc X-Tenant: acme", result);
    }

    [Fact]
    public void FindPlaceholders_returns_all_names()
    {
        var names = EnvInterpolator.FindPlaceholders("${FOO} and ${BAR:-default} and ${FOO}");
        Assert.Equal(2, names.Count);
        Assert.Contains("FOO", names);
        Assert.Contains("BAR", names);
    }

    [Fact]
    public void FindPlaceholders_empty_when_no_placeholders()
    {
        var names = EnvInterpolator.FindPlaceholders("{{template_var}} and plain text");
        Assert.Empty(names);
    }

    [Fact]
    public void FindRequiredPlaceholders_excludes_defaulted()
    {
        var names = EnvInterpolator.FindRequiredPlaceholders("${REQUIRED} ${OPTIONAL:-x}");
        Assert.Equal(["REQUIRED"], names);
    }

    [Fact]
    public void FindRequiredPlaceholders_required_when_any_occurrence_lacks_default()
    {
        // FOO appears without a default once and with one once → still required.
        var names = EnvInterpolator.FindRequiredPlaceholders("${FOO} and ${FOO:-x}");
        Assert.Equal(["FOO"], names);
    }

    [Fact]
    public void FindRequiredPlaceholders_empty_when_all_defaulted()
    {
        var names = EnvInterpolator.FindRequiredPlaceholders("${A:-1} ${B:-2}");
        Assert.Empty(names);
    }

    [Fact]
    public void Multiple_defaulted_placeholders_each_resolve_independently()
    {
        // Regression: a defaulted placeholder must stop at its own '}' and not swallow
        // forward through unrelated text (including a later {{template}} or ${...}) up to
        // whatever '}' happens to appear next in the file.
        var result = Apply("a: ${A:-1}\nb: ${B:-2}\nc: {{template}}\nd: ${D:-3}");
        Assert.Equal("a: 1\nb: 2\nc: {{template}}\nd: 3", result);
    }
}
