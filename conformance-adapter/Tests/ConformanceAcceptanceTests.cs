namespace ConformanceAdapter.Tests;

// §6 acceptance: SpecRunner'ın gerçekten KOŞTUĞUNU ve assertion'ın SPEC'TE olduğunu (paket
// gizleyemez) gerçek execution + gözlem ile doğrular. LLM-judge YOK; tek generic runner.
//
// SPEC'ler aileden (T3.3) gelir — burada CreateInvoice throws + validation spec'lerinin AYNEN
// JSON metni kullanılır. Adapter onları ÜRETMEZ, yalnızca tüketir.
public sealed class ConformanceAcceptanceTests
{
    // T3.3 §6.2 Spec 1 — throws(DuplicateInvoice) → NotProcessable. (manifest.json türevli)
    const string ThrowsSpec = """
    {
      "construct": "throws",
      "opId": "CreateInvoice",
      "arrange": { "kind": "duplicate", "on": "CreateInvoice", "key": ["customerId"] },
      "act": { "call": "CreateInvoice", "with": { "customerId": "c-1", "amount": 100 } },
      "assert": {
        "resultType": "NotProcessable",
        "code": "DuplicateInvoice",
        "source": "manifest.json#errors[id=DuplicateInvoice].resultType=NotProcessable + operation[CreateInvoice].throws[0]"
      }
    }
    """;

    // T3.3 §6.2 Spec 2 — validation(amount > 0) → NotValid. (manifest.json türevli)
    const string ValidationSpec = """
    {
      "construct": "validation",
      "opId": "CreateInvoice",
      "arrange": {},
      "act": { "call": "CreateInvoice", "with": { "customerId": "c-1", "amount": 0 } },
      "assert": {
        "resultType": "NotValid",
        "violated": "amount > 0",
        "source": "manifest.json#operation[CreateInvoice].validation[0].ast (cmp > amount 0) → sınır-dışı amount=0"
      }
    }
    """;

    // ── DOĞRU seam (throwaway): dup → DuplicateInvoice (NotProcessable), amount<=0 → NotValid, else Success ──
    const string CorrectSeam = """
            if (request.Amount <= 0)
                return Task.FromResult<Result<Invoice>>(
                    new NotValid<Invoice>(new Dictionary<string, string> { ["amount"] = "amount > 0" }));
            if (!_seen.Add(request.CustomerId))
                return Task.FromResult(DuplicateInvoice($"customer {request.CustomerId} zaten faturalı"));
            return Task.FromResult<Result<Invoice>>(
                new Success<Invoice>(new Invoice { Id = "inv-1", CustomerId = request.CustomerId, Amount = request.Amount }));
    """;

    // ── YANLIŞ seam (throwaway): dup → generic ServerError (DuplicateInvoice DEĞİL). throws spec FAIL olmalı ──
    const string WrongSeam = """
            if (request.Amount <= 0)
                return Task.FromResult<Result<Invoice>>(
                    new NotValid<Invoice>(new Dictionary<string, string> { ["amount"] = "amount > 0" }));
            if (!_seen.Add(request.CustomerId))
                return Task.FromResult<Result<Invoice>>(new ServerError<Invoice>("generic hata"));
            return Task.FromResult<Result<Invoice>>(
                new Success<Invoice>(new Invoice { Id = "inv-1", CustomerId = request.CustomerId, Amount = request.Amount }));
    """;

    // T3.3 — invariant(Invoice.amount >= 0) → property test. (manifest.json#entities[Invoice].invariants[0])
    const string InvariantSpec = """
    {
      "construct": "invariant",
      "opId": "CreateInvoice",
      "arrange": { "kind": "property" },
      "act": { "call": "CreateInvoice", "with": { "customerId": "c-1", "amount": 1 } },
      "assert": {
        "field": "amount", "op": ">=", "bound": 0,
        "source": "manifest.json#entities[Invoice].invariants[0].ast (cmp >= amount 0)"
      }
    }
    """;

    static IReadOnlyList<Spec> CreateInvoiceSpecs() =>
        SpecJson.ParseMany(new[] { ThrowsSpec, ValidationSpec });

    // §6.2 — doldurulmuş (doğru) seam → TÜM CreateInvoice spec'leri PASS.
    [Fact]
    public async Task Filled_correct_seam_all_specs_pass()
    {
        var dir = AppFixture.TempDir();
        try
        {
            AppFixture.Emit(dir);
            AppFixture.FillCreateInvoiceSeam(dir, CorrectSeam);
            var dll = AppFixture.Build(dir);

            using var app = GeneratedApp.Load(dll);
            var results = await new SpecRunner().RunAsync(CreateInvoiceSpecs(), app);

            Assert.All(results, r => Assert.True(r.IsPass,
                "Beklenen PASS ama: " + r));
            Assert.Equal(2, results.Count(r => r.IsPass));
        }
        finally { TryDelete(dir); }
    }

    // §6.3 — kasıtlı YANLIŞ seam (DuplicateInvoice yerine ServerError) → throws spec FAIL.
    // Assertion spec'te olduğundan paket onu gizleyemez. (A3 kanıtı)
    [Fact]
    public async Task Wrong_seam_throws_spec_fails()
    {
        var dir = AppFixture.TempDir();
        try
        {
            AppFixture.Emit(dir);
            AppFixture.FillCreateInvoiceSeam(dir, WrongSeam);
            var dll = AppFixture.Build(dir);

            using var app = GeneratedApp.Load(dll);
            var results = await new SpecRunner().RunAsync(CreateInvoiceSpecs(), app);

            var throwsResult = results.Single(r => r.Spec.Construct == "throws");
            Assert.True(throwsResult.IsFail,
                "throws spec FAIL olmalıydı (yanlış seam ServerError döndürdü) ama: " + throwsResult);
            // Kanıt: FAIL nedeni spec'in beklediği resultType ile gözlenenin uyuşmaması.
            Assert.Contains("NotProcessable", throwsResult.Detail);
            Assert.Contains("ServerError", throwsResult.Detail);

            // validation spec'i hâlâ PASS (yanlış-seam yalnız dup kolunu bozdu) — runner seçici.
            var validationResult = results.Single(r => r.Spec.Construct == "validation");
            Assert.True(validationResult.IsPass, "validation PASS olmalıydı: " + validationResult);
        }
        finally { TryDelete(dir); }
    }

    // Step 5.2 — invariant property test: RandomDecimals üreteci ile N tur, persist edilen amount >= 0.
    // Doğru seam amount'u echo eder → invariant korunur → PASS. (üreteç gerçekten koşulur)
    [Fact]
    public async Task Invariant_property_generator_runs_and_holds()
    {
        var dir = AppFixture.TempDir();
        try
        {
            AppFixture.Emit(dir);
            AppFixture.FillCreateInvoiceSeam(dir, CorrectSeam);
            var dll = AppFixture.Build(dir);

            using var app = GeneratedApp.Load(dll);
            var results = await new SpecRunner().RunAsync(SpecJson.ParseMany(new[] { InvariantSpec }), app);

            var inv = results.Single();
            Assert.True(inv.IsPass, "invariant property PASS olmalıydı: " + inv);
            Assert.Contains("property tur", inv.Detail);   // üreteç gerçekten N tur koştu
        }
        finally { TryDelete(dir); }
    }

    static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* best-effort temizlik */ }
    }
}
