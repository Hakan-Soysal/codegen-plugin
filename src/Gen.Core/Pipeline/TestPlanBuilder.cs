using Gen.Core.Gm;
using Gen.Core.Model;

namespace Gen.Core.Pipeline;

/// <summary>
/// Containment (process→flow→op) + orphan (flow/op) türetir → deterministik <see cref="TestPlan"/>.
/// Tüm çıktı listeleri ordinal-sıralı (GmBuilder determinizm pattern'ı). RunSequence içi sıra ise
/// contract-meaningful (Items sırası) — ORDINAL DEĞİL. Ön-gereksinim + WriteSet manifest.access (Kapı 0
/// authority — 4-key Reads/Creates/Updates/Deletes) üzerinden türetilir; çoklu/sıfır creator → DUR
/// (PrereqKind.Ambiguous/Missing, CreatorOp=null — ASLA bir creator seçilmez).
/// </summary>
public static class TestPlanBuilder
{
    public static TestPlan Build(ContractFile? c, IReadOnlyList<OperationJson> manifestOps)
    {
        // Standalone / eski contract / eksik strüktür → boş plan (çökme YOK).
        if (c?.Processes is null || c.Flows is null)
            return new TestPlan(new List<ProcessTest>(), new List<ScenarioTest>(), new List<ScenarioTest>());

        // --- Ön-gereksinim altyapısı: op-id index + creator ters-index (manifest.access, ordinal) ---
        var opById = new Dictionary<string, OperationJson>(StringComparer.Ordinal);
        foreach (var op in manifestOps ?? new List<OperationJson>())
            opById[op.Id] = op; // dup id → son kazanır (toleranslı)

        // creators: entity → [op.Id where Access.Creates contains entity] (ordinal-sıralı).
        var creators = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var op in (manifestOps ?? new List<OperationJson>()).OrderBy(o => o.Id, StringComparer.Ordinal))
            foreach (var e in op.Access.Creates)
            {
                if (!creators.TryGetValue(e, out var list)) { list = new List<string>(); creators[e] = list; }
                if (!list.Contains(op.Id)) list.Add(op.Id);
            }

        // --- DerivePrerequisites + WriteSet (manifest.access; deterministik) ---
        // produced = runSeq op'larının Access.Creates birleşimi.
        // needed   = runSeq op'larının (Access.Reads ∪ Access.Updates) − produced (ordinal distinct).
        //   creators[e].Count==1 → Single(creators[e][0]); >1 → Ambiguous(null); 0 → Missing(null).
        //   Single'lar creator-op bağımlılığına göre topo-sıralı (entity-id ordinal tie-break);
        //   Ambiguous/Missing sona (entity-id ordinal).
        // WriteSet = runSeq op'larının Access.{Creates,Updates,Deletes} birleşimi (ordinal distinct).
        (IReadOnlyList<PrereqStep> Prereqs, IReadOnlyList<string> WriteSet) Derive(IReadOnlyList<string> runSeq)
        {
            var produced = new HashSet<string>(StringComparer.Ordinal);
            var needed = new HashSet<string>(StringComparer.Ordinal);
            var writeSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var opId in runSeq)
            {
                if (!opById.TryGetValue(opId, out var op)) continue; // manifest'te yoksa skip (toleranslı)
                foreach (var e in op.Access.Creates) produced.Add(e);
            }
            foreach (var opId in runSeq)
            {
                if (!opById.TryGetValue(opId, out var op)) continue;
                foreach (var e in op.Access.Reads) needed.Add(e);
                foreach (var e in op.Access.Updates) needed.Add(e);
                foreach (var e in op.Access.Creates) writeSet.Add(e);
                foreach (var e in op.Access.Updates) writeSet.Add(e);
                foreach (var e in op.Access.Deletes) writeSet.Add(e);
            }
            needed.ExceptWith(produced);

            // Sınıflama: Single / Ambiguous / Missing.
            var single = new List<string>();   // entity (creators count==1)
            var deferred = new List<PrereqStep>(); // Ambiguous + Missing
            foreach (var e in needed)
            {
                var count = creators.TryGetValue(e, out var cl) ? cl.Count : 0;
                if (count == 1) single.Add(e);
                else if (count > 1) deferred.Add(new PrereqStep(e, null, PrereqKind.Ambiguous));
                else deferred.Add(new PrereqStep(e, null, PrereqKind.Missing));
            }

