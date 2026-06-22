# Fix Plan: Bağımsız denetim boşlukları (INV-7 sessiz-düşürmeler + partial'lar)

## Overview
4 bağımsız denetçi, grammar'ın tamamına karşı üreteci denetledi ve **8 sessiz-düşürme (INV-7
ihlali) + 4 partial** buldu — hepsi fixture'ın dokunmadığı (ya da dokunduğu ama emitter'ın
yok saydığı) construct'lar. Kök-neden tek: üreteç "handle edince kaydet" yapıyor; **"GM'deki
HER construct ya emit ya UnsupportedConstruct" completeness check'i yok**. Bu plan önce o
gate'i ekler (tüm drop'ları RED test'e çevirir), sonra construct'ları teker teker yeşile çeker.

## Architecture Decisions
- **Completeness gate = manifest construct census.** `manifest`/GM'den, mevcut HER construct
  örneğini `(construct, id)` olarak sayan bir fonksiyon (§6.4 tablosunun kod-karşılığı). Gate,
  census ⊄ build-report ise **SilentDrop** kaydeder → `Clean=false` → test RED. Bu, INV-7'yi
  gerçekten *zorlayan* mekanizma (eski `Clean` yalnız kayıtlı girdilere bakıyordu).
- **Risk-first sıra:** gate önce (kök-neden), sonra fixture'ı tüm-keyword'e genişlet, sonra fix.
  Gate her fix'i sürükleyen failing-test olur (TDD).
- **ponytail sınırı:** fix'ler minimal — `@http`/`@trigger` → hedef-stub + §8 policy (tam binding
  framework değil); `deployable` → compose/AppHost stub (orchestration motoru değil);
  `consistency` → tx/outbox seçim-iskeleti. Gate + POCO-Ext en yüksek kaldıraç, en az kod.
- **Go kapsam-dışı:** Go bilinçli kısmi spike; bu plan **.NET** boşluklarını kapatır. Gate Go'ya
  uygulanmaz (Go'nun kendi sınırlı census'u olur ya da muaf tutulur — Open Q3).

## Task List

### Phase A — Completeness gate (kök-neden, risk-first)
- Task A1 — Manifest construct census + gate · M · Dep: None
  AC: `Census(manifest)` mevcut her construct örneğini `(construct,id)` döndürür; gate census⊄report → `report.SilentDrop(...)` + `Clean=false`; mevcut fixture'da gate **RED** (throws/note/consistency/deployable yakalanır).
  Verify: yeni test `Full_fixture_has_no_silent_drops` RED listeler; `dotnet test`.
- Task A2 — CLI/emit sonu gate entegrasyonu + exit kodu · S · Dep: A1
  AC: CLI emit sonrası gate çalışır; SilentDrop varsa exit≠0 + build-report'ta listelenir.

**Checkpoint A:** gate RED, tüm Sınıf-1 drop'ları (throws/note/consistency/deployable) test çıktısında görünür.

### Phase B — Sınıf-1 fix (fixture'da MEVCUT drop'lar) → gate kısmen yeşil
- Task B1 — `throws` + named-error catalog · M · Dep: A1
  AC: `errors[]`→error tipleri/sabitleri; `op.throws`→result binding (`NotProcessable<T>.Code` bağlı); census kaydı realized.
- Task B2 — `consistency {risk,mode}` · S · Dep: A1
  AC: strong→in-proc tx, eventual→outbox seçim-iskeleti (mode async/durable yorumlu); realized + policy("consistency-mode").
- Task B3 — `deployable` → GMDeployable + host-orchestration · M · Dep: A1
  AC: `GMDeployable` GM'e eklenir; compose/AppHost wiring stub'ı emit; realized.
- Task B4 — `note` → doc-comment · S · Dep: A1
  AC: `op.note`→handler üstü XML doc-comment; realized.

**Checkpoint B:** mevcut fixture'da gate **YEŞİL** (Sınıf-1 kapandı).

### Phase C — POCO completeness (latent ext + uncharted + sourceOfTruth)
- Task C1 — Tüm annotation-site'larına `Ext` · M · Dep: A1
  AC: `ParamJson`/`FieldJson`/`Deployable`/`ModuleDecl`/`EntityJson`/`TypeJson`'a `Ext`; her site'ta @crypto/@sensitivity/… emit+report (entity-field emsali); census kapsar.
