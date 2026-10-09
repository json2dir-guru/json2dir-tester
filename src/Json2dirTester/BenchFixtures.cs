using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Json2dirTester;

sealed record BenchEntry(string Kind, string Content = "", int? Mode = null);
sealed record BenchWorkload(string Id, string Family, int Size, string Mode = "create");
sealed record BenchFixture(byte[] Input, Dictionary<string, BenchEntry> Expected,
    Dictionary<string, BenchEntry> Setup, string Hash, long PayloadBytes, int Depth)
{
    public int Count(string kind) => Expected.Values.Count(e => e.Kind == kind);
}

static class BenchFixtures
{
    public static List<BenchWorkload> Workloads(string suite = "standard")
    {
        var extended = ExtendedWorkloads();
        return suite switch
        {
            "standard" => extended.Where(w => w.Id is "empty" or "files-1000" or "payload-8MiB" or
                "balanced-1000" or "depth-64" or "escapes-1MiB" or "config" or "update").ToList(),
            "extended" => extended,
            _ => throw new ArgumentException("suite must be standard or extended"),
        };
    }

    static List<BenchWorkload> ExtendedWorkloads() =>
    [
        new("empty", "startup", 0), new("one-file", "startup", 1),
        .. new[] { 100, 1000, 10000 }.Select(n => new BenchWorkload($"files-{n}", "files", n)),
        .. new[] { 1, 8, 64 }.Select(n => new BenchWorkload($"payload-{n}MiB", "payload", n * 1048576)),
        .. new[] { 1000, 10000 }.Select(n => new BenchWorkload($"balanced-{n}", "balanced", n)),
        .. new[] { 16, 64, 256 }.Select(n => new BenchWorkload($"depth-{n}", "depth", n)),
        .. new[] { "directories", "links", "scripts" }.SelectMany(k => new[] { 100, 1000 }
            .Select(n => new BenchWorkload($"{k}-{n}", k, n))),
        .. new[] { "utf8", "escapes" }.SelectMany(k => new[] { 1, 8 }
            .Select(n => new BenchWorkload($"{k}-{n}MiB", k, n * 1048576))),
        new("config", "config", 600), new("reapply", "config", 600, "reapply"),
        new("update", "config", 600, "update"),
    ];

