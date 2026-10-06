namespace Json2dirTester;

enum Status { Pass, Fail }

sealed record CaseResult(Case Case, Status Status, string Reason, string Stderr);

static class Runner
{
    /// <summary>
    /// Runs one case the way conformance/run.py does: the command runs in an empty target
    /// directory with the document on stdin, then the tree is read back and compared.
    /// </summary>
    public static CaseResult Run(Case c, string command, TimeSpan timeout)
    {
        var sandbox = Directory.CreateTempSubdirectory("json2dir-sandbox-").FullName;
        var inputDir = Directory.CreateTempSubdirectory("json2dir-input-").FullName;
        try
        {
            var root = Path.Combine(sandbox, "root");
            Directory.CreateDirectory(root);
            if (c.Setup is not null)
                Tree.Write(root, c.Setup);

            var inputPath = Path.Combine(inputDir, "input.json");
            File.WriteAllBytes(inputPath, c.Input);
            var resolved = command
                .Replace("{input}", Shell.Quote(inputPath))
                .Replace("{output}", Shell.Quote(root));

            var result = Shell.Run(resolved, root, c.Input, timeout);
            if (result.TimedOut)
                return new(c, Status.Fail, $"timed out after {timeout.TotalSeconds:g}s", result.Stderr);

            var escaped = Directory.EnumerateFileSystemEntries(sandbox)
                .Select(Path.GetFileName)
                .Where(n => n != "root")
                .Order(StringComparer.Ordinal)
                .ToList();
            if (escaped.Count > 0)
                return new(c, Status.Fail, $"wrote outside the target directory: {string.Join(", ", escaped)}", result.Stderr);

            if (result.ExitCode != 0)
            {
                if (c.ExpectError || c.AcceptError)
                    return new(c, Status.Pass, "", result.Stderr);
                var last = Shell.Tail(result.Stderr);
                return new(c, Status.Fail, last.Length > 0 ? $"exit status {result.ExitCode}: {last}" : $"exit status {result.ExitCode}", result.Stderr);
            }

            if (c.ExpectError)
                return new(c, Status.Fail, "expected failure (non-zero exit status), got success", result.Stderr);

            var diff = Tree.Diff(c.ExpectTree!, Tree.Read(root));
            return diff.Count > 0
                ? new(c, Status.Fail, string.Join("; ", diff), result.Stderr)
                : new(c, Status.Pass, "", result.Stderr);
        }
        finally
        {
            ForceDelete(sandbox);
            ForceDelete(inputDir);
        }
    }

    /// <summary>Deletes a sandbox even if the implementation left read-only directories behind.</summary>
    static void ForceDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Shell.Run($"chmod -R u+rwx {Shell.Quote(path)} 2>/dev/null; rm -rf {Shell.Quote(path)}", "/", null, TimeSpan.FromSeconds(30));
        }
    }
}
