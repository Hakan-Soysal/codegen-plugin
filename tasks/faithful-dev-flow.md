# Faithful Development Flow — dökümanlara TAM uygun app üretimi

> Amaç: İş+teknik DSL dökümanlarından, **tam dökümanlara-bağlı** (ilk-taslak DEĞİL) derlenen
> bir .NET uygulaması üretmenin uçtan-uca, tekrarlanabilir akışı. Bu doküman doğrudan **skill**'e
> dönüştürülecek şekilde yazıldı: tetikleyiciler, fazlar, per-op tarifler, doğrulama kapıları,
> karar noktaları, anti-pattern'ler.
>
> Bağlam: bu akış, mevcut `techgen-sync` skill'ini (yalnız generate+sync+reconcile) **iş-mantığı
> doldurmayı dökümanlara bağlı yaparak** genişletir. Mekanik kısım üreteçte; muhakeme bu akışta.

---

## 0. Girdi dökümanları — hangisi NEYİ otoriter tanımlar

| Döküman | Otoriter tanımladığı | Hangi fazda tüketilir |
|---|---|---|
| `*.cdsl` (CommandDSL) | İş analizi kaynağı → `operations.json`'a derlenir | Faz 0 (derleme) |
| `*.operations.json` (contract) | operations (signature/**effects**/guards), entities, actors, **flows**, **processes**, relations | Faz 2 (effect+rule) |
| `*.tcdsl` (TechDsl) | modüller, externals, concurrency, idempotent, pagination, events, ABAC, guard-AST → `manifest.json`'a derlenir | Faz 0 (derleme) |
| `manifest.json` (techgen girdisi) | teknik model: modules/entities/types/events/subscriptions/externals/callEdges/deployables/uncharted + op başına emits/idempotent/pagination/guards/auth | Faz 1 (generate) |
| `*-is-analizi.md` (iş analizi) | **§6 "İşlemler (kural özeti)"** (op başına iş kuralı), §4 Süreçler, §5 Akışlar, **Ek: Dashboard Metrik Eşlemesi** (rapor→metrik) | Faz 2 (her op'un niyeti) |

**Kural:** her implementasyon kararı bir dökümana izlenebilir olmalı. Döküman söylemiyorsa → **uydurma, kullanıcıya sor** (anti-pattern: sessiz varsayım).

---

## 1. Faz 0 — Ön koşullar & üreteç hazırlığı

1. **manifest.json var mı?** Yoksa (`cikti/`'de sadece `.tcdsl` varsa) `.tcdsl → manifest.json` derle (CommandDSL `emitManifest`; working-dir dışı → izin gerekir). manifest, contract'ı relative referansladığından (`contract './x.operations.json'`) **operations.json ile aynı dizine** yazılmalı.
2. **Üreteç sağlığı:** techgen suite yeşil + (ideal) gerçek-model compile-gate fixture'ı yeşil.
3. **Tool-gap kapısı (KRİTİK ders):** üreteci scratch dizine çalıştır → `dotnet build`. Derleme **tip/emit açığı** yüzünden kırılırsa (seam değil) bu bir **TOOL açığıdır** → `gen/`'i değil **üreteci** düzelt, gerçek-model fixture ekle, doğrula. (Bkz. memory `techgen-real-model-gaps`: `clean=True`/INV-7 "derleniyor" demek DEĞİL.)

**Çıkış kriteri:** boş/`NotImplemented` iskelet ile üretilen app `dotnet build exit 0`.

---

## 2. Faz 1 — İskelet üretimi (mekanik; techgen-sync)

`techgen <manifest.json> <targetDir>` →
- `gen/` (üreteç-sahibi, prune'lu): module-shared `.g.cs` (`gen/{M}/` kökü: Types/Entities/Errors/Events/Wiring) + op başına feature-slice `gen/{M}/{Op}/` (request, `.Endpoint.g.cs`=Add{Op}/Map{Op}, Guards/Auth/…) + Bootstrap/GlobalUsings/Generated.props
- `src/{Module}/{Op}/{Op}Handler.Logic.cs` (HumanSeam, yoksa-üret; feature-slice klasörü): doldurulacak gövdeler
- `Program.cs`/`App.csproj` (HumanShell)
- `provenance.json` + `build-report.json`

**Kapı:** `build-report` 0 SilentDrop **VE** `dotnet build exit 0` — doldurmaya başlamadan önce.

---

## 3. Faz 2 — Op başına DÖKÜMANA-BAĞLI doldurma (akışın kalbi)

Her op için iş gövdesi, **o op'un TÜM generated artefaktlarını + döküman kuralını** tüketmeli — yalnız CRUD değil. Artefakt → sorumluluk haritası:

| Generated artefakt / döküman | İçerik | Faithful gövde ne yapmalı |
|---|---|---|
| `gen/{M}/{Op}/{Op}.g.cs` | request record + dönüş tipi | imzayı birebir uygula |
| contract `operations[].effects` | create/update/delete/calculate/send + target | **etkiyi uygula** (hangi entity/alan) |
| `*-is-analizi.md` §6 (op id ile) | iş kuralı (insan dili) | niyeti belirle (hangi entity, hangi geçiş) |
| `gen/{M}/{Op}/{Op}.Guards.g.cs` | `static bool Rule_N({Op}RuleNInput)` + input record | **input'u kur + Rule_N çağır**; false → aşağıdaki Throws/NotValid |
| `gen/{M}/{Op}/{Op}.Throws.g.cs` | `static Result<T> {Error}(msg)` fabrikaları + `ThrowableErrors[]` | rule ihlalinde **ilgili fabrikayı döndür** |
| `gen/{M}/{Op}/{Op}.Auth.g.cs` | `RequiredRoles/RequiredScopes/Ownership` | rol/scope/ownership kontrolü (claim/row-level) |
| `gen/{M}/{Op}/{Op}.Page.g.cs` | `PaginationStrategy` (cursor/offset) + `DefaultPageSize` | **gerçek pagination** (cursor encode/decode), `null` değil |
| `gen/{M}/{Op}/{Op}.Idem.g.cs` + `IIdempotencyStore` | `IdempotencyKeys` | idempotent op'ta **dedup kontrolü** |
| manifest `op.emits` | yayılan event'ler | SaveChanges sonrası **`IEventBus` ile publish** |
| `gen/{M}/{e}.Invariants.g.cs` | entity invariant predicate'ları | mutasyon öncesi/sonrası **invariant doğrula** |
| Ek: Dashboard Metrik Eşlemesi | rapor → metrik tanımı | rapor op'larında **agregasyon** (ham liste değil) |

### Faithful gövde iskeleti (genel)
```csharp
public partial async Task<Result<T>> ExecuteAsync(XCommand request, CancellationToken ct)
{
    // 1) AUTH: RequiredRoles/Scopes/Ownership (claim/row-level) — başarısız → NotAuthorized/NotAuthenticated
    // 2) IDEMPOTENCY (varsa): IIdempotencyStore ile key kontrol → varsa önceki sonucu döndür
    // 3) LOAD (state-change/query): entity'yi yükle; yoksa NotProcessable("not_found", ...)
    // 4) GUARDS: {Op}RuleNInput kur → Rule_N(input) çağır;
    //    validation false → NotValid(errors); rule false → {Op}.{Error}(msg) (Throws fabrikası)
    // 5) EFFECT: contract effects'e göre create/update/delete + "calculate" alan set + INVARIANT doğrula
    // 6) SaveChangesAsync(ct)
    // 7) EVENTS: op.emits → _eventBus.Publish(...) (outbox)
    // 8) IDEMPOTENCY store + return Success<T>(...)
}
```

### Arketip tarifleri (faithful)
- **Create komut** (`Result<string>`): auth → guards → effect=create(entity, contract'tan alan eşleme + invariant) → SaveChanges → emits → Success(id).
- **State-change komut** (`Result<Unit>`): auth → load(404) → guards (ör. `Status=="planli"`) → effect=update(alan/status, contract calculate/send) → invariant → SaveChanges → emits → Success(Unit).
- **Query** (`Result<Page<E>>`): auth → **gerçek keyset/offset** (`Page.g.cs` strategy) + filtre (actor ownership: "kendi" raporları MemberId/InstructorId ile) → Success(Page, nextCursor).
- **Report** (`Result<Page<E>>` / DTO): **Dashboard Metrik Eşlemesi**'ne göre agregasyon (GroupBy/Count/Sum) → Success.
- **Event-consumer / trigger / notification**: subscription/trigger sözleşmesine göre; external client (PushService) çağrısı varsa Boundary client üzerinden.

### DI ctor (Logic.cs'te insan kurar)
Op'un ihtiyacına göre: `AppDbContext` + (emits varsa) `IEventBus` + (idempotent varsa) `IIdempotencyStore` + (callEdge/external varsa) `I{External}` client. Hepsi DI'da kayıtlı (Bootstrap).

**Belirsizlik:** §6 kuralı bir alanı/geçişi netleştirmiyorsa → **kullanıcıya sor**, default uydurma.

---

## 4. Faz 3 — Doğrulama kapıları (zorunlu, katmanlı)

1. **Compile gate (otoriter):** `dotnet build exit 0` — **orkestratör çalıştırır, agent self-report'una GÜVENME.**
2. **Coverage gate:** `grep -r NotImplementedException src/` = 0 (doldurulmayan kalmadı).
3. **Faithfulness lint'leri** (dökümana-bağlılık kanıtı):
   - guard'lı her op `Rule_N`/`Validation_N` çağırıyor mu (gen guard tipi Logic'te referanslı mı)?
   - `emits` olan her op `IEventBus` kullanıyor mu?
   - `Page.g.cs` olan her op cursor kullanıyor mu (`null` literal değil)?
   - rapor op'ları agregasyon içeriyor mu (düz `ToListAsync` değil)?
   - idempotent op `IIdempotencyStore` kullanıyor mu?
4. **Davranış gate (opsiyonel ama önerilir):** app'i çalıştır + birkaç endpoint smoke (InMemory/SQLite provider seam'i ile).

Herhangi biri kırmızı → **döngüden çıkma**, ilgili op'u düzelt (CLAUDE.md: yarım bırakma).

---

## 5. Faz 4 — Reconcile & re-sync (brownfield; manifest evrildiğinde)

techgen-sync sözleşmesi: re-run techgen (prune + Logic.cs koru) → stale seam (signature değişimi) uzlaştır → orphan Logic.cs sil/taşı (onayla). Faithful katman: değişen op'un guard/effect/emit setini yeniden gözden geçir.

---

## 6. Rol & model ataması (skill için)

- **Mekanik (deterministik) → script/CLI:** techgen run, build-gate, provenance/orphan tespiti, faithfulness lint'leri.
- **Muhakeme (per-op faithful fill) → LLM agent:** guard/effect/event/invariant wiring + §6 semantiği **doğruluk** gerektirir → **sonnet/opus**. haiku yalnız en mekanik arketiplerde (basit list/report) ve **sıkı şablonla**. (Bu session'ın dersi: sadelik bir brief/model trade-off'uydu; FAITHFUL için zengin brief + güçlü model.)
- **Paralellik:** per-op Logic.cs bağımsız → fan-out; sonra **otoriter build-gate**.
- **Brief zorunluları:** her agent'a op'un TÜM artefakt yollarını + §6 kuralını ver; "guard'ı çağır, event'i publish et, cursor kullan, raporu agregle" açıkça iste.

---

## 7. Karar noktaları (skill yüzeye çıkarmalı)

- `manifest.json` yok → `.tcdsl`'den derle (CommandDSL erişimi/izni) ya da sor.
- tip/scalar/typed-id modelleme açığı → **tool fix** kararı (bkz. memory `techgen-real-model-gaps`).
- §6 kuralı belirsiz / döküman çelişkili → **kullanıcıya sor**, uydurma.
- orphan Logic.cs sil (geri-alınamaz) → **onay iste**.

---

## 8. Anti-pattern'ler (bu session'ın dürüst review'undan)

- Guard/event/pagination/agregasyonu **sessizce kesme** → o "faithful" değil, taslaktır. Hız için kesiyorsan **söyle + onay al**.
- Agent'ın "build başarılı" raporuna **güvenme** → otoriter gate'i kendin çalıştır.
- `gen/`'i elle düzenleme → tool'u veya dökümanı düzelt.
- `build-report` yeşil = "derleniyor/faithful" sanma (INV-7 ≠ compile ≠ doğru iş kuralı).
- Dökümana izlenemeyen implementasyon ekleme.

---

## 9. Tek cümle

**Döküman → manifest → iskelet (mekanik, üreteç) → op-başı faithful fill (muhakeme, güçlü model, TÜM artefakt+§6 tüketilir) → katmanlı doğrulama (otoriter build + faithfulness lint).** Mekanik ile muhakemeyi ayır; her kararı bir dökümana bağla; kestiğin köşeyi açıkça bildir.