- Task C2 — `uncharted` tipli model + çağrı-adapter · M · Dep: A1
  AC: `UnchartedJson` (ops+entities+types+owned); GM'e taşı; çağrı-adapter interface+stub (external emsali, `owned` korunur); realized.
- Task C3 — `sourceOfTruth` → cross-module FK · S · Dep: A1
  AC: `field.sourceOfTruth`→FK alanı/ilişki yorumu (navigasyon AÇMA); realized.
- Task C4 — `for guard`/`guardRef` kararı · S · Dep: A1, Open Q1
  AC: build-time ise §6.4'te N/A işaretle + census'tan çıkar; değilse guard-linkage emit. (Karar Open Q1.)

**Checkpoint C:** latent drop'lar kapandı (genişletilmiş fixture öncesi POCO hazır).

### Phase D — Partial'lar
- Task D1 — serving `@grpc`/`@queue` · S · Dep: A1
  AC: rest-dışı protokol → en az `UnsupportedConstruct`+report (sessiz `""` YASAK); ideal: binding stub.
- Task D2 — external BoundaryOp `serving`+`validation` · S · Dep: A1
  AC: `BoundaryOpJson`'a serving+validation; caller-side validation AST→predicate (INV-4); realized.
- Task D3 — `on`/subscriptions → consumer wiring · M · Dep: A1
  AC: consumer handler iskeleti + registration (sadece report değil); realized.
- Task D4 — `@http`/`@trigger` hedef-handling · M · Dep: C1
  AC: @trigger→hosted-service stub; @http→endpoint-detail (route/query/header); §8 policy + report (ponytail: minimal).

### Phase E — Tam-keyword fixture + final gate + regression
- Task E1 — Full-coverage fixture · M · Dep: B*,C*,D*
  AC: fixture grammar'ın HER keyword'ünü (uncharted, grpc, sourceOfTruth, tüm ext-site'lar, subscriptions, …) içerir.
- Task E2 — Final gate yeşil + regression · S · Dep: E1
  AC: full fixture'da gate **YEŞİL**; 0 SilentDrop; .NET+Go compile gate geçer; determinizm; tüm testler yeşil.

**Checkpoint E (Complete):** grammar'ın her keyword'ü ya COVERED ya açıkça UnsupportedConstruct/N/A — INV-7 mekanik olarak zorlanıyor.

## Risks and Mitigations
| Risk | Etki | Önlem |
|---|---|---|
| Census §6.4'ten drift eder | High | Census = §6.4'ün tek-kaynak kod-karşılığı; yeni construct → census satırı (test zorlar) |
| Gate çok agresif (build-time construct'ları drop sanır) | Med | N/A-build-time seti açıkça muaf (standalone/contract/import/rolemap/extension-decl/realizes) |
| Fix'ler over-engineering'e kayar | Med | ponytail: stub+policy+report; tam binding/orchestration motoru YOK |
| `for guard` niyeti belirsiz | Low | Open Q1 ile karara bağla; çözülene dek C4 bloklu |

## Resolved decisions (kullanıcı, bu tur)
1. **`for guard`/`guardRef`** → predicate'e **yorum** olarak emit (COVERED, düşürme yok). CommandDSL teyit: build-time kapsama bağı ama "full" için emit edilir. → C4 = guardRef yorum emisyonu.
2. **`@http`/`@trigger`** → **minimal** hedef-stub + §8 policy (tam framework değil). → D4.
3. **Gate kapsamı** → yalnız **.NET** ("önce .NET'i fulle"); Go sonra. Gate Go'ya uygulanmaz.

> Scope: bu tur **.NET'i tam kapsamaya** getiriyoruz; Go bilinçli kısmi spike kalıyor.

## Verification convention (her fix task'ı)
- Construct census'ta var → build-report'ta `realized` (veya açık `unsupported`), `SilentDrop` YOK.
- Üretilen app `dotnet build` (gerekirse `go build`) exit 0.
- İlgili construct için golden/hedefli test.
