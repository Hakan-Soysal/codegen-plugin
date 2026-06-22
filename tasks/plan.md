# Implementation Plan: Tech DSL → Kod Üreteci (.NET-first, 4-dil hazır seam)

## Overview
`manifest.json` (+`operations.json`) → derlenen, gövdesi-`NotImplemented` .NET uygulaması
üreten deterministik araç. Paylaşılan nötr çekirdek (load/join/GM) + ince .NET emitter.
Go ikinci sırada **seam doğrulama spike'ı** olarak; structure ancak ondan sonra "stabil"
ilan edilir. Üreteç .NET 10/C# ile yazılır.

## Decisions locked
1. **Üretilen app şekli:** Minimal API + düz CQRS handler + EF Core + küçük `Result<T>`. Framework icat YOK. ✓
2. **Go spike Phase 5'te** (erken seam doğrulama gate'i — .NET long-tail'den ÖNCE). ✓
3. **Stub ayrımı:** `partial class` → `{X}.g.cs` (üreteç sahibi, her zaman ezilir) + `{X}.Logic.cs` (insan/LLM sahibi, **asla ezilmez**, yoksa-üret). Drift engine YOK. ✓

## Architecture Decisions
- GM = in-memory C# record'lar, yayınlanmış kontrat değil. `generation-model.schema.json` Phase 6'ya ertelendi (Go = rule-of-three anı).
- Tek hedef, tek transport: REST/Minimal API. Protocol-binding pluggability YOK.
- Input kontrat tek sürüm (One-Version Rule): 4 dil de aynı `manifest.json`'u okur; dile-özel varyant yasak.
- Determinizm: tüm koleksiyonlar emit öncesi `id`'ye göre sıralı. **build-report.json** tutulur (no-silent-loss / INV-7).
- `ponytail:` silinen kapsam — drift engine, protocol pluggability, GM JSON Schema (ertelendi), portable conformance suite.

## Task List

### Phase 1: Foundation (nötr çekirdek — dil-bağımsız)
- Task 1 — Solution + manifest fixture · S · Dep: None
- Task 2 — POCO'lar + ExprNode polimorfik converter · M · Dep: 1
- Task 3 — Load + realizes-join + GM kurulumu (yalnız operations) · M · Dep: 2
- Task 4 — build-report iskeleti · S · Dep: 3

**Checkpoint: Foundation** — tests pass, `dotnet build` temiz, GM bir gerçek manifest'ten kuruluyor, build-report yazılıyor.

### Phase 2: .NET walking skeleton (uçtan uca DERLENEN çıktı — en yüksek risk)
- Task 5a — Operation → `{Name}Command/Query` record + `{Name}Handler.g.cs` + `{Name}Handler.Logic.cs` (yoksa-üret) · M · Dep: 4
- Task 5b — exposed serving → Minimal API endpoint; `access` write-sınıfı → command/query türevi · M · Dep: 5a
- Task 6 — `Result<T>` 6'lı taksonomi (C# discriminated union; NotAuthenticated ayrı kalır) · S · Dep: 5b

**Checkpoint: Walking skeleton** — üretilen .NET app gerçekten derleniyor; stub ayrı dosyada, regen korunuyor; aynı girdi → byte-aynı `.g.cs`.

### Phase 3: ExprNode → C# predicate (tek non-trivial logic)
- Task 7 — ExprNode → C# expression emitter (and/or/cmp/arith/agg/call/path/literal; actor.*→claim, resource.*→request/entity; unknown→UnsupportedConstruct) · M · Dep: 6
- Task 8 — validation→NotValid / rule→NotProcessable / permit→authz / invariant→entity check (hepsi Task 7 üstüne, stub değil) · S · Dep: 7

### Phase 4: .NET core construct'ları (Go'yu stres edecek minimum gerçek-uygulama)
- Task 9 — entity/field/sourceOfTruth/concurrency → EF entity · M · Dep: 7
- Task 10 — type/enum → record/enum · S · Dep: 6
- Task 11 — event/emits/on → record + publish/consume stub + outbox-konvansiyonu · M · Dep: 6
- Task 12 — auth (roles/ownership/scopes) → attribute/guard iskeleti · S · Dep: 8

**Checkpoint: .NET core (Go spike'a HAZIR)** — gerçek bir modül (op+entity+event+auth+expr) uçtan uca derleniyor; build-report tüm core construct'ları `realized`.

### Phase 5: Go spike — SEAM DOĞRULAMA GATE
- Task 13 — Go emitter, yalnız Phase 2–4 construct'ları, AYNI GM'den (`go build` exit 0; result-type→`(T,error)`; ExprNode→Go; actor extends→composition) · L→dilimle · Dep: 12
- Task 14 — Seam bulguları raporu (GM'de neyi değiştirmek gerekti → Phase 6 girdisi) · S · Dep: 13

**Checkpoint: SEAM GATE** — .NET ve Go aynı GM'den derleniyor; dil-sızıntıları tespit edildi; **ancak şimdi structure 'stabil'.**

### Phase 6: GM sertleştirme + kontrat sabitleme (rule-of-three anı)
- Task 15 — GM'i Go bulgularına göre düzelt · M · Dep: 14
- Task 16 — `generation-model.schema.json` yaz (artık 2 gerçek tüketici var) · S · Dep: 15

### Phase 7: .NET long-tail (seam kanıtlandıktan sonra, düşük risk)
- Task 17 — pagination (keyset/offset sorgu iskeleti) · S
- Task 18 — calls + compensate → saga iskeleti (`ponytail:` orchestration-state in-memory, gerekirse swap) · M
- Task 19 — idempotent → param-keyed dedup konvansiyonu · S
- Task 20 — passthrough prelude'lar (@http/@trigger/@crypto/@audit/@sensitivity/@metric → attribute/hosted-service-stub/EF-converter/comment) · M
- Task 21 — external/uncharted → çağrı-adapter stub + caller-side validation · S

**Checkpoint: Complete** — §6.4'ün her satırı `realized` veya `unsupported` (sessiz düşürme yok); 4-dil seam'i Go ile doğrulanmış; Java/Node artık tekrarlanan emitter işi.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|---|---|---|
| Seam'i .NET'e göre stabilize edip Go'da kırma | High | Go spike Phase 5 = açık gate; .NET long-tail Phase 7'de (spike'tan SONRA) |
| ExprNode→dil predicate derlenmiyor | High | Task 7 erken + zorunlu test; unknown-node→unsupported |
| Üretilen kod derlenmiyor ("kağıt üstünde çalışıyor") | High | Her emit task'ı `dotnet build`/`go build` exit-0 ile doğrulanır |
| GM'i çok erken JSON Schema'ya bağlama | Med | Schema Phase 6'ya ertelendi |

## Verification convention (her emit task'ı)
- Golden-file eşleşir · emit edilen proje `dotnet build`/`go build` exit 0 · regen `.Logic.cs`'i ezmez · build-report'ta `realized`.
- Non-trivial logic (ExprNode emitter) → çalışan assert testi bırakır.
