using System.Text.Json;
using Gen.Core;
using Gen.Core.Model;

namespace Gen.Tests;

public class ExprNodeTests
{
    static ExprNode Parse(string json) => Json.Parse<ExprNode>(json);

    [Fact]
    public void Cmp_with_path_and_number()
    {
        var n = Parse("""{"node":"cmp","op":">","left":{"path":["amount"]},"right":{"kind":"number","value":0}}""");
        var b = Assert.IsType<BinaryNode>(n);
        Assert.Equal("cmp", b.NodeKind);
        Assert.Equal(">", b.Op);
        Assert.Equal(new[] { "amount" }, Assert.IsType<PathNode>(b.Left).Path);
        Assert.Equal(0d, Assert.IsType<LiteralNode>(b.Right).Value);
    }

    [Fact]
    public void Path_node()
    {
        var p = Assert.IsType<PathNode>(Parse("""{"path":["resource","creditLimit"]}"""));
        Assert.Equal(new[] { "resource", "creditLimit" }, p.Path);
    }

    [Fact]
    public void Literals_string_bool()
    {
        Assert.Equal("x", Assert.IsType<LiteralNode>(Parse("""{"kind":"string","value":"x"}""")).Value);
        Assert.Equal(true, Assert.IsType<LiteralNode>(Parse("""{"kind":"boolean","value":true}""")).Value);
    }

    [Fact]
    public void Agg_call_duration()
    {
        var a = Assert.IsType<AggNode>(Parse("""{"node":"agg","fn":"sum","path":["lines","total"]}"""));
        Assert.Equal("sum", a.Fn);
        var c = Assert.IsType<CallNode>(Parse("""{"node":"call","name":"now","args":[]}"""));
        Assert.Equal("now", c.Name);
        var d = Assert.IsType<DurationNode>(Parse("""{"kind":"duration","value":30,"unit":"day","text":"30 days"}"""));
        Assert.Equal("day", d.Unit);
    }

    [Fact]
    public void Unknown_shape_throws()
    {
        Assert.ThrowsAny<JsonException>(() => Parse("""{"weird":1}"""));
    }

    [Fact]
    public void Round_trip_serialization_is_lossless()
    {
        // parse → write byte-aynı kaynağı verir (determinizm temeli; record value-eq koleksiyonları
        // deep karşılaştırmaz, o yüzden property = serialize-stabilitesi).
        const string src = """{"node":"and","left":{"node":"cmp","op":">","left":{"path":["amount"]},"right":{"kind":"number","value":0}},"right":{"kind":"boolean","value":true}}""";
        Assert.Equal(src, Json.Write(Parse(src)));
    }
}

public class ManifestPocoTests
{
    static ManifestJson M() => Json.Parse<ManifestJson>(Fixtures.Read("manifest.json"));

    [Fact]
    public void Deserializes_expected_counts()
    {
        var m = M();
        Assert.Equal("linked", m.Mode);
        Assert.Equal(3, m.Operations.Count);
        Assert.Single(m.Entities);
        Assert.Equal(2, m.Types.Count);
        Assert.Single(m.Events);
    }

    [Fact]
    public void Operation_validation_ast_is_typed()
    {
        var op = M().Operations.Single(o => o.Id == "CreateInvoice");
        var ast = op.Validation.Single().Ast;
        Assert.Equal("cmp", Assert.IsType<BinaryNode>(ast).NodeKind);
        Assert.Equal(new[] { "Invoice" }, op.Access.Creates);
        Assert.Contains("InvoiceCreated", op.Emits);
    }

    [Fact]
    public void Entity_invariant_and_concurrency()
    {
        var e = M().Entities.Single();
        Assert.Equal("optimistic", e.Concurrency);
        Assert.Equal(5, e.Fields.Count);
        Assert.IsType<BinaryNode>(e.Invariants.Single().Ast);
    }

    [Fact]
    public void Contract_file_parses()
    {
        var c = Json.Parse<ContractFile>(Fixtures.Read("operations.json"));
        Assert.Equal(2, c.Meta!.SchemaVersion);
        Assert.Equal("biz.CreateInvoice", c.Operations!.Single().Id);
    }
}
