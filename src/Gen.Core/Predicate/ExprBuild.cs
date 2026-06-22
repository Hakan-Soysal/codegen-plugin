using System.Globalization;
using Gen.Core.Model;

namespace Gen.Core.Predicate;

/// <summary>
/// ExprNode → DİL-NÖTR predicate ifadesi (INV-4). Operatörler (&amp;&amp; || == &gt; &lt;= + - …) C#/Go/Java/JS'te
/// aynı; path'ler `input.Prop`'a çözülür. Sadece tip-eşlemesi + record/struct sözdizimi dile özel.
/// Bu, "predicate'in şekli yapısal, tipli kod (dynamic yok)" seam'inin 4-dil-ortak çekirdeğidir.
/// </summary>
public static class ExprBuild
{
    /// <summary>İfade + referans verilen path'ler (sıralı, distinct). Emitter path → tipli alan map'ler.</summary>
    public static (string Expr, IReadOnlyList<IReadOnlyList<string>> Paths) Build(ExprNode root)
    {
        var paths = new List<IReadOnlyList<string>>();
        var seen = new HashSet<string>();

        string Walk(ExprNode n) => n switch
        {
            BinaryNode b => $"({Walk(b.Left)} {Op(b)} {Walk(b.Right)})",
            PathNode p => PathRef(p),
            LiteralNode l => Literal(l),
            _ => throw new UnsupportedConstruct($"ExprNode '{n.GetType().Name}' tipli predicate'e indirilemiyor")
        };

        string PathRef(PathNode p)
        {
            var prop = PropName(p.Path);
            if (seen.Add(prop)) paths.Add(p.Path);
            return "input." + prop;
        }

        return (Walk(root), paths);
    }

    /// <summary>Path → input alan adı (collision-safe Pascal-join): ['resource','creditLimit'] → ResourceCreditLimit.</summary>
    public static string PropName(IReadOnlyList<string> path) => string.Concat(path.Select(Pascal));

    static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string Op(BinaryNode b) => b.NodeKind switch
    {
        "and" => "&&",
        "or" => "||",
        _ => MapOp(b.Op ?? throw new UnsupportedConstruct($"'{b.NodeKind}' op'suz"))
    };

    static string MapOp(string op) => op == "=" ? "==" : op;

    static string Literal(LiteralNode l) => l.Value switch
    {
        string s => $"\"{s}\"",
        bool bo => bo ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => l.Value.ToString()!
    };
}