    public static BenchFixture Generate(BenchWorkload w, int seed)
    {
        var entries = new Dictionary<string, BenchEntry>(StringComparer.Ordinal);
        var setup = new Dictionary<string, BenchEntry>(StringComparer.Ordinal);
        var random = new Random(seed);
        string Text(int length) => new(Enumerable.Range(0, length).Select(_ => (char)random.Next(97, 123)).ToArray());
        void DirectoryEntry(string path) => entries[path] = new("directory");
        void FileEntry(string path, string value, string kind = "file") => entries[path] = new(kind, value);
        switch (w.Family)
        {
            case "startup":
                if (w.Size > 0) FileEntry("hello", "Hello, world!\n");
                break;
            case "files":
                for (var i = 0; i < w.Size; i++) FileEntry($"f{i:D5}", Text(64));
                break;
            case "payload": FileEntry("payload", new string('x', w.Size)); break;
            case "balanced":
                // Branching factor 10; at most 10 files per leaf directory.
                for (var i = 0; i < w.Size; i++)
                {
                    var path = $"d{i / 1000:D2}/d{i / 100 % 10}/d{i / 10 % 10}";
                    var parts = path.Split('/');
                    DirectoryEntry(parts[0]); DirectoryEntry(string.Join('/', parts[..2])); DirectoryEntry(path);
                    FileEntry($"{path}/f{i % 10}", Text(64));
                }
                break;
            case "depth":
                var nested = "";
                for (var i = 0; i < w.Size; i++) { nested += (i > 0 ? "/" : "") + "d"; DirectoryEntry(nested); }
                FileEntry(nested + "/file", "deep\n");
                break;
            case "directories":
                for (var i = 0; i < w.Size; i++) DirectoryEntry($"d{i:D5}");
                break;
            case "links":
                for (var i = 0; i < w.Size; i++) FileEntry($"l{i:D5}", "missing-target", "link");
                break;
            case "scripts":
                for (var i = 0; i < w.Size; i++) FileEntry($"s{i:D5}", "#!/bin/sh\necho hello\n", "script");
                break;
            case "utf8": case "escapes":
                // é is two UTF-8 bytes, so decoded payload sizes match the ASCII payload family.
                FileEntry("unicode", new string('é', w.Size / 2));
                break;
            case "config":
                for (var d = 0; d < 10; d++)
                {
                    DirectoryEntry($"config{d:D2}");
                    for (var f = 0; f < 50; f++) FileEntry($"config{d:D2}/f{f:D2}", Text(256));
                    for (var f = 0; f < 5; f++)
                    {
                        FileEntry($"config{d:D2}/l{f}", $"f{f:D2}", "link");
                        FileEntry($"config{d:D2}/s{f}", "#!/bin/sh\necho hello\n", "script");
                    }
                }
                if (w.Mode != "create")
                {
                    setup = new(entries, StringComparer.Ordinal);
                    foreach (var path in setup.Keys.ToArray())
                        if (setup[path].Kind == "directory") setup[path] = setup[path] with { Mode = 488 }; // 0750
                    setup["unlisted"] = new("file", "keep me\n", 416); // 0640
                    if (w.Mode == "update")
                    {
                        for (var d = 0; d < 10; d++)
                        {
                            FileEntry($"config{d:D2}/f00", "short");
                            FileEntry($"config{d:D2}/f01", "#!/bin/sh\nexit 0\n", "script");
                            FileEntry($"config{d:D2}/s0", "now regular\n");
                            FileEntry($"config{d:D2}/l0", "replacement\n");
                            FileEntry($"config{d:D2}/f02", "f03", "link");
                            DirectoryEntry($"config{d:D2}/new");
                            FileEntry($"config{d:D2}/new/file", "merged\n");
                        }
                        // A replaced symlink must not write to its target outside root.
                        setup["config00/l0"] = new("link", "../../outside.txt");
                    }
                }
                break;
        }
        var input = Encode(entries, w.Family == "escapes");
        var expected = new Dictionary<string, BenchEntry>(entries, StringComparer.Ordinal);
        foreach (var (path, entry) in setup)
        {
            if (entry.Kind == "directory" && expected.TryGetValue(path, out var output) && output.Kind == "directory")
                expected[path] = output with { Mode = entry.Mode };
            else expected.TryAdd(path, entry);
        }
        return new(input, expected, setup, Convert.ToHexStringLower(SHA256.HashData(input)),
            entries.Values.Where(e => e.Kind is "file" or "script").Sum(e => (long)Encoding.UTF8.GetByteCount(e.Content)),
            entries.Count == 0 ? 0 : entries.Keys.Max(p => p.Count(c => c == '/')));
    }

