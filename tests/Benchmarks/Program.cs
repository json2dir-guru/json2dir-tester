using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Json2dirTester;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

if (!OperatingSystem.IsLinux()) throw new Exception("Tests require Linux");
var temp = Directory.CreateTempSubdirectory("json2dir-bench-tests-").FullName;
try
{
    foreach (var w in BenchFixtures.Workloads())
    {
        var fixture = BenchFixtures.Generate(w, 1729);
        var again = BenchFixtures.Generate(w, 1729);
        Check(fixture.Hash == again.Hash && fixture.Input.SequenceEqual(again.Input), w.Id + " is deterministic");
        using var parsed = JsonDocument.Parse(fixture.Input, new JsonDocumentOptions { MaxDepth = 4096 });
        Check(parsed.RootElement.ValueKind == JsonValueKind.Object, w.Id + " is JSON object");
        var root = Path.Combine(temp, "fixture"); Directory.CreateDirectory(root);
        BenchFixtures.Prepare(root, fixture.Expected);
        Check(BenchFixtures.Verify(root, fixture.Expected) is null, w.Id + " verifier accepts exact tree");
        Directory.Delete(root, true);
    }
    var literal = BenchFixtures.Generate(new("utf8", "utf8", 1048576), 0);
    var escaped = BenchFixtures.Generate(new("escapes", "escapes", 1048576), 0);
    Check(literal.PayloadBytes == escaped.PayloadBytes && escaped.Input.Length > literal.Input.Length,
        "UTF-8 and escape workloads have equal decoded bytes and distinct input lengths");
    var flat = BenchFixtures.Generate(new("files", "files", 100), 0);
    Check(flat.Count("file") == 100 && flat.PayloadBytes == 6400, "file counts and payload bytes");
    var tree = Path.Combine(temp, "verify"); Directory.CreateDirectory(tree);
    var expected = new Dictionary<string, BenchEntry> { ["file"] = new("file", "exact\n"), ["link"] = new("link", "file"), ["script"] = new("script", "echo ok\n") };
    BenchFixtures.Prepare(tree, expected);
    File.WriteAllText(Path.Combine(tree, "file"), "short");
    Check(BenchFixtures.Verify(tree, expected) is not null, "truncated output rejected");
    File.WriteAllText(Path.Combine(tree, "file"), "exact\n");
    File.WriteAllBytes(Path.Combine(tree, "file"), [255, 255, 255, 255, 255, 255]);
    Check(BenchFixtures.Verify(tree, expected)?.Contains("UTF-8") == true, "invalid UTF-8 output rejected without throwing");
    File.WriteAllText(Path.Combine(tree, "file"), "exact\n");
    File.SetUnixFileMode(Path.Combine(tree, "script"), (UnixFileMode)420);
    Check(BenchFixtures.Verify(tree, expected)?.Contains("execute") == true, "missing execute bits rejected");
    File.SetUnixFileMode(Path.Combine(tree, "script"), (UnixFileMode)493);
    File.Delete(Path.Combine(tree, "link")); File.CreateSymbolicLink(Path.Combine(tree, "link"), "elsewhere");
    Check(BenchFixtures.Verify(tree, expected)?.Contains("symlink") == true, "wrong link target rejected");
    File.Delete(Path.Combine(tree, "link")); File.CreateSymbolicLink(Path.Combine(tree, "link"), "file");
    File.WriteAllText(Path.Combine(tree, "extra"), "extra");
    Check(BenchFixtures.Verify(tree, expected)?.Contains("unexpected") == true, "extra entry rejected");
    File.Delete(Path.Combine(tree, "extra")); File.Delete(Path.Combine(tree, "file"));
    Check(BenchFixtures.Verify(tree, expected)?.Contains("missing") == true, "missing entry rejected");
    File.Delete(Path.Combine(tree, "script"));
    await BenchProcess.Run("mkfifo script", tree, null, TimeSpan.FromSeconds(2));
    Check(BenchFixtures.Verify(tree, expected) is not null, "FIFO rejected without blocking");
    Directory.Delete(tree, true);
    var lockedDir = Path.Combine(temp, "locked"); Directory.CreateDirectory(lockedDir);
    File.WriteAllText(Path.Combine(lockedDir, "file"), "contents");
    File.SetUnixFileMode(lockedDir, 0);
    BenchFixtures.Cleanup(lockedDir);
    Check(!Directory.Exists(lockedDir), "nonempty mode-000 directory cleaned up");
    var modeRoot = Path.Combine(temp, "modes"); Directory.CreateDirectory(modeRoot);
    var modeFile = Path.Combine(modeRoot, "file"); File.WriteAllText(modeFile, "contents"); File.SetUnixFileMode(modeFile, 0);
    var readTree = Tree.Read(modeRoot);
    Check((string?)readTree["file"] == "contents" && File.GetUnixFileMode(modeFile) == 0, "conformance can inspect mode-000 files and restores permissions");
    BenchFixtures.Cleanup(modeRoot);
    var statistics = Benchmarks.Statistics([1, 2, 3, 4]);
    Check(statistics is { Median: 2.5, Q1: 1.75, Q3: 3.25, Count: 4 }, "interpolated median and quartiles");
    Check(Benchmarks.Statistics([]) is null, "empty samples are missing, not zero");
    var info = new BenchFixtureInfo("fixture", "files", 100, "create", "hash", 100, 0, 0, 0, 500, 1048576, 0);
    var sample = new BenchSample("impl", "fixture", "disk", "timing", 0, "ok", "", 1000);
    var cell = Benchmarks.Summarize("impl", info, "disk", [sample], "ok", "", 1);
    Check(cell is { EntriesPerSecond: 100, MiBPerSecond: 1, MaxRssKiB: null }, "throughput derives from output size; missing RSS stays null");
    cell = Benchmarks.Summarize("impl", info, "disk", [sample], "incorrect-output", "wrong tree", 1);
    Check(cell.EntriesPerSecond is null && cell.Status == "incorrect-output", "incorrect output never receives throughput");
    cell = Benchmarks.Summarize("impl", info, "disk", [sample], "ok", "", 15);
    Check(cell.Status == "incomplete" && cell.MiBPerSecond is null, "partial samples excluded from rankings");

    var clock = Stopwatch.StartNew();
    var result = await BenchProcess.Run("sleep 20", temp, new byte[4 * 1048576], TimeSpan.FromMilliseconds(300));
    Check(result.ExitCode is null && clock.Elapsed.TotalSeconds < 4, "deadline applies while stdin is blocked");
    result = await BenchProcess.Run("exit 0", temp, new byte[4 * 1048576], TimeSpan.FromSeconds(2));
    Check(result.ExitCode == 0, "early exit without reading stdin is handled");
    result = await BenchProcess.Run("head -c 200000 /dev/zero; head -c 200000 /dev/zero >&2", temp, null, TimeSpan.FromSeconds(2));
    Check(result.ExitCode == 0 && result.Stdout.Length <= 65536 && result.Stderr.Length <= 65536, "both output streams drained with bounded logs");
    clock.Restart();
    result = await BenchProcess.Run("sleep 20 & echo $! > child.pid", temp, null, TimeSpan.FromMilliseconds(500));
    Check(result.LeftChildren || result.ExitCode is null, "lingering child is rejected");
    var pid = File.ReadAllText(Path.Combine(temp, "child.pid")).Trim();
    var stat = Path.Combine("/proc", pid, "stat");
    Check(!File.Exists(stat) || File.ReadAllText(stat).Split(' ')[2] == "Z", "child process terminated");
    Check(clock.Elapsed.TotalSeconds < 4, "descendant output pipe cannot hang harness");
    var timeCommand = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
        .Select(p => Path.Combine(p, "time")).FirstOrDefault(File.Exists);
    if (timeCommand is not null)
    {
        result = await BenchProcess.Run("printf done", temp, null, TimeSpan.FromSeconds(2), profile: true, timeCommand: timeCommand);
        Check(result.ExitCode == 0 && result.MaxRssKiB is > 0 && result.UserSeconds is >= 0, "resource run returns GNU time metrics");
        Check(!Directory.EnumerateFiles(temp, ".resources-*").Any(), "resource files cleaned up");
    }

    var ws = Workspace.Discover(null, null);
    var rejection = Cases.Load(ws.Cases).First(c => c.ExpectError && !c.RequiresNonRoot);
    var leaked = Runner.Run(rejection, "sleep 20 &", TimeSpan.FromMilliseconds(500),
        (command, cwd, input, timeout, umask) =>
        {
            var execution = BenchProcess.Run(command, cwd, input, timeout, umask: umask).GetAwaiter().GetResult();
            return new ShellResult(execution.LeftChildren ? null : execution.ExitCode, execution.Stdout, execution.Stderr);
        });
    Check(leaked.Status == Status.Fail, "leaked children cannot pass an expected-error conformance case");
    await Benchmarks.Main(["bench", "--help"], ws);
    try { await Benchmarks.Main(["bench", "--all", "--dir", temp, "--timeout", "NaN"], ws); throw new Exception("accepted NaN"); }
    catch (ArgumentException) { Console.WriteLine("PASS nonfinite CLI values rejected"); }
    var campaign = new BenchCampaign(1, "test", "now", new(temp, ["impl"], false),
        new(1, [], [new("impl", "https://example.com", "revision", "command", null)]), [],
        [new BenchImplementation { Definition = new("impl", "https://example.com", "revision", "command", null) }], [info], [cell]);
    File.WriteAllText(Path.Combine(temp, "campaign.json"), JsonSerializer.Serialize(campaign, Benchmarks.Json));
    File.WriteAllText(Path.Combine(temp, "samples.json"), JsonSerializer.Serialize(new BenchRaw(1, "test", [sample]), Benchmarks.Json));
    var export = Path.Combine(temp, "export");
    await Benchmarks.Main(["bench-export", "--dir", temp, "--out", export], ws);
    Check(Benchmarks.Read<BenchCampaign>(Path.Combine(export, "benchmarks.json")).Id == "test", "versioned export round-trip");
    File.WriteAllText(Path.Combine(temp, "samples.json"), JsonSerializer.Serialize(new BenchRaw(1, "other", []), Benchmarks.Json));
    try { await Benchmarks.Main(["bench-export", "--dir", temp, "--out", export], ws); throw new Exception("accepted mixed campaign"); }
    catch (ArgumentException) { Console.WriteLine("PASS mixed campaign files rejected"); }

    // Exercise the complete campaign/export path with a local, committed adapter.
    // No downloads or changes to a real implementation repository are needed.
    var smoke = Path.Combine(temp, "smoke");
    var stub = Path.Combine(smoke, "work", "others", "stub"); Directory.CreateDirectory(stub);
    File.WriteAllText(Path.Combine(stub, "json2dir.py"), """
        import json, os, sys
        from pathlib import Path
        if len(sys.argv) != 1:
            sys.exit(1)
        def write(tree, root):
            if not isinstance(tree, dict):
                raise ValueError('root must be object')
            for name, value in tree.items():
                if not name or name in ('.', '..') or '/' in name or '\0' in name:
                    raise ValueError('invalid name')
                path = root / name
                if path.is_symlink() or (path.exists() and not path.is_dir()):
                    path.unlink()
                if isinstance(value, dict):
                    path.mkdir(exist_ok=True)
                    write(value, path)
                elif isinstance(value, str):
                    path.write_bytes(value.encode('utf-8'))
                elif isinstance(value, list) and len(value) == 2 and isinstance(value[1], str):
                    if value[0] == 'link':
                        path.symlink_to(value[1])
                    elif value[0] == 'script':
                        path.write_bytes(value[1].encode('utf-8'))
                        path.chmod(path.stat().st_mode | 0o111)
                    else:
                        raise ValueError('unknown node')
                else:
                    raise ValueError('invalid value')
        write(json.load(sys.stdin), Path.cwd())
        """);
    var init = await BenchProcess.Run("git init -q && git add json2dir.py && git -c user.name=BenchTests -c user.email=bench@example.invalid -c commit.gpgsign=false -c core.hooksPath=/dev/null commit -qm fixture", stub, null, TimeSpan.FromSeconds(5));
    Check(init.ExitCode == 0, "local smoke adapter committed");
    var revision = await BenchProcess.Run("git rev-parse HEAD", stub, null, TimeSpan.FromSeconds(2));
    var smokeLock = new BenchLock(1, [], [new("stub", "local", revision.Stdout.Trim(), "python3 {src}/json2dir.py", null)]);
    var missingPackage = "/nix/store/00000000000000000000000000000000-benchmark-missing";
    var packaged = new BenchLockedImplementation("stub", "local", "revision", missingPackage + "/bin/stub", null,
        PackagePath: missingPackage);
    try { Benchmarks.ValidatePackage(packaged); throw new Exception("accepted missing package"); }
    catch (IOException) { }
    try { Benchmarks.ValidatePackage(packaged with { Command = "python3 other.py" }); throw new Exception("accepted changed package command"); }
    catch (InvalidOperationException) { }
    try { Benchmarks.ValidatePackage(packaged with { Build = "make" }); throw new Exception("accepted package rebuild"); }
    catch (InvalidOperationException) { }
    try { Benchmarks.ValidatePackage(packaged with { PackagePath = stub }); throw new Exception("accepted package outside Nix store"); }
    catch (InvalidOperationException) { }
    File.WriteAllText(Path.Combine(smoke, "lock.json"), JsonSerializer.Serialize(smokeLock, Benchmarks.Json));
    if (timeCommand is not null)
    {
        var tools = Path.Combine(smoke, "work", "runtimes", "tools", "bin"); Directory.CreateDirectory(tools);
        File.CreateSymbolicLink(Path.Combine(tools, "time"), timeCommand);
    }
    var console = Console.Out;
    using var progress = new StringWriter();
    int smokeStatus;
    try
    {
        Console.SetOut(progress);
        smokeStatus = await Benchmarks.Main(["bench", "--all", "--dir", smoke, "--filter", "one-file", "--storage", "disk",
            "--warmups", "1", "--repetitions", "2", "--profiles", timeCommand is null ? "0" : "1", "--timeout", "2", "--budget", "20"], ws);
    }
    finally { Console.SetOut(console); }
    var messages = progress.ToString();
    Check(messages.IndexOf("Preparation:", StringComparison.Ordinal) < messages.IndexOf("Conformance:", StringComparison.Ordinal) &&
        messages.IndexOf("Conformance:", StringComparison.Ordinal) < messages.IndexOf("Benchmarking:", StringComparison.Ordinal),
        "logs distinguish preparation, conformance and benchmarking phases");
    Check(messages.Contains("outside the benchmark budget") && messages.Contains("Conformance stub: 10/68 cases") &&
        messages.Contains("Conformance stub: 68/68 cases") && messages.Contains("last case:"), "conformance logs show intermediate and final progress");
    Check(messages.Contains("warmup round 1/1") && messages.Contains("timing round 2/2") && messages.Contains("one-file/disk complete: 1 ok"),
        "benchmark logs show phases, rounds and workload outcomes");
    Check(smokeStatus == 0, "complete local campaign succeeds");
    await Benchmarks.Main(["bench-export", "--dir", smoke, "--out", Path.Combine(smoke, "export")], ws);
    var published = Benchmarks.Read<BenchCampaign>(Path.Combine(smoke, "export", "benchmarks.json"));
    Check(published.Completed && published.Results.Single().Status == "ok" && published.Results.Single().Timing?.Count == 2,
        "complete campaign exports verified timings and completion state");
    Check(published.Implementations.Single().Conformance.Count > 0, "campaign includes conformance annotations");
}
finally { Directory.Delete(temp, true); }
