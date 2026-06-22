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
        if (gm.Entities.Count > 0) WriteAlways(Path.Combine(src, "AppDbContext.g.cs"), DbContextFile(gm));
        if (gm.Events.Count > 0) WriteAlways(Path.Combine(src, "EventBus.g.cs"), EventBus());
        foreach (var ev in gm.Events) report.Realized("event", ev.Id);
        foreach (var s in gm.Subscriptions) report.Realized("subscription", $"{s.Event.Name}->{s.Consumer.Op}");

        foreach (var module in gm.Modules)
        {
            var dir = Path.Combine(src, module.Name);
            Directory.CreateDirectory(dir);

            var types = gm.Types.Where(t => t.Module == module.Name).ToList();
            var entities = gm.Entities.Where(e => e.Module == module.Name).ToList();
            var events = gm.Events.Where(e => e.Module == module.Name).ToList();
            if (events.Count > 0) WriteAlways(Path.Combine(dir, "Events.g.cs"), EventsFile(module.Name, events));
            if (types.Count > 0) WriteAlways(Path.Combine(dir, "Types.g.cs"), TypesFile(module.Name, types));
            if (entities.Count > 0) WriteAlways(Path.Combine(dir, "Entities.g.cs"), EntitiesFile(module.Name, entities));
            foreach (var e in entities)
            {
                var inv = InvariantsFile(module.Name, e, gm, report);
                if (inv is not null) WriteAlways(Path.Combine(dir, $"{e.Id}.Invariants.g.cs"), inv);
            }

            foreach (var op in gm.Operations.Where(o => o.Module == module.Name))
            {
                WriteAlways(Path.Combine(dir, $"{op.Id}.g.cs"), OperationFile(module.Name, op));
                WriteIfAbsent(Path.Combine(dir, $"{op.Id}Handler.Logic.cs"), LogicFile(module.Name, op));
                var guards = GuardsFile(module.Name, op, gm, report);
                if (guards is not null) WriteAlways(Path.Combine(dir, $"{op.Id}.Guards.g.cs"), guards);
                var auth = AuthFile(module.Name, op, report);
                if (auth is not null) WriteAlways(Path.Combine(dir, $"{op.Id}.Auth.g.cs"), auth);
                foreach (var ev in op.Op.Emits) report.Realized("emits", $"{op.Id}->{ev}");
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
          <ItemGroup>
            <!-- ponytail: pin; gerekince bump -->
            <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.9" />
          </ItemGroup>
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

    // entity → EF entity class. concurrency optimistic → [Timestamp] RowVersion.
    static string EntitiesFile(string module, List<EntityJson> entities)
    {
        var sb = new StringBuilder();
        sb.Append("using System.ComponentModel.DataAnnotations;\n\n");
        sb.Append($"namespace {Root}.{module};\n\n");
        foreach (var e in entities)
        {
            sb.Append($"public class {e.Id}\n{{\n");
            foreach (var f in e.Fields)
                sb.Append($"    public {Naming.Type(f.Type, f.Collection)} {Naming.Pascal(f.Name)} {{ get; set; }} = default!;\n");
            if (e.Concurrency == "optimistic")
                sb.Append("    [Timestamp] public byte[] RowVersion { get; set; } = default!;\n");
            sb.Append("}\n\n");
        }
        return sb.ToString();
    }

    // tek AppDbContext + entity başına DbSet. Provider Program.cs'te seam.
    static string DbContextFile(GenerationModel gm)
    {
        var sets = new StringBuilder();
        foreach (var e in gm.Entities)
            sets.Append($"    public DbSet<{Root}.{e.Module}.{e.Id}> {e.Id}s => Set<{Root}.{e.Module}.{e.Id}>();\n");
        return
            $$"""
            using Microsoft.EntityFrameworkCore;

            namespace {{Root}};

            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
            {
            {{sets}}}

            """;
    }

    // ── guard/invariant predicate'leri (INV-4, tipli — dynamic YOK) ──────
    // Her predicate: tip-çözümlü `input` record + tipli predicate. Veri-besleme tipli seam.
    static string? GuardsFile(string module, GmOperation op, GenerationModel gm, BuildReport report)
    {
        var methods = new List<string>();
        var records = new List<string>();
        void Add(string kind, int i, Gen.Core.Model.ExprNode ast, string text)
        {
            var (method, rec) = Predicate(kind, i, ast, text, op.Id, entityId: null, gm, report);
            methods.Add(method);
            if (rec is not null) records.Add(rec);
        }
        for (var i = 0; i < op.Op.Validation.Count; i++) Add("Validation", i, op.Op.Validation[i].Ast, op.Op.Validation[i].Text);
        for (var i = 0; i < op.Op.Rule.Count; i++) Add("Rule", i, op.Op.Rule[i].Ast, op.Op.Rule[i].Text);
        if (op.Op.Abac is not null) Add("Permit", 0, op.Op.Abac.Permit, op.Op.Abac.Permit.ToString() ?? "permit");
        if (methods.Count == 0) return null;

        return
            $$"""
            namespace {{Root}}.{{module}};

            // validation→NotValid(400) · rule→NotProcessable(422) · permit→authz. Tipli predicate (dynamic yok);
            // `input` record manifest tiplerinden — çözülemeyen alan tipli seam (inferred), insan input'u doldurur.
            public partial class {{op.Id}}Handler
            {
            {{string.Join("\n", methods)}}
            }

            {{string.Join("\n", records)}}
            """;
    }

    static (string Method, string? Record) Predicate(
        string kind, int i, Gen.Core.Model.ExprNode ast, string text,
        string? opId, string? entityId, GenerationModel gm, BuildReport report)
    {
        var owner = opId ?? entityId!;
        var key = $"{owner}#{kind}{i}";
        try
        {
            var (expr, paths) = Gen.Core.Predicate.ExprBuild.Build(ast);
            var inputName = $"{owner}{kind}{i}Input";
            var anyInferred = false;
            var fields = new List<string>();
            foreach (var p in paths)
            {
                var (cs, resolved) = ResolveType(p, gm, opId, entityId);
                if (!resolved) anyInferred = true;
                fields.Add($"{cs} {Gen.Core.Predicate.ExprBuild.PropName(p)}");
            }
            report.Realized(kind.ToLowerInvariant(), key + (anyInferred ? " [inferred-seam]" : ""));
            var record = $"public sealed record {inputName}({string.Join(", ", fields)});";
            return ($"    static bool {kind}_{i}({inputName} input) => {expr};", record);
        }
        catch (Gen.Core.UnsupportedConstruct e)
        {
            report.Unsupported(kind.ToLowerInvariant(), key, e.Message);
            return ($"    static bool {kind}_{i}() => throw new NotImplementedException(\"unsupported: {Escape(text)}\");", null);
        }
    }

    // path → C# tip + çözüldü mü. Nötr çözümü GM yapar; burada dile map'lenir. Çözülemeyen→decimal (tipli seam).
    static (string Cs, bool Resolved) ResolveType(IReadOnlyList<string> path, GenerationModel gm, string? opId, string? entityId)
    {
        var mt = gm.Env.ResolvePath(gm, path, opId, entityId);
        return mt is null ? ("decimal", false) : (Naming.Type(mt, false), true);
    }

    static string? InvariantsFile(string module, EntityJson e, GenerationModel gm, BuildReport report)
    {
        if (e.Invariants.Count == 0) return null;
        var methods = new List<string>();
        var records = new List<string>();
        for (var i = 0; i < e.Invariants.Count; i++)
        {
            var (method, rec) = Predicate("Check", i, e.Invariants[i].Ast, e.Invariants[i].Text, opId: null, entityId: e.Id, gm, report);
            methods.Add(method.Replace("static bool", "public static bool"));
            if (rec is not null) records.Add(rec);
        }
        return
            $$"""
            namespace {{Root}}.{{module}};

            // entity invariant'ları (kalıcı veri-bütünlüğü). Tipli predicate; `input` entity alanlarından.
            public static class {{e.Id}}Invariants
            {
            {{string.Join("\n", methods)}}
            }

            {{string.Join("\n", records)}}
            """;
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── event / emits / on (Task 11) ─────────────────────────────────────
    static string EventsFile(string module, List<EventJson> events)
    {
        var sb = new StringBuilder();
        sb.Append($"namespace {Root}.{module};\n\n");
        foreach (var e in events)
        {
            var props = string.Join(", ", e.Payload.Select(f => $"{Naming.Type(f.Type, f.Collection)} {Naming.Pascal(f.Name)}"));
            sb.Append($"public sealed record {e.Id}({props});\n\n");
        }
        return sb.ToString();
    }

    static string EventBus() =>
        $$"""
        namespace {{Root}};

        // cross-module yayın seam'i. ponytail: outbox/broker/ack/retry insan+altyapı (§8 event taşıma).
        public interface IEventBus { Task PublishAsync(object @event, CancellationToken ct = default); }

        public sealed class OutboxEventBus : IEventBus
        {
            public Task PublishAsync(object @event, CancellationToken ct = default)
                => throw new NotImplementedException("outbox: event taşıma doldurulacak");
        }

        """;

    // ── auth iskeleti (Task 12) — roles/ownership/scopes ──────────────────
    static string? AuthFile(string module, GmOperation op, BuildReport report)
    {
        var o = op.Op;
        if (o.Roles.Count == 0 && o.Scopes.Count == 0 && o.Ownership is null) return null;

        if (o.Roles.Count > 0) report.Realized("roles", op.Id);
        if (o.Scopes.Count > 0) report.Realized("scopes", op.Id);
        if (o.Ownership is not null) report.Realized("ownership", op.Id);

        var roles = string.Join(", ", o.Roles.Select(r => $"\"{Escape(r)}\""));
        var scopes = string.Join(", ", o.Scopes.Select(s => $"\"{Escape(s)}\""));
        var ownership = o.Ownership is null ? "null" : $"\"{Escape(o.Ownership)}\"";
        return
            $$"""
            namespace {{Root}}.{{module}};

            // authz iskeleti (roles+scopes AND, ownership row-level). ponytail: meta + seam;
            // gerçek claim/row-level kontrol insan/runtime.
            public partial class {{op.Id}}Handler
            {
                public static readonly string[] RequiredRoles = [{{roles}}];
                public static readonly string[] RequiredScopes = [{{scopes}}];
                public const string? Ownership = {{ownership}};
            }

            """;
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
        usings.Append("using Microsoft.EntityFrameworkCore;\n");
        foreach (var m in gm.Modules) usings.Append($"using {Root}.{m.Name};\n");

        var di = new StringBuilder();
        if (gm.Entities.Count > 0)
            di.Append("builder.Services.AddDbContext<AppDbContext>(o => { /* ponytail: provider seam — o.UseNpgsql(...) vb. */ });\n");
        if (gm.Events.Count > 0)
            di.Append("builder.Services.AddScoped<IEventBus, OutboxEventBus>();\n");
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
