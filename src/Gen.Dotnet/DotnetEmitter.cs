using System.Text;
using Gen.Core.Gm;
using Gen.Core.Model;
using Gen.Core.Report;

namespace Gen.Dotnet;

/// <summary>
/// GM → derlenen .NET uygulaması (Minimal API + düz CQRS + Result&lt;T&gt;). İş gövdeleri
/// ayrı `.Logic.cs` partial'da (yoksa-üret), `.g.cs` her zaman ezilir.
/// </summary>
public static class DotnetEmitter
{
    const string Root = "App";

    public static void Emit(GenerationModel gm, string outDir, BuildReport report)
    {
        Directory.CreateDirectory(outDir);
        var src = Path.Combine(outDir, "src");
        Directory.CreateDirectory(src);

        WriteAlways(Path.Combine(outDir, "App.csproj"), Csproj());
        WriteAlways(Path.Combine(src, "Result.g.cs"), ResultTypes());        // Task 6
        WriteAlways(Path.Combine(src, "ResultHttp.g.cs"), ResultHttp());     // result-type → wire

        foreach (var t in gm.Types) report.Realized(t.Kind, t.Id);
        foreach (var e in gm.Entities) report.Realized("entity", e.Id);

        foreach (var module in gm.Modules)
        {
            var dir = Path.Combine(src, module.Name);
            Directory.CreateDirectory(dir);

            var types = gm.Types.Where(t => t.Module == module.Name).ToList();
            var entities = gm.Entities.Where(e => e.Module == module.Name).ToList();
            if (types.Count > 0) WriteAlways(Path.Combine(dir, "Types.g.cs"), TypesFile(module.Name, types));
            if (entities.Count > 0) WriteAlways(Path.Combine(dir, "Entities.g.cs"), EntitiesFile(module.Name, entities));

            foreach (var op in gm.Operations.Where(o => o.Module == module.Name))
            {
                WriteAlways(Path.Combine(dir, $"{op.Id}.g.cs"), OperationFile(module.Name, op));
                WriteIfAbsent(Path.Combine(dir, $"{op.Id}Handler.Logic.cs"), LogicFile(module.Name, op));
                report.Realized("operation", op.Id);
            }
        }

        WriteAlways(Path.Combine(outDir, "Program.cs"), Program(gm));
    }

    // ── dosya yazımı (regeneration sözleşmesi) ──────────────────────────
    static void WriteAlways(string path, string content) => File.WriteAllText(path, content);
    static void WriteIfAbsent(string path, string content) { if (!File.Exists(path)) File.WriteAllText(path, content); }

    // ── şablonlar ───────────────────────────────────────────────────────
    static string Csproj() =>
        """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>

        """;

    static string ResultTypes() =>
        $$"""
        namespace {{Root}};

        // Kapalı 6'lı result taksonomisi (INV-5). NotAuthenticated AYRI kalır (401 vs 403).
        public abstract record Result<T>;
        public sealed record Success<T>(T Value) : Result<T>;
        public sealed record NotAuthenticated<T>(string Reason) : Result<T>;
        public sealed record NotAuthorized<T>(string Reason) : Result<T>;
        public sealed record NotValid<T>(IReadOnlyDictionary<string, string> Errors) : Result<T>;
        public sealed record NotProcessable<T>(string Code, string Message) : Result<T>;
        public sealed record ServerError<T>(string Message) : Result<T>;

        """;

    static string ResultHttp() =>
        $$"""
        using Microsoft.AspNetCore.Http;

        namespace {{Root}};

        // result-type → HTTP wire eşlemesi (pluggable protokol-binding; ponytail: tek REST switch).
        public static class ResultHttp
        {
            public static IResult ToHttp<T>(Result<T> r) => r switch
            {
                Success<T> s => Results.Ok(s.Value),
                NotAuthenticated<T> => Results.StatusCode(401),
                NotAuthorized<T> => Results.StatusCode(403),
                NotValid<T> v => Results.ValidationProblem(v.Errors.ToDictionary(e => e.Key, e => new[] { e.Value })),
                NotProcessable<T> p => Results.UnprocessableEntity(new { code = p.Code, message = p.Message }),
                ServerError<T> e => Results.Json(new { message = e.Message }, statusCode: 500),
                _ => Results.StatusCode(500)
            };
        }

        """;

