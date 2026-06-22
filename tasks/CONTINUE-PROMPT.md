# Devam Promptu — .NET üretecini DSL'e %100 uyumlu hale getir

> Bu dosya `/clear` sonrası yapıştırılmak için. Kendi-yeterli; bağlam sıfır varsayar.

## Misyon
**Tech DSL kod üretecinin .NET adaptörünü grammar'a %100 uyumlu hale getir.** Tanım:
`Completeness` gate'i, grammar'ın HER keyword'ünü kullanan bir fixture'a karşı **SIFIR
SilentDrop** üretmeli. Her construct ya gerçek artefakt olarak emit edilmeli (COVERED),
ya açıkça `UnsupportedConstruct` raporlanmalı, ya da doğru biçimde build-time-only (census'tan muaf).

## Önce oku (bu sırayla)
1. `tasks/fix-plan.md` + `tasks/fix-todo.md` — fix planı (Phase A bitti; B/C/D/E kaldı) ve todo.
2. `/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CommandDSL/tech-dsl.langium` + `shared.langium` — **grammar = "%100"ün tanımı**. Her keyword karşılanmalı.
3. `/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CommandDSL/src/tech/manifest.ts` — grammar→manifest.json eşlemesi (her construct ne emit ediyor; otoriter).
4. `src/Gen.Core/Report/Completeness.cs` — **census = §6.4 sözleşmesi** (kod-karşılığı). "%100" bunu geçmek demek.
5. `/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CommandDSL/docs/technical/uretec-dil-bagimsiz-spec.md` (§6.4 tablo, §8 politikalar) — gerekirse.

## Mevcut durum (ne hazır)
- Proje: `/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1` (git repo). Üreteç **C#/.NET 10**.
  - `src/Gen.Core` (load+join+GM+build-report+**Completeness gate**), `src/Gen.Dotnet` (.NET emitter, BİRİNCİL), `src/Gen.Go` (bilinçli kısmi spike), `src/Gen.Cli` (çalıştırılabilir), `tests/Gen.Tests`.
- Phases 1–7 + Fix-Phase-A bitti. **39 test yeşil.** İki gerçek compile gate: emit→`dotnet build` (ve `go build`) exit 0.
- Çalıştır: `dotnet test Gen.slnx` ; üret+gate: `dotnet run --project src/Gen.Cli -- tests/fixtures/manifest.json out` (silentDrops'u basar).
- **Completeness gate** INV-7'yi mekanik zorluyor: census'taki construct build-report'ta yoksa `SilentDrop` → `Clean=false`.
- **Ratchet test**: `tests/Gen.Tests/CompletenessTests.cs` içinde `KnownDebt` allowlist'i. KURAL: her fix bu setten bir madde **çıkar** (asla ekleme); YENİ drop testi kırar. Set boşalınca = gate tam yeşil.

## Çözülmüş kararlar (uy)
- `for guard`/guardRef → predicate'e **yorum** olarak emit (COVERED, düşürme yok).
- `@http`/`@trigger` → **minimal** hedef-stub + §8 policy (tam framework değil; ponytail).
- **Go ertelendi** — gate yalnız .NET. Go kısmi spike kalır; Go=MISSING beklenen.
- Üretilen app şekli: Minimal API + düz CQRS + EF Core + `Result<T>`; stub'lar ayrı `.Logic.cs` (yoksa-üret). Framework icat YOK. ponytail: fix'ler minimal (stub+policy+report).

## Kalan iş (bağımsız denetimden, gate'in RED listesi)
**Mevcut fixture borcu (KnownDebt'te):** `deployable/BillingService`, `error/DuplicateInvoice`,
`note/CreateInvoice`, `throws/CreateInvoice->DuplicateInvoice`, `serving/{3 op}:rest`.
**Latent (fixture'da yok → full-fixture'da çıkacak):** `consistency{risk,mode}`, `uncharted`,
`sourceOfTruth`, serving `@grpc`/`@queue`, external BoundaryOp `serving`+`validation` AST (INV-4),
`on`/subscriptions consumer-wiring, `@http`/`@trigger` hedef-handling, param/composite-field/event-payload/module/type/deployable/entity **ext** site'ları.

Plan fazları (fix-plan.md): **B** Sınıf-1 (mevcut borcu yeşile) → **C** latent POCO/uncharted/sourceOfTruth/guardRef → **D** grpc-queue/external-validation/subscriptions/@http-@trigger → **E** full-keyword fixture + gate tam yeşil.

## Çalışma yöntemi
- agent-skills `/build` (incremental-implementation + TDD). Tek tek task; her task: fix → `dotnet test Gen.slnx` (39+ yeşil, emit app derlenir) → KnownDebt'ten ilgili maddeyi çıkar → commit.
- Her construct fix'i: emitter gerçek artefakt üretir VEYA `report.Realized(...)`/`report.Unsupported(...)` çağırır (census ile aynı `(construct, owner)` adıyla — Completeness.cs'e bak). §8 kararları `report.Policy(...)` ile.
- **Validate, don't trust**: emit edilen app gerçekten `dotnet build` geçmeli (Emitted_app_compiles testi zaten yapıyor). Kağıt üstü "çalışıyor" yasak.
- Commit mesajı sonu: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. UYARI: `git commit -m` içinde **backtick kullanma** (shell substitution tetikler).

## Definition of Done (%100)
1. `tests/fixtures/manifest.json` grammar'ın **HER keyword'ünü** kullanır (standalone hariç tüm clause'lar, uncharted, grpc/queue serving, sourceOfTruth, tüm ext-site'lar, subscriptions, calls+compensate, vb.).
2. `Completeness` gate o fixture'a karşı **0 SilentDrop** (`KnownDebt` boş).
3. Tüm testler yeşil; emit edilen .NET app `dotnet build` exit 0; determinizm korunur.
4. Her grammar keyword: COVERED (artefakt) | açık UnsupportedConstruct (raporlu) | doğru N/A(build-time). Sessiz düşürme YOK.

## Başla
`/build` (tek task) ya da `/build auto` (tek onayla tümü). İlk task: **fix-plan B1 — throws + named-error catalog**.
