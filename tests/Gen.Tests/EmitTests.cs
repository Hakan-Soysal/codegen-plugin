using System.Text.Json;
using System.Diagnostics;
using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Dotnet;

namespace Gen.Tests;

public class EmitTests
{
    static GenerationModelHolder Gm() => new(GmBuilder.Build(
        Json.Parse<ManifestJson>(Fixtures.Read("manifest.json")),
        Json.Parse<ContractFile>(Fixtures.Read("operations.json"))));

    sealed record GenerationModelHolder(Gen.Core.Gm.GenerationModel Value);

    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "gen-emit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static (int code, string output) Dotnet(string args, string cwd)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = cwd
        };
        var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o);
    }

    [Fact]
    public void Emitted_app_compiles_and_gate_has_no_silent_drops()
    {
        var dir = TempDir();
        try
        {
            var report = new BuildReport();
            var m = Json.Parse<ManifestJson>(Fixtures.Read("manifest.json"));
            DotnetEmitter.Emit(Gm().Value, dir, report);
            Completeness.Check(m, report);   // full gate over the full-keyword fixture

            Assert.True(File.Exists(Path.Combine(dir, "App.csproj")));
            // Gate sözleşmesi = 0 SilentDrop (INV-7). Unsupported (grpc/queue serving) açık rapordur, drop değil.
            Assert.True(report.SilentDrops.Count == 0,
                "SilentDrop var:\n" + string.Join("\n", report.SilentDrops.Select(d => $"{d.Construct}/{d.Id}")));

            var (code, output) = Dotnet("build App.csproj -v q --nologo", dir);
            Assert.True(code == 0, "Üretilen app derlenmedi:\n" + output);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Operation_file_has_request_record_and_partial_handler()
    {
        var dir = TempDir();
        try
        {
            DotnetEmitter.Emit(Gm().Value, dir, new BuildReport());
            var f = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.g.cs"));
            Assert.Contains("public sealed record CreateInvoiceCommand(string CustomerId, decimal Amount);", f);
            Assert.Contains("public partial Task<Result<Invoice>> ExecuteAsync(CreateInvoiceCommand request, CancellationToken ct);", f);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Entity_emits_ef_class_with_rowversion_and_dbcontext()
    {
        var dir = TempDir();
        try
        {
            DotnetEmitter.Emit(Gm().Value, dir, new BuildReport());
            var ent = File.ReadAllText(Path.Combine(dir, "src", "Billing", "Entities.g.cs"));
            Assert.Contains("public class Invoice", ent);
            Assert.Contains("[Timestamp] public byte[] RowVersion", ent);   // concurrency optimistic
            var ctx = File.ReadAllText(Path.Combine(dir, "src", "AppDbContext.g.cs"));
            Assert.Contains("public DbSet<App.Billing.Invoice> Invoices", ctx);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Event_record_bus_and_auth_metadata_emitted()
    {
        var dir = TempDir();
        try
        {
            DotnetEmitter.Emit(Gm().Value, dir, new BuildReport());
            var ev = File.ReadAllText(Path.Combine(dir, "src", "Billing", "Events.g.cs"));
            Assert.Contains("public sealed record InvoiceCreated(string InvoiceId, Money Amount);", ev);
            Assert.True(File.Exists(Path.Combine(dir, "src", "EventBus.g.cs")));
            var auth = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.Auth.g.cs"));
            Assert.Contains("RequiredRoles = [\"Clerk\"]", auth);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Paginated_query_returns_page_and_takes_cursor_size()
    {
        var dir = TempDir();
        try
        {
            DotnetEmitter.Emit(Gm().Value, dir, new BuildReport());
            var f = File.ReadAllText(Path.Combine(dir, "src", "Billing", "ListInvoices.g.cs"));
            Assert.Contains("Page<Invoice>", f);
            Assert.Contains("string? Cursor, int Size", f);
            Assert.Contains("ORDER BY CreatedAt desc", f);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Boundary_idempotent_ext_and_policies_emitted()
    {
        var dir = TempDir();
        try
        {
            var report = new BuildReport();
            DotnetEmitter.Emit(Gm().Value, dir, report);

            var boundary = File.ReadAllText(Path.Combine(dir, "src", "Boundary.g.cs"));
            Assert.Contains("public interface IPaymentGateway", boundary);
            Assert.Contains("compensate: PaymentGateway.refund", boundary);            // saga skeleton
            Assert.True(File.Exists(Path.Combine(dir, "src", "Idempotency.g.cs")));
            Assert.Contains("IdempotencyKeys = [\"customerId\"]", File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.Idem.g.cs")));
            Assert.Contains("@crypto.encrypted", File.ReadAllText(Path.Combine(dir, "src", "Billing", "Entities.g.cs")));

            // §8 politikaları açıkça çözülmüş (INV-3) — build-report'ta kayıtlı
            using var rep = JsonDocument.Parse(report.ToJson());
            var pol = rep.RootElement.GetProperty("policies");
            foreach (var k in new[] { "dedup-store", "saga-orchestration-state", "crypto-realization", "cursor-token" })
                Assert.True(pol.TryGetProperty(k, out _), $"policy eksik: {k}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Error_catalog_and_throws_binding_emitted()
    {
        var dir = TempDir();
        try
        {
            var report = new BuildReport();
            DotnetEmitter.Emit(Gm().Value, dir, report);

            // adlı-hata kataloğu: kod sabiti (agnostik ad; resultType yorumlu)
            var cat = File.ReadAllText(Path.Combine(dir, "src", "Billing", "Errors.g.cs"));
            Assert.Contains("public const string DuplicateInvoice = \"DuplicateInvoice\";", cat);

            // throws → tipli Result fabrikası (NotProcessable<T>.Code bağlı)
            var thr = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.Throws.g.cs"));
            Assert.Contains("ThrowableErrors = [Errors.DuplicateInvoice]", thr);
            Assert.Contains("new NotProcessable<Invoice>(Errors.DuplicateInvoice, message)", thr);

            // census kayıtları realized
            Assert.True(report.Covers("error", "DuplicateInvoice"));
            Assert.True(report.Covers("throws", "CreateInvoice->DuplicateInvoice"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Logic_file_is_preserved_on_regeneration()
    {
        var dir = TempDir();
        try
        {
            var gm = Gm().Value;
            DotnetEmitter.Emit(gm, dir, new BuildReport());
            var logic = Path.Combine(dir, "src", "Billing", "CreateInvoiceHandler.Logic.cs");
            File.WriteAllText(logic, "// HUMAN BODY\n");

            DotnetEmitter.Emit(gm, dir, new BuildReport());   // regen
            Assert.Equal("// HUMAN BODY\n", File.ReadAllText(logic));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Generated_g_files_are_byte_deterministic()
    {
        var (a, b) = (TempDir(), TempDir());
        try
        {
            var gm = Gm().Value;
            DotnetEmitter.Emit(gm, a, new BuildReport());
            DotnetEmitter.Emit(gm, b, new BuildReport());
            var fa = File.ReadAllText(Path.Combine(a, "src", "Billing", "CreateInvoice.g.cs"));
            var fb = File.ReadAllText(Path.Combine(b, "src", "Billing", "CreateInvoice.g.cs"));
            Assert.Equal(fa, fb);
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }
}
