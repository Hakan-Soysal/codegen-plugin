using System.Text.Json;

namespace ConformanceAdapter;

// TEK generic conformance harness (per-construct sınıf YOK). Girdi = SPEC listesi (aileden, T3.3) +
// üretilen app assembly (zaten build edilmiş). Her spec için: AddGenerated DI host (GeneratedApp) →
// arrange → op handler resolve + act (ExecuteAsync) → assert.
//
// A3 değişmezi: ASSERTION SPEC'TEN gelir, adapter'dan DEĞİL. Aşağıda hiçbir yerde beklenen bir
// resultType/code literal'i gömülü değildir — `spec.Assert.ResultType` / `spec.Assert.Code` ile
// gözlenen `ResultShape` karşılaştırılır. Paket assertion'ı fudge edemez (6.3).
public sealed class SpecRunner
{
    // Her spec'i koş → per-spec pass/fail. Gerçek execution + deterministik assert (LLM-judge YOK).
    public async Task<IReadOnlyList<SpecResult>> RunAsync(IEnumerable<Spec> specs, GeneratedApp app)
    {
        var results = new List<SpecResult>();
        foreach (var spec in specs)
            results.Add(await RunOneAsync(spec, app).ConfigureAwait(false));
        return results;
    }

    async Task<SpecResult> RunOneAsync(Spec spec, GeneratedApp app)
    {
        // saga (ve diğer setup-ağır construct'lar) v1'de stub: koşulmaz, kapsam belgelenir (sessiz değil).
        if (spec.Assert.Stub)
            return SpecResult.Skipped(spec, $"stub (v1, ertelenmiş): {spec.Assert.Expected}");

        try
        {
            using var scope = app.CreateScope();
            var handler = app.ResolveHandler(scope, spec.OpId);

            // invariant (property test): rastgele girdi üreteci ile N tur → her persist edilen entity
            // invariant'ı İHLAL ETMEMELİ. Beklenti (field/op/bound) SPEC'ten okunur (gömülü değil).
            if (spec.Construct == "invariant")
                return await RunInvariantPropertyAsync(spec, app, handler).ConfigureAwait(false);

            // ── arrange ── dil-nötr kurulum talimatını yorumla (kind'a göre, construct'a göre değil).
            // duplicate: aynı handler instance'ında ölçülen act'ten ÖNCE bir seed çağrısı yap → seam'in
            // dedup durumu (instance state) korunur (tek scope, statik yok).
            ArrangeKind(spec.Arrange, out var arrangeKind);
            if (arrangeKind == "duplicate")
            {
                var seedReq = app.BuildRequest(handler, spec.Act.With);
                await app.ActAsync(handler, seedReq).ConfigureAwait(false);
            }

            // ── act ── op'u çağır (ExecuteAsync).
            var request = app.BuildRequest(handler, spec.Act.With);
            var resultObj = await app.ActAsync(handler, request).ConfigureAwait(false);

            // ── assert ── gözlenen Result<T>'yi spec'in beklentisiyle karşılaştır.
            var shape = GeneratedApp.Inspect(resultObj);
            return AssertAgainstSpec(spec, shape);
        }
        catch (Exception ex)
        {
            // NotImplementedException (boş seam) dahil her execution hatası = FAIL (gizlenemez).
            var inner = ex.InnerException ?? ex;
            return SpecResult.Fail(spec, $"execution exception: {inner.GetType().Name}: {inner.Message}");
        }
    }

    // ASSERTION BURADA — ama beklenen değerler SPEC'TEN okunur (gömülü literal yok).
    static SpecResult AssertAgainstSpec(Spec spec, ResultShape observed)
    {
        var expectedType = spec.Assert.ResultType;
        if (expectedType is null)
            return SpecResult.Fail(spec, "spec.assert.resultType yok — koşulamaz (stub değil)");

        if (!string.Equals(observed.ResultType, expectedType, StringComparison.Ordinal))
            return SpecResult.Fail(spec,
                $"resultType beklenen='{expectedType}' (spec), gözlenen='{observed.ResultType}'");

        // code (throws): spec code taşıyorsa eşleşmeli (NotProcessable<T>.Code).
        if (spec.Assert.Code is { } expectedCode)
        {
            if (!string.Equals(observed.Code, expectedCode, StringComparison.Ordinal))
                return SpecResult.Fail(spec,
                    $"code beklenen='{expectedCode}' (spec), gözlenen='{observed.Code ?? "<null>"}'");
        }

        return SpecResult.Pass(spec,
            $"resultType='{observed.ResultType}'{(observed.Code is { } c ? $", code='{c}'" : "")}");
    }

