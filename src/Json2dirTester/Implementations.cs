using System.Text.Json;

namespace Json2dirTester;

/// <summary>
/// An implementation under test, described by implementations/&lt;name&gt;.json.
/// <c>build</c> and <c>command</c> are /bin/sh snippets; {src} and {runtimes} expand to shell-quoted paths.
/// <c>command</c> also takes {input} and {output}, like the conformance runner.
/// </summary>
sealed record Implementation(string Name, string Description, string Repo, string? Build, string Command)
{
    public string SourceDir(Workspace ws) => Path.Combine(ws.Sources, Name);

    public string Expand(string template, Workspace ws) => template
        .Replace("{src}", Shell.Quote(SourceDir(ws)))
        .Replace("{runtimes}", Shell.Quote(ws.Runtimes));
}

static class Implementations
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<Implementation> Load(string directory) =>
        Directory.EnumerateFiles(directory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(f => JsonSerializer.Deserialize<Implementation>(File.ReadAllText(f), Options)
                ?? throw new InvalidOperationException($"{f}: empty implementation file"))
            .ToList();
}

/// <summary>
/// Paths the tester works with. Sources of the implementations live in "others/" and
/// toolchains in "runtimes/" of the json2dir_MANY workspace, two levels above the repository.
/// </summary>
sealed record Workspace(string Repo, string Sources, string Runtimes)
{
    public string Cases => Path.Combine(Repo, "cases");
    public string ImplementationsDir => Path.Combine(Repo, "implementations");

    public static Workspace Discover(string? sources, string? runtimes)
    {
        var repo = FindRepo(AppContext.BaseDirectory) ?? FindRepo(Environment.CurrentDirectory)
            ?? throw new InvalidOperationException("cannot find the json2dir-tester repository (no cases/ and implementations/ above)");
        var workspace = Path.GetFullPath(Path.Combine(repo, "..", ".."));
        return new Workspace(
            repo,
            Path.GetFullPath(sources ?? Path.Combine(workspace, "others")),
            Path.GetFullPath(runtimes ?? Path.Combine(workspace, "runtimes")));
    }

    static string? FindRepo(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "cases")) && Directory.Exists(Path.Combine(dir.FullName, "implementations")))
                return dir.FullName;
        return null;
    }
}
