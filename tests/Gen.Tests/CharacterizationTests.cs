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

    // Üretilen APP ağacı snapshot'lanır; üreteç metadata'sı (provenance/build-report) değil.
    static readonly HashSet<string> Metadata = new(StringComparer.Ordinal)
        { "provenance.json", "provenance.json.tmp", "build-report.json" };

    /// <summary>Emit ağacını "relpath\tsha256" satırları olarak, ordinal sıralı döndürür.</summary>
    static string Snapshot(string root)
    {
        var lines = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (rel: Path.GetRelativePath(root, f).Replace('\\', '/'), full: f))
            .Where(x => !Metadata.Contains(Path.GetFileName(x.rel)))
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

    [Fact]
    public void Unchanged_input_does_not_rewrite_generated_file()   // write-only-if-changed
    {
        var dir = Path.Combine(Path.GetTempPath(), "gen-char-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var gm = GmBuilder.Build(
                Json.Parse<ManifestJson>(Fixtures.Read("manifest.json")),
                Json.Parse<ContractFile>(Fixtures.Read("operations.json")));
            DotnetEmitter.Emit(gm, dir, new BuildReport());

            // mtime'ı bilinen geçmişe çek; içerik aynıysa ikinci emit DOKUNMAMALI.
            var f = Path.Combine(dir, "gen", "Result.g.cs");
            var past = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(f, past);

            DotnetEmitter.Emit(gm, dir, new BuildReport());

            Assert.Equal(past, File.GetLastWriteTimeUtc(f));   // yeniden yazılmadı → mtime korundu
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Removed_operation_prunes_generated_file_but_keeps_human_logic()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gen-char-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var gm = GmBuilder.Build(
                Json.Parse<ManifestJson>(Fixtures.Read("manifest.json")),
                Json.Parse<ContractFile>(Fixtures.Read("operations.json")));
            DotnetEmitter.Emit(gm, dir, new BuildReport());

            var genFile = Path.Combine(dir, "gen", "Billing", "GetInvoice.g.cs");
            var humanLogic = Path.Combine(dir, "src", "Billing", "GetInvoiceHandler.Logic.cs");
            Assert.True(File.Exists(genFile), "ön koşul: .g.cs üretildi");
            Assert.True(File.Exists(humanLogic), "ön koşul: Logic.cs üretildi");
            File.WriteAllText(humanLogic, "// HUMAN BODY\n");   // insan emeği

            // GetInvoice manifest'ten çıkarıldı → yeniden üret
            var trimmed = gm with { Operations = gm.Operations.Where(o => o.Id != "GetInvoice").ToList() };
            DotnetEmitter.Emit(trimmed, dir, new BuildReport());

            Assert.False(File.Exists(genFile), "orphan .g.cs prune edilmeli (manifest-diff)");
            Assert.True(File.Exists(humanLogic), "human Logic.cs korunmalı — orphan, asla auto-silinmez");
            Assert.Equal("// HUMAN BODY\n", File.ReadAllText(humanLogic));
        }
        finally { Directory.Delete(dir, true); }
    }
}
