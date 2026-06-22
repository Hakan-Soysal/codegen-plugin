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

    // ── B3 — deployable → modular-monolith host topolojisi ────────────────
    [Fact]
    public void Deployable_emits_modular_monolith_host_topology()
    {
        var (report, dir, _) = EmitMut(m => m);   // fixture already has BillingService(units=[Billing])
        try
        {
            var host = File.ReadAllText(Path.Combine(dir, "src", "Host.g.cs"));
            Assert.Contains("public static class DeploymentTopology", host);
            Assert.Contains("[\"BillingService\"] = [\"Billing\"],", host);
            Assert.True(report.Covers("deployable", "BillingService"));
            NoDrop(report, "deployable", "BillingService");
        }
        finally { Directory.Delete(dir, true); }
    }

    static ExtJson Ext(string ns, string name) => new(ns, name, new Dictionary<string, System.Text.Json.JsonElement>());

    // ── C1 — ext on every annotation-site ─────────────────────────────────
    [Fact]
    public void Ext_realized_on_all_annotation_sites()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            var mod = m.Modules[0] with { Ext = new() { Ext("audit", "trace") } };
            var money = m.Types.First(t => t.Id == "Money");
            var moneyExt = money with
            {
                Ext = new() { Ext("schema", "versioned") },
                Fields = money.Fields!.Select(f => f.Name == "amount" ? f with { Ext = new() { Ext("metric", "gauge") } } : f).ToList()
            };
            var inv = m.Entities[0] with { Ext = new() { Ext("audit", "table") } };
            var create = Op(m, "CreateInvoice");
            var createExt = create with
            {
                Signature = create.Signature with
                {
                    Params = create.Signature.Params.Select(p => p.Name == "customerId" ? p with { Ext = new() { Ext("sensitivity", "pii") } } : p).ToList()
                }
            };
            return WithOp(m with { Modules = new() { mod }, Types = m.Types.Select(t => t.Id == "Money" ? moneyExt : t).ToList(), Entities = new() { inv } }, createExt);
        });
        try
        {
            Assert.True(report.Covers("@audit.trace", "Billing"));          // module
            Assert.True(report.Covers("@schema.versioned", "Money"));       // type
            Assert.True(report.Covers("@metric.gauge", "Money.amount"));    // type-field
            Assert.True(report.Covers("@audit.table", "Invoice"));          // entity
            Assert.True(report.Covers("@sensitivity.pii", "CreateInvoice.customerId")); // op-param
            Assert.Empty(report.SilentDrops.Where(d => d.Construct.StartsWith("@")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── C4 — for guard / guardRef → predicate comment (resolved Q1) ────────
    [Fact]
    public void GuardRef_emitted_as_predicate_comment()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            var create = Op(m, "CreateInvoice");
            var val = create.Validation.Select((g, i) => i == 0 ? g with { GuardRef = "credit-policy" } : g).ToList();
            return WithOp(m, create with { Validation = val });
        });
        try
        {
            var f = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.Guards.g.cs"));
            Assert.Contains("// for guard: \"credit-policy\"", f);
            Assert.True(report.Covers("guardRef", "CreateInvoice"));
            NoDrop(report, "guardRef", "CreateInvoice");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── C3 — sourceOfTruth → cross-module FK ──────────────────────────────
    [Fact]
    public void SourceOfTruth_emits_cross_module_fk_comment_no_navigation()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            var inv = m.Entities[0];
            var fields = inv.Fields.Select(f => f.Name == "customerId"
                ? f with { SourceOfTruth = new SourceOfTruth("Customers", "Customer") } : f).ToList();
            return m with { Entities = new() { inv with { Fields = fields } } };
        });
        try
        {
            var f = File.ReadAllText(Path.Combine(dir, "src", "Billing", "Entities.g.cs"));
            Assert.Contains("sourceOfTruth: Customers.Customer", f);
            Assert.DoesNotContain("public Customer Customer", f);   // navigasyon AÇILMAZ
            Assert.True(report.Covers("sourceOfTruth", "Invoice.customerId"));
            NoDrop(report, "sourceOfTruth", "Invoice.customerId");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── C2 — uncharted typed model + call-adapter ─────────────────────────
    [Fact]
    public void Uncharted_emits_call_adapter_and_owned_model()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            var entity = new UnchartedEntity("Account", new(),
                new() { new EntityFieldJson("id", "String", false, "one", "", null, null, null) }, null);
            var type = new UnchartedType("Ledger", "composite",
                new() { new FieldJson("balance", "Decimal", false) }, null);
            var op = new BoundaryOpJson("Post",
                new SignatureJson(new() { new ParamJson("amount", "Decimal", false) }, "Decimal"));
            var u = new UnchartedJson("PaymentLedger", false, "BillingService",
                new() { op }, new() { entity }, new() { type });
            return m with { Uncharted = new() { u } };
        });
        try
        {
            var f = File.ReadAllText(Path.Combine(dir, "src", "Uncharted", "PaymentLedger.g.cs"));
            Assert.Contains("public interface IPaymentLedger", f);
            Assert.Contains("public class Account", f);                        // owned entity
            Assert.Contains("public sealed record Ledger(decimal Balance);", f); // owned type
            Assert.Contains("NotImplementedException(\"PaymentLedger.Post\")", f); // call-adapter stub
            Assert.True(report.Covers("uncharted", "PaymentLedger"));
            NoDrop(report, "uncharted", "PaymentLedger");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── B4 — note → doc-comment ───────────────────────────────────────────
    [Fact]
    public void Note_emits_xml_doc_comment_on_handler()
    {
        var (report, dir, _) = EmitMut(m => m);   // fixture CreateInvoice has a note
        try
        {
            var f = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.g.cs"));
            Assert.Contains("/// <summary>Müşteri kredi limiti dış servisten gelir.</summary>", f);
            Assert.True(report.Covers("note", "CreateInvoice"));
            NoDrop(report, "note", "CreateInvoice");
        }
        finally { Directory.Delete(dir, true); }
    }
}
