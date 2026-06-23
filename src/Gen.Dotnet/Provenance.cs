using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gen.Dotnet;

/// <summary>Üretilen dosyanın sahiplik sınıfı. Yalnız <c>Generated</c> prune edilir.</summary>
public enum FileClass { Generated, HumanSeam, HumanShell }

public sealed record ProvenanceEntry(string Path, string Class, string Sha256);

/// <summary>Üreteç provenance manifesti: hangi dosyaları ürettiğini sınıf+hash ile kaydeder.
/// Orphan prune (manifest-diff) ve drift tespiti bunun üzerinden çalışır.</summary>
public sealed record Provenance(string Generator, string Version, IReadOnlyList<ProvenanceEntry> Files);

public static class ProvenanceIo
{
    public const string FileName = "provenance.json";

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string PathFor(string outDir) => System.IO.Path.Combine(outDir, FileName);

    /// <summary>Var olan manifesti oku. Yoksa VEYA parse edilemezse null → prune atlanır
    /// (parse edemediğin manifestten asla silme hesaplama; bozuk dosya canlı dosya silmesin).</summary>
    public static Provenance? TryRead(string outDir)
    {
        var p = PathFor(outDir);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<Provenance>(File.ReadAllText(p), Opts); }
        catch { return null; }
    }

    /// <summary>Atomik yaz: temp'e yaz + taşı. Yarım manifest sonraki run'da yanlış silme yapmasın.</summary>
    public static void Write(string outDir, Provenance prov)
    {
        var p = PathFor(outDir);
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(prov, Opts) + "\n");
        File.Move(tmp, p, overwrite: true);
    }

    public static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
