using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Json2dirTester;

sealed record BenchOptions(string Directory, string[] Names, bool All, string Storage = "both", string? Filter = null,
    int Repetitions = 15, int Warmups = 3, int Profiles = 3, double Timeout = 30, double Budget = 600, int Seed = 1729);
sealed record BenchSample(string Implementation, string Workload, string Storage, string Phase, int Round,
    string Status, string Reason, double? Milliseconds, double? UserSeconds = null,
    double? SystemSeconds = null, double? MaxRssKiB = null);
sealed record BenchStatistics(int Count, double Median, double Q1, double Q3, double Minimum, double Maximum);
sealed record BenchLockedImplementation(string Name, string Repo, string Revision, string Command, string? Build,
    string Language = "", string Description = "", string? Source = null, string? PackagePath = null);
sealed record BenchLock(int SchemaVersion, Dictionary<string, string> Toolchains, BenchLockedImplementation[] Implementations,
    JsonElement? Provisioning = null);
sealed class BenchImplementation
{
    public required BenchLockedImplementation Definition { get; init; }
    public string Status { get; set; } = "ready";
    public string Reason { get; set; } = "";
    public List<object> Conformance { get; set; } = [];
    public double BudgetUsedSeconds { get; set; }
}
sealed record BenchFixtureInfo(string Id, string Family, int Size, string Mode, string Hash,
    int Files, int Directories, int Links, int Scripts, long InputBytes, long PayloadBytes, int Depth);
sealed record BenchCell(string Implementation, string Workload, string Storage, string Status, string Reason,
    BenchStatistics? Timing, double? EntriesPerSecond, double? MiBPerSecond,
    double? UserSeconds, double? SystemSeconds, double? MaxRssKiB);
sealed record BenchCampaign(int SchemaVersion, string Id, string Created, BenchOptions Options, BenchLock Lock,
    Dictionary<string, string> Environment, List<BenchImplementation> Implementations,
    List<BenchFixtureInfo> Workloads, List<BenchCell> Results, bool Completed = false, List<BenchWorkload>? RequestedWorkloads = null);
sealed record BenchRaw(int SchemaVersion, string CampaignId, List<BenchSample> Samples);

