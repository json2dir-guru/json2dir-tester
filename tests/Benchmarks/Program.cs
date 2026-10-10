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
    var standard = BenchFixtures.Workloads();
    Check(standard.Select(w => w.Id).SequenceEqual(new[] { "empty", "files-1000", "payload-8MiB", "balanced-1000",
        "depth-64", "escapes-1MiB", "config", "update" }), "standard suite has eight ordered workloads and sixteen storage pairs");
    Check(BenchFixtures.Workloads("extended").Count == 26, "extended suite retains 26 workloads and 52 storage pairs");
    Check(!standard.Any(w => w.Id == "payload-64MiB") && BenchFixtures.Workloads("extended").Any(w => w.Id == "payload-64MiB"),
        "extended workloads remain explicitly selectable");
    var defaults = new BenchOptions(temp, [], true);
    Check(defaults is { Timeout: 30, Budget: 240, Warmups: 3, Repetitions: 15, Profiles: 3 }, "benchmark limit and repetition defaults");
    foreach (var w in BenchFixtures.Workloads("extended"))
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
    var shellResult = Shell.Run("sleep 20", temp, new byte[4 * 1048576], TimeSpan.FromMilliseconds(300));
    Check(shellResult.TimedOut && clock.Elapsed.TotalSeconds < 4, "standard shell deadline applies while stdin is blocked");
    shellResult = Shell.Run("exec 0<&-; sleep 0.1; exit 7", temp, new byte[4 * 1048576], TimeSpan.FromSeconds(2));
    Check(shellResult.ExitCode == 7, "standard shell preserves exit status after early stdin close");
    var binaryInput = Enumerable.Range(0, 1048576).Select(i => (byte)i).ToArray();
    shellResult = Shell.Run("cat > stdin.bin; head -c 200000 /dev/zero; head -c 200000 /dev/zero >&2", temp, binaryInput, null);
    Check(shellResult.ExitCode == 0 && File.ReadAllBytes(Path.Combine(temp, "stdin.bin")).SequenceEqual(binaryInput) &&
        shellResult.Stdout.Length == 200000 && shellResult.Stderr.Length == 200000,
        "standard shell preserves binary stdin and drains both output streams with no deadline");
    shellResult = Shell.Run("cat > inherited-stdin.bin", temp, binaryInput, TimeSpan.FromSeconds(2), inheritOutput: true);
    Check(shellResult.ExitCode == 0 && shellResult.Stdout == "" && shellResult.Stderr == "" &&
        File.ReadAllBytes(Path.Combine(temp, "inherited-stdin.bin")).SequenceEqual(binaryInput),
        "standard shell supports inherited output while feeding stdin");
    var blockedInput = new byte[4 * 1048576];
    foreach (var (inherit, input) in new (bool, byte[]?)[] { (false, blockedInput), (true, blockedInput), (false, null) })
    {
        clock.Restart();
        try
        {
            shellResult = Shell.Run("exec 3<&0; sleep 20 <&3 & echo $! > shell-child.pid; printf partial; printf diagnostic >&2; exit 7",
                temp, input, TimeSpan.FromMilliseconds(300), inheritOutput: inherit);
            Check(shellResult.TimedOut && clock.Elapsed.TotalSeconds < 4,
                $"standard shell bounds inherited child pipes with inheritOutput={inherit}, stdin={input is not null}");
            if (!inherit)
                Check(shellResult.Stdout == "partial" && shellResult.Stderr == "diagnostic", "standard shell retains partial timeout output");
        }
        finally
        {
            if (File.Exists(Path.Combine(temp, "shell-child.pid")))
            {
                var childId = int.Parse(File.ReadAllText(Path.Combine(temp, "shell-child.pid")));
                try { using var child = Process.GetProcessById(childId); child.Kill(); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                File.Delete(Path.Combine(temp, "shell-child.pid"));
            }
        }
    }
    clock.Restart();
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
        smokeStatus = await Benchmarks.Main(["bench", "--all", "--dir", smoke, "--suite", "extended", "--filter", "one-file", "--storage", "disk",
            "--warmups", "1", "--repetitions", "2", "--profiles", timeCommand is null ? "0" : "1", "--timeout", "2", "--budget", "20"], ws);
    }
    finally { Console.SetOut(console); }
    var messages = progress.ToString();
    Check(messages.IndexOf("Preparation:", StringComparison.Ordinal) < messages.IndexOf("Benchmarking:", StringComparison.Ordinal) &&
        !messages.Contains("Conformance:"), "benchmark logs proceed from preparation to timing without a conformance phase");
    Check(messages.Contains("warmup round 1/1") && messages.Contains("timing round 2/2") && messages.Contains("one-file/disk complete: 1 ok"),
        "benchmark logs show phases, rounds and workload outcomes");
    Check(smokeStatus == 0, "complete local campaign succeeds");
    await Benchmarks.Main(["bench-export", "--dir", smoke, "--out", Path.Combine(smoke, "export")], ws);
    var published = Benchmarks.Read<BenchCampaign>(Path.Combine(smoke, "export", "benchmarks.json"));
    Check(published.Completed && published.Results.Single().Status == "ok" && published.Results.Single().Timing?.Count == 2,
        "complete campaign exports verified timings and completion state");
    var recordedSmoke = Benchmarks.Read<BenchCampaign>(Path.Combine(smoke, "campaign.json"));
    Check(published.Results.Single().BudgetUsedSeconds is > 0 &&
        published.Results.Single().BudgetUsedSeconds == recordedSmoke.Results.Single().BudgetUsedSeconds &&
        published.Results.Single().BudgetUsedSeconds == published.Implementations.Single().BudgetUsedSeconds &&
        messages.Contains("maximum pair spend"), "export preserves actual charged pair time separately from latency samples");
    Check(published.Implementations.Single().Conformance.Count == 0 && published.Environment["conformance"].StartsWith("not run"),
        "campaign explicitly records that conformance was not run");
    Check(published.Options is { Suite: "extended", BudgetScope: "implementation-workload-storage" } &&
        messages.Contains("extended suite, 1 workloads, 1 storage conditions (1 pairs per implementation)") && messages.Contains("20 seconds per implementation/workload/storage pair"),
        "campaign provenance and logs describe selected suite and per-pair allowance");

    // Use the committed local adapter to isolate limits.
    var limitsRepo = Path.Combine(temp, "limits-repo"); Directory.CreateDirectory(Path.Combine(limitsRepo, "cases"));
    var limitsWs = ws with { Repo = limitsRepo };
    var bounded = Path.Combine(temp, "campaign-limited"); Directory.CreateDirectory(bounded);
    var boundedCommand = "python3 -c " + Shell.Quote("import json, sys, time; data=json.load(sys.stdin); time.sleep(20) if data else None");
    var boundedLock = smokeLock with { Implementations = [smokeLock.Implementations.Single() with { Command = boundedCommand, Source = stub }] };
    File.WriteAllText(Path.Combine(bounded, "lock.json"), JsonSerializer.Serialize(boundedLock, Benchmarks.Json));
    var campaignClock = Stopwatch.StartNew();
    var boundedStatus = await Benchmarks.Main(["bench", "--all", "--dir", bounded, "--storage", "disk",
        "--warmups", "0", "--repetitions", "1", "--profiles", "0", "--timeout", "20", "--budget", "60", "--max-duration", "2"], limitsWs);
    Check(boundedStatus == 2 && campaignClock.Elapsed < TimeSpan.FromSeconds(8),
        "whole campaign deadline stops an active invocation independently of unchanged invocation and pair limits");
    await Benchmarks.Main(["bench-export", "--dir", bounded, "--out", Path.Combine(bounded, "export")], limitsWs);
    var boundedExport = Benchmarks.Read<BenchCampaign>(Path.Combine(bounded, "export", "benchmarks.json"));
    Check(!boundedExport.Completed && boundedExport.StopReason == "campaign time limit reached" &&
        boundedExport.Options is { Timeout: 20, Budget: 60, MaxDuration: 2 }, "stopped campaign exports its deadline and retains individual limits");
    Check(boundedExport.Results.Single(c => c.Workload == "empty").Status == "ok" &&
        boundedExport.Results.Where(c => c.Workload != "empty").All(c => c.Status == "incomplete" && c.EntriesPerSecond is null),
        "completed faster benchmarks remain ranked while interrupted and unstarted benchmarks stay unranked");
    Check(!Directory.Exists(Path.Combine(bounded, "targets")), "campaign deadline cleans benchmark targets");

    // A conformance fixture that cannot be parsed must not be loaded by benchmarking.
    var unrelatedRepo = Path.Combine(temp, "unrelated-conformance-repo");
    Directory.CreateDirectory(Path.Combine(unrelatedRepo, "cases", "conformance"));
    File.WriteAllText(Path.Combine(unrelatedRepo, "cases", "conformance", "broken.json"), "not JSON");
    var independent = Path.Combine(temp, "without-conformance"); Directory.CreateDirectory(independent);
    File.WriteAllText(Path.Combine(independent, "lock.json"), JsonSerializer.Serialize(smokeLock with
        { Implementations = [smokeLock.Implementations.Single() with { Source = stub }] }, Benchmarks.Json));
    var independentStatus = await Benchmarks.Main(["bench", "--all", "--dir", independent,
        "--filter", "empty", "--storage", "disk", "--warmups", "0", "--repetitions", "1", "--profiles", "0"], ws with { Repo = unrelatedRepo });
    Check(independentStatus == 0, "benchmarking does not load unrelated conformance fixtures");
    try { Implementations.FromLock(smokeLock, []); throw new Exception("accepted source lock for conformance"); }
    catch (ArgumentException) { }
    try { Implementations.FromLock(smokeLock with { Implementations = [packaged] }, []); throw new Exception("accepted missing conformance package"); }
    catch (IOException) { }

    using (var cancelProcess = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
    {
        var childFile = Path.Combine(temp, "cancel-child.pid");
        try
        {
            await BenchProcess.Run("sleep 20 & echo $! > " + Shell.Quote(childFile) + "; wait", temp, null,
                TimeSpan.FromSeconds(20), cancellationToken: cancelProcess.Token);
            Check(false, "campaign cancellation must propagate");
        }
        catch (OperationCanceledException) when (cancelProcess.IsCancellationRequested) { }
        var childPid = File.ReadAllText(childFile).Trim();
        var childState = Path.Combine("/proc", childPid, "stat");
        Check(!File.Exists(childState) || File.ReadAllText(childState).Split(')')[1].TrimStart().StartsWith('Z'),
            "campaign cancellation terminates the active invocation's process group");
    }
    async Task<BenchCampaign> LimitCampaign(string name, string timeout, string budget)
    {
        var directory = Path.Combine(temp, name); Directory.CreateDirectory(directory);
        var slowLock = smokeLock with { Implementations = [smokeLock.Implementations.Single() with { Command = "sleep 20", Source = stub }] };
        File.WriteAllText(Path.Combine(directory, "lock.json"), JsonSerializer.Serialize(slowLock, Benchmarks.Json));
        await Benchmarks.Main(["bench", "--all", "--dir", directory, "--suite", "extended", "--filter", "files-", "--storage", "both",
            "--warmups", "0", "--repetitions", "2", "--profiles", "0", "--timeout", timeout, "--budget", budget], limitsWs);
        await Benchmarks.Main(["bench-export", "--dir", directory, "--out", Path.Combine(directory, "export")], limitsWs);
        return Benchmarks.Read<BenchCampaign>(Path.Combine(directory, "export", "benchmarks.json"));
    }
    var budgetLimited = await LimitCampaign("budget-limited", "2", "0.1");
    var availableCells = budgetLimited.Results.Where(c => c.Status != "storage-unavailable").ToList();
    Check(availableCells.Count >= 3 && availableCells.All(c => c.Status == "budget-exhausted" && c.Timing is null && c.MiBPerSecond is null),
        "pair budget exhaustion does not skip larger workloads or rank incomplete pairs");
    var budgetRaw = Benchmarks.Read<BenchRaw>(Path.Combine(temp, "budget-limited", "samples.json"));
    Check(availableCells.All(c => budgetRaw.Samples.Any(s => s.Workload == c.Workload && s.Storage == c.Storage && s.Status == "budget-exhausted")),
        "every later workload and available storage gets a fresh pair budget and invocation");
    Check(budgetLimited.Implementations.Single().BudgetUsedSeconds > budgetLimited.Options.Budget,
        "accumulated budget telemetry does not prevent later pairs running");
    Check(availableCells.All(c => c.BudgetUsedSeconds >= budgetLimited.Options.Budget) &&
        Math.Abs(budgetLimited.Results.Sum(c => c.BudgetUsedSeconds ?? 0) - budgetLimited.Implementations.Single().BudgetUsedSeconds) < 1e-9,
        "export retains per-pair exhaustion spend and its accumulated total");
    var commandLimited = await LimitCampaign("command-limited", "0.1", "2");
    Check(commandLimited.Results.Where(c => c.Status != "storage-unavailable").All(c =>
        c.Status == (c.Workload == "files-100" ? "timeout" : "skipped")),
        "command deadlines still suppress larger workloads within each storage family");

    var legacyPath = Path.Combine(temp, "legacy"); Directory.CreateDirectory(legacyPath);
    var legacyJson = JsonSerializer.SerializeToNode(campaign, Benchmarks.Json)!;
    legacyJson["options"]!.AsObject().Remove("suite");
    legacyJson["options"]!.AsObject().Remove("budgetScope");
    legacyJson["options"]!["timeout"] = 30;
    legacyJson["options"]!["budget"] = 600;
    foreach (var legacyResult in legacyJson["results"]!.AsArray()) legacyResult!.AsObject().Remove("budgetUsedSeconds");
    File.WriteAllText(Path.Combine(legacyPath, "campaign.json"), legacyJson.ToJsonString());
    File.WriteAllText(Path.Combine(legacyPath, "samples.json"), JsonSerializer.Serialize(new BenchRaw(1, "test", [sample]), Benchmarks.Json));
    await Benchmarks.Main(["bench-export", "--dir", legacyPath, "--out", Path.Combine(legacyPath, "export")], ws);
    var legacy = Benchmarks.Read<BenchCampaign>(Path.Combine(legacyPath, "export", "benchmarks.json"));
    Check(legacy.Options is { Suite: null, BudgetScope: null, MaxDuration: null, Timeout: 30, Budget: 600 } && legacy.StopReason is null,
        "legacy export retains original limits without claiming new suite or budget semantics");
    Check(legacy.Results.All(c => c.BudgetUsedSeconds is null), "legacy export does not invent charged pair time from latency samples");
}
finally { Directory.Delete(temp, true); }
