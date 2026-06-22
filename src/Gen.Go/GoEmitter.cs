using System.Text;
using Gen.Core;
using Gen.Core.Gm;
using Gen.Core.Model;
using Gen.Core.Predicate;
using Gen.Core.Report;

namespace Gen.Go;

/// <summary>
/// SEAM SPIKE (Phase 5): aynı GM'den derlenen Go. Amaç kod değil KEŞİF — GM'deki .NET-ism'leri
/// açığa çıkarmak. Tek paket `app`; result → (T, error)+Failure; stub'lar ayrı `_logic.go` (yoksa-üret).
/// </summary>
public static class GoEmitter
{
    public static void Emit(GenerationModel gm, string outDir, BuildReport report)
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "go.mod"), "module app\n\ngo 1.23\n");
        File.WriteAllText(Path.Combine(outDir, "result.g.go"), Result());
        File.WriteAllText(Path.Combine(outDir, "model.g.go"), Model(gm));

        foreach (var op in gm.Operations)
        {
            var lower = op.Id.ToLowerInvariant();
            File.WriteAllText(Path.Combine(outDir, $"{lower}.g.go"), OperationFile(op));
            WriteIfAbsent(Path.Combine(outDir, $"{lower}_logic.go"), LogicFile(op));

            var guards = GuardsFile(op, gm, report);
            if (guards is not null) File.WriteAllText(Path.Combine(outDir, $"{lower}_guards.g.go"), guards);
        }
    }

    static void WriteIfAbsent(string path, string content) { if (!File.Exists(path)) File.WriteAllText(path, content); }

    static string Result() =>
        """
        package app

        // result-type 6'lı taksonomi → Go idiom: (T, error) + tipli Failure.
        // FINDING: .NET'in Result<T> sum-type'ı yerine Go (T,error) — adapter eşlemesi, GM nötr kaldı (sorunsuz port).
        type FailureKind int

        const (
        	NotAuthenticated FailureKind = iota
        	NotAuthorized
        	NotValid
        	NotProcessable
        	ServerError
        )

        type Failure struct {
        	Kind     FailureKind
        	Message  string
        	Errors   map[string]string
        }

        func (f *Failure) Error() string { return f.Message }

        """;

    static string Model(GenerationModel gm)
    {
        var usesTime =
            gm.Entities.Any(e => e.Fields.Any(f => GoNaming.UsesTime(f.Type))) ||
            gm.Types.Any(t => (t.Fields ?? new()).Any(f => GoNaming.UsesTime(f.Type))) ||
            gm.Events.Any(e => e.Payload.Any(f => GoNaming.UsesTime(f.Type)));

        var sb = new StringBuilder("package app\n\n");
        if (usesTime) sb.Append("import \"time\"\n\n");

        foreach (var t in gm.Types)
        {
            if (t.Kind == "enum")
            {
                sb.Append($"type {t.Id} string\n\nconst (\n");
                foreach (var v in t.Values ?? new()) sb.Append($"\t{t.Id}{v} {t.Id} = \"{v}\"\n");
                sb.Append(")\n\n");
            }
            else
            {
                sb.Append($"type {t.Id} struct {{\n");
                foreach (var f in t.Fields ?? new()) sb.Append($"\t{GoNaming.Pascal(f.Name)} {GoNaming.Type(f.Type, f.Collection)}\n");
                sb.Append("}\n\n");
            }
        }
        foreach (var e in gm.Entities)
        {
            sb.Append($"type {e.Id} struct {{\n");
            foreach (var f in e.Fields) sb.Append($"\t{GoNaming.Pascal(f.Name)} {GoNaming.Type(f.Type, f.Collection)}\n");
            if (e.Concurrency == "optimistic") sb.Append("\tRowVersion []byte\n");
            sb.Append("}\n\n");
        }
        foreach (var ev in gm.Events)
        {
            sb.Append($"type {ev.Id} struct {{\n");
            foreach (var f in ev.Payload) sb.Append($"\t{GoNaming.Pascal(f.Name)} {GoNaming.Type(f.Type, f.Collection)}\n");
            sb.Append("}\n\n");
        }
        return sb.ToString();
    }

    static string ReqName(GmOperation op) => op.IsCommand ? $"{op.Id}Command" : $"{op.Id}Query";

    static string OperationFile(GmOperation op)
    {
        var req = ReqName(op);
        var sb = new StringBuilder("package app\n\n");
        sb.Append($"type {req} struct {{\n");
        foreach (var p in op.Op.Signature.Params) sb.Append($"\t{GoNaming.Pascal(p.Name)} {GoNaming.Type(p.Type, p.Collection)}\n");
        sb.Append("}\n\n");
        sb.Append($"type {op.Id}Handler struct{{}}\n");
        return sb.ToString();
    }

    static string LogicFile(GmOperation op)
    {
        var req = ReqName(op);
        var ret = GoNaming.Type(op.Op.Signature.Returns, false);
        return
            $$"""
            package app

            import (
            	"context"
            	"errors"
            )

            // İş gövdesi seam (yoksa-üret; üreteç ezmez).
            func (h *{{op.Id}}Handler) Execute(ctx context.Context, req {{req}}) ({{ret}}, error) {
            	return {{ret}}{}, errors.New("{{op.Id}}: not implemented")
            }

            """;
    }

    // GM tip-env sonrası: tipli Go predicate (dynamic yok, .NET ile aynı dil-nötr ifade çekirdeği).
    static string? GuardsFile(GmOperation op, GenerationModel gm, BuildReport report)
    {
        var funcs = new List<string>();
        var structs = new List<string>();
        var fn = char.ToLowerInvariant(op.Id[0]) + op.Id[1..];

        void Add(string kind, int i, ExprNode ast, string text)
        {
            var key = $"{op.Id}#{kind}{i}";
            try
            {
                var (expr, paths) = ExprBuild.Build(ast);
                var inputName = $"{op.Id}{kind}{i}Input";
                var inferred = false;
                var fields = new List<string>();
                foreach (var p in paths)
                {
                    var mt = gm.Env.ResolvePath(gm, p, op.Id, null);
                    if (mt is null) inferred = true;
                    fields.Add($"\t{ExprBuild.PropName(p)} {(mt is null ? "float64" : GoNaming.Type(mt, false))}");
                }
                report.Realized(kind.ToLowerInvariant(), key + (inferred ? " [inferred-seam]" : ""));
                structs.Add($"type {inputName} struct {{\n{string.Join("\n", fields)}\n}}");
                funcs.Add($"func {fn}{kind}{i}(input {inputName}) bool {{\n\treturn {expr}\n}}");
            }
            catch (UnsupportedConstruct e)
            {
                report.Unsupported(kind.ToLowerInvariant(), key, e.Message);
                funcs.Add($"// unsupported: {text}\nfunc {fn}{kind}{i}() bool {{ panic(\"unsupported\") }}");
            }
        }

        for (var i = 0; i < op.Op.Validation.Count; i++) Add("Validation", i, op.Op.Validation[i].Ast, op.Op.Validation[i].Text);
        for (var i = 0; i < op.Op.Rule.Count; i++) Add("Rule", i, op.Op.Rule[i].Ast, op.Op.Rule[i].Text);
        if (funcs.Count == 0) return null;
        return "package app\n\n" + string.Join("\n\n", structs.Concat(funcs)) + "\n";
    }
}
