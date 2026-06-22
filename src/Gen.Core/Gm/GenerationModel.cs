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
    IReadOnlyList<ErrorJson> Errors);

/// <summary>Operasyon + join'den gelen iş-bağlamı + türev komut/sorgu ayrımı.</summary>
public sealed record GmOperation(OperationJson Op, ContractOp? Business, bool IsCommand)
{
    public string Id => Op.Id;
    public string Module => Op.Module;
}
