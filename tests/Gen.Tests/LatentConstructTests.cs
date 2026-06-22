using Gen.Core;
using Gen.Core.Gm;
using Gen.Core.Model;
using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Dotnet;

namespace Gen.Tests;

/// <summary>
/// Latent construct'lar (mevcut fixture'ı tetiklemeyen grammar keyword'leri) için hedefli testler.
/// Fixture manifest'i in-memory mutasyonla (record `with`) genişletir, emitter desteğini + census
/// kapsamasını (SilentDrop yok) doğrular. Tam entegrasyon = Phase E full-keyword fixture + compile gate.
/// </summary>
public class LatentConstructTests
{
    static ManifestJson M() => Json.Parse<ManifestJson>(Fixtures.Read("manifest.json"));
    static ContractFile C() => Json.Parse<ContractFile>(Fixtures.Read("operations.json"));

    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "gen-latent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static (BuildReport report, string dir, ManifestJson m) EmitMut(Func<ManifestJson, ManifestJson> mutate)
    {
        var m = mutate(M());
        var gm = GmBuilder.Build(m, C());
        var dir = TempDir();
        var report = new BuildReport();
        DotnetEmitter.Emit(gm, dir, report);
        Completeness.Check(m, report);
        return (report, dir, m);
    }

    static OperationJson Op(ManifestJson m, string id) => m.Operations.First(o => o.Id == id);
    static ManifestJson WithOp(ManifestJson m, OperationJson op) =>
        m with { Operations = m.Operations.Select(o => o.Id == op.Id ? op : o).ToList() };

    static void NoDrop(BuildReport r, string construct, string owner) =>
        Assert.DoesNotContain(r.SilentDrops, d => d.Construct == construct && d.Id == owner);

    // ── B2 — consistency {risk, mode} ─────────────────────────────────────
    [Fact]
    public void Consistency_eventual_emits_outbox_skeleton_and_policy()
    {
        var (report, dir, _) = EmitMut(m =>
            WithOp(m, Op(m, "CreateInvoice") with { Consistency = new Consistency("eventual", "durable") }));
        try
        {
            var f = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.Consistency.g.cs"));
            Assert.Contains("outbox", f);
            Assert.Contains("ConsistencyRisk = \"eventual\"", f);
            Assert.Contains("ConsistencyMode = \"durable\"", f);
            Assert.True(report.Covers("consistency", "CreateInvoice"));
            NoDrop(report, "consistency", "CreateInvoice");
        }
        finally { Directory.Delete(dir, true); }
    }
}
