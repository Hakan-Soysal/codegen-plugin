using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Predicate;

namespace Gen.Tests;

public class ExprBuildTests
{
    static ExprNode P(string json) => Json.Parse<ExprNode>(json);
    static string Expr(string json) => ExprBuild.Build(P(json)).Expr;

    [Fact]
    public void Cmp_path_vs_number_uses_typed_input()
        => Assert.Equal("(input.Amount > 0)", Expr("""{"node":"cmp","op":">","left":{"path":["amount"]},"right":{"kind":"number","value":0}}"""));

    [Fact]
    public void Resource_path_becomes_collision_safe_input_field()
    {
        var (expr, paths) = ExprBuild.Build(P("""{"node":"cmp","op":"<=","left":{"path":["amount"]},"right":{"path":["resource","creditLimit"]}}"""));
        Assert.Equal("(input.Amount <= input.ResourceCreditLimit)", expr);
        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public void And_or_eq_mapping()
        => Assert.Equal("((input.A == 1) && (input.B || input.C))",
            Expr("""{"node":"and","left":{"node":"cmp","op":"=","left":{"path":["a"]},"right":{"kind":"number","value":1}},"right":{"node":"or","left":{"path":["b"]},"right":{"path":["c"]}}}"""));

    [Fact]
    public void Distinct_paths_collected_once()
    {
        var (_, paths) = ExprBuild.Build(P("""{"node":"and","left":{"node":"cmp","op":">","left":{"path":["x"]},"right":{"kind":"number","value":0}},"right":{"node":"cmp","op":"<","left":{"path":["x"]},"right":{"kind":"number","value":9}}}"""));
        Assert.Single(paths);   // x bir kez
    }

    [Fact]
    public void Unsupported_nodes_throw()
    {
        Assert.Throws<UnsupportedConstruct>(() => ExprBuild.Build(P("""{"node":"agg","fn":"sum","path":["lines","total"]}""")));
        Assert.Throws<UnsupportedConstruct>(() => ExprBuild.Build(P("""{"node":"call","name":"now","args":[]}""")));
        Assert.Throws<UnsupportedConstruct>(() => ExprBuild.Build(P("""{"kind":"duration","value":30,"unit":"day","text":"30 days"}""")));
    }

    [Fact]
    public void PropName_is_pascal_join()
        => Assert.Equal("ResourceCreditLimit", ExprBuild.PropName(new[] { "resource", "creditLimit" }));
}
