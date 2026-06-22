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
        => Build(root, null);

    /// <summary>
    /// .NET tip-duyarlı varyant: <paramref name="resolveType"/> bir path'i NÖTR manifest tipine ("Decimal"/"Int"/…)
    /// çözer. Decimal bağlamındaki sayısal literal'ler C# `decimal` ('m') suffix'i ile basılır → `decimal vs double`
    /// CS0019 önlenir (INV-4: "asla bozuk kod"). resolveType null ise davranış eski (Go + dil-nötr) ile aynıdır.
    /// </summary>
    public static (string Expr, IReadOnlyList<IReadOnlyList<string>> Paths) Build(ExprNode root, Func<IReadOnlyList<string>, string?>? resolveType)
    {
        var paths = new List<IReadOnlyList<string>>();
        var seen = new HashSet<string>();

        // bottom-up nötr sayısal tip (yalnız resolveType verildiğinde): Decimal baskındır.
        string? TypeOf(ExprNode n) => n switch
        {
            PathNode p => resolveType?.Invoke(p.Path),
            LiteralNode { Value: double d } => d == Math.Floor(d) ? "Int" : "Double",
            BinaryNode b when b.NodeKind is "add" or "sub" or "mul" or "div" =>
                (TypeOf(b.Left) == "Decimal" || TypeOf(b.Right) == "Decimal") ? "Decimal"
                : (TypeOf(b.Left) == "Double" || TypeOf(b.Right) == "Double") ? "Double" : "Int",
            _ => null
        };

        string Walk(ExprNode n, bool decimalCtx) => n switch
        {
            BinaryNode b => WalkBinary(b),
            PathNode p => PathRef(p),
            LiteralNode l => Literal(l, decimalCtx),
            _ => throw new UnsupportedConstruct($"ExprNode '{n.GetType().Name}' tipli predicate'e indirilemiyor")
        };

        // cmp/aritmetik operandlarından biri Decimal ise alt-ifadeler decimal bağlamında basılır.
        string WalkBinary(BinaryNode b)
        {
            var childDecimal = resolveType is not null && b.NodeKind is not "and" and not "or"
                && (TypeOf(b.Left) == "Decimal" || TypeOf(b.Right) == "Decimal");
            return $"({Walk(b.Left, childDecimal)} {Op(b)} {Walk(b.Right, childDecimal)})";
        }

        string PathRef(PathNode p)
        {
            var prop = PropName(p.Path);
            if (seen.Add(prop)) paths.Add(p.Path);
            return "input." + prop;
        }

        return (Walk(root, false), paths);
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

    static string Literal(LiteralNode l, bool decimalCtx = false) => l.Value switch
    {
        string s => $"\"{s}\"",
        bool bo => bo ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture) + (decimalCtx ? "m" : ""),
        _ => l.Value.ToString()!
    };
}
