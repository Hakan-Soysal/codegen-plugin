using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Report;
using Gen.Dotnet;

namespace Gen.Tests;

public class CompletenessTests
{
    static ManifestJson Manifest() => Json.Parse<ManifestJson>(Fixtures.Read("manifest.json"));

    static (BuildReport report, string outDir) Emit()
    {
        var m = Manifest();
        var c = Json.Parse<ContractFile>(Fixtures.Read("operations.json"));
        var gm = Gen.Core.Pipeline.GmBuilder.Build(m, c);
        var dir = Path.Combine(Path.GetTempPath(), "gen-comp-" + Guid.NewGuid().ToString("N"));
        var report = new BuildReport();
        DotnetEmitter.Emit(gm, dir, report);
        Completeness.Check(m, report);   // gate
        return (report, dir);
    }

    // Ratchet: bilinen-borç allowlist'i. Her fix bu seti küçültür; YENİ drop (allowlist dışı) testi kırar.
    // Set boşalınca = gate tam yeşil (Phase E hedefi).
    static readonly HashSet<string> KnownDebt = new();

    [Fact]
    public void No_silent_drops_beyond_known_debt()
    {
        var (report, dir) = Emit();
        try
        {
            var actual = report.SilentDrops.Select(d => $"{d.Construct}/{d.Id}").ToHashSet();
            var unexpected = actual.Except(KnownDebt).ToList();
            Assert.True(unexpected.Count == 0, "YENİ SESSİZ DROP (allowlist dışı):\n" + string.Join("\n", unexpected));
        }
        finally { Directory.Delete(dir, true); }
    }
}
