using System.Text.Encodings.Web;
using System.Text.Json;

namespace Gen.Core.Report;

public enum ConstructStatus { Realized, Unsupported, EmitConflict }

public sealed record BuildEntry(string Construct, string Id, ConstructStatus Status, string? Reason);

/// <summary>
/// INV-7/INV-8 falsifiye-edilebilir kaydı (spec §12 build-manifest). Her construct'ın durumu +
/// çözülen §8 politikaları. Emitter'lar buraya append eder; deterministik JSON yazılır.
/// </summary>
public sealed class BuildReport
{
    readonly List<BuildEntry> _entries = new();
    readonly SortedDictionary<string, string> _policies = new(StringComparer.Ordinal);

    public void Realized(string construct, string id) => _entries.Add(new(construct, id, ConstructStatus.Realized, null));
    public void Unsupported(string construct, string id, string reason) => _entries.Add(new(construct, id, ConstructStatus.Unsupported, reason));
    public void Conflict(string construct, string id, string reason) => _entries.Add(new(construct, id, ConstructStatus.EmitConflict, reason));
    public void Policy(string name, string decision) => _policies[name] = decision;

    public IReadOnlyList<BuildEntry> Entries => _entries;
    public bool Clean => _entries.All(e => e.Status == ConstructStatus.Realized);

    static readonly JsonSerializerOptions Pretty = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ToJson()
    {
        var constructs = _entries
            .OrderBy(e => e.Construct, StringComparer.Ordinal)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new { construct = e.Construct, id = e.Id, status = Tag(e.Status), reason = e.Reason });
        return JsonSerializer.Serialize(new { constructs, policies = _policies }, Pretty);
    }

    public void WriteTo(string path) => File.WriteAllText(path, ToJson() + "\n");

    static string Tag(ConstructStatus s) => s switch
    {
        ConstructStatus.Realized => "realized",
        ConstructStatus.Unsupported => "unsupported",
        _ => "emitConflict"
    };
}
