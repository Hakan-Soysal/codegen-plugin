using System.Text;
using System.Text.Json;
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

        if (gm.Deployables.Count > 0)
            WriteAlways(Path.Combine(src, "Host.g.cs"), HostFile(gm, report));            // B3 (modular-monolith host)
        if (gm.Externals.Count > 0 || gm.CallEdges.Count > 0)
            WriteAlways(Path.Combine(src, "Boundary.g.cs"), BoundaryFile(gm, report));   // Task 18+21
        if (gm.Uncharted.Count > 0)
        {
            Directory.CreateDirectory(Path.Combine(src, "Uncharted"));
            foreach (var u in gm.Uncharted) WriteAlways(Path.Combine(src, "Uncharted", $"{u.Name}.g.cs"), UnchartedFile(u, report));   // C2
        }
        if (gm.Operations.Any(o => o.Op.Idempotent is not null))
            WriteAlways(Path.Combine(src, "Idempotency.g.cs"), IdempotencyStore());       // Task 19

        foreach (var module in gm.Modules)
        {
            var dir = Path.Combine(src, module.Name);
            Directory.CreateDirectory(dir);

            var types = gm.Types.Where(t => t.Module == module.Name).ToList();
            var entities = gm.Entities.Where(e => e.Module == module.Name).ToList();
            var events = gm.Events.Where(e => e.Module == module.Name).ToList();
            var errors = gm.Errors.Where(e => e.Module == module.Name).ToList();
            if (errors.Count > 0) WriteAlways(Path.Combine(dir, "Errors.g.cs"), ErrorsFile(module.Name, errors, report));
            if (module.Ext is { Count: > 0 })
                WriteAlways(Path.Combine(dir, "Module.g.cs"),
                    $"namespace {Root}.{module.Name};\n\n// module-level cross-cutting prelude'lar (realizasyon = §8 policy).\n{ExtComment(module.Ext, module.Name, report)}");
            if (events.Count > 0) WriteAlways(Path.Combine(dir, "Events.g.cs"), EventsFile(module.Name, events));
            if (types.Count > 0) WriteAlways(Path.Combine(dir, "Types.g.cs"), TypesFile(module.Name, types, report));
            if (entities.Count > 0) WriteAlways(Path.Combine(dir, "Entities.g.cs"), EntitiesFile(module.Name, entities, report));
            foreach (var e in entities)
            {
                var inv = InvariantsFile(module.Name, e, gm, report);
                if (inv is not null) WriteAlways(Path.Combine(dir, $"{e.Id}.Invariants.g.cs"), inv);
            }

            foreach (var op in gm.Operations.Where(o => o.Module == module.Name))
            {
                WriteAlways(Path.Combine(dir, $"{op.Id}.g.cs"), OperationFile(module.Name, op, report));
                WriteIfAbsent(Path.Combine(dir, $"{op.Id}Handler.Logic.cs"), LogicFile(module.Name, op));
                var guards = GuardsFile(module.Name, op, gm, report);
                if (guards is not null) WriteAlways(Path.Combine(dir, $"{op.Id}.Guards.g.cs"), guards);
                var auth = AuthFile(module.Name, op, report);
                if (auth is not null) WriteAlways(Path.Combine(dir, $"{op.Id}.Auth.g.cs"), auth);
                foreach (var ev in op.Op.Emits) report.Realized("emits", $"{op.Id}->{ev}");
                if (op.Op.Pagination is not null) { report.Realized("pagination", op.Id); report.Policy("cursor-token", "opaque (generator-policy)"); }
                if (op.Op.Idempotent is not null)
                {
                    WriteAlways(Path.Combine(dir, $"{op.Id}.Idem.g.cs"), IdemPartial(module.Name, op));
                    report.Realized("idempotent", op.Id); report.Policy("dedup-store", "in-memory (generator-policy)");
                }
                var ext = ExtPartial(module.Name, op, report);
                if (ext is not null) WriteAlways(Path.Combine(dir, $"{op.Id}.Ext.g.cs"), ext);
                var throws = ThrowsPartial(module.Name, op, gm, report);
                if (throws is not null) WriteAlways(Path.Combine(dir, $"{op.Id}.Throws.g.cs"), throws);
                var consistency = ConsistencyPartial(module.Name, op, report);
                if (consistency is not null) WriteAlways(Path.Combine(dir, $"{op.Id}.Consistency.g.cs"), consistency);
                if (op.Op.Note is not null) report.Realized("note", op.Id);
                foreach (var s in op.Op.Serving)
                {
                    if (s.Protocol == "rest") report.Realized("serving", $"{op.Id}:{s.Protocol}");   // artefakt = Program.cs map (MapLine)
                    else
                    {
                        report.Unsupported("serving", $"{op.Id}:{s.Protocol}", $"protokol '{s.Protocol}' .NET adaptöründe binding yok (REST-only); açık UnsupportedConstruct.");
                        report.Policy($"serving-{s.Protocol}", "unsupported: REST-only binding (generator-policy)");
                    }
                }
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

        // pagination zarfı (cursor-token kodlaması = generator-policy / §8).
        public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

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

    static string TypesFile(string module, List<TypeJson> types, BuildReport report)
    {
        var sb = new StringBuilder();
        sb.Append($"namespace {Root}.{module};\n\n");
        foreach (var t in types)
        {
            sb.Append(ExtComment(t.Ext, t.Id, report));
            foreach (var f in t.Fields ?? new()) sb.Append(ExtComment(f.Ext, $"{t.Id}.{f.Name}", report));
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

    // entity → EF entity class. concurrency optimistic → [Timestamp] RowVersion. Field ext → yorum (@crypto/@sensitivity).
    static string EntitiesFile(string module, List<EntityJson> entities, BuildReport report)
    {
        var sb = new StringBuilder();
        sb.Append("using System.ComponentModel.DataAnnotations;\n\n");
        sb.Append($"namespace {Root}.{module};\n\n");
        foreach (var e in entities)
        {
            sb.Append(ExtComment(e.Ext, e.Id, report));
            sb.Append($"public class {e.Id}\n{{\n");
            foreach (var f in e.Fields)
            {
                if (f.Ext is { Count: > 0 })
                {
                    foreach (var x in f.Ext) { report.Realized($"@{x.Ns}.{x.Name}", $"{e.Id}.{f.Name}"); report.Policy($"{x.Ns}-realization", "EF value-converter/attribute (generator-policy)"); }
                    sb.Append($"    // {string.Join(" ; ", f.Ext.Select(x => $"@{x.Ns}.{x.Name}"))} (realizasyon = policy)\n");
                }
                if (f.SourceOfTruth is { } sot)
                {
                    report.Realized("sourceOfTruth", $"{e.Id}.{f.Name}");
                    report.Policy("source-of-truth", "cross-module FK reference; no navigation (generator-policy)");
                    sb.Append($"    // sourceOfTruth: {sot.Module}.{sot.Entity} — cross-module FK (kanonik veri orada; navigasyon AÇILMAZ).\n");
                }
                sb.Append($"    public {Naming.Type(f.Type, f.Collection)} {Naming.Pascal(f.Name)} {{ get; set; }} = default!;\n");
            }
            if (e.Concurrency == "optimistic")
                sb.Append("    [Timestamp] public byte[] RowVersion { get; set; } = default!;\n");
            sb.Append("}\n\n");
        }
        return sb.ToString();
    }

    // deployable → modular-monolith host topolojisi: her deployable, units (modüller) TEK host process'te barındırır.
    // ponytail: tek host; ayrı-deploy gerekirse units'i ayrı host'a taşı (seam). Docker/orchestrator = §8 / insan.
    static string HostFile(GenerationModel gm, BuildReport report)
    {
        report.Policy("deployment-topology", "modular-monolith host: units single-process co-hosted (generator-policy)");
        var entries = new List<string>();
        var extComments = new List<string>();
        foreach (var d in gm.Deployables)
        {
            report.Realized("deployable", d.Name);
            if (d.Ext is { Count: > 0 })
            {
                foreach (var x in d.Ext) { report.Realized($"@{x.Ns}.{x.Name}", d.Name); report.Policy($"{x.Ns}-realization", "host annotation (generator-policy)"); }
                extComments.Add($"    // {d.Name} ext: {string.Join(" ; ", d.Ext.Select(x => $"@{x.Ns}.{x.Name}"))} (realizasyon = policy)");
            }
            var units = string.Join(", ", d.Units.Select(u => $"\"{Escape(u)}\""));
            entries.Add($"        [\"{Escape(d.Name)}\"] = [{units}],");
        }
        var ext = extComments.Count > 0 ? string.Join("\n", extComments) + "\n" : "";
        return
            $$"""
            namespace {{Root}};

            // modular-monolith host topolojisi: deployable → tek process'te barındırılan units (modüller).
            // ponytail: tek host; ayrı-deploy = units'i ayrı host'a taşı (seam). Docker/orchestrator = §8 / insan.
            public static class DeploymentTopology
            {
            {{ext}}    public static readonly IReadOnlyDictionary<string, string[]> Deployables =
                new Dictionary<string, string[]>
                {
            {{string.Join("\n", entries)}}
                };
            }

            """;
    }

    // ── adlı-hata kataloğu + throws binding (ADR-0022 K8/K9) ──────────────
    // error → kod sabiti (agnostik ad; wire-kod yok). resultType → Result<T> alt-tipi (yorumlu).
    static string ErrorsFile(string module, List<ErrorJson> errors, BuildReport report)
    {
        var sb = new StringBuilder($"namespace {Root}.{module};\n\n");
        sb.Append("// adlı-hata kataloğu (ADR-0022 K8/K9): kod = agnostik ad; resultType → Result<T> alt-tipi.\n");
        sb.Append("public static class Errors\n{\n");
        foreach (var e in errors)
        {
            report.Realized("error", e.Id);
            sb.Append($"    // resultType: {e.ResultType}\n");
            sb.Append($"    public const string {e.Id} = \"{Escape(e.Id)}\";\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    // op.throws → atılabilir hata kodları + tipli Result fabrikaları (closed taksonomi; NotProcessable<T>.Code bağlı).
    static string? ThrowsPartial(string module, GmOperation op, GenerationModel gm, BuildReport report)
    {
        if (op.Op.Throws.Count == 0) return null;
        var ret = ReturnType(op);
        var consts = new List<string>();
        var factories = new List<string>();
        foreach (var id in op.Op.Throws)
        {
            report.Realized("throws", $"{op.Id}->{id}");
            var err = gm.Errors.FirstOrDefault(e => e.Id == id);
            var codeRef = err is null || err.Module == module ? $"Errors.{id}" : $"{Root}.{err.Module}.Errors.{id}";
            consts.Add(codeRef);
            factories.Add(ThrowFactory(err?.ResultType ?? "NotProcessable", ret, id, codeRef));
        }
        return
            $$"""
            using {{Root}};

            namespace {{Root}}.{{module}};

            // throws: op'un atabileceği adlı-hatalar → tipli Result fabrikaları (iş gövdesi çağırır).
            public partial class {{op.Id}}Handler
            {
                public static readonly string[] ThrowableErrors = [{{string.Join(", ", consts)}}];

            {{string.Join("\n", factories)}}
            }

            """;
    }

    // resultType → Result<T> alt-tipi fabrikası (closed taksonomi; tanınmayan → NotProcessable kod-bağı).
    static string ThrowFactory(string resultType, string ret, string name, string codeRef) => resultType switch
    {
        "NotValid" => $"    public static Result<{ret}> {name}(IReadOnlyDictionary<string, string> errors) => new NotValid<{ret}>(errors);",
        "NotAuthorized" => $"    public static Result<{ret}> {name}(string reason) => new NotAuthorized<{ret}>(reason);",
        "NotAuthenticated" => $"    public static Result<{ret}> {name}(string reason) => new NotAuthenticated<{ret}>(reason);",
        "ServerError" => $"    public static Result<{ret}> {name}(string message) => new ServerError<{ret}>(message);",
        _ => $"    public static Result<{ret}> {name}(string message) => new NotProcessable<{ret}>({codeRef}, message);"
    };

    // consistency {risk, mode}: eventual → outbox seçim-iskeleti; strong+mode → in-proc tx (mode yorumlu).
    // Census ile aynı koşul: yalnız mode!=null VEYA risk==eventual (sade strong/no-mode = örtük ambient tx, artefakt yok).
    static string? ConsistencyPartial(string module, GmOperation op, BuildReport report)
    {
        var c = op.Op.Consistency;
        if (c is null || (c.Mode is null && c.Risk != "eventual")) return null;
        report.Realized("consistency", op.Id);
        var mode = c.Mode ?? "default";
        report.Policy("consistency-mode", $"{c.Risk}/{mode} (generator-policy)");
        var modeConst = c.Mode is null ? "null" : $"\"{Escape(c.Mode)}\"";
        var strategy = c.Risk == "eventual"
            ? "outbox seçim-iskeleti: write + outbox-kaydı TEK tx; ayrı dispatcher publish eder. Taşıma/retry = §8 policy."
            : "in-proc transaction (strong): SaveChanges ambient tx içinde. mode = ek garanti yorumu.";
        return
            $$"""
            namespace {{Root}}.{{module}};

            // consistency: {{c.Risk}} (mode: {{mode}}) → {{strategy}}
            public partial class {{op.Id}}Handler
            {
                public const string ConsistencyRisk = "{{Escape(c.Risk)}}";
                public const string? ConsistencyMode = {{modeConst}};
            }

            """;
    }

    // ── boundary (external/uncharted çağrı-adapter) + saga (calls+compensate) ──
    static string BoundaryFile(GenerationModel gm, BuildReport report)
    {
        var sb = new StringBuilder($"namespace {Root};\n\n");
        sb.Append("// external çağrı-adapter'leri (generated:false → sistemi ÜRETME, yalnız çağıran arayüz + stub client).\n\n");
        foreach (var ext in gm.Externals)
        {
            report.Realized("external", ext.Name);
            string Sig(BoundaryOpJson b)
            {
                var ps = string.Join(", ", b.Signature.Params.Select(p => $"{Naming.Type(p.Type, p.Collection)} {p.Name}"));
                var comma = b.Signature.Params.Count > 0 ? ", " : "";
                return $"Task<{Naming.Type(b.Signature.Returns, false)}> {Naming.Pascal(b.Id)}({ps}{comma}CancellationToken ct = default)";
            }
            sb.Append($"public interface I{ext.Name}\n{{\n");
            foreach (var b in ext.Operations) { report.Realized("boundary-op", $"{ext.Name}.{b.Id}"); sb.Append($"    {Sig(b)};\n"); }
            sb.Append("}\n\n");
            sb.Append($"public sealed class {ext.Name}Client : I{ext.Name}\n{{\n");
            foreach (var b in ext.Operations) sb.Append($"    public {Sig(b)} => throw new NotImplementedException(\"{ext.Name}.{b.Id}\");\n");
            sb.Append("}\n\n");
        }
        foreach (var ce in gm.CallEdges)
        {
            report.Realized("calls", $"{ce.From}->{ce.To.System}.{ce.To.Op}");
            if (ce.Compensate is not null)
            {
                report.Realized("compensate", $"{ce.From}:{ce.To.System}.{ce.To.Op}");
                report.Policy("saga-orchestration-state", "in-memory (generator-policy)");
                sb.Append($"// saga: {ce.From} → {ce.To.System}.{ce.To.Op} (compensate: {ce.Compensate.System}.{ce.Compensate.Op})\n");
                sb.Append($"// ponytail: committed-adımları izle; hata → ters-sıra {ce.Compensate.Op} çağır (orchestration-state seam).\n\n");
            }
        }
        return sb.ToString();
    }

    // uncharted → çağrı-adapter STUB (external emsali) + OWNED entity/type POCO'ları (kendi namespace'inde).
    static string UnchartedFile(UnchartedJson u, BuildReport report)
    {
        report.Realized("uncharted", u.Name);
        report.Policy("uncharted-realization", "call-adapter stub + owned POCOs (generator-policy)");
        var sb = new StringBuilder("using System.ComponentModel.DataAnnotations;\n\n");
        sb.Append($"namespace {Root}.Uncharted.{u.Name};\n\n");
        sb.Append($"// uncharted '{u.Name}' (generated:false): çağrı-adapter STUB + OWNED model (entity/type korunur).\n");
        if (u.Deployable is not null) sb.Append($"// deployable: {u.Deployable}\n");
        sb.Append("\n");
        foreach (var t in u.Types)
        {
            sb.Append(ExtComment(t.Ext, $"{u.Name}.{t.Id}", report));
            foreach (var f in t.Fields ?? new()) sb.Append(ExtComment(f.Ext, $"{u.Name}.{t.Id}.{f.Name}", report));
            if (t.Kind == "enum")
                sb.Append($"public enum {t.Id} {{ {string.Join(", ", t.Values ?? new())} }}\n\n");
            else
                sb.Append($"public sealed record {t.Id}({string.Join(", ", (t.Fields ?? new()).Select(f => $"{Naming.Type(f.Type, f.Collection)} {Naming.Pascal(f.Name)}"))});\n\n");
        }
        foreach (var e in u.Entities)
        {
            sb.Append(ExtComment(e.Ext, $"{u.Name}.{e.Id}", report));
            sb.Append($"public class {e.Id}\n{{\n");
            foreach (var f in e.Fields)
            {
                sb.Append(ExtComment(f.Ext, $"{u.Name}.{e.Id}.{f.Name}", report, "    "));
                sb.Append($"    public {Naming.Type(f.Type, f.Collection)} {Naming.Pascal(f.Name)} {{ get; set; }} = default!;\n");
            }
            if (e.Concurrency == "optimistic") sb.Append("    [Timestamp] public byte[] RowVersion { get; set; } = default!;\n");
            sb.Append("}\n\n");
        }
        string Sig(BoundaryOpJson b)
        {
            var ps = string.Join(", ", b.Signature.Params.Select(p => $"{Naming.Type(p.Type, p.Collection)} {p.Name}"));
            var comma = b.Signature.Params.Count > 0 ? ", " : "";
            return $"Task<{Naming.Type(b.Signature.Returns, false)}> {Naming.Pascal(b.Id)}({ps}{comma}CancellationToken ct = default)";
        }
        sb.Append($"public interface I{u.Name}\n{{\n");
        foreach (var b in u.Operations) sb.Append($"    {Sig(b)};\n");
        sb.Append("}\n\n");
        sb.Append($"public sealed class {u.Name}Client : I{u.Name}\n{{\n");
        foreach (var b in u.Operations) sb.Append($"    public {Sig(b)} => throw new NotImplementedException(\"{u.Name}.{b.Id}\");\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    static string IdempotencyStore() =>
        $$"""
        namespace {{Root}};

        // idempotency dedup seam. ponytail: in-memory; kalıcı store/pencere/replay = §8 policy.
        public interface IIdempotencyStore { Task<bool> TryBeginAsync(string key, CancellationToken ct = default); }

        public sealed class InMemoryIdempotencyStore : IIdempotencyStore
        {
            readonly HashSet<string> _seen = new();
            public Task<bool> TryBeginAsync(string key, CancellationToken ct = default)
            {
                lock (_seen) return Task.FromResult(_seen.Add(key));
            }
        }

        """;

    static string IdemPartial(string module, GmOperation op) =>
        $$"""
        namespace {{Root}}.{{module}};

        // idempotent by: {{string.Join(", ", op.Op.Idempotent!.Keys)}} (dedup-store = generator-policy)
        public partial class {{op.Id}}Handler
        {
            public static readonly string[] IdempotencyKeys = [{{string.Join(", ", op.Op.Idempotent!.Keys.Select(k => $"\"{Escape(k)}\""))}}];
        }

        """;

    static string? ExtPartial(string module, GmOperation op, BuildReport report)
    {
        if (op.Op.Ext is not { Count: > 0 }) return null;
        var lines = new List<string>();
        foreach (var e in op.Op.Ext)
        {
            report.Realized($"@{e.Ns}.{e.Name}", op.Id);
            report.Policy($"{e.Ns}-realization", "interceptor/attribute (generator-policy)");
            lines.Add($"    // @{e.Ns}.{e.Name}({string.Join(", ", e.Args.Select(a => $"{a.Key}={a.Value}"))})");
        }
        var audit = op.Op.Ext.FirstOrDefault(e => e.Ns == "audit");
        var metric = op.Op.Ext.FirstOrDefault(e => e.Ns == "metric");
        if (audit is not null && audit.Args.TryGetValue("category", out var cat)) lines.Add($"    public const string AuditCategory = {JsonStr(cat)};");
        if (metric is not null && metric.Args.TryGetValue("name", out var mn)) lines.Add($"    public const string MetricName = {JsonStr(mn)};");
        return
            $$"""
            namespace {{Root}}.{{module}};

            // passthrough prelude'lar (core yorumlamaz; hedef-özel realizasyon = §8 policy).
            public partial class {{op.Id}}Handler
            {
            {{string.Join("\n", lines)}}
            }

            """;
    }

    static string JsonStr(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => $"\"{Escape(v.GetString()!)}\"",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "null"
    };

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
        void Add(string kind, int i, Gen.Core.Model.ExprNode ast, string text, string? guardRef = null)
        {
            var (method, rec) = Predicate(kind, i, ast, text, op.Id, entityId: null, gm, report, guardRef);
            methods.Add(method);
            if (rec is not null) records.Add(rec);
        }
        for (var i = 0; i < op.Op.Validation.Count; i++) Add("Validation", i, op.Op.Validation[i].Ast, op.Op.Validation[i].Text, op.Op.Validation[i].GuardRef);
        for (var i = 0; i < op.Op.Rule.Count; i++) Add("Rule", i, op.Op.Rule[i].Ast, op.Op.Rule[i].Text, op.Op.Rule[i].GuardRef);
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
        string? opId, string? entityId, GenerationModel gm, BuildReport report, string? guardRef = null)
    {
        var owner = opId ?? entityId!;
        var key = $"{owner}#{kind}{i}";
        // for guard "id" → predicate'e yorum (build-time kapsama bağı; navigasyon/runtime bağı AÇILMAZ). Resolved Q1.
        var guardComment = "";
        if (guardRef is not null)
        {
            report.Realized("guardRef", key);
            report.Policy("guard-linkage", "build-time coverage link; emitted as comment (generator-policy)");
            guardComment = $"    // for guard: \"{Escape(guardRef)}\" (build-time kapsama bağı)\n";
        }
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
            return ($"{guardComment}    static bool {kind}_{i}({inputName} input) => {expr};", record);
        }
        catch (Gen.Core.UnsupportedConstruct e)
        {
            report.Unsupported(kind.ToLowerInvariant(), key, e.Message);
            return ($"{guardComment}    static bool {kind}_{i}() => throw new NotImplementedException(\"unsupported: {Escape(text)}\");", null);
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
            var (method, rec) = Predicate("Invariant", i, e.Invariants[i].Ast, e.Invariants[i].Text, opId: null, entityId: e.Id, gm, report, e.Invariants[i].GuardRef);
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
    static string XmlEscape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ext passthrough → yorum + census kaydı (her annotation-site ortak; realizasyon = §8 policy).
    // owner census AddExt ile aynı: type→Id, type-field→"Id.field", entity→Id, op-param→"opId.param", module→name.
    static string ExtComment(List<ExtJson>? ext, string owner, BuildReport report, string indent = "")
    {
        if (ext is not { Count: > 0 }) return "";
        foreach (var x in ext) { report.Realized($"@{x.Ns}.{x.Name}", owner); report.Policy($"{x.Ns}-realization", "annotation/interceptor (generator-policy)"); }
        return $"{indent}// ext: {string.Join(" ; ", ext.Select(x => $"@{x.Ns}.{x.Name}"))} (realizasyon = policy)\n";
    }

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

    // pagination → dönüş Page<T>; istek param'larına opak cursor + size eklenir (cursor kodlaması = §8 policy).
    static string ReturnType(GmOperation op)
    {
        var ret = Naming.Type(op.Op.Signature.Returns, false);
        return op.Op.Pagination is not null ? $"Page<{ret}>" : ret;
    }

    static string RequestFields(GmOperation op)
    {
        var fields = op.Op.Signature.Params.Select(p => $"{Naming.Type(p.Type, p.Collection)} {Naming.Pascal(p.Name)}").ToList();
        if (op.Op.Pagination is not null) { fields.Add("string? Cursor"); fields.Add("int Size"); }
        return string.Join(", ", fields);
    }

    static string OperationFile(string module, GmOperation op, BuildReport report)
    {
        var req = RequestName(op);
        var ret = ReturnType(op);
        var pageComment = op.Op.Pagination is { } p
            ? $"\n// pagination keyset: ORDER BY {string.Join(", ", p.Keys.Select(k => $"{Naming.Pascal(k.Field)} {k.Direction}"))} (keyset/offset + cursor-token = generator-policy)"
            : "";
        // param ext → request alanı başına yorum + census kaydı (realizasyon = §8 policy).
        var paramExt = string.Concat(op.Op.Signature.Params.Select(p =>
            p.Ext is { Count: > 0 } ? $"// {Naming.Pascal(p.Name)}: {ExtComment(p.Ext, $"{op.Id}.{p.Name}", report).Replace("// ", "").TrimEnd()}\n" : ""));
        // note → handler üstü XML doc-comment (op.note); business-note ayrı satır.
        var doc = op.Op.Note is null ? "" : $"/// <summary>{XmlEscape(op.Op.Note)}</summary>\n";
        return
            $$"""
            using {{Root}};

            namespace {{Root}}.{{module}};
            {{pageComment}}
            {{paramExt}}public sealed record {{req}}({{RequestFields(op)}});

            {{doc}}public partial class {{op.Id}}Handler
            {
                // İş gövdesi {{op.Id}}Handler.Logic.cs'te (yoksa-üret; üreteç ezmez).
                public partial Task<Result<{{ret}}>> ExecuteAsync({{req}} request, CancellationToken ct);
            }

            """;
    }

    static string LogicFile(string module, GmOperation op)
    {
        var req = RequestName(op);
        var ret = ReturnType(op);
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
        foreach (var ext in gm.Externals)
            di.Append($"builder.Services.AddSingleton<I{ext.Name}, {ext.Name}Client>();\n");
        foreach (var u in gm.Uncharted)
            di.Append($"builder.Services.AddSingleton<{Root}.Uncharted.{u.Name}.I{u.Name}, {Root}.Uncharted.{u.Name}.{u.Name}Client>();\n");
        if (gm.Operations.Any(o => o.Op.Idempotent is not null))
            di.Append("builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();\n");
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
        var lambdaList = routeParams.Select(p => $"string {p}").ToList();
        var ctorArgs = routeParams.ToList();
        if (op.Op.Pagination is not null)   // paginated GET → cursor/size query-string'den
        {
            lambdaList.Add("string? cursor"); lambdaList.Add("int size");
            ctorArgs.Add("cursor"); ctorArgs.Add("size");
        }
        var lambdaParams = lambdaList.Count > 0 ? string.Join(", ", lambdaList) + ", " : "";
        var ctor = $"new {req}({string.Join(", ", ctorArgs)})";
        return $"app.{verb}(\"{route}\", async ({lambdaParams}{op.Id}Handler handler, CancellationToken ct) => ResultHttp.ToHttp(await handler.ExecuteAsync({ctor}, ct)));\n";
    }
}
