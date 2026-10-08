namespace Json2dirTester;

enum Status { Pass, Fail, Skip }

sealed record CaseResult(Case Case, Status Status, string Reason, string Stderr);

static class Runner
{
    const string Setpriv = "/usr/bin/setpriv";
    const string Nobody = "65534:65534";

    /// <summary>
    /// Runs one case the way conformance/run.py does: the command runs in an empty target
    /// directory with the document on stdin, then the tree is read back and compared.
    /// </summary>
    public static CaseResult Run(Case c, string command, TimeSpan timeout,
        Func<string, string, byte[]?, TimeSpan, string, ShellResult>? execute = null)
    {
        // Root ignores permissions, so such cases run as "nobody" via setpriv (util-linux).
        var dropPrivileges = c.RequiresNonRoot && Environment.IsPrivilegedProcess;
        if (dropPrivileges && !File.Exists(Setpriv))
            return new(c, Status.Skip, "needs a non-root user, and setpriv is not available to switch to one", "");

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
            if (c.Args.Count > 0)
                resolved += " " + string.Join(" ", c.Args.Select(Shell.Quote));
            if (dropPrivileges)
            {
                Shell.Run($"chown -R {Nobody} {Shell.Quote(sandbox)} {Shell.Quote(inputDir)}", "/", null, TimeSpan.FromSeconds(30));
                resolved = $"{Setpriv} --reuid={Nobody.Split(':')[0]} --regid={Nobody.Split(':')[1]} --clear-groups -- /bin/sh -c {Shell.Quote(resolved)}";
            }

            var result = execute is null ? Shell.Run(resolved, root, c.Input, timeout, umask: c.Umask)
                : execute(resolved, root, c.Input, timeout, c.Umask);
            if (result.TimedOut)
                return new(c, Status.Fail, $"timed out after {timeout.TotalSeconds:g}s", result.Stderr);

            var escaped = Directory.EnumerateFileSystemEntries(sandbox)
                .Select(Path.GetFileName)
                .Where(n => n != "root")
                .Order(StringComparer.Ordinal)
                .ToList();
            if (escaped.Count > 0)
                return new(c, Status.Fail, $"wrote outside the target directory: {string.Join(", ", escaped)}", result.Stderr);

            if (CheckOutput(c, result) is { } outputProblem)
                return new(c, Status.Fail, outputProblem, result.Stderr);

            // 126/127: the shell could not run the command at all (missing build, wrong path).
            // That is a broken setup, not a rejection, so it must not pass expect-error cases.
            if (result.ExitCode is 126 or 127)
                return new(c, Status.Fail, $"command could not be run (exit status {result.ExitCode}): {Shell.Tail(result.Stderr)}", result.Stderr);

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

    static string? CheckOutput(Case c, ShellResult result)
    {
        if (c.StderrContains is { } err && !result.Stderr.Contains(err, StringComparison.OrdinalIgnoreCase))
            return $"stderr does not contain \"{err}\"";
        if (c.StdoutContains is { } @out && !result.Stdout.Contains(@out, StringComparison.OrdinalIgnoreCase))
            return $"stdout does not contain \"{@out}\"";
        if (c.StdoutEmpty && result.Stdout.Length > 0)
            return $"expected empty stdout, got: {Shell.Tail(result.Stdout)}";
        return null;
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