static class Benchmarks
{
    internal static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static async Task<int> Main(string[] args, Workspace original)
    {
        var names = new List<string>(); var all = false;
        string? directory = null, output = null, filter = null;
        var storage = "both"; var repetitions = 15; var warmups = 3; var profiles = 3;
        double timeout = 30, budget = 600; var seed = 1729;
        for (var i = 1; i < args.Length; i++)
        {
            string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"{args[i - 1]} needs a value");
            switch (args[i])
            {
                case "--dir": directory = Path.GetFullPath(Next()); break;
                case "--out": output = Path.GetFullPath(Next()); break;
                case "--all": all = true; break;
                case "--storage": storage = Next(); break;
                case "--filter": filter = Next(); break;
                case "--repetitions": repetitions = ParseInt(Next()); break;
                case "--warmups": warmups = ParseInt(Next()); break;
                case "--profiles": profiles = ParseInt(Next()); break;
                case "--timeout": timeout = ParseDouble(Next()); break;
                case "--budget": budget = ParseDouble(Next()); break;
                case "--seed": seed = ParseInt(Next()); break;
                case "--help":
                    Console.WriteLine("bench NAME...|--all --dir DIR [--storage disk|tmpfs|both] [--filter TEXT] [--repetitions 15] [--warmups 3] [--profiles 3] [--timeout 30] [--budget 600] [--seed 1729]\nbench-export --dir DIR --out DIR\nPrepare DIR/lock.json and DIR/work first with benchmarks/prepare.py.");
                    return 0;
                case var option when option.StartsWith('-'): throw new ArgumentException($"unknown benchmark option {option}");
                default: names.Add(args[i]); break;
            }
        }
        if (directory is null) throw new ArgumentException("--dir is required");
        if (args[0] == "bench-export")
        {
            if (output is null) throw new ArgumentException("--out is required");
            Export(directory, output); return 0;
        }
        if (!OperatingSystem.IsLinux()) throw new ArgumentException("benchmarks require Linux");
        if (all == (names.Count > 0)) throw new ArgumentException("name implementations or pass --all");
        if (storage is not ("disk" or "tmpfs" or "both")) throw new ArgumentException("storage must be disk, tmpfs or both");
        if (repetitions < 1 || warmups < 0 || profiles < 0 || timeout <= 0 || budget <= 0)
            throw new ArgumentException("repetitions, timeout and budget must be positive; warmups/profiles must be nonnegative");
        if (File.Exists(Path.Combine(directory, "campaign.json")))
            throw new ArgumentException("campaign already exists; use a new directory to avoid mixing runs");
        var options = new BenchOptions(directory, names.ToArray(), all, storage, filter, repetitions, warmups, profiles, timeout, budget, seed);
        return await Run(options, original);
    }

    static int ParseInt(string value) => int.TryParse(value, CultureInfo.InvariantCulture, out var n)
        ? n : throw new ArgumentException($"invalid integer: {value}");
    static double ParseDouble(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
        ? n : throw new ArgumentException($"invalid number: {value}");

    static async Task<int> Run(BenchOptions options, Workspace original)
    {
        var dir = options.Directory;
        var locked = Read<BenchLock>(Path.Combine(dir, "lock.json"));
        if (locked.SchemaVersion != 1 || locked.Implementations.Length == 0) throw new ArgumentException("invalid benchmark lock");
        if (locked.Implementations.Select(i => i.Name).Distinct().Count() != locked.Implementations.Length)
            throw new ArgumentException("duplicate implementations in benchmark lock");
        if (options.Names.Any(n => !locked.Implementations.Any(i => i.Name == n))) throw new ArgumentException("implementation is absent from lock");
        var work = Path.Combine(dir, "work");
        var ws = new Workspace(original.Repo, work, Path.Combine(work, "others"), Path.Combine(work, "runtimes"));
        var implementations = locked.Implementations.Where(i => options.All || options.Names.Contains(i.Name))
            .Select(i => new BenchImplementation { Definition = i }).ToList();
        var workloads = BenchFixtures.Workloads().Where(w => options.Filter is null || w.Id.Contains(options.Filter, StringComparison.Ordinal)).ToList();
        if (workloads.Count == 0) throw new ArgumentException("no workloads match --filter");
        Directory.CreateDirectory(dir);
        var environment = await EnvironmentInfo(dir, original.Repo);
        var samples = new List<BenchSample>(); var fixtureInfos = new List<BenchFixtureInfo>();
        var cells = new List<BenchCell>();
        var id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        var created = DateTime.UtcNow.ToString("O");
        var campaign = new BenchCampaign(1, id, created, options, locked, environment, implementations, fixtureInfos, cells, RequestedWorkloads: workloads);
        var raw = new BenchRaw(1, id, samples);
        void Save() { Write(Path.Combine(dir, "campaign.json"), campaign); Write(Path.Combine(dir, "samples.json"), raw); }
        void Log(string message) => Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}Z] {message}");
        Log($"Preparation: {implementations.Count} implementations");
        var prepared = 0;
        // Build all selected tools before timing any of them.
        foreach (var impl in implementations)
        {
            var definition = impl.Definition;
            var adapter = Adapter(definition);
            var source = adapter.SourceDir(ws);
            Log($"Preparation [{++prepared}/{implementations.Count}]: {definition.Name} — " +
                (definition.PackagePath is null ? "checking source and build" : "checking cached executable"));
            try
            {
                if (definition.PackagePath is not null)
                {
                    ValidatePackage(definition);
                    Save();
                    continue;
                }
                var revision = await BenchProcess.Run("git rev-parse HEAD", source, null, TimeSpan.FromSeconds(10));
                var dirty = await BenchProcess.Run("git status --porcelain", source, null, TimeSpan.FromSeconds(10));
                if (revision.ExitCode != 0 || revision.Stdout.Trim() != definition.Revision || dirty.ExitCode != 0 || dirty.Stdout.Length > 0)
                    throw new InvalidOperationException("source differs from locked revision or has local changes");
                if (definition.Build is { } build)
                {
                    var result = await BenchProcess.Run(Command(adapter, build, ws), source, null, TimeSpan.FromMinutes(15));
                    if (result.ExitCode != 0 || result.LeftChildren)
                        throw new InvalidOperationException("build failed: " + Shell.Tail(result.Stderr));
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { impl.Status = "build-failed"; impl.Reason = e.Message; Log($"Preparation failed: {definition.Name}: {e.Message}"); }
            Save();
        }
        // These are annotations, not a global eligibility gate.
        var cases = Cases.Load(ws.Cases).Where(c => c.Name.StartsWith("conformance/")).ToList();
        var ready = implementations.Where(i => i.Status == "ready").ToList();
        Log($"Preparation complete: {ready.Count} ready, {implementations.Count - ready.Count} failed");
        Log($"Conformance: {ready.Count} implementations; these checks are outside the benchmark budget");
        var checkedImplementations = 0;
        foreach (var impl in ready)
        {
            var adapter = Adapter(impl.Definition);
            var applicable = cases.Where(c => c.AppliesTo(adapter.Name)).ToList();
            var elapsed = Stopwatch.StartNew();
            var lastProgress = TimeSpan.Zero;
            var completed = 0; var passed = 0; var failed = 0; var skipped = 0;
            Log($"Conformance [{++checkedImplementations}/{ready.Count}]: {adapter.Name} — 0/{applicable.Count} cases");
            foreach (var c in applicable)
            {
                string status, reason;
                try
                {
                    var result = Runner.Run(c, Command(adapter, adapter.Command, ws), TimeSpan.FromSeconds(options.Timeout),
                        (command, cwd, input, timeout, umask) =>
                        {
                            var execution = BenchProcess.Run(command, cwd, input, timeout, umask: umask).GetAwaiter().GetResult();
                            return new ShellResult(execution.LeftChildren ? null : execution.ExitCode, execution.Stdout, execution.Stderr);
                        });
                    status = result.Status.ToString().ToLowerInvariant(); reason = result.Reason;
                    impl.Conformance.Add(new { @case = c.Name, status, result.Reason });
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { status = "harness-error"; reason = e.Message; impl.Conformance.Add(new { @case = c.Name, status, reason }); }
                completed++;
                if (status == "pass") passed++;
                else if (status == "skip") skipped++;
                else { failed++; Log($"Conformance {adapter.Name}: {c.Name}: {status}: {reason}"); }
                if (completed % 10 == 0 || completed == applicable.Count || elapsed.Elapsed - lastProgress >= TimeSpan.FromSeconds(10))
                {
                    Log($"Conformance {adapter.Name}: {completed}/{applicable.Count} cases; {passed} passed, {failed} failed, {skipped} skipped; " +
                        $"{elapsed.Elapsed.TotalSeconds:F1}s elapsed; last case: {c.Name}");
                    lastProgress = elapsed.Elapsed;
                }
            }
            Save();
        }
        var storages = options.Storage == "both" ? new[] { "disk", "tmpfs" } : [options.Storage];
        var bases = new Dictionary<string, string>();
        bases["disk"] = Path.Combine(dir, "targets");
        var tmpfs = await BenchProcess.Run("findmnt -n -o FSTYPE -T /dev/shm", dir, null, TimeSpan.FromSeconds(5));
        environment["tmpfsFilesystem"] = tmpfs.Stdout.Trim();
        if (tmpfs.ExitCode == 0 && tmpfs.Stdout.Trim() == "tmpfs")
            bases["tmpfs"] = Path.Combine("/dev/shm", "json2dir-bench-" + id);
        var blocked = new Dictionary<(string Impl, string Storage, string Family, string Mode), int>();
        var random = new Random(options.Seed);
        Log($"Benchmarking: {workloads.Count} workloads, {storages.Length} storage conditions, {implementations.Count} implementations");
        var workloadNumber = 0;
        try
        {
            foreach (var w in workloads)
            {
                Log($"Workload [{++workloadNumber}/{workloads.Count}]: {w.Id} — generating fixture");
                var fixture = BenchFixtures.Generate(w, options.Seed);
                var info = Describe(w, fixture);
                fixtureInfos.Add(info);
                var inputPath = Path.Combine(dir, "input.json");
                File.WriteAllBytes(inputPath, fixture.Input);
                foreach (var storage in storages)
                {
                    Log($"Benchmark {w.Id} on {storage}");
                    var failures = new Dictionary<string, (string Status, string Reason)>();
                    foreach (var impl in implementations)
                    {
                        if (impl.Status != "ready") failures[impl.Definition.Name] = (impl.Status, impl.Reason);
                        else if (!bases.ContainsKey(storage)) failures[impl.Definition.Name] = ("storage-unavailable", "tmpfs is not mounted at /dev/shm");
                        else if (blocked.TryGetValue((impl.Definition.Name, storage, w.Family, w.Mode), out var limit) && w.Size > limit)
                            failures[impl.Definition.Name] = ("skipped", "smaller workload in this family timed out");
                    }
                    foreach (var (phase, rounds) in new[] { ("warmup", options.Warmups), ("timing", options.Repetitions), ("profile", options.Profiles) })
                    {
                        for (var round = 0; round < rounds; round++)
                        {
                            var active = implementations.Count(i => !failures.ContainsKey(i.Definition.Name) && i.BudgetUsedSeconds < options.Budget);
                            if (active > 0) Log($"{w.Id}/{storage}: {phase} round {round + 1}/{rounds}; {active} implementations remaining");
                            var roundElapsed = Stopwatch.StartNew();
                            var lastProgress = TimeSpan.Zero;
                            var completed = 0;
                            var order = implementations.ToArray(); random.Shuffle(order);
                            foreach (var impl in order)
                            {
                                var name = impl.Definition.Name;
                                if (failures.ContainsKey(name)) continue;
                                if (impl.BudgetUsedSeconds >= options.Budget)
                                { failures[name] = ("budget-exhausted", "implementation campaign budget exhausted"); continue; }
                                var spent = Stopwatch.StartNew();
                                var remaining = options.Budget - impl.BudgetUsedSeconds;
                                BenchSample sample;
                                try
                                {
                                    sample = await Trial(impl.Definition, ws, fixture, w, storage, phase, round,
                                        bases[storage], inputPath, TimeSpan.FromSeconds(Math.Min(options.Timeout, remaining)));
                                }
                                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                                { sample = new(name, w.Id, storage, phase, round, "harness-error", e.Message, null); }
                                impl.BudgetUsedSeconds += spent.Elapsed.TotalSeconds;
                                samples.Add(sample);
                                if (sample.Status != "ok")
                                {
                                    failures[name] = (sample.Status, sample.Reason);
                                    Log($"{w.Id}/{storage}: {name}: {phase} round {round + 1}/{rounds}: {sample.Status}: {sample.Reason}");
                                    if (sample.Status == "timeout") blocked[(name, storage, w.Family, w.Mode)] = w.Size;
                                }
                                completed++;
                                if (roundElapsed.Elapsed - lastProgress >= TimeSpan.FromSeconds(10))
                                {
                                    Log($"{w.Id}/{storage}: {phase} round {round + 1}/{rounds}; {completed}/{active} invocations completed; " +
                                        $"{roundElapsed.Elapsed.TotalSeconds:F1}s elapsed; last implementation: {name}");
                                    lastProgress = roundElapsed.Elapsed;
                                }
                            }
                        }
                    }
                    var outcomes = new Dictionary<string, int>();
                    foreach (var impl in implementations)
                    {
                        var name = impl.Definition.Name;
                        var own = samples.Where(s => s.Implementation == name && s.Workload == w.Id && s.Storage == storage).ToList();
                        var failed = failures.GetValueOrDefault(name);
                        var cell = Summarize(name, info, storage, own, failed.Status ?? "ok", failed.Reason ?? "", options.Repetitions, options.Warmups, options.Profiles);
                        cells.Add(cell);
                        outcomes[cell.Status] = outcomes.GetValueOrDefault(cell.Status) + 1;
                    }
                    Log($"{w.Id}/{storage} complete: " + string.Join(", ", outcomes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Value} {pair.Key}")));
                    Save();
                }
                File.Delete(inputPath);
            }
        }
        finally
        {
            try
            {
                foreach (var target in bases.Values) BenchFixtures.Cleanup(target);
                File.Delete(Path.Combine(dir, "input.json"));
            }
            finally { Save(); }
        }
        campaign = campaign with { Completed = true }; Save();
        Console.WriteLine($"saved campaign {id} to {dir}");
        return cells.Any(c => c.Status != "ok") ? 1 : 0;
    }

    internal static void ValidatePackage(BenchLockedImplementation definition)
    {
        var path = definition.PackagePath;
        if (path is null || Path.GetDirectoryName(path) != "/nix/store" ||
            definition.Name != Path.GetFileName(definition.Name) || definition.Build is not null ||
            definition.Command != Shell.Quote(Path.Combine(path, "bin", definition.Name)))
            throw new InvalidOperationException("invalid packaged executable in benchmark lock");
        if (!File.Exists(Path.Combine(path, "bin", definition.Name)))
            throw new IOException("locked package is missing; reproduce the campaign before benchmarking");
    }

    static Implementation Adapter(BenchLockedImplementation i) => new(i.Name, i.Description, i.Repo, i.Build, i.Command, Source: i.Source, Language: i.Language);
    static string Command(Implementation i, string template, Workspace ws) =>
        "export PATH=" + Shell.Quote(Path.Combine(ws.Runtimes, "tools", "bin")) + ":$PATH\n" +
        "export XDG_CACHE_HOME=" + Shell.Quote(Path.Combine(ws.Runtimes, "cache")) + "\n" + i.Expand(template, ws);

    static async Task<BenchSample> Trial(BenchLockedImplementation impl, Workspace ws, BenchFixture fixture,
        BenchWorkload w, string storage, string phase, int round, string targetBase, string inputPath, TimeSpan timeout)
    {
        var sandbox = Path.Combine(targetBase, Guid.NewGuid().ToString("N"));
        var root = Path.Combine(sandbox, "root"); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(sandbox, "outside.txt"), "outside sentinel\n");
            BenchFixtures.Prepare(root, fixture.Setup);
            var adapter = Adapter(impl);
            var command = Command(adapter, impl.Command, ws).Replace("{input}", Shell.Quote(inputPath)).Replace("{output}", Shell.Quote(root));
            var timeCommand = Path.Combine(ws.Runtimes, "tools", "bin", "time");
            if (phase == "profile" && !File.Exists(timeCommand))
                return new(impl.Name, w.Id, storage, phase, round, "resource-unavailable", "GNU time is missing", null);
            var result = await BenchProcess.Run(command, root, fixture.Input, timeout, phase == "profile", timeCommand: timeCommand);
            var status = result.ExitCode is null ? "timeout" : result.LeftChildren ? "left-children" : result.ExitCode != 0 ? "exit-error" : "ok";
            var reason = status == "timeout" ? "invocation deadline exceeded" : status == "left-children" ? "command left descendant processes running"
                : status == "exit-error" ? $"exit {result.ExitCode}: {Shell.Tail(result.Stderr)}" : "";
            if (status == "ok")
            {
                try { reason = BenchFixtures.Verify(root, fixture.Expected) ?? ""; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { reason = "cannot inspect output: " + e.Message; }
                if (reason.Length > 0) status = "incorrect-output";
                if (!BenchFixtures.SentinelIntact(Path.Combine(sandbox, "outside.txt")) ||
                    Directory.EnumerateFileSystemEntries(sandbox).Any(p => Path.GetFileName(p) is not ("root" or "outside.txt")))
                { status = "incorrect-output"; reason = "wrote outside target directory"; }
                if (phase == "profile" && result.MaxRssKiB is null)
                { status = "resource-unavailable"; reason = "GNU time did not return valid resource measurements"; }
            }
            return new(impl.Name, w.Id, storage, phase, round, status, reason,
                status == "ok" && phase == "timing" ? result.Milliseconds : null,
                status == "ok" && phase == "profile" ? result.UserSeconds : null,
                status == "ok" && phase == "profile" ? result.SystemSeconds : null,
                status == "ok" && phase == "profile" ? result.MaxRssKiB : null);
        }
        finally { BenchFixtures.Cleanup(sandbox); }
    }

    static BenchFixtureInfo Describe(BenchWorkload w, BenchFixture fixture) => new(w.Id, w.Family, w.Size, w.Mode, fixture.Hash,
        fixture.Count("file") - (w.Mode != "create" ? 1 : 0), fixture.Count("directory"), fixture.Count("link"), fixture.Count("script"),
        fixture.Input.LongLength, fixture.PayloadBytes, fixture.Depth);

    internal static BenchStatistics? Statistics(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) return null;
        double Percentile(double p)
        {
            var index = (values.Length - 1) * p; var lo = (int)index; var hi = (int)Math.Ceiling(index);
            return values[lo] + (values[hi] - values[lo]) * (index - lo);
        }
        return new(values.Length, Percentile(.5), Percentile(.25), Percentile(.75), values[0], values[^1]);
    }

    internal static BenchCell Summarize(string name, BenchFixtureInfo info, string storage, List<BenchSample> samples,
        string status, string reason, int repetitions, int warmups = 0, int profiles = 0)
    {
        var timing = Statistics(samples.Where(s => s.Phase == "timing" && s.Status == "ok").Select(s => s.Milliseconds!.Value));
        if (status == "ok" && timing?.Count != repetitions) { status = "incomplete"; reason = "not all timing rounds completed"; }
        if (status == "ok" && (samples.Count(s => s.Phase == "warmup" && s.Status == "ok") != warmups ||
            samples.Count(s => s.Phase == "profile" && s.Status == "ok") != profiles))
        { status = "incomplete"; reason = "not all warmup/profiling rounds completed"; }
        double? Median(Func<BenchSample, double?> metric) => Statistics(samples.Where(s => s.Phase == "profile" && s.Status == "ok")
            .Select(metric).Where(v => v.HasValue).Select(v => v!.Value))?.Median;
        var seconds = timing?.Median / 1000;
        return new(name, info.Id, storage, status, reason, timing,
            status == "ok" && seconds > 0 ? (info.Files + info.Directories + info.Links + info.Scripts) / seconds : null,
            status == "ok" && seconds > 0 ? info.PayloadBytes / 1048576d / seconds : null,
            Median(s => s.UserSeconds), Median(s => s.SystemSeconds), Median(s => s.MaxRssKiB));
    }

    static async Task<Dictionary<string, string>> EnvironmentInfo(string dir, string repo)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, command) in new[] { ("kernel", "uname -srmo"), ("cpu", "lscpu"),
            ("filesystem", "findmnt -n -o FSTYPE,OPTIONS,SOURCE -T " + Shell.Quote(dir)), ("testerRevision", "git -C " + Shell.Quote(repo) + " rev-parse HEAD"),
            ("testerWorkingTree", "git -C " + Shell.Quote(repo) + " status --porcelain") })
        {
            var execution = await BenchProcess.Run(command, dir, null, TimeSpan.FromSeconds(5));
            result[key] = execution.ExitCode == 0 ? execution.Stdout.Trim() : "unavailable";
        }
        foreach (var key in new[] { "ImageOS", "ImageVersion", "RUNNER_OS", "RUNNER_ARCH", "GITHUB_RUN_ID", "GITHUB_SHA" })
            result[key] = Environment.GetEnvironmentVariable(key) ?? "local";
        result["timing"] = "fresh process; warm caches; buffered writes; common setsid/sh wrapper included; no fsync";
        result["rss"] = "maximum process RSS (largest child), not aggregate process-tree memory";
        result["locale"] = "C.UTF-8";
        return result;
    }

    internal static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new ArgumentException($"empty JSON: {path}");
    static void Write<T>(string path, T value)
    {
        var temp = path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(value, Json) + "\n"); File.Move(temp, path, true);
    }

    static void Export(string dir, string output)
    {
        var campaign = Read<BenchCampaign>(Path.Combine(dir, "campaign.json"));
        var raw = Read<BenchRaw>(Path.Combine(dir, "samples.json"));
        if (campaign.SchemaVersion != 1 || raw.SchemaVersion != 1 || campaign.Id != raw.CampaignId)
            throw new ArgumentException("unsupported or mismatched campaign files");
        var options = campaign.Options;
        if (options.Repetitions < 1 || options.Warmups < 0 || options.Profiles < 0 || options.Storage is not ("disk" or "tmpfs" or "both"))
            throw new ArgumentException("invalid campaign options");
        foreach (var w in campaign.RequestedWorkloads ?? [])
            if (!campaign.Workloads.Any(i => i.Id == w.Id)) campaign.Workloads.Add(Describe(w, BenchFixtures.Generate(w, options.Seed)));
        var storages = options.Storage == "both" ? new[] { "disk", "tmpfs" } : [options.Storage];
        var identities = new HashSet<(string, string, string, string, int)>();
        foreach (var sample in raw.Samples)
        {
            var rounds = sample.Phase switch { "timing" => options.Repetitions, "warmup" => options.Warmups, "profile" => options.Profiles, _ => 0 };
            if (!campaign.Implementations.Any(i => i.Definition.Name == sample.Implementation) ||
                !campaign.Workloads.Any(w => w.Id == sample.Workload) || !storages.Contains(sample.Storage) ||
                sample.Round < 0 || sample.Round >= rounds || !identities.Add((sample.Implementation, sample.Workload, sample.Storage, sample.Phase, sample.Round)) ||
                new[] { sample.Milliseconds, sample.UserSeconds, sample.SystemSeconds, sample.MaxRssKiB }.Any(n => n.HasValue && (!double.IsFinite(n.Value) || n.Value < 0)) ||
                (sample.Status == "ok" && sample.Phase == "timing" && sample.Milliseconds is null) ||
                (sample.Status == "ok" && sample.Phase == "profile" && (sample.MaxRssKiB is null || sample.UserSeconds is null || sample.SystemSeconds is null)))
                throw new ArgumentException("invalid or duplicate raw sample");
        }
        var recomputed = new List<BenchCell>();
        foreach (var impl in campaign.Implementations)
        foreach (var w in campaign.Workloads)
        foreach (var storage in storages)
        {
            var name = impl.Definition.Name;
            var own = raw.Samples.Where(s => s.Implementation == name && s.Workload == w.Id && s.Storage == storage).ToList();
            var prior = campaign.Results.FirstOrDefault(c => c.Implementation == name && c.Workload == w.Id && c.Storage == storage);
            var failure = own.FirstOrDefault(s => s.Status != "ok");
            var status = failure?.Status ?? prior?.Status ?? (impl.Status == "ready" ? "incomplete" : impl.Status);
            var reason = failure?.Reason ?? prior?.Reason ?? (impl.Status == "ready" ? "campaign stopped before this workload completed" : impl.Reason);
            recomputed.Add(Summarize(name, w, storage, own, status, reason, options.Repetitions, options.Warmups, options.Profiles));
        }
        campaign = campaign with { Results = recomputed };
        Directory.CreateDirectory(output);
        Write(Path.Combine(output, "benchmarks.json"), campaign);
        Write(Path.Combine(output, "benchmark-samples.json"), raw);
        Console.WriteLine($"exported campaign {campaign.Id} to {output}");
    }
}
