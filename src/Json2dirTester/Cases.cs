using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Json2dirTester;

sealed class CaseException(string message) : Exception(message);

/// <summary>One test case in the conformance format (see cases/conformance/README.md).</summary>
sealed record Case(
    string Name,
    string Description,
    string Section,
    string Level,
    JsonObject? Setup,
    byte[] Input,
    JsonObject? ExpectTree,
    bool ExpectError,
    bool AcceptError);

static class Cases
{
    static readonly string[] InputFields = ["input", "input_text", "input_base64"];
    static readonly HashSet<string> CaseFields = ["description", "section", "level", "setup", "expect", .. InputFields];
    static readonly HashSet<string> ExpectFields = ["tree", "error", "accept_error"];
    // Cases nest deeper than the default limit of 64 (see core/009-deep-nesting).
    static readonly JsonDocumentOptions DocOptions = new() { MaxDepth = 4096 };

    /// <summary>Loads every *.json under <paramref name="directory"/>; names are "suite/level/file".</summary>
    public static List<Case> Load(string directory)
    {
        var cases = new List<Case>();
        var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var name = Path.GetRelativePath(directory, file).Replace('\\', '/')[..^".json".Length];
            cases.Add(Parse(name, File.ReadAllBytes(file)));
        }
        return cases;
    }

    static Case Parse(string name, byte[] bytes)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(bytes, DocOptions); }
        catch (JsonException e) { throw new CaseException($"{name}: {e.Message}"); }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new CaseException($"{name}: a case must be a JSON object");

        var unknown = root.EnumerateObject().Select(p => p.Name).Where(n => !CaseFields.Contains(n)).ToList();
        if (unknown.Count > 0)
            throw new CaseException($"{name}: unknown fields: {string.Join(", ", unknown.Order())}");
        foreach (var key in new[] { "description", "section", "level", "expect" })
            if (!root.TryGetProperty(key, out _))
                throw new CaseException($"{name}: missing field '{key}'");

        var inputs = InputFields.Where(f => root.TryGetProperty(f, out _)).ToList();
        if (inputs.Count != 1)
            throw new CaseException($"{name}: exactly one of {string.Join(", ", InputFields)} is required");
        var inputElement = root.GetProperty(inputs[0]);
        byte[] input = inputs[0] switch
        {
            // The raw text keeps the document exactly as written in the case file.
            "input" => Encoding.UTF8.GetBytes(inputElement.GetRawText()),
            "input_text" => Encoding.UTF8.GetBytes(inputElement.GetString()!),
            _ => Convert.FromBase64String(inputElement.GetString()!),
        };

        var expect = root.GetProperty("expect");
        if (expect.ValueKind != JsonValueKind.Object || expect.EnumerateObject().Any(p => !ExpectFields.Contains(p.Name)))
            throw new CaseException($"{name}: expect must be an object with {string.Join(", ", ExpectFields.Order())}");
        var tree = expect.TryGetProperty("tree", out var t) ? JsonNode.Parse(t.GetRawText(), documentOptions: DocOptions) : null;
        var error = expect.TryGetProperty("error", out var e1) && e1.ValueKind == JsonValueKind.True;
        var acceptError = expect.TryGetProperty("accept_error", out var e2) && e2.ValueKind == JsonValueKind.True;
        if ((tree is null) == !error || (error && acceptError))
            throw new CaseException($"{name}: expect needs either 'tree' or 'error': true");
        if (tree is not null and not JsonObject)
            throw new CaseException($"{name}: expect.tree must be an object");

        JsonObject? setup = null;
        if (root.TryGetProperty("setup", out var s))
            setup = JsonNode.Parse(s.GetRawText(), documentOptions: DocOptions) as JsonObject
                ?? throw new CaseException($"{name}: setup must be an object");

        return new Case(
            name,
            root.GetProperty("description").GetString()!,
            root.GetProperty("section").ToString(),
            root.GetProperty("level").GetString()!,
            setup,
            input,
            (JsonObject?)tree,
            error,
            acceptError);
    }
}
