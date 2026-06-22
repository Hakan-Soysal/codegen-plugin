using System.Text.Json;
using Gen.Core.Model;

namespace Gen.Core.Pipeline;

/// <summary>Aşama 1 (Load): manifest + opsiyonel operations.json'u tipli modele ayrıştırır.</summary>
public static class Loader
{
    public static ManifestJson LoadManifest(string path)
    {
        if (!File.Exists(path)) throw new LoadError($"manifest bulunamadı: {path}");
        try { return Json.Parse<ManifestJson>(File.ReadAllText(path)); }
        catch (JsonException e) { throw new LoadError($"manifest ayrıştırılamadı: {e.Message}"); }
    }

    /// <summary>contract path manifest'e göreli çözülür. Dosya yok/bozuksa null (linked'de join
    /// aşaması JoinError üretir — TS loadContract emsali).</summary>
    public static ContractFile? LoadContract(string manifestPath, string? contractPath)
    {
        if (contractPath is null) return null;
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath) ?? ".", contractPath));
        if (!File.Exists(resolved)) return null;
        try { return Json.Parse<ContractFile>(File.ReadAllText(resolved)); }
        catch (JsonException) { return null; }
    }
}
