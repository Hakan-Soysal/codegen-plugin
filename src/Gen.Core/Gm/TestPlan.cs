namespace Gen.Core.Gm;

/// <summary>Deterministik test IR (tasarım §3b). Tüm listeler ordinal-sıralı.</summary>
public sealed record TestPlan(
    IReadOnlyList<ProcessTest> ProcessTests,
    IReadOnlyList<ScenarioTest> OrphanFlowTests,
    IReadOnlyList<ScenarioTest> OrphanOpTests);

/// <summary>Bir sürecin testi: sıralı çağrı zinciri + ön-gereksinim + (Kapı 0) yazma-kümesi.</summary>
public sealed record ProcessTest(
    string ProcessId, string? Entity,
    IReadOnlyList<string> RunSequence,
    IReadOnlyList<PrereqStep> Prerequisites,
    IReadOnlyList<string> WriteSet);

/// <summary>Orphan flow / orphan op testi: tek-scope senaryo, aynı 3-faz.</summary>
public sealed record ScenarioTest(
    string Id, string Scope,            // Scope ∈ "OrphanFlow" | "OrphanOp"
    IReadOnlyList<string> RunSequence,
    IReadOnlyList<PrereqStep> Prerequisites,
    IReadOnlyList<string> WriteSet);

public enum PrereqKind { Single, Ambiguous, Missing }
public sealed record PrereqStep(string Entity, string? CreatorOp, PrereqKind Kind);
