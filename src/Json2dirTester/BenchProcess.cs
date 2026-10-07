using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Json2dirTester;

sealed record BenchExecution(int? ExitCode, double Milliseconds, string Stdout, string Stderr,
    bool LeftChildren = false, double? UserSeconds = null, double? SystemSeconds = null, double? MaxRssKiB = null);

static class BenchProcess
{
    const int LogLimit = 65536;
    [DllImport("libc", SetLastError = true)] static extern int kill(int pid, int signal);

    public static async Task<BenchExecution> Run(string command, string cwd, byte[]? input, TimeSpan timeout,
        bool profile = false, string umask = "022", string timeCommand = "/usr/bin/time")
    {
        var resourcePath = Path.Combine(cwd, ".resources-" + Guid.NewGuid().ToString("N"));
        var psi = new ProcessStartInfo("setsid")
        {
            WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add("/bin/sh"); psi.ArgumentList.Add("-c");
        // A common wrapper is part of each CLI timing, including the reference timing.
        var invocation = profile
            ? $"exec {Shell.Quote(timeCommand)} -f '%U %S %M' -o {Shell.Quote(resourcePath)} /bin/sh -c {Shell.Quote(command)}"
            : command;
        psi.ArgumentList.Add("umask " + umask + "\n" + invocation);
        psi.Environment["LC_ALL"] = "C.UTF-8";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var deadline = new CancellationTokenSource(timeout);
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start setsid");
        var stdout = Drain(process.StandardOutput);
        var stderr = Drain(process.StandardError);
        var feeding = Feed(process.StandardInput.BaseStream, input, deadline.Token);
        var timedOut = false;
        var children = false;
        double elapsed;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            elapsed = clock.Elapsed.TotalMilliseconds;
            // Descendants must not keep mutating the output or holding redirected pipes open.
            children = kill(-process.Id, 0) == 0;
            if (children) Terminate(process);
            await Task.WhenAll(feeding, stdout, stderr).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true; elapsed = clock.Elapsed.TotalMilliseconds;
            Terminate(process);
        }
        finally
        {
            // Covers a failure while feeding/reading too. Escaping the process session is unsupported.
            Terminate(process);
        }
        // A broken command must not turn pipe cleanup into an unbounded wait.
        try { await Task.WhenAll(feeding, stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception e) when (e is TimeoutException or IOException or ObjectDisposedException) { }
        double? user = null, system = null, rss = null;
        if (profile && !timedOut && File.Exists(resourcePath))
        {
            var parts = File.ReadAllLines(resourcePath).LastOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is { Length: 3 } && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var u)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                (user, system, rss) = (u, s, r);
        }
        File.Delete(resourcePath);
        return new(timedOut ? null : process.ExitCode, elapsed,
            stdout.IsCompletedSuccessfully ? stdout.Result : "", stderr.IsCompletedSuccessfully ? stderr.Result : "",
            children, user, system, rss);
    }

    static void Terminate(Process process)
    {
        kill(-process.Id, 9);
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    static async Task Feed(Stream pipe, byte[]? input, CancellationToken token)
    {
        try { if (input is not null) await pipe.WriteAsync(input, token); }
        catch (IOException) { } // A converter can exit without consuming the document.
        catch (OperationCanceledException) { }
        finally { try { pipe.Close(); } catch (IOException) { } }
    }

    static async Task<string> Drain(StreamReader reader)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        try
        {
            int n;
            while ((n = await reader.ReadAsync(buffer)) > 0)
                if (result.Length < LogLimit) result.Append(buffer, 0, Math.Min(n, LogLimit - result.Length));
        }
        catch (IOException) { }
        return result.ToString();
    }
}
