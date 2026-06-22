using Gen.Core;
using Gen.Core.Model;
using Gen.Dotnet;

namespace Gen.Tests;

public class ExprEmitTests
{
    static ExprNode P(string json) => Json.Parse<ExprNode>(json);

    [Fact]
    public void Cmp_path_vs_number()
        => Assert.Equal("(request.amount > 0)", ExprEmit.Emit(P("""{"node":"cmp","op":">","left":{"path":["amount"]},"right":{"kind":"number","value":0}}""")));

    [Fact]
    public void Resource_and_actor_roots()
    {
        Assert.Equal("(request.amount <= resource.creditLimit)",
            ExprEmit.Emit(P("""{"node":"cmp","op":"<=","left":{"path":["amount"]},"right":{"path":["resource","creditLimit"]}}""")));
        Assert.Equal("(actor.id == request.ownerId)",
            ExprEmit.Emit(P("""{"node":"cmp","op":"=","left":{"path":["actor","id"]},"right":{"path":["ownerId"]}}""")));
    }

    [Fact]
    public void And_or_nesting_and_eq_mapping()
        => Assert.Equal("((request.a == 1) && (request.b || request.c))",
            ExprEmit.Emit(P("""{"node":"and","left":{"node":"cmp","op":"=","left":{"path":["a"]},"right":{"kind":"number","value":1}},"right":{"node":"or","left":{"path":["b"]},"right":{"path":["c"]}}}""")));

    [Fact]
    public void Invariant_uses_entity_root()
        => Assert.Equal("(entity.amount >= 0)",
            ExprEmit.Emit(P("""{"node":"cmp","op":">=","left":{"path":["amount"]},"right":{"kind":"number","value":0}}"""), "entity"));

    [Fact]
    public void Unsupported_nodes_throw()
    {
        Assert.Throws<UnsupportedConstruct>(() => ExprEmit.Emit(P("""{"node":"agg","fn":"sum","path":["lines","total"]}""")));
        Assert.Throws<UnsupportedConstruct>(() => ExprEmit.Emit(P("""{"node":"call","name":"now","args":[]}""")));
        Assert.Throws<UnsupportedConstruct>(() => ExprEmit.Emit(P("""{"kind":"duration","value":30,"unit":"day","text":"30 days"}""")));
    }
}
