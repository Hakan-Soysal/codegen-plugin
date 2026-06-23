using System.Text.Json;

namespace Gen.Core.Model;

// operations.json — Tech'in tükettiği ContractOp alt-kümesi (src/tech/contract.ts).
// schedule/delegation şimdilik tüketilmiyor (ignore); gerekince Phase 7'de eklenir.

public sealed record ContractFile(
    ContractMeta? Meta,
    List<ContractOp>? Operations,
    List<ContractEntity>? Entities,
    List<ContractActor>? Actors,
    List<JsonElement>? Relations);

public sealed record ContractMeta(int? SchemaVersion);
public sealed record ContractSignature(string Actor, string Verb, string Ownership, string Resource);
public sealed record ContractGuard(string Id, string Kind, string? Role, string? Calendar, string? Text, ExprNode? Ast);
// Target gerçek contract'ta string ("biz.Invoice") VEYA path dizisi (["Appointment","status"]).
// Emit'te tüketilmiyor (yalnız realizes-join için parse edilir), bu yüzden toleranslı JsonElement.
public sealed record ContractEffect(string Kind, JsonElement? Target);
public sealed record ContractAccess(List<string> Writes, List<string> Reads);

public sealed record ContractOp(
    string Id, string Kind, ContractSignature? Signature, string? Description,
    List<ContractGuard> Guards, List<ContractEffect> Effects, ContractAccess Access,
    List<string> Flows, List<string> Processes, string? Domain);

public sealed record ContractEntity(string Id, string Name, string? Domain);
public sealed record ContractActor(string Id, string? Extends);
