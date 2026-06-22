using Gen.Core.Gm;
using Gen.Core.Model;

namespace Gen.Core.Pipeline;

/// <summary>Aşama 2+3 (Join &amp; Validate → Generation Model). Hedef-bağımsız.</summary>
public static class GmBuilder
{
    public static GenerationModel Build(ManifestJson m, ContractFile? contract)
    {
        var linked = m.Mode == "linked";
        if (linked && m.Contract is not null && contract is null)
            throw new JoinError($"linked mod ama operations.json çözülemedi: {m.Contract}");

        // standalone'da join N/A (INV-9); linked'de realizes çözülür.
        var ops = linked ? (contract?.Operations ?? new()).ToDictionary(o => o.Id) : new();
        var ents = linked ? (contract?.Entities ?? new()).ToDictionary(e => e.Id) : new();

        var operations = m.Operations
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => BuildOp(o, linked, ops))
            .ToList();

        if (linked)
            foreach (var e in m.Entities)
                foreach (var rid in e.Realizes)
                    if (!ents.ContainsKey(rid))
                        throw new JoinError($"entity '{e.Id}' realizes '{rid}' operations.json'da yok");

        var env = new TypeEnv(
            OpParams: m.Operations.ToDictionary(
                o => o.Id,
                o => (IReadOnlyDictionary<string, string>)o.Signature.Params.ToDictionary(p => p.Name, p => p.Type)),
            EntityFields: m.Entities.ToDictionary(
                e => e.Id,
                e => (IReadOnlyDictionary<string, string>)e.Fields.ToDictionary(f => f.Name, f => f.Type)));

        return new GenerationModel(
            Mode: m.Mode,
            Modules: m.Modules.OrderBy(x => x.Name, StringComparer.Ordinal).ToList(),
            Operations: operations,
            Entities: m.Entities.OrderBy(x => x.Id, StringComparer.Ordinal).ToList(),
            Types: m.Types.OrderBy(x => x.Id, StringComparer.Ordinal).ToList(),
            Events: m.Events.OrderBy(x => x.Id, StringComparer.Ordinal).ToList(),
            Subscriptions: m.Subscriptions
                .OrderBy(x => x.Event.Module, StringComparer.Ordinal).ThenBy(x => x.Event.Name, StringComparer.Ordinal)
                .ThenBy(x => x.Consumer.Op, StringComparer.Ordinal).ToList(),
            Errors: m.Errors.OrderBy(x => x.Id, StringComparer.Ordinal).ToList(),
            Env: env);
    }

    static GmOperation BuildOp(OperationJson o, bool linked, Dictionary<string, ContractOp> contractOps)
    {
        ContractOp? business = null;
        if (linked && o.Realizes is not null)
        {
            if (!contractOps.TryGetValue(o.Realizes, out business))
                throw new JoinError($"operation '{o.Id}' realizes '{o.Realizes}' operations.json'da yok");
        }
        var isCommand = o.Access.Creates.Count > 0 || o.Access.Updates.Count > 0 || o.Access.Deletes.Count > 0;
        return new GmOperation(o, business, isCommand);
    }
}