    // invariant property koşumu: RandomDecimals üreteci ile N tur. Her tur: rastgele (geçerli) girdi →
    // op çağrılır → persist edilen entity'nin invariant alanı (field) spec'in op/bound'ına UYMALI.
    // İlk ihlal = FAIL (karşı-örnek). Beklenti SPEC'ten (assert.field/op/bound) — adapter'a gömülü DEĞİL.
    async Task<SpecResult> RunInvariantPropertyAsync(Spec spec, GeneratedApp app, object handler)
    {
        var field = spec.Assert.Field;
        var op = spec.Assert.Op;
        var bound = spec.Assert.Bound;
        if (field is null || op is null || bound is null)
            return SpecResult.Skipped(spec,
                "invariant assert eksik (field/op/bound) — property koşulamadı (deferred-with-coverage)");

        const int rounds = 50;
        // act.with'teki sayısal alanı rastgele (geçerli aralıkta) değiştirerek girdi türet.
        var numericKey = FirstNumericKey(spec.Act.With);
        var samples = RandomDecimals(rounds, 0m, 1_000m, seed: 20260625).ToList();

        for (var i = 0; i < samples.Count; i++)
        {
            using var scope = app.CreateScope();
            var h = app.ResolveHandler(scope, spec.OpId);
            var with = ReplaceNumeric(spec.Act.With, numericKey, samples[i]);
            var request = app.BuildRequest(h, with);
            var resultObj = await app.ActAsync(h, request).ConfigureAwait(false);

            if (!GeneratedApp.TryGetSuccessFieldDecimal(resultObj, field, out var persisted))
                continue;   // bu tur Success/field üretmedi → invariant gözlemlenecek bir şey yok.

            if (!SatisfiesPredicate(persisted, op, bound.Value))
                return SpecResult.Fail(spec,
                    $"invariant ihlali: persist '{field}'={persisted} '{op}' {bound} sağlamadı (karşı-örnek tur #{i})");
        }
        return SpecResult.Pass(spec, $"{rounds} property turunda '{field}' '{op}' {bound} invariant'ı korundu");
    }

    static bool SatisfiesPredicate(decimal lhs, string op, decimal rhs) => op switch
    {
        ">=" => lhs >= rhs,
        ">" => lhs > rhs,
        "<=" => lhs <= rhs,
        "<" => lhs < rhs,
        "==" or "=" => lhs == rhs,
        _ => throw new InvalidOperationException($"desteklenmeyen invariant op: {op}")
    };

    static string? FirstNumericKey(JsonElement with)
    {
        if (with.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in with.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number) return p.Name;
        return null;
    }

    static JsonElement ReplaceNumeric(JsonElement with, string? key, decimal value)
    {
        if (key is null) return with;
        var dict = new Dictionary<string, object?>();
        foreach (var p in with.EnumerateObject())
            dict[p.Name] = p.Name == key ? value : (object?)p.Value.GetRawText();
        // basit yeniden-serileştirme: sayısal alanı yeni değerle, diğerlerini ham metinden.
        var sb = new System.Text.StringBuilder("{");
        var first = true;
        foreach (var p in with.EnumerateObject())
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(p.Name).Append("\":");
            sb.Append(p.Name == key ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : p.Value.GetRawText());
        }
        sb.Append('}');
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }

    static void ArrangeKind(JsonElement arrange, out string? kind)
    {
        kind = null;
        if (arrange.ValueKind == JsonValueKind.Object
            && arrange.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
            kind = k.GetString();
    }

    // ── Step 5.2: property-test rastgele girdi üreteci (invariant construct'ları için) ──
    // invariant spec'i için rastgele girdiler üretir → op çağrılır → persist edilen entity invariant'ı
    // ihlal ETMEMELİ. Üreteç deterministik tohumlanabilir (tekrarlanabilir property koşumu).
    public static IEnumerable<decimal> RandomDecimals(int count, decimal min, decimal max, int seed = 12345)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
            yield return min + (decimal)rng.NextDouble() * (max - min);
    }
}

// Per-spec koşum sonucu. Status: Pass | Fail | Skipped(stub). Detail = insan-okunur kanıt.
public sealed record SpecResult(Spec Spec, SpecStatus Status, string Detail)
{
    public static SpecResult Pass(Spec s, string detail) => new(s, SpecStatus.Pass, detail);
    public static SpecResult Fail(Spec s, string detail) => new(s, SpecStatus.Fail, detail);
    public static SpecResult Skipped(Spec s, string detail) => new(s, SpecStatus.Skipped, detail);

    public bool IsPass => Status == SpecStatus.Pass;
    public bool IsFail => Status == SpecStatus.Fail;
    public override string ToString() => $"[{Status}] {Spec.Construct}/{Spec.OpId}: {Detail}";
}

public enum SpecStatus { Pass, Fail, Skipped }