    static string TypesFile(string module, List<TypeJson> types)
    {
        var sb = new StringBuilder();
        sb.Append($"namespace {Root}.{module};\n\n");
        foreach (var t in types)
        {
            if (t.Kind == "enum")
                sb.Append($"public enum {t.Id} {{ {string.Join(", ", t.Values ?? new())} }}\n\n");
            else
            {
                var props = string.Join(", ", (t.Fields ?? new()).Select(f => $"{Naming.Type(f.Type, f.Collection)} {Naming.Pascal(f.Name)}"));
                sb.Append($"public sealed record {t.Id}({props});\n\n");
            }
        }
        return sb.ToString();
    }

    // ponytail: entity şimdilik düz record; Task 9 EF entity'ye (RowVersion/ilişki) yükseltir.
    static string EntitiesFile(string module, List<EntityJson> entities)
    {
        var sb = new StringBuilder();
        sb.Append($"namespace {Root}.{module};\n\n");
        foreach (var e in entities)
        {
            var props = string.Join(", ", e.Fields.Select(f => $"{Naming.Type(f.Type, f.Collection)} {Naming.Pascal(f.Name)}"));
            sb.Append($"public sealed record {e.Id}({props});\n\n");
        }
        return sb.ToString();
    }

    static string RequestName(GmOperation op) => op.IsCommand ? $"{op.Id}Command" : $"{op.Id}Query";

    static string OperationFile(string module, GmOperation op)
    {
        var req = RequestName(op);
        var ret = Naming.Type(op.Op.Signature.Returns, false);
        var ps = op.Op.Signature.Params;
        var reqParams = string.Join(", ", ps.Select(p => $"{Naming.Type(p.Type, p.Collection)} {Naming.Pascal(p.Name)}"));
        return
            $$"""
            using {{Root}};

            namespace {{Root}}.{{module}};

            public sealed record {{req}}({{reqParams}});

            public partial class {{op.Id}}Handler
            {
                // İş gövdesi {{op.Id}}Handler.Logic.cs'te (yoksa-üret; üreteç ezmez).
                public partial Task<Result<{{ret}}>> ExecuteAsync({{req}} request, CancellationToken ct);
            }

            """;
    }

    static string LogicFile(string module, GmOperation op)
    {
        var req = RequestName(op);
        var ret = Naming.Type(op.Op.Signature.Returns, false);
        return
            $$"""
            using {{Root}};

            namespace {{Root}}.{{module}};

            public partial class {{op.Id}}Handler
            {
                public partial Task<Result<{{ret}}>> ExecuteAsync({{req}} request, CancellationToken ct)
                    => throw new NotImplementedException("{{op.Id}}: iş mantığı doldurulacak");
            }

            """;
    }

    static string Program(GenerationModel gm)
    {
        var usings = new StringBuilder($"using {Root};\n");
        foreach (var m in gm.Modules) usings.Append($"using {Root}.{m.Name};\n");

        var di = new StringBuilder();
        var maps = new StringBuilder();
        foreach (var op in gm.Operations)
        {
            di.Append($"builder.Services.AddScoped<{op.Id}Handler>();\n");
            foreach (var s in op.Op.Serving)
                maps.Append(MapLine(op, s));
        }

        return
            $$"""
            {{usings}}
            var builder = WebApplication.CreateBuilder(args);
            {{di}}var app = builder.Build();

            {{maps}}app.Run();
            """;
    }

    static string MapLine(GmOperation op, ServingJson s)
    {
        if (s.Protocol != "rest") return "";
        var method = s.Args.FirstOrDefault(a => a.Kind == "keyword")?.Value ?? "GET";
        var routeArg = s.Args.FirstOrDefault(a => a.Kind == "string");
        var route = routeArg?.Value ?? "/";
        var verb = Naming.HttpVerb(method);
        var req = RequestName(op);

        if (Naming.BindsBody(method))
            return $"app.{verb}(\"{route}\", async ({req} body, {op.Id}Handler handler, CancellationToken ct) => ResultHttp.ToHttp(await handler.ExecuteAsync(body, ct)));\n";

        // route param'larından request kur (ad eşleşmesi route token = signature param).
        var routeParams = routeArg?.Params ?? new();
        var lambdaParams = string.Join("", routeParams.Select(p => $"string {p}, "));
        var ctor = $"new {req}({string.Join(", ", routeParams)})";
        return $"app.{verb}(\"{route}\", async ({lambdaParams}{op.Id}Handler handler, CancellationToken ct) => ResultHttp.ToHttp(await handler.ExecuteAsync({ctor}, ct)));\n";
    }
}
