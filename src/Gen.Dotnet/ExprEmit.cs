using System.Globalization;
using Gen.Core;
using Gen.Core.Model;

namespace Gen.Dotnet;

/// <summary>
/// ExprNode → C# ifade dizisi (INV-4). Predicate gövdesi dynamic ctx'te değerlendirilir
/// (request/resource/actor): yapısal şekil emit edilir, tip-bağlama insan seam'i.
/// `actor.*` opak claim; `resource.*` write-hedefi entity. Desteklenmeyen düğüm → UnsupportedConstruct.
/// </summary>
public static class ExprEmit
{
    public static string Emit(ExprNode n, string defaultRoot = "request") => n switch
    {
        BinaryNode b => Binary(b, defaultRoot),
        PathNode p => Path(p, defaultRoot),
        LiteralNode l => Literal(l),
        _ => throw new UnsupportedConstruct($"ExprNode '{n.GetType().Name}' C# predicate'ine indirilemiyor")
    };

    static string Binary(BinaryNode b, string root)
    {
        var op = b.NodeKind switch
        {
            "and" => "&&",
            "or" => "||",
            _ => MapOp(b.Op ?? throw new UnsupportedConstruct($"'{b.NodeKind}' op'suz"))
        };
        return $"({Emit(b.Left, root)} {op} {Emit(b.Right, root)})";
    }

    static string MapOp(string op) => op == "=" ? "==" : op;

    static string Path(PathNode p, string defaultRoot)
    {
        var first = p.Path[0];
        var isRooted = first is "actor" or "resource";
        var root = isRooted ? first : defaultRoot;
        var segs = isRooted ? p.Path.Skip(1) : p.Path;
        return root + string.Concat(segs.Select(s => "." + s));
    }

    static string Literal(LiteralNode l) => l.Value switch
    {
        string s => $"\"{s}\"",
        bool bo => bo ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => l.Value.ToString()!
    };
}
