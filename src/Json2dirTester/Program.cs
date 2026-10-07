using System.Text.Encodings.Web;
using System.Text.Json;
using Json2dirTester;

const string Usage = """
    Usage:
      json2dir-tester list
      json2dir-tester build (NAME... | --all)
      json2dir-tester run (NAME... | --all) [options]
      json2dir-tester serve [--dir DIR] [--bind ADDR] [--port N]
      json2dir-tester export [--dir DIR] [--out DIR]

    Run options:
      --suite NAME      only cases from cases/NAME (repeatable)
      --level NAME      only cases of this level, e.g. core (repeatable)
      --filter TEXT     only cases whose name contains TEXT
      --timeout SEC     time limit per case (default: the implementation's "timeout", else 10)
      --json FILE       also write results as JSON
      --verbose         print stderr of failing cases
      --cases DIR       load cases from DIR instead of cases/

    Serve options (live dashboard over a campaign directory with groupN.lst/.txt logs):
      --dir DIR         campaign directory (default: prelim)
      --bind ADDR       address to listen on (default: 127.0.0.1)
      --port N          port (default: 8080)

    Export options (static copy of the dashboard: index.html + results.json):
      --dir DIR         campaign directory (default: prelim)
      --out DIR         output directory (default: site)

    Global options:
      --sources DIR     where implementation sources live (default: <workspace>/others)
      --runtimes DIR    where toolchains live (default: <workspace>/runtimes)

    Implementations are described in implementations/*.json.
    The tester must run on Linux (on Windows: use run.ps1, which runs it in WSL).
    """;

