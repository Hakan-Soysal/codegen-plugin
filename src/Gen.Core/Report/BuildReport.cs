using System.Text.Encodings.Web;
using System.Text.Json;

namespace Gen.Core.Report;

public enum ConstructStatus { Realized, Unsupported, EmitConflict, SilentDrop }

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
    /// <summary>Completeness gate: manifest'te VAR ama ne emit ne rapor edilmiş construct (INV-7 ihlali).</summary>
    public void SilentDrop(string construct, string id) => _entries.Add(new(construct, id, ConstructStatus.SilentDrop, "manifest'te var; ne emit ne rapor (INV-7)"));
    public void Policy(string name, string decision) => _policies[name] = decision;

    /// <summary>Bir construct örneği zaten kayıtlı mı (realized/unsupported) — gate matching için.</summary>
    public bool Covers(string construct, string owner) =>
        _entries.Any(e => string.Equals(e.Construct, construct, StringComparison.OrdinalIgnoreCase)
                          && e.Status != ConstructStatus.SilentDrop && IdMatches(e.Id, owner));

    /// <summary>Census owner ↔ rapor Id eşleşmesi: tam eşit VEYA owner + sınır-ayracı önek
    /// (compound Id'ler: "{op}#Validation0", "{op}->{err}", "{op}:{proto}", " [inferred-seam]").
    /// Substring değil — "Get" census'u "GetInvoice" raporunu YANLIŞ örtmesin (INV-7 soundness).</summary>
    static bool IdMatches(string id, string owner) =>
        id == owner || (id.StartsWith(owner, StringComparison.Ordinal)
                        && id.Length > owner.Length && id[owner.Length] is '#' or '-' or ':' or ' ');

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

    /// <summary>Gate sonrası sessiz-düşen construct'lar (boşsa INV-7 temiz).</summary>
    public IReadOnlyList<BuildEntry> SilentDrops => _entries.Where(e => e.Status == ConstructStatus.SilentDrop).ToList();

    static string Tag(ConstructStatus s) => s switch
    {
        ConstructStatus.Realized => "realized",
        ConstructStatus.Unsupported => "unsupported",
        ConstructStatus.SilentDrop => "silentDrop",
        _ => "emitConflict"
    };
}
