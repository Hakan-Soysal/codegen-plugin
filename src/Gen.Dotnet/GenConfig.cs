using Gen.Core;

namespace Gen.Dotnet;

/// <summary>
/// Hedef-app .NET üreteç parametreleri (manifest yanındaki `gen.config.json`). Determinizm
/// girdisi: (manifest + gen.config) → byte-aynı. Dosya yoksa null → mevcut davranış (provider yok).
/// DbProvider .NET-özel; paylaşılan manifest'e girmez (dile-özel varyant yasak).
/// TestDbProvider = üretilen xUnit Fixture harness'ının EF provider'ı (proje-bazında elicit edilir;
/// null → InMemory = harness doğrulama varsayılanı). Production DbProvider'dan AYRI (test izolasyonu).
/// </summary>
public record GenConfig(string? DbProvider, string? TestDbProvider = null)
{
    public static GenConfig? Load(string path) =>
        File.Exists(path) ? Json.Parse<GenConfig>(File.ReadAllText(path)) : null;
}
