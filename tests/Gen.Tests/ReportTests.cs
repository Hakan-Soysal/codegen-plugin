using System.Text.Json;
using Gen.Core.Report;

namespace Gen.Tests;

public class ReportTests
{
    [Fact]
    public void Records_status_and_is_clean_only_when_all_realized()
    {
        var r = new BuildReport();
        r.Realized("operation", "CreateInvoice");
        Assert.True(r.Clean);
        r.Unsupported("invariant", "Invoice", "no native receiver");
        Assert.False(r.Clean);
    }

    [Fact]
    public void Covers_matches_compound_ids_but_not_prefix_collisions()
    {
        var r = new BuildReport();
        r.Realized("validation", "CreateInvoice#Validation0 [inferred-seam]");
        r.Realized("throws", "CreateInvoice->DuplicateInvoice");
        r.Realized("roles", "GetInvoice");
        // sınır-ayraçlı compound Id'ler census owner'ını örter
        Assert.True(r.Covers("validation", "CreateInvoice"));
        Assert.True(r.Covers("throws", "CreateInvoice"));
        Assert.True(r.Covers("roles", "GetInvoice"));
        // ama prefix-çakışması ÖRTMEZ (substring soundness hatası)
        Assert.False(r.Covers("roles", "Get"));
        Assert.False(r.Covers("roles", "GetInvoiceX"));
    }

    [Fact]
    public void Json_is_deterministically_ordered()
    {
        var r = new BuildReport();
        r.Realized("operation", "ListInvoices");
        r.Realized("entity", "Invoice");
        r.Realized("operation", "CreateInvoice");
        using var doc = JsonDocument.Parse(r.ToJson());
        var ids = doc.RootElement.GetProperty("constructs").EnumerateArray()
            .Select(e => $"{e.GetProperty("construct").GetString()}/{e.GetProperty("id").GetString()}").ToArray();
        Assert.Equal(new[] { "entity/Invoice", "operation/CreateInvoice", "operation/ListInvoices" }, ids);
    }

    [Fact]
    public void Unsupported_carries_reason_and_policy_recorded()
    {
        var r = new BuildReport();
        r.Unsupported("compensate", "RefundPayment", "saga engine yok");
        r.Policy("dedup-store", "in-memory");
        using var doc = JsonDocument.Parse(r.ToJson());
        var c = doc.RootElement.GetProperty("constructs")[0];
        Assert.Equal("unsupported", c.GetProperty("status").GetString());
        Assert.Equal("saga engine yok", c.GetProperty("reason").GetString());
        Assert.Equal("in-memory", doc.RootElement.GetProperty("policies").GetProperty("dedup-store").GetString());
    }
}