var positional = new List<string>();
var suites = new List<string>();
var levels = new List<string>();
string? filter = null, jsonPath = null, sources = null, runtimes = null, casesDir = null, serveDir = null, outDir = null;
var bind = "127.0.0.1";
var port = 8080;
TimeSpan? timeout = null;
bool all = false, verbose = false;

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"{args[i - 1]} needs a value");
        switch (args[i])
        {
            case "--all": all = true; break;
            case "--verbose": verbose = true; break;
            case "--suite": suites.Add(Next()); break;
            case "--level": levels.Add(Next()); break;
            case "--filter": filter = Next(); break;
            case "--timeout": timeout = TimeSpan.FromSeconds(double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture)); break;
            case "--json": jsonPath = Path.GetFullPath(Next()); break;
            case "--sources": sources = Next(); break;
            case "--runtimes": runtimes = Next(); break;
            case "--cases": casesDir = Path.GetFullPath(Next()); break;
            case "--dir": serveDir = Path.GetFullPath(Next()); break;
            case "--bind": bind = Next(); break;
            case "--port": port = int.Parse(Next()); break;
            case "--out": outDir = Path.GetFullPath(Next()); break;
            case "-h" or "--help": Console.WriteLine(Usage); return 0;
            case var a when a.StartsWith("--"): throw new ArgumentException($"unknown option {a}");
            default: positional.Add(args[i]); break;
        }
    }
    if (positional.Count == 0)
        throw new ArgumentException("missing command");

    var ws = Workspace.Discover(sources, runtimes);
    var known = Implementations.Load(ws.ImplementationsDir);

    List<Implementation> Select()
    {
        var names = positional.Skip(1).ToList();
        if (all == (names.Count > 0))
            throw new ArgumentException("name implementations or pass --all");
        if (all)
            return known;
        return names.Select(n => known.FirstOrDefault(k => k.Name == n)
            ?? throw new ArgumentException($"unknown implementation '{n}' (see: json2dir-tester list)")).ToList();
    }

    switch (positional[0])
    {
        case "list":
            foreach (var impl in known)
                Console.WriteLine($"{impl.Name,-20} {impl.Description}");
            return 0;
        case "build":
            RequireLinux();
            return Select().Select(impl => Build(impl, ws)).Max();
        case "run":
            RequireLinux();
            return RunAll(Select(), ws);
        case "serve":
            return Serve.Run(ws, known, serveDir ?? Path.Combine(ws.Repo, "prelim"), bind, port);
        case "export":
            return Serve.Export(ws, known, serveDir ?? Path.Combine(ws.Repo, "prelim"), outDir ?? Path.Combine(ws.Repo, "site"));
        default:
            throw new ArgumentException($"unknown command '{positional[0]}'");
    }
}
catch (ArgumentException e)
{
    Console.Error.WriteLine($"error: {e.Message}\n\n{Usage}");
    return 2;
}
catch (Exception e) when (e is CaseException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

static void RequireLinux()
{
    if (!OperatingSystem.IsLinux())
        throw new InvalidOperationException("json2dir implementations are tested on Linux; on Windows use run.ps1 (runs the tester in WSL)");
}

static int Build(Implementation impl, Workspace ws)
{
    var src = impl.SourceDir(ws);
    Console.WriteLine($"== {impl.Name}: {src}");
    if (!Directory.Exists(src))
    {
        var clone = Shell.Run($"git clone {Shell.Quote(impl.Repo)} {Shell.Quote(src)}", ws.Repo, null, null, inheritOutput: true);
        if (clone.ExitCode != 0)
            return 1;
    }
    if (impl.Build is null)
        return 0;
    var result = Shell.Run(impl.Expand(impl.Build, ws), src, null, null, inheritOutput: true);
    Console.WriteLine(result.ExitCode == 0 ? $"== {impl.Name}: built" : $"== {impl.Name}: build failed ({result.ExitCode})");
    return result.ExitCode == 0 ? 0 : 1;
}

int RunAll(List<Implementation> impls, Workspace ws)
{
    var cases = Cases.Load(casesDir ?? ws.Cases)
        .Where(c => suites.Count == 0 || suites.Contains(c.Name.Split('/')[0]))
        .Where(c => levels.Count == 0 || levels.Contains(c.Level))
        .Where(c => filter is null || c.Name.Contains(filter))
        .ToList();
    var color = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
    string Paint(string text, string code) => color ? $"\e[{code}m{text}\e[0m" : text;

    var report = new List<(Implementation Impl, List<CaseResult> Results)>();
    foreach (var impl in impls)
    {
        Console.WriteLine(Paint($"== {impl.Name} — {impl.Description}", "1"));
        var command = impl.Expand(impl.Command, ws);
        var results = new List<CaseResult>();
        // An implementation may limit itself to some suites (e.g. one too slow for more); --suite overrides that.
        foreach (var c in cases.Where(c => c.AppliesTo(impl.Name) && (suites.Count > 0 || impl.InSuites(c))))
        {
            var r = Runner.Run(c, command, timeout ?? TimeSpan.FromSeconds(impl.Timeout ?? 10));
            results.Add(r);
            if (r.Status == Status.Pass)
            {
                Console.WriteLine($"{Paint("✓", "32")} {c.Name} {Paint(c.Description, "2")}");
                continue;
            }
            if (r.Status == Status.Skip)
            {
                Console.WriteLine($"{Paint("-", "33")} {c.Name} {Paint(c.Description, "2")}");
                Console.WriteLine($"    skipped: {r.Reason}");
                continue;
            }
            Console.WriteLine($"{Paint("✗", "31")} {c.Name} {Paint(c.Description, "2")}");
            Console.WriteLine($"    {r.Reason}  [RFC §{c.Section}]");
            if (verbose && r.Stderr.Trim().Length > 0)
                foreach (var line in r.Stderr.Trim().Split('\n'))
                    Console.WriteLine($"    | {line}");
        }

        Console.WriteLine();
        foreach (var group in results.GroupBy(r => Group(r.Case)))
        {
            Console.WriteLine(Paint($"{group.Key,-24} {Score(group.ToList())}", "1"));
        }
        Console.WriteLine();
        report.Add((impl, results));
    }

    if (impls.Count > 1)
    {
        var groups = report.SelectMany(x => x.Results).Select(r => Group(r.Case)).Distinct().ToList();
        Console.WriteLine(Paint($"{"implementation",-20} " + string.Join(" ", groups.Select(g => $"{g,24}")), "1"));
        foreach (var (impl, results) in report)
            Console.WriteLine($"{impl.Name,-20} " + string.Join(" ", groups.Select(g =>
            {
                var rs = results.Where(r => Group(r.Case) == g).ToList();
                var ran = rs.Where(r => r.Status != Status.Skip).ToList();
                return $"{(rs.Count == 0 ? "-" : ran.Count(r => r.Status == Status.Pass) + "/" + ran.Count),24}";
            })));
    }

    if (jsonPath is not null)
    {
        var json = new
        {
            implementations = report.Select(x => new
            {
                name = x.Impl.Name,
                results = x.Results.Select(r => new
                {
                    @case = r.Case.Name,
                    level = r.Case.Level,
                    status = r.Status.ToString().ToLowerInvariant(),
                    reason = r.Reason,
                }),
            }),
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(json, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");
    }

    return report.Any(x => x.Results.Any(r => r.Status == Status.Fail)) ? 1 : 0;
}

static string Score(List<CaseResult> results)
{
    var ran = results.Where(r => r.Status != Status.Skip).ToList();
    var passed = ran.Count(r => r.Status == Status.Pass);
    var skipped = results.Count - ran.Count;
    var verdict = passed == ran.Count ? "conformant" : "not conformant";
    return $"{passed}/{ran.Count} passed — {verdict}" + (skipped > 0 ? $" ({skipped} skipped)" : "");
}

// "conformance/core/001-empty-object" -> "conformance/core"
static string Group(Case c) => c.Name[..c.Name.LastIndexOf('/')];
