using Gen.Core.Model;

namespace Gen.Core.Report;

/// <summary>
/// Manifest construct census = §6.4 traceability tablosunun kod-karşılığı (tek-kaynak).
/// Manifest'te VAR olan her construct örneğini (construct, owner) olarak sayar. Build-time-only
/// construct'lar (standalone/contract/import/rolemap/extension-decl/realizes) census'a GİRMEZ.
/// </summary>
public static class Completeness
{
    public static List<(string Construct, string Owner)> Census(ManifestJson m)
    {
        var x = new List<(string, string)>();

        foreach (var d in m.Deployables) { x.Add(("deployable", d.Name)); AddExt(x, d.Ext, d.Name); }
        foreach (var mod in m.Modules) AddExt(x, mod.Ext, mod.Name);
        foreach (var e in m.Errors) x.Add(("error", e.Id));
        foreach (var ext in m.Externals)
        {
            x.Add(("external", ext.Name));
            foreach (var b in ext.Operations)
            {
                x.Add(("boundary-op", $"{ext.Name}.{b.Id}"));
                if (b.Validation is { Count: > 0 }) x.Add(("validation", $"{ext.Name}.{b.Id}"));
                foreach (var s in b.Serving ?? new()) x.Add(("serving", $"{ext.Name}.{b.Id}:{s.Protocol}"));
            }
        }
        foreach (var u in m.Uncharted) x.Add(("uncharted", u.Name));
        foreach (var s in m.Subscriptions) x.Add(("subscription", s.Event.Name));
        foreach (var ce in m.CallEdges) { x.Add(("calls", ce.From)); if (ce.Compensate is not null) x.Add(("compensate", ce.From)); }
        foreach (var ev in m.Events) x.Add(("event", ev.Id));
        foreach (var t in m.Types) { x.Add((t.Kind, t.Id)); AddExt(x, t.Ext, t.Id); foreach (var f in t.Fields ?? new()) AddExt(x, f.Ext, $"{t.Id}.{f.Name}"); }

        foreach (var en in m.Entities)
        {
            x.Add(("entity", en.Id));
            AddExt(x, en.Ext, en.Id);
            if (en.Invariants.Count > 0) x.Add(("invariant", en.Id));
            if (en.Invariants.Any(g => g.GuardRef is not null)) x.Add(("guardRef", en.Id));
            foreach (var f in en.Fields)
            {
                if (f.SourceOfTruth is not null) x.Add(("sourceOfTruth", $"{en.Id}.{f.Name}"));
                AddExt(x, f.Ext, $"{en.Id}.{f.Name}");
            }
        }

        foreach (var op in m.Operations)
        {
            x.Add(("operation", op.Id));
            if (op.Roles.Count > 0) x.Add(("roles", op.Id));
            if (op.Scopes.Count > 0) x.Add(("scopes", op.Id));
            if (op.Ownership is not null) x.Add(("ownership", op.Id));
            if (op.Validation.Count > 0) x.Add(("validation", op.Id));
            if (op.Rule.Count > 0) x.Add(("rule", op.Id));
            if (op.Validation.Concat(op.Rule).Any(g => g.GuardRef is not null)) x.Add(("guardRef", op.Id));
            if (op.Abac is not null) x.Add(("permit", op.Id));
            if (op.Note is not null) x.Add(("note", op.Id));
            foreach (var t in op.Throws) x.Add(("throws", $"{op.Id}->{t}"));
            if (op.Idempotent is not null) x.Add(("idempotent", op.Id));
            if (op.Pagination is not null) x.Add(("pagination", op.Id));
            foreach (var ev in op.Emits) x.Add(("emits", $"{op.Id}->{ev}"));
            if (op.Consistency is not null && (op.Consistency.Mode is not null || op.Consistency.Risk == "eventual")) x.Add(("consistency", op.Id));
            foreach (var s in op.Serving) x.Add(("serving", $"{op.Id}:{s.Protocol}"));
            foreach (var p in op.Signature.Params) AddExt(x, p.Ext, $"{op.Id}.{p.Name}");
            AddExt(x, op.Ext, op.Id);
        }
        return x.Distinct().ToList();
    }

    static void AddExt(List<(string, string)> x, List<ExtJson>? ext, string owner)
    {
        if (ext is null) return;
        foreach (var e in ext) x.Add(($"@{e.Ns}.{e.Name}", owner));
    }

    /// <summary>Gate: census'taki her construct build-report'ta kayıtlı mı? Değilse SilentDrop (INV-7).
    /// Yalnız .NET adaptörü için (Go bilinçli kısmi spike — Resolved Q3).</summary>
    public static void Check(ManifestJson m, BuildReport report)
    {
        foreach (var (construct, owner) in Census(m))
            if (!report.Covers(construct, owner))
                report.SilentDrop(construct, owner);
    }
}
