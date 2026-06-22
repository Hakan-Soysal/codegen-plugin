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
    List<UnchartedJson> Uncharted,   // uncharted = external-benzeri çağrı-adapter AMA kendi entity/type'larını OWN eder
    List<CallEdgeJson> CallEdges,
    Coverage Coverage);

public sealed record Meta(bool HasErrors, int ErrorCount);
public sealed record Deployable(string Name, List<string> Units, List<ExtJson>? Ext = null);
public sealed record ModuleDecl(string Name, bool PureTechnical, List<ExtJson>? Ext = null);

public sealed record ParamJson(string Name, string Type, bool Collection, List<ExtJson>? Ext = null);
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
    List<GuardedExpr> Invariants, string? Concurrency, List<ExtJson>? Ext = null);

public sealed record FieldJson(string Name, string Type, bool Collection, List<ExtJson>? Ext = null);
public sealed record TypeJson(string Id, string Module, string Kind, List<FieldJson>? Fields, List<string>? Values, List<ExtJson>? Ext = null);

public sealed record ErrorJson(string Id, string Module, string ResultType);
public sealed record EventJson(string Id, string Module, List<FieldJson> Payload);
public sealed record EventRef(string Module, string Name);
public sealed record ConsumerRef(string Module, string Op);
public sealed record SubscriptionJson(EventRef Event, ConsumerRef Consumer);
public sealed record CallTarget(string System, string Op);
public sealed record CallEdgeJson(string From, CallTarget To, string Kind, CallTarget? Compensate);
public sealed record BoundaryOpJson(string Id, SignatureJson Signature, List<ServingJson>? Serving = null, List<GuardedExpr>? Validation = null);
public sealed record ExternalJson(string Name, bool Generated, List<BoundaryOpJson> Operations);

// uncharted (T-4.3): external gibi çağrı-adapter (generated:false) AMA kendi entity/type'larını OWN eder.
public sealed record UnchartedEntity(string Id, List<string> Realizes, List<EntityFieldJson> Fields, string? Concurrency, List<ExtJson>? Ext = null);
public sealed record UnchartedType(string Id, string Kind, List<FieldJson>? Fields, List<string>? Values, List<ExtJson>? Ext = null);
public sealed record UnchartedJson(
    string Name, bool Generated, string? Deployable,
    List<BoundaryOpJson> Operations, List<UnchartedEntity> Entities, List<UnchartedType> Types);
public sealed record Coverage(List<string> UnrealizedBusinessOps, List<string> UncoveredEntities);
