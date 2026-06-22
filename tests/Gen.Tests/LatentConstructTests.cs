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
    static System.Text.Json.JsonElement JStr(string s) => System.Text.Json.JsonDocument.Parse($"\"{s}\"").RootElement.Clone();
    static ExtJson ExtArgs(string ns, string name, params (string k, string v)[] args) =>
        new(ns, name, args.ToDictionary(a => a.k, a => JStr(a.v)));

    // ── D4 — @http/@trigger → target stubs (resolved Q2) ──────────────────
    [Fact]
    public void Http_and_trigger_emit_target_stubs()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            var create = Op(m, "CreateInvoice");
            var ext = new List<ExtJson> { Ext("trigger", "cron"), ExtArgs("http", "endpoint", ("route", "/invoices"), ("method", "POST")) };
            return WithOp(m, create with { Ext = ext });
        });
        try
        {
            var trig = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.Trigger.g.cs"));
            Assert.Contains("public sealed class CreateInvoiceCronTrigger", trig);
            Assert.Contains(": IHostedService", trig);
            var extf = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.Ext.g.cs"));
            Assert.Contains("public const string HttpRoute = \"/invoices\";", extf);
            Assert.Contains("public const string HttpMethod = \"POST\";", extf);
            Assert.True(report.Covers("@trigger.cron", "CreateInvoice"));
            Assert.True(report.Covers("@http.endpoint", "CreateInvoice"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── C1 — ext on every annotation-site ─────────────────────────────────
    [Fact]
    public void Ext_realized_on_all_annotation_sites()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            // additive: mevcut tüm construct'ları koru, sadece seçili site'lara ext EKLE.
            var modules = m.Modules.Select(x => x.Name == "Billing" ? x with { Ext = new() { Ext("audit", "trace") } } : x).ToList();
            var types = m.Types.Select(t => t.Id == "Money"
                ? t with { Ext = new() { Ext("schema", "versioned") }, Fields = t.Fields!.Select(f => f.Name == "amount" ? f with { Ext = new() { Ext("metric", "gauge") } } : f).ToList() }
                : t).ToList();
            var entities = m.Entities.Select(e => e.Id == "Invoice" ? e with { Ext = new() { Ext("audit", "table") } } : e).ToList();
            var create = Op(m, "CreateInvoice");
            var createExt = create with
            {
                Signature = create.Signature with
                {
                    Params = create.Signature.Params.Select(p => p.Name == "customerId" ? p with { Ext = new() { Ext("sensitivity", "pii") } } : p).ToList()
                }
            };
            return WithOp(m with { Modules = modules, Types = types, Entities = entities }, createExt);
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

    // ── D3 — on/subscriptions → consumer wiring ───────────────────────────
    [Fact]
    public void Subscription_emits_consumer_handler_skeleton()
    {
        var (report, dir, _) = EmitMut(m => m with
        {
            Subscriptions = new() { new SubscriptionJson(new EventRef("Billing", "InvoiceCreated"), new ConsumerRef("Billing", "GetInvoice")) }
        });
        try
        {
            var f = File.ReadAllText(Path.Combine(dir, "src", "Subscriptions.g.cs"));
            Assert.Contains("public sealed class InvoiceCreatedToGetInvoiceConsumer", f);
            Assert.Contains("App.Billing.GetInvoiceHandler handler", f);
            Assert.Contains("App.Billing.InvoiceCreated @event", f);
            Assert.True(report.Covers("subscription", "InvoiceCreated"));
            NoDrop(report, "subscription", "InvoiceCreated");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── visibility (@internal) → public route gate ────────────────────────
    [Fact]
    public void Internal_visibility_suppresses_public_route()
    {
        var (report, dir, _) = EmitMut(m =>
            WithOp(m, Op(m, "GetInvoice") with { Visibility = "internal" }));   // serving korunur, route DÜŞMELİ
        try
        {
            var prog = File.ReadAllText(Path.Combine(dir, "Program.cs"));
            Assert.DoesNotContain("\"/invoices/{id}\"", prog);   // internal GetInvoice → route YOK
            Assert.Contains("\"/invoices\"", prog);              // exposed CreateInvoice/ListInvoices → route VAR
            Assert.True(report.Covers("visibility", "GetInvoice"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── D2 — external BoundaryOp serving + validation (INV-4) ─────────────
    [Fact]
    public void Boundary_op_serving_and_caller_validation_emitted()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            var guard = Op(m, "CreateInvoice").Validation[0];   // `amount > 0` (gerçek Ast); charge param'ı = amount
            var ext = m.Externals[0];
            var ops = ext.Operations.Select(b => b.Id == "charge"
                ? b with { Serving = new() { new ServingJson("rest", new(), "rest") }, Validation = new() { guard } }
                : b).ToList();
            return m with { Externals = new() { ext with { Operations = ops } } };
        });
        try
        {
            var f = File.ReadAllText(Path.Combine(dir, "src", "Boundary.g.cs"));
            Assert.Contains("public static class PaymentGatewaychargeValidation", f);
            Assert.Contains("(input.Amount > 0)", f);
            Assert.True(report.Covers("validation", "PaymentGateway.charge"));
            Assert.True(report.Covers("serving", "PaymentGateway.charge:rest"));
            NoDrop(report, "validation", "PaymentGateway.charge");
            NoDrop(report, "serving", "PaymentGateway.charge:rest");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── D1 — serving @grpc/@queue → explicit UnsupportedConstruct ─────────
    [Fact]
    public void Serving_rest_realized_nonrest_unsupported_not_silent()
    {
        var (report, dir, _) = EmitMut(m =>
        {
            var create = Op(m, "CreateInvoice");
            var serving = create.Serving.Append(new ServingJson("grpc", new(), "grpc")).ToList();
            return WithOp(m, create with { Serving = serving });
        });
        try
        {
            Assert.True(report.Covers("serving", "CreateInvoice:rest"));   // realized
            // grpc covered as explicit Unsupported (NOT a silent drop)
            Assert.Contains(report.Entries, e => e.Construct == "serving" && e.Id == "CreateInvoice:grpc" && e.Status == ConstructStatus.Unsupported);
            NoDrop(report, "serving", "CreateInvoice:grpc");
            NoDrop(report, "serving", "CreateInvoice:rest");
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
