using System.Diagnostics;
using System.Text;

namespace Json2dirTester;

sealed record ShellResult(int? ExitCode, string Stdout, string Stderr)
{
    public bool TimedOut => ExitCode is null;
}

static class Shell
{
    /// <summary>POSIX single-quote quoting, like Python's shlex.quote.</summary>
    public static string Quote(string s) =>
        s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || "@%+=:,./-_".Contains(c))
            ? s
            : "'" + s.Replace("'", "'\"'\"'") + "'";

    /// <summary>Runs <paramref name="command"/> with /bin/sh under umask 022; a null exit code means timeout.</summary>
    public static ShellResult Run(string command, string cwd, byte[]? stdin, TimeSpan? timeout, bool inheritOutput = false)
    {
        var psi = new ProcessStartInfo("/bin/sh")
        {
            WorkingDirectory = cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = !inheritOutput,
            RedirectStandardError = !inheritOutput,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("umask 022\n" + command);

        using var process = Process.Start(psi)!;
        var stdout = inheritOutput ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
        var stderr = inheritOutput ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
        try
        {
            if (stdin is not null)
                process.StandardInput.BaseStream.Write(stdin);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The implementation exited without reading all of its input.
        }

        if (!process.WaitForExit(timeout ?? Timeout.InfiniteTimeSpan))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return new ShellResult(null, stdout.Result, stderr.Result);
        }
        process.WaitForExit();
        return new ShellResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    public static string Tail(string text) =>
        text.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";

    public static readonly Encoding Utf8 = new UTF8Encoding(false);
}
