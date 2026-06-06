using SteadyCron.Cli.Manifest;
using Xunit;

namespace SteadyCron.Cli.Tests;

public sealed class EnvFileTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".env");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Parses_basic_key_value_pairs()
    {
        var path = WriteTemp("FOO=bar\nBAZ=qux\n");
        var vars = EnvFile.Load([path]);

        Assert.Equal("bar", vars["FOO"]);
        Assert.Equal("qux", vars["BAZ"]);
    }

    [Fact]
    public void Skips_blank_lines_and_comments()
    {
        var path = WriteTemp("# a comment\n\nFOO=bar\n   # indented comment\n");
        var vars = EnvFile.Load([path]);

        Assert.Single(vars);
        Assert.Equal("bar", vars["FOO"]);
    }

    [Fact]
    public void Strips_export_prefix()
    {
        var path = WriteTemp("export TOKEN=secret\n");
        var vars = EnvFile.Load([path]);

        Assert.Equal("secret", vars["TOKEN"]);
    }

    [Fact]
    public void Single_quotes_are_literal()
    {
        var path = WriteTemp("V='a # b \\n c'\n");
        var vars = EnvFile.Load([path]);

        Assert.Equal("a # b \\n c", vars["V"]);
    }

    [Fact]
    public void Double_quotes_unescape()
    {
        var path = WriteTemp("V=\"line1\\nline2\"\n");
        var vars = EnvFile.Load([path]);

        Assert.Equal("line1\nline2", vars["V"]);
    }

    [Fact]
    public void Unquoted_value_strips_inline_comment()
    {
        var path = WriteTemp("V=hello   # trailing\n");
        var vars = EnvFile.Load([path]);

        Assert.Equal("hello", vars["V"]);
    }

    [Fact]
    public void Hash_without_leading_space_is_kept()
    {
        var path = WriteTemp("V=a#b\n");
        var vars = EnvFile.Load([path]);

        Assert.Equal("a#b", vars["V"]);
    }

    [Fact]
    public void Later_file_overrides_earlier()
    {
        var a = WriteTemp("V=first\n");
        var b = WriteTemp("V=second\n");
        var vars = EnvFile.Load([a, b]);

        Assert.Equal("second", vars["V"]);
    }

    [Fact]
    public void Missing_file_throws()
    {
        var ex = Assert.Throws<ManifestException>(() =>
            EnvFile.Load([Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))]));
        Assert.Contains("Env file not found", ex.Message);
    }

    [Fact]
    public void Malformed_line_throws_with_line_number()
    {
        var path = WriteTemp("FOO=bar\nnot a pair\n");
        var ex = Assert.Throws<ManifestException>(() => EnvFile.Load([path]));
        Assert.Contains("line 2", ex.Message);
    }

    [Fact]
    public void Invalid_key_throws()
    {
        var path = WriteTemp("1BAD=x\n");
        var ex = Assert.Throws<ManifestException>(() => EnvFile.Load([path]));
        Assert.Contains("Invalid variable name", ex.Message);
    }

    [Fact]
    public void Empty_value_is_empty_string()
    {
        var path = WriteTemp("EMPTY=\n");
        var vars = EnvFile.Load([path]);

        Assert.Equal(string.Empty, vars["EMPTY"]);
    }
}
