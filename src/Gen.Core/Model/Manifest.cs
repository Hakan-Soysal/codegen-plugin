using System.Text.Json;

namespace Gen.Core.Model;

// manifest.json şeması (src/tech/manifest.ts ManifestJson). Bilinmeyen JSON alanları
// System.Text.Json tarafından sessizce yok sayılır (ör. operation.edges — şimdilik tüketilmiyor).

public sealed record ManifestJson(
    string Mode,
    string? Contract,
    Meta Meta,
    List<Deployable> Deployables,
    List<ModuleDecl> Modules,
    List<OperationJson> Operations,
    List<EntityJson> Entities,
    List<TypeJson> Types,
    List<ErrorJson> Errors,
    List<EventJson> Events,
    List<SubscriptionJson> Subscriptions,
    List<ExternalJson> Externals,
    List<JsonElement> Uncharted,   // ponytail: uncharted external'a benzer; minimal report (entity/type-kapsayıcı sonra)
    List<CallEdgeJson> CallEdges,
    Coverage Coverage);

public sealed record Meta(bool HasErrors, int ErrorCount);
public sealed record Deployable(string Name, List<string> Units);
public sealed record ModuleDecl(string Name, bool PureTechnical);

public sealed record ParamJson(string Name, string Type, bool Collection);
public sealed record SignatureJson(List<ParamJson> Params, string Returns);
public sealed record ServingArg(string Kind, string Value, List<string>? Params);
public sealed record ServingJson(string Protocol, List<ServingArg> Args, string Raw);
public sealed record AccessJson(List<string> Reads, List<string> Creates, List<string> Updates, List<string> Deletes);
public sealed record GuardedExpr(string Text, ExprNode Ast, string? GuardRef);
public sealed record Consistency(string Risk, string? Mode);
public sealed record Abac(ExprNode Permit);
public sealed record Idempotent(List<string> Keys);
public sealed record PaginationKey(string Field, string Direction);
public sealed record Pagination(string Strategy, List<PaginationKey> Keys, int? Size);

public sealed record ExtJson(string Ns, string Name, Dictionary<string, JsonElement> Args);

public sealed record OperationJson(
    string Id, string Module, string Visibility, string? Realizes,
    SignatureJson Signature, List<ServingJson> Serving, List<string> Roles,
    string? Ownership, AccessJson Access, List<GuardedExpr> Validation, List<GuardedExpr> Rule,
    string? Note, string? BusinessNote, Consistency Consistency, Abac? Abac,
    List<string> Scopes, List<string> Throws, Idempotent? Idempotent, List<string> Emits, Pagination? Pagination,
    List<ExtJson>? Ext = null);

public sealed record SourceOfTruth(string Module, string Entity);
public sealed record EntityFieldJson(
    string Name, string Type, bool Collection, string Cardinality, string Ref,
    string? TargetModule, bool? CrossModule, SourceOfTruth? SourceOfTruth, List<ExtJson>? Ext = null);
public sealed record EntityJson(
    string Id, string Module, List<string> Realizes, List<EntityFieldJson> Fields,
    List<GuardedExpr> Invariants, string? Concurrency);

public sealed record FieldJson(string Name, string Type, bool Collection);
public sealed record TypeJson(string Id, string Module, string Kind, List<FieldJson>? Fields, List<string>? Values);

public sealed record ErrorJson(string Id, string Module, string ResultType);
public sealed record EventJson(string Id, string Module, List<FieldJson> Payload);
public sealed record EventRef(string Module, string Name);
public sealed record ConsumerRef(string Module, string Op);
public sealed record SubscriptionJson(EventRef Event, ConsumerRef Consumer);
public sealed record CallTarget(string System, string Op);
public sealed record CallEdgeJson(string From, CallTarget To, string Kind, CallTarget? Compensate);
public sealed record BoundaryOpJson(string Id, SignatureJson Signature);
public sealed record ExternalJson(string Name, bool Generated, List<BoundaryOpJson> Operations);
public sealed record Coverage(List<string> UnrealizedBusinessOps, List<string> UncoveredEntities);
