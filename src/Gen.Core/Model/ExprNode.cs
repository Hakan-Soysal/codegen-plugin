using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gen.Core.Model;

/// <summary>
/// Canonical Expr AST (src/shared/expr-ast.ts karşılığı). Discriminated union:
/// `node` → binary/agg/call; `path` → PathNode; `kind` → literal/duration.
/// </summary>
public abstract record ExprNode;

/// <summary>node ∈ and|or|cmp|add|sub|mul|div. Op yalnız cmp/arith'te dolu.</summary>
public sealed record BinaryNode(string NodeKind, string? Op, ExprNode Left, ExprNode Right) : ExprNode;
public sealed record AggNode(string Fn, IReadOnlyList<string> Path) : ExprNode;
public sealed record CallNode(string Name, IReadOnlyList<ExprNode> Args) : ExprNode;
public sealed record PathNode(IReadOnlyList<string> Path) : ExprNode;
/// <summary>kind ∈ string|number|boolean. Value = string|double|bool.</summary>
public sealed record LiteralNode(string LitKind, object Value) : ExprNode;
public sealed record DurationNode(double Value, string Unit, string Text) : ExprNode;

public sealed class ExprNodeConverter : JsonConverter<ExprNode>
{
    public override ExprNode Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return FromElement(doc.RootElement);
    }

    public static ExprNode FromElement(JsonElement el)
    {
        if (el.TryGetProperty("node", out var nEl))
        {
            var node = nEl.GetString()!;
            switch (node)
            {
                case "agg":
                    return new AggNode(el.GetProperty("fn").GetString()!, ReadStrings(el.GetProperty("path")));
                case "call":
                    var args = new List<ExprNode>();
                    foreach (var a in el.GetProperty("args").EnumerateArray()) args.Add(FromElement(a));
                    return new CallNode(el.GetProperty("name").GetString()!, args);
                default:
                    var op = el.TryGetProperty("op", out var oEl) ? oEl.GetString() : null;
                    return new BinaryNode(node, op, FromElement(el.GetProperty("left")), FromElement(el.GetProperty("right")));
            }
        }
        if (el.TryGetProperty("path", out var pEl))
            return new PathNode(ReadStrings(pEl));
        if (el.TryGetProperty("kind", out var kEl))
        {
            var kind = kEl.GetString()!;
            if (kind == "duration")
                return new DurationNode(el.GetProperty("value").GetDouble(), el.GetProperty("unit").GetString()!, el.GetProperty("text").GetString()!);
            var v = el.GetProperty("value");
            object val = kind switch
            {
                "string" => v.GetString()!,
                "number" => v.GetDouble(),
                "boolean" => v.GetBoolean(),
                _ => throw new JsonException($"unknown literal kind: {kind}")
            };
            return new LiteralNode(kind, val);
        }
        throw new JsonException("unknown ExprNode shape");
    }

    static List<string> ReadStrings(JsonElement arr)
    {
        var list = new List<string>();
        foreach (var e in arr.EnumerateArray()) list.Add(e.GetString()!);
        return list;
    }

    public override void Write(Utf8JsonWriter writer, ExprNode value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case BinaryNode b:
                writer.WriteString("node", b.NodeKind);
                if (b.Op is not null) writer.WriteString("op", b.Op);
                writer.WritePropertyName("left"); Write(writer, b.Left, options);
                writer.WritePropertyName("right"); Write(writer, b.Right, options);
                break;
            case AggNode a:
                writer.WriteString("node", "agg");
                writer.WriteString("fn", a.Fn);
                writer.WritePropertyName("path"); WriteStrings(writer, a.Path);
                break;
            case CallNode c:
                writer.WriteString("node", "call");
                writer.WriteString("name", c.Name);
                writer.WritePropertyName("args");
                writer.WriteStartArray();
                foreach (var arg in c.Args) Write(writer, arg, options);
                writer.WriteEndArray();
                break;
            case PathNode p:
                writer.WritePropertyName("path"); WriteStrings(writer, p.Path);
                break;
            case LiteralNode l:
                writer.WriteString("kind", l.LitKind);
                writer.WritePropertyName("value");
                switch (l.Value)
                {
                    case string s: writer.WriteStringValue(s); break;
                    case bool bo: writer.WriteBooleanValue(bo); break;
                    case double d: writer.WriteNumberValue(d); break;
                    default: JsonSerializer.Serialize(writer, l.Value, options); break;
                }
                break;
            case DurationNode dn:
                writer.WriteString("kind", "duration");
                writer.WriteNumber("value", dn.Value);
                writer.WriteString("unit", dn.Unit);
                writer.WriteString("text", dn.Text);
                break;
        }
        writer.WriteEndObject();
    }

    static void WriteStrings(Utf8JsonWriter writer, IReadOnlyList<string> items)
    {
        writer.WriteStartArray();
        foreach (var s in items) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}
