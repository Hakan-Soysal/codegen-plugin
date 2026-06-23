using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Dotnet;

namespace Gen.Tests;

/// <summary>
/// Refactor güvenlik ağı (Faz 0). Fixture'ın TÜM emit edilen ağacının (her dosya yolu +
/// içerik SHA-256) snapshot'ını commit'li golden ile karşılaştırır. Refactor generated
/// içeriği/yapısını SESSİZCE değiştirirse bu test kırılır.
/// Golden'ı kasıtlı değiştirdiğinde (Faz 2 yol değişimi, Faz 3 içerik değişimi) yeniden
/// üret: <c>UPDATE_GOLDEN=1 dotnet test</c>.
/// </summary>
public class CharacterizationTests
{
    static string GoldenPath([CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, "golden", "emit-snapshot.txt");

    static string Sha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>Emit ağacını "relpath\tsha256" satırları olarak, ordinal sıralı döndürür.</summary>
    static string Snapshot(string root)
    {
        var lines = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (rel: Path.GetRelativePath(root, f).Replace('\\', '/'), full: f))
            .OrderBy(x => x.rel, StringComparer.Ordinal)
            .Select(x => $"{x.rel}\t{Sha256(x.full)}");
        return string.Join("\n", lines) + "\n";
    }

    [Fact]
    public void Emit_tree_matches_golden_snapshot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gen-char-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var gm = GmBuilder.Build(
                Json.Parse<ManifestJson>(Fixtures.Read("manifest.json")),
                Json.Parse<ContractFile>(Fixtures.Read("operations.json")));
            DotnetEmitter.Emit(gm, dir, new BuildReport());

            var actual = Snapshot(dir);
            var golden = GoldenPath();

            if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1" || !File.Exists(golden))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
                File.WriteAllText(golden, actual);
                Assert.True(File.Exists(golden), "golden yazıldı: " + golden);
                return;
            }

            var expected = File.ReadAllText(golden).Replace("\r\n", "\n");
            actual = actual.Replace("\r\n", "\n");
            if (actual != expected)
            {
                var exp = expected.Split('\n').ToHashSet();
                var act = actual.Split('\n').ToHashSet();
                var added = act.Except(exp);
                var removed = exp.Except(act);
                Assert.Fail("Emit ağacı golden'dan saptı (kasıtlıysa UPDATE_GOLDEN=1 ile yenile).\n"
                    + "YENİ/DEĞİŞEN:\n  " + string.Join("\n  ", added) + "\n"
                    + "KAYIP/ESKİ:\n  " + string.Join("\n  ", removed));
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