    static byte[] Encode(Dictionary<string, BenchEntry> entries, bool escaped)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        { MaxDepth = 4096, Encoder = escaped ? JavaScriptEncoder.Default : JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var children = entries.Keys.GroupBy(p => p.Contains('/') ? p[..p.LastIndexOf('/')] : "")
            .ToDictionary(g => g.Key, g => g.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        void WriteDirectory(string parent)
        {
            writer.WriteStartObject();
            foreach (var path in children.GetValueOrDefault(parent, []))
            {
                writer.WritePropertyName(path[(path.LastIndexOf('/') + 1)..]);
                var entry = entries[path];
                if (entry.Kind == "directory") WriteDirectory(path);
                else if (entry.Kind == "file") writer.WriteStringValue(entry.Content);
                else { writer.WriteStartArray(); writer.WriteStringValue(entry.Kind == "link" ? "link" : "script"); writer.WriteStringValue(entry.Content); writer.WriteEndArray(); }
            }
            writer.WriteEndObject();
        }
        WriteDirectory(""); writer.Flush();
        return stream.ToArray();
    }

    public static void Prepare(string root, Dictionary<string, BenchEntry> setup)
    {
        foreach (var (path, entry) in setup.OrderBy(p => p.Key.Count(c => c == '/')).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var full = Path.Combine(root, path);
            if (entry.Kind == "directory")
            {
                Directory.CreateDirectory(full);
                File.SetUnixFileMode(full, (UnixFileMode)(entry.Mode ?? 493));
            }
            else if (entry.Kind == "link") File.CreateSymbolicLink(full, entry.Content);
            else
            {
                File.WriteAllText(full, entry.Content, Shell.Utf8);
                File.SetUnixFileMode(full, (UnixFileMode)(entry.Mode ?? (entry.Kind == "script" ? 493 : 420)));
            }
        }
    }

    public static string? Verify(string root, IReadOnlyDictionary<string, BenchEntry> expected)
    {
        if (new DirectoryInfo(root).LinkTarget is not null) return "target root replaced by a symlink";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.TryPop(out var directory))
        {
            foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                var path = Path.GetRelativePath(root, info.FullName);
                if (!expected.TryGetValue(path, out var entry)) return $"unexpected entry: {path}";
                seen.Add(path);
                if (entry.Mode is { } exactMode && (int)File.GetUnixFileMode(info.FullName) != exactMode)
                    return $"wrong preserved permissions: {path}";
                if (info.LinkTarget is { } target)
                {
                    if (entry.Kind != "link" || entry.Content != target) return $"wrong symlink: {path}";
                }
                else if (info is DirectoryInfo)
                {
                    if (entry.Kind != "directory") return $"unexpected directory: {path}";
                    stack.Push(info.FullName);
                }
                else
                {
                    if (entry.Kind is not ("file" or "script")) return $"wrong entry type: {path}";
                    // Reject FIFOs/devices before opening: inspection must never block on special files.
                    if (NativeFileType(info.FullName) != 0x8000) return $"not a regular file: {path}";
                    var mode = File.GetUnixFileMode(info.FullName) & (UnixFileMode)73;
                    if ((int)mode != (entry.Kind == "script" ? 73 : 0)) return $"wrong execute bits: {path}";
                    using var file = File.OpenRead(info.FullName);
                    if (file.Length != Encoding.UTF8.GetByteCount(entry.Content)) return $"wrong content length: {path}";
                    using var reader = new StreamReader(file, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
                    var buffer = new char[32768]; var offset = 0;
                    int count;
                    try
                    {
                        while ((count = reader.Read(buffer)) > 0)
                        {
                            if (offset + count > entry.Content.Length || !buffer.AsSpan(0, count).SequenceEqual(entry.Content.AsSpan(offset, count))) return $"wrong content: {path}";
                            offset += count;
                        }
                    }
                    catch (DecoderFallbackException) { return $"invalid UTF-8 content: {path}"; }
                    if (offset != entry.Content.Length) return $"wrong content: {path}";
                }
            }
        }
        return seen.Count == expected.Count ? null : $"missing entry: {expected.Keys.First(k => !seen.Contains(k))}";
    }

    public static bool SentinelIntact(string path)
    {
        try { return NativeFileType(path) == 0x8000 && File.ReadAllText(path) == "outside sentinel\n"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void Cleanup(string root)
    {
        if (!Directory.Exists(root)) return;
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.TryPop(out var path))
        {
            var info = new DirectoryInfo(path);
            if (info.LinkTarget is not null) continue;
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var child in info.EnumerateFileSystemInfos())
                if (child is DirectoryInfo && child.LinkTarget is null) stack.Push(child.FullName);
        }
        Directory.Delete(root, true);
    }

    // statx has a stable Linux ABI; only the type bits are needed here.
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    static extern int statx(int dirfd, string path, int flags, uint mask, [System.Runtime.InteropServices.Out] byte[] buffer);
    static int NativeFileType(string path)
    {
        var buffer = new byte[256];
        if (statx(-100, path, 0x100, 2, buffer) != 0) throw new IOException($"statx failed: {path}");
        return BitConverter.ToUInt16(buffer, 28) & 0xf000;
    }
}
