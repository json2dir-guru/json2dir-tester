using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Json2dirTester;

/// <summary>Reads and writes directory trees in the json2dir scheme itself.</summary>
static class Tree
{
    const UnixFileMode ExecBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static readonly JsonSerializerOptions ShowOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 4096 };

    /// <summary>Describes a directory without following symlinks.</summary>
    public static JsonObject Read(string root)
    {
        var tree = new JsonObject();
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            tree[entry.Name] = ReadEntry(entry);
        return tree;
    }

    static JsonNode ReadEntry(FileSystemInfo entry)
    {
        if (entry.LinkTarget is { } target)
            return new JsonArray("link", target);
        if (entry is DirectoryInfo)
            return Read(entry.FullName);

        var data = File.ReadAllBytes(entry.FullName);
        string content;
        try { content = StrictUtf8.GetString(data); }
        catch (DecoderFallbackException) { return new JsonArray("bytes", Convert.ToBase64String(data)); }

        var mode = File.GetUnixFileMode(entry.FullName);
        var exec = mode & ExecBits;
        if (exec == 0)
            return JsonValue.Create(content);
        if (exec == ExecBits)
            return new JsonArray("script", content);
        return new JsonArray("mode", Convert.ToString((int)mode, 8).PadLeft(4, '0'), content);
    }

    /// <summary>Materializes a tree; used to prepare the pre-existing state of a case.</summary>
    public static void Write(string root, JsonObject tree)
    {
        foreach (var (name, value) in tree.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(root, name);
            switch (value)
            {
                case JsonObject dir:
                    Directory.CreateDirectory(path);
                    Write(path, dir);
                    break;
                case JsonValue file:
                    File.WriteAllText(path, file.GetValue<string>(), StrictUtf8);
                    break;
                case JsonArray { Count: 2 } a when (string?)a[0] == "script":
                    File.WriteAllText(path, (string)a[1]!, StrictUtf8);
                    File.SetUnixFileMode(path, File.GetUnixFileMode(path) | ExecBits);
                    break;
                case JsonArray { Count: 2 } a when (string?)a[0] == "link":
                    File.CreateSymbolicLink(path, (string)a[1]!);
                    break;
                default:
                    throw new CaseException($"unsupported setup value at {path}: {Show(value)}");
            }
        }
    }

    /// <summary>Lists differences as "path: problem", like the reference conformance runner.</summary>
    public static List<string> Diff(JsonObject expected, JsonObject actual, string prefix = "")
    {
        var lines = new List<string>();
        var names = expected.Select(p => p.Key).Union(actual.Select(p => p.Key)).Order(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var path = prefix + name;
            if (!actual.ContainsKey(name))
                lines.Add($"{path}: missing");
            else if (!expected.ContainsKey(name))
                lines.Add($"{path}: unexpected entry");
            else if (expected[name] is JsonObject e && actual[name] is JsonObject a)
                lines.AddRange(Diff(e, a, path + "/"));
            else if (!JsonNode.DeepEquals(expected[name], actual[name]))
                lines.Add($"{path}: expected {Show(expected[name])}, got {Show(actual[name])}");
        }
        return lines;
    }

    public static string Show(JsonNode? value) => value?.ToJsonString(ShowOptions) ?? "null";
}