            // Topo-sort Single'lar: e1 → e2 kenarı (e1 e2'ye bağımlı) = creators[e1][0]'ın
            // (Access.Reads ∪ Access.Updates) needed-Single kümesindeki e2'yi içermesi. Yani e2
            // önce gelmeli. Deterministik: her adımda deps'i tükenmiş en küçük ordinal entity'yi seç;
            // hiçbiri uygun değilse (döngü) en küçük ordinal kalanı seç (sonsuz döngü YOK).
            var singleSet = new HashSet<string>(single, StringComparer.Ordinal);
            var deps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var e in single)
            {
                var d = new HashSet<string>(StringComparer.Ordinal);
                if (opById.TryGetValue(creators[e][0], out var cop))
                {
                    foreach (var r in cop.Access.Reads) if (r != e && singleSet.Contains(r)) d.Add(r);
                    foreach (var u in cop.Access.Updates) if (u != e && singleSet.Contains(u)) d.Add(u);
                }
                deps[e] = d;
            }
            var remaining = single.OrderBy(e => e, StringComparer.Ordinal).ToList();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<PrereqStep>();
            while (remaining.Count > 0)
            {
                var pick = remaining.FirstOrDefault(e => deps[e].All(emitted.Contains)) ?? remaining[0];
                ordered.Add(new PrereqStep(pick, creators[pick][0], PrereqKind.Single));
                emitted.Add(pick);
                remaining.Remove(pick);
            }
            ordered.AddRange(deferred.OrderBy(p => p.Entity, StringComparer.Ordinal));

            var writeSetList = writeSet.OrderBy(e => e, StringComparer.Ordinal).ToList();
            return (ordered, writeSetList);
        }

        var flowById = new Dictionary<string, FlowJson>();
        foreach (var f in c.Flows)
            flowById[f.Id] = f; // dup id → son kazanır (toleranslı; küme-mantığı bozulmaz)

        // --- ProcessTests: containment RunSequence (Items sırası korunur) ---
        // flowsInProcess: process'lerde referans edilen flow id'leri (set).
        var flowsInProcess = new HashSet<string>(StringComparer.Ordinal);
        var processTests = new List<ProcessTest>();
        foreach (var p in c.Processes)
        {
            var runSeq = new List<string>();
            foreach (var stage in p.Items ?? new List<ProcessStage>())
            {
                if (stage.Flow is null) continue;
                flowsInProcess.Add(stage.Flow);
                if (!flowById.TryGetValue(stage.Flow, out var flow)) continue; // dangling → skip+devam (NO throw)
                foreach (var step in flow.Items ?? new List<FlowStep>())
                    if (step.Target is not null)
                        runSeq.Add(step.Target);
            }
            var (prereqs, writeSet) = Derive(runSeq);
            processTests.Add(new ProcessTest(p.Id, p.Entity, runSeq, prereqs, writeSet));
        }

        // --- opsInFlow: TÜM flow'ların (orphan dahil) target'ları (set) — küme-farkından ÖNCE hesaplanır ---
        var opsInFlow = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in c.Flows)
            foreach (var step in f.Items ?? new List<FlowStep>())
                if (step.Target is not null)
                    opsInFlow.Add(step.Target);

        // --- OrphanFlowTests: Flows \ flowsInProcess ---
        var orphanFlowTests = new List<ScenarioTest>();
        foreach (var f in c.Flows)
        {
            if (flowsInProcess.Contains(f.Id)) continue;
            var runSeq = new List<string>();
            foreach (var step in f.Items ?? new List<FlowStep>())
                if (step.Target is not null)
                    runSeq.Add(step.Target);
            var (prereqs, writeSet) = Derive(runSeq);
            orphanFlowTests.Add(new ScenarioTest(f.Id, "OrphanFlow", runSeq, prereqs, writeSet));
        }

        // --- OrphanOpTests: contract Operations \ opsInFlow ---
        var orphanOpTests = new List<ScenarioTest>();
        foreach (var op in c.Operations ?? new List<ContractOp>())
            if (!opsInFlow.Contains(op.Id))
            {
                var runSeq = new List<string> { op.Id };
                var (prereqs, writeSet) = Derive(runSeq);
                orphanOpTests.Add(new ScenarioTest(op.Id, "OrphanOp", runSeq, prereqs, writeSet));
            }

        return new TestPlan(
            processTests.OrderBy(t => t.ProcessId, StringComparer.Ordinal).ToList(),
            orphanFlowTests.OrderBy(t => t.Id, StringComparer.Ordinal).ToList(),
            orphanOpTests.OrderBy(t => t.Id, StringComparer.Ordinal).ToList());
    }
}
