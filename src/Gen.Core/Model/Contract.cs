using System.Text.Json;

namespace Gen.Core.Model;

// operations.json — Tech'in tükettiği ContractOp alt-kümesi (src/tech/contract.ts).
// schedule/delegation şimdilik tüketilmiyor (ignore); gerekince Phase 7'de eklenir.

public sealed record ContractFile(
    ContractMeta? Meta,
    List<ContractOp>? Operations,
    List<ContractEntity>? Entities,
    List<ContractActor>? Actors,
    List<JsonElement>? Relations,
    List<ProcessJson>? Processes = null,
    List<FlowJson>? Flows = null);

public sealed record ContractMeta(int? SchemaVersion);
public sealed record ContractSignature(string Actor, string Verb, string Ownership, string Resource);
public sealed record ContractGuard(string Id, string Kind, string? Role, string? Calendar, string? Text, ExprNode? Ast);
// Target gerçek contract'ta string ("biz.Invoice") VEYA path dizisi (["Appointment","status"]).
// Emit'te tüketilmiyor (yalnız realizes-join için parse edilir), bu yüzden toleranslı JsonElement.
// Expr = calculate effect'in ExprNode değeri (varsa); Text = ham literal metni (ör. "'iptal'").
// İkisi de nullable → eski/değersiz effect'ler bozulmaz. Field-ASSERT (T-2.2) bunları okur.
public sealed record ContractEffect(string Kind, JsonElement? Target, ExprNode? Expr = null, string? Text = null);
public sealed record ContractAccess(List<string> Writes, List<string> Reads);

public sealed record ContractOp(
    string Id, string Kind, ContractSignature? Signature, string? Description,
    List<ContractGuard> Guards, List<ContractEffect> Effects, ContractAccess Access,
    List<string> Flows, List<string> Processes, string? Domain);

public sealed record ContractEntity(string Id, string Name, string? Domain);
public sealed record ContractActor(string Id, string? Extends);

// operations.json process/flow strüktürü (TestPlan IR girdisi). Toleranslı: bilinmeyen alan ignore.
public sealed record ProcessJson(string Id, string? Entity, string? Note, List<ProcessStage>? Items);
public sealed record ProcessStage(string Type, string Name, string? StageKind, string? Flow, string? By);
public sealed record FlowJson(string Id, string? Actor, string? Note, List<FlowStep>? Items);
public sealed record FlowStep(string Type, string Name, string? Target, bool Optional, bool Repeat, string? Using);
