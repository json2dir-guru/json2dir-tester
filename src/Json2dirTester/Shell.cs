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

    /// <summary>Runs <paramref name="command"/> with /bin/sh under <paramref name="umask"/>; a null exit code means timeout.</summary>
    public static ShellResult Run(string command, string cwd, byte[]? stdin, TimeSpan? timeout, bool inheritOutput = false, string umask = "022")
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
        psi.ArgumentList.Add($"umask {umask}\n" + command);

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(psi)!;
        using var cancellation = new CancellationTokenSource();
        async Task<string> CaptureOutput(StreamReader reader)
        {
            var output = new StringBuilder();
            var buffer = new char[4096];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token)) != 0)
                    output.Append(buffer, 0, count);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            return output.ToString();
        }
        var stdout = inheritOutput ? Task.FromResult("") : CaptureOutput(process.StandardOutput);
        var stderr = inheritOutput ? Task.FromResult("") : CaptureOutput(process.StandardError);
        async Task FeedInput()
        {
            try
            {
                try
                {
                    if (stdin is not null)
                        await process.StandardInput.BaseStream.WriteAsync(stdin, cancellation.Token);
                }
                finally { process.StandardInput.Close(); }
            }
            catch (IOException)
            {
                // The implementation exited without reading all of its input.
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        var completed = Task.WhenAll(FeedInput(), process.WaitForExitAsync(), stdout, stderr);
        var elapsed = clock.Elapsed;
        var remaining = timeout is null || timeout == Timeout.InfiniteTimeSpan
            ? Timeout.InfiniteTimeSpan
            : timeout.Value > elapsed ? timeout.Value - elapsed : TimeSpan.Zero;
        try
        {
            completed.WaitAsync(remaining).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            cancellation.Cancel();
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // The process exited as the deadline elapsed.
            completed.GetAwaiter().GetResult();
            return new ShellResult(null, stdout.Result, stderr.Result);
        }
        return new ShellResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    public static string Tail(string text) =>
        text.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";

    public static readonly Encoding Utf8 = new UTF8Encoding(false);
}
