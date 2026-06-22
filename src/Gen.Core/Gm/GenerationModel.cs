using System.Text.Json;
using Gen.Core.Model;

namespace Gen.Core.Gm;

/// <summary>
/// Hedef-nötr IR (spec §6). manifest ⋈ operations.json join ürünü. Build-time; runtime
/// semantiği yok. ponytail: manifest zaten normalize; GM onu sarmalar + join + türevleri ekler.
/// JSON Schema (spec §12) Phase 6'ya ertelendi — Go ikinci tüketici olunca pinlenir.
/// </summary>
public sealed record GenerationModel(
    string Mode,
    IReadOnlyList<ModuleDecl> Modules,
    IReadOnlyList<GmOperation> Operations,
    IReadOnlyList<EntityJson> Entities,
    IReadOnlyList<TypeJson> Types,
    IReadOnlyList<EventJson> Events,
    IReadOnlyList<SubscriptionJson> Subscriptions,
    IReadOnlyList<ErrorJson> Errors,
    IReadOnlyList<ExternalJson> Externals,
    IReadOnlyList<CallEdgeJson> CallEdges,
    IReadOnlyList<Deployable> Deployables,
    IReadOnlyList<JsonElement> Uncharted,
    TypeEnv Env);

/// <summary>
/// Tip ortamı (Go seam bulgusu sonrası eklendi): predicate path'lerini manifest tiplerine
/// çözer → adapter'lar DİL-TİPLİ predicate emit eder (dynamic YOK). Çözülemeyen path = tipli seam.
/// </summary>
public sealed record TypeEnv(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> OpParams,    // opId → (param → manifestType)
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EntityFields) // entityId → (field → manifestType)
{
    /// <summary>Op'un yazma-hedefi entity'si (resource.* çözümü için); yoksa null.</summary>
    public static string? WriteTarget(GmOperation op) =>
        op.Op.Access.Creates.Concat(op.Op.Access.Updates).Concat(op.Op.Access.Deletes).FirstOrDefault();

    /// <summary>Bir predicate path'ini NÖTR manifest tipine çözer (dil-bağımsız). actor.*→"String" (opak claim);
    /// resource.*→write-hedefi entity alanı; bare→op param (guard) / entity alanı (invariant). Çözülemeyen→null.</summary>
    public string? ResolvePath(GenerationModel gm, IReadOnlyList<string> path, string? opId, string? entityId)
    {
        if (path.Count == 0) return null;
        if (path[0] == "actor") return "String";
        if (path[0] == "resource" && opId is not null)
        {
            var op = gm.Operations.FirstOrDefault(o => o.Id == opId);
            var wt = op is null ? null : WriteTarget(op);
            if (wt is not null && path.Count >= 2 && EntityFields.TryGetValue(wt, out var ef) && ef.TryGetValue(path[1], out var rt))
                return rt;
            return null;
        }
        if (opId is not null && OpParams.TryGetValue(opId, out var ps) && ps.TryGetValue(path[0], out var pt)) return pt;
        if (entityId is not null && EntityFields.TryGetValue(entityId, out var fs) && fs.TryGetValue(path[0], out var ft)) return ft;
        return null;
    }
}

/// <summary>Operasyon + join'den gelen iş-bağlamı + türev komut/sorgu ayrımı.</summary>
public sealed record GmOperation(OperationJson Op, ContractOp? Business, bool IsCommand)
{
    public string Id => Op.Id;
    public string Module => Op.Module;
}
