# Tasarım: Süreç-yapılı test üretimi (process-structured test generation)

> **Durum:** ANALİZ/TASARIM — implementasyon YOK. `api-and-interface-design` + `architecture-reviewer`
> mercekleriyle hazırlandı; kaynak: `src/Gen.*` (üretici C#) + `operations.json`/`manifest.json` gerçek fixture.

## 1. Amaç & kapsam

Üretici (Gen binary), her **süreç (process)** için yapılandırılmış bir unit/integration test **iskeletini
deterministik** emit eder; içini (yalnız ARRANGE) LLM doldurur. Test yapısı her test için sabit 3-faz:

1. **Temiz data** (izole/sıfırlanmış state)
2. **Ön-gereksinimlerin oluşturulması** (sürecin koşması için gereken önceki entity'ler)
3. **Sürecin işletilmesi** (process→flow→op çağrı sırası)

**Kapsama (orphan coverage) — gerçek fixture (studyo) ile doğrulandı:**
- 4 process-test · 1 orphan-flow-test (`DersTipiYonetimi`) · 18 orphan-op-test = **23 test sınıfı**.
- Hiçbir sürece dahil olmayan **flow** → kendi testi (aynı 3-faz).
- Hiçbir flow/sürece dahil olmayan **operasyon** → kendi testi (aynı 3-faz).

## 2. Mevcut mimari (grounding — kaynaktan okundu)

| Bileşen | Dosya | Rol |
|---|---|---|
| Contract model | `Gen.Core/Model/Contract.cs` | `operations.json` → `ContractFile`. **processes/flows STRUKTÜRÜNÜ parse ETMİYOR** (yalnız op'ta `List<string> Flows/Processes` üyelik ref'i). |
| Manifest model | `Gen.Core/Model/Manifest.cs` | `AccessJson(Reads,Creates,Updates,Deletes)` = tech 4-key (Kapı 0 ground-truth). |
| GM (IR) | `Gen.Core/Gm/GenerationModel.cs` | manifest⋈operations join; `TypeEnv.WriteTarget` = Access.{Creates,Updates,Deletes}. **processes/flows YOK.** |
| Pipeline | `Gen.Core/Pipeline/GmBuilder.cs` | `Build(manifest, contract)` — hepsi `OrderBy(Ordinal)` (determinizm kutsal). |
| Emitter | `Gen.Dotnet/DotnetEmitter.cs` | `gen/` owned-tree (prune+provenance) + `src/` human-seam (`WriteIfAbsent`, `doldurulacak` marker); feature-slice `gen/{Module}/{Op}` ↔ `src/{Module}/{Op}/{Op}Handler.Logic.cs`. |
| Report/Prov | `Gen.Core/Report/BuildReport.cs`, `Gen.Dotnet/Provenance.cs` | construct realized-raporu + owned-tree sha. |
| Go emitter | `Gen.Go/GoEmitter.cs` | ikinci tüketici (çok-dilli). |

**Sonuç:** Test üretimi 4 katmana dokunur — (a) Contract model'i genişlet, (b) TestPlan IR türet,
(c) emitter'a test-pass ekle, (d) build-report/provenance entegre et. Hepsi mevcut desenlerin tekrarı.

## 3. Tasarım — katman katman

### 3a. Model extension — Contract First, **additive** (Prefer Addition Over Modification)

`ContractFile`'a nullable alanlar ekle (standalone modda yok → geriye uyumlu, INV-9 korunur):

```csharp
// Gen.Core/Model/Contract.cs — additive; bilinmeyen alanlar zaten ignore (toleranslı parse)
public sealed record ContractFile(
    ContractMeta? Meta, List<ContractOp>? Operations, List<ContractEntity>? Entities,
    List<ContractActor>? Actors, List<JsonElement>? Relations,
    List<ProcessJson>? Processes,   // YENİ
    List<FlowJson>? Flows);         // YENİ

public sealed record ProcessJson(string Id, string? Entity, string? Note, List<ProcessStage> Items);
public sealed record ProcessStage(string Type, string Name, string StageKind, string? Flow, string? By);
public sealed record FlowJson(string Id, string? Actor, string? Note, List<FlowStep> Items);
public sealed record FlowStep(string Type, string Name, string? Target, bool Optional, bool Repeat, string? Using);
```

> **Hyrum/determinizm:** Bu kayıtlar gözlemlenebilir çıktı sırasını belirler → emit'te **ordinal-order**
> zorunlu (mevcut GmBuilder deseni). Sıra bir kez yayınlandı = sözleşme.

### 3b. TestPlan IR — deterministik türev (Gen.Core, yeni)

`GenerationModel`'e additive `TestPlan? TestPlan` ekle; `GmBuilder` (ya da yeni `TestPlanBuilder`) üretir:

```csharp
public sealed record TestPlan(
    IReadOnlyList<ProcessTest> ProcessTests,
    IReadOnlyList<ScenarioTest> OrphanFlowTests,
    IReadOnlyList<ScenarioTest> OrphanOpTests);

public sealed record ProcessTest(
    string ProcessId, string? Entity,
    IReadOnlyList<string> RunSequence,      // process→flow→op, sıralı (containment'tan)
    IReadOnlyList<PrereqStep> Prerequisites, // topo-sorted creator op'lar
    IReadOnlyList<string> WriteSet);         // Kapı 0 yazma-kümesi = ASSERT çapası

public sealed record PrereqStep(string Entity, string CreatorOp, PrereqKind Kind); // Kind: Single | Ambiguous | Missing
```

**Türetme (hepsi deterministik, `operations.json`/`manifest.json`'dan):**
- **Containment:** `processes[].items[].flow` → `flows[].items[].target` → ordered op listesi.
- **Orphan-flow:** `flows[]` \ `{process'lerde geçen flow}`. **Orphan-op:** `operations[]` \ `{flow'larda geçen op}`.
- **Ön-gereksinim grafiği:** test'in op-kümesi için gereken entity = ∪ `manifest.access.{reads,updates}`
  − içeride üretilen (`creates`). Her entity → tek creator op (`access.creates` ters-index). **Topolojik sıra.**
- **WriteSet (ASSERT çapası):** `manifest.access.{creates,updates,deletes}` — Kapı 0 ile **aynı authority**.

### 3c. Emission — Generation Gap (DotnetEmitter'a test-pass)

`Emit(...)` içine yeni pass (mevcut owned/seam desenini birebir tekrarla):

| Artefakt | Yol | Sahiplik | İçerik |
|---|---|---|---|
| Test iskeleti | `gen.tests/{Scope}/{Name}.g.cs` | **owned** (prune+provenance) | 3-faz method yapısı + RunSequence çağrıları + **contract-çapalı ASSERT** + ARRANGE seam marker'ı |
| ARRANGE gövdesi | `tests/{Scope}/{Name}.Logic.cs` | **human-seam** (`WriteIfAbsent`, marker) | temiz-data değerleri + ön-gereksinim payload'ları — **LLM doldurur** |
| Test projesi | `Tests.csproj` | **HumanShell** (`WriteIfAbsent`) | **xUnit** (Q1): App ref + `xunit` + `Microsoft.NET.Test.Sdk`. Owned `.g.cs` `[Fact]`/`[Theory]` method emit eder. |

> Scope ∈ {`Process`, `OrphanFlow`, `OrphanOp`}; Name = ilgili id. Feature-slice deseniyle aynı:
> owned `.g.cs` ↔ mirror `.Logic.cs`.

### 3d. Anti-circularity — **ARRANGE/ASSERT ayrımı (tasarımın kalbi)**

PoC tehlikesi: LLM hem Logic.cs'i doldurur hem testin assertion'ını yazarsa → **totolojik / yanlış-yeşil**
(family-gate §0 + A3'ün yasakladığı self-certifying). Çözüm — A3'ü koru:

- **ASSERT üretilir, LLM yargısı DEĞİL.** **Field-seviyesi** (Q3), `operations[].effects[]`'ten türetilir.
  Effect→assertion eşlemesi (studyo: 38 effect, **25 `calculate` / 0 koşullu** → temiz):

  | effect.kind | sayı | Üretilen assertion |
  |---|---|---|
  | `calculate` | 25 | `target` (entity.field) `==` `expr` değeri → `Assert.Equal(<expr>, entity.Field)`. **`expr` = ExprNode → üretici `ExprBuild` machinery'sini yeniden kullanır** (predicate emisyonuyla aynı; dynamic YOK). |
  | `create` | 2 | entity varlık + WriteSet (Kapı 0 tabanı). |
  | `send` | 7 | event-emit assertion (`emits`/event — davranışsal). |
  | `perform` | 4 | downstream op-çağrı assertion (callEdge). |

  Ek taban: `throws` → negatif assertion. Hepsi contract-türevli, **emit edilir** (completeness gate epistemolojisi:
  contract'tan yapı yalan söyleyemez; A3 korunur).
- **LLM yalnız ARRANGE doldurur** (temiz data + ön-gereksinim payload). Seam marker'ı **yalnız arrange bölgesinde**.

> **Koşullu-effect inceliği (fixture'da 0 ama gelecekte olabilir):** business açıklaması koşullu olsa da
> (ör. OlusturRandevu "doluysa beklemede") modellenen `effects[].expr` tek literal değere çözülmüş
> (`'onaylandi'`). Field-assert modellenen değeri bekler → **ARRANGE o değeri üreten branch'i kurmalı**
> (ARRANGE↔ASSERT branch-eşleşmesi). Çok-branch effect modeli gelirse → xUnit `[Theory]` + branch-başına
> üretilen beklenen değer.

Bu, geçen commit'teki **Kapı 0 access-authority** işinin doğal devamı — aynı `manifest.access` write-set'i
hem fill-coverage'ı hem (taban) test-assertion'ı çapalar; field-seviyesi onu `effects` ile derinleştirir.

### 3e. Build-report + provenance entegrasyonu

- Her emit edilen test = build-report'ta construct (`{construct:"test", id:<testId>, status:"realized"}`)
  → **family-gate completeness** test'i envantere alır (sessiz düşüş yakalanır).
- Owned-tree test `.g.cs` → provenance sha (owned-tree dokunulmazlık denetimi çalışır).
- Human-seam test `.Logic.cs` → provenance'a GİRMEZ (diğer seam'ler gibi).

## 4. Determinizm sınırı

| Deterministik (üretici) | LLM (seam fill) |
|---|---|
| Enumerasyon (process/orphan-flow/orphan-op) · 3-faz iskelet · RunSequence · ön-gereksinim topo-sıra · ASSERT çapaları | Temiz-data değerleri · ön-gereksinim payload'ları (ARRANGE) |

Aynı girdi → aynı iskelet (ordinal-order). ARRANGE deterministik değil ama **WriteIfAbsent + commit** ile donar (INV-C).

## 5. Ön-gereksinim türetme — kanıt (studyo)

| Süreç | RunSequence kaynağı | Ön-gereksinim (türetilen) |
|---|---|---|
| TakvimSureci | SeansPlanlama+SeanslarimGoruntule | `ClassType`→TanimlaDersTipi |
| RandevuKatilimSureci | (8 op) | `ClassType`,`Package`,`Session` → tek-creator |
| PaketYonetimSureci | (4 op) | `Package`→OlusturPaket |
| UyelikPaketSureci | (4 op) | ∅ (self-contained) |

**8 entity'nin 7'si tek-creator → temiz topolojik sıralama.** Tek çoklu-creator `UsageRecord`
(`IsaretleGeldi`/`SeansaGiris`) → **`PrereqKind.Ambiguous`**. Sıfır-creator (seed/external) → `PrereqKind.Missing`.

**Dispozisyon (Q5 = DUR):** `Ambiguous`/`Missing` → üretici **DUR-marker emit eder**
(`build-report.constructs[{construct:"test-prereq", status:"unsupported"}]`); o test'in ARRANGE'ı **stub'lanmaz**
→ skill fill-time'da unsupported'ı görür → **gap-protokol DUR+sor** (K4/rung). **Sessiz uydurma/varsayım YOK.**
(Üretici asla "herhalde bu creator" diye seçmez — belirsizlik kullanıcıya gider.)

## 6. Conformance ile örtüşme (granülerlik sınırı)

- **Conformance (mevcut):** per-construct, aile-sahibi SPEC + adapter (`Conformance.dll`). "CreateInvoice
  duplicate'i reddediyor mu?" — davranış, op-seviyesi.
- **Süreç-testi (bu tasarım):** end-to-end, ön-gereksinim state'iyle. "OnaylaRandevu gerçek bekleyen-randevu
  + paket-hakkı state'i üzerinde çalışıyor mu?" — integration, süreç-seviyesi.
- **Karar (Q2) = BAĞIMSIZ iki katman, dedupe YOK.** 23 scope'un hepsi üretilir (18 orphan-op dahil).
  Conformance (per-construct, adapter) ve süreç-testleri (scenario, xUnit) **ayrı katmanlar** — biri diğerini
  kapsamaz/iptal etmez. Kullanıcı örtüşmeyi bilinçle kabul etti: granülerlik farklı (construct-davranışı vs
  ön-gereksinim-state'li end-to-end senaryo). Bakım maliyeti bu ayrımın kabul edilmiş bedeli.

## 7. Çok-dilli (Go pariteti)

TestPlan IR **dil-nötr** (Gen.Core). Emit dil-özel (DotnetEmitter / GoEmitter). Go pariteti **sonraya**
ertelenir (blocker değil; .NET önce, sonra GoEmitter aynı IR'ı tüketir).

## 8. Skill değişikliği (base-dotnet-rest)

- Yeni arketip: **`test-arrange`** — test seam'i (`tests/**/*.Logic.cs`) yalnız ARRANGE doldurulur
  (ASSERT'e DOKUNMA — owned `.g.cs`'te, contract-çapalı). Mevcut WriteIfAbsent+marker mekaniğiyle aynı.
- **Kapı 0 (access-coverage)** zaten hizalı: test ASSERT'i ve fill-coverage aynı `manifest.access` write-set'inden.
- Verify: test projesi build + koşum (yeşil) — ama "yeşil" yalnız ARRANGE doğru + ASSERT contract-çapalı
  olduğu için anlamlı (LLM kendi assertion'ını yazmadığından totoloji değil).

## 9. Kilitli kararlar (kullanıcı onayı — 2026-06-26)

1. **Test runner = xUnit projesi.** `Tests.csproj` xUnit (App ref + xunit + test SDK). Conformance-adapter
   genişletilmez. → §3c, §8.
2. **Conformance ⟂ süreç-testleri = BAĞIMSIZ iki katman.** Dedupe YOK. 23 scope'un **hepsi** üretilir (18
   orphan-op dahil). Conformance per-construct (adapter) kalır; süreç-testleri ayrı xUnit scenario katmanı.
   → §6, §11 (ikinci-harness tradeoff'u **tam-bağımsızlıkla** kabul; merge edilmez).
3. **ASSERT = field-seviyesi** (`effects`'ten türetilir). → §3d (genişletildi).
4. **Go pariteti = SONRA.** TestPlan IR dil-nötr kalır; şimdilik yalnız DotnetEmitter. → §7.
5. **Belirsiz/eksik creator = DUR.** `Ambiguous`(≥2)/`Missing`(0) → üretici **DUR-marker emit eder**
   (`build-report.constructs[status=unsupported]`); skill fill-time'da gap-protokol ile DUR+sor. Sessiz uydurma YOK.
   → §5.

## 10. Determinizm/sıra sözleşmesi (Hyrum)

Yeni model alanları + TestPlan türevleri gözlemlenebilir çıktıyı belirler. **Tüm enumerasyon ordinal-order;
sıra yayınlandıktan sonra sözleşmedir.** Test id şeması (`Process_{id}` / `OrphanFlow_{id}` / `OrphanOp_{id}`)
build-report + provenance'ta görünür → bir kez sabitlenir, additive değişir.

## 11. Mimari inceleme (architecture-reviewer — build-time'a uyarlandı)

> `architecture-reviewer` runtime dimensiyonları (failure/cache/queue/blast-radius/resilience/isolation)
> **build-time üreteç** bağlamına uyarlandı: edge-case input = failure scenario; determinizm = blast radius;
> stale-seam = data consistency; ikinci harness = coupling. Gerçek bulgular:

#### [HIGH] Edge-case input failure scenarios (Faz 1) — TestPlan türetme robustluğu
TestPlan deterministik ama **kötü/uç contract'ta çökmemeli** (üretim additive; bozuk bir process tüm gen'i
düşürmemeli — graceful skip + report). Açıkça ele alınmalı:
- boş process (stage yok) / boş flow (step yok) → test üretme + report, çökme yok.
- process→**olmayan flow** ref'i / flow→**olmayan op** target'ı → `Missing` işaretle, DUR-marker, sessiz atlama YOK.
- **op ≥2 flow'da** → orphan-op DEĞİL (dahil); ama RunSequence çift-saymamalı — set-logic net olmalı.
- orphan-flow içindeki op → orphan-op SAYILMAZ (orphan-flow testi kapsar). Küme farkı sırası: ops − (flow'larda geçen) önce, sonra flow − (process'lerde geçen).

#### [HIGH] Determinizm blast radius (Faz 4) — topo-sort dahil ordinal
TestPlan sırası → build-report + provenance sha. **Herhangi bir non-ordinal iterasyon** (dict/set gezme,
kararsız topo-sort) byte-tekrar-üretimi kırar → family-gate owned-tree sha mismatch → **yanlış RED**.
Zorunlu: ön-gereksinim **topolojik sıralaması deterministik** (eşitlikte ordinal tie-break); tüm test
enumerasyonu `OrderBy(Ordinal)`. (Mevcut GmBuilder invariant'ı — test'lere de uygulanmalı.)

#### [HIGH] Stale-seam consistency (Faz 2) — contract değişince ARRANGE bayatlar
Contract değişince (op bir process'e eklenir, flow yeniden sıralanır) owned test `.g.cs` **yeniden üretilir**
(yeni RunSequence/ASSERT) ama human ARRANGE `.Logic.cs` **WriteIfAbsent → güncellenmez** → eski ARRANGE yeni
iskelete karşı = derleme kırığı / yanlış setup. Bu, mevcut **"v1 bayat-seam fill yapmaz"** sınırının aynı
sınıfı. Çözüm: test-seam'leri **techgen-sync delta-sync**'in signature-uzlaştırmasına dahil et (handler
seam'leriyle aynı mekanik); bayat test-seam = build-kırığı yüzeyle, sessiz geçme yok.

#### [MEDIUM→KARAR] İkinci test-harness coupling (Faz 6) — xUnit + conformance YAN YANA
Reviewer "iki harness, iki 'yeşil' modeli" coupling riskini işaret etti. **Kullanıcı kararı (Q1+Q2): xUnit
ayrı katman, conformance ayrı — TAM BAĞIMSIZ.** Coupling riski **merge-etmeyerek değil, tam-ayırarak** kabul
edildi: ikisi birbirinin invariant'ına bağlı değil, ayrı koşar. **Kalıcı şart (bu coupling'i kontrol altında
tutar):** her iki katmanın da assertion'ı **contract-çapalı** kalmalı (xUnit ASSERT = `effects`/`access`/`throws`'tan
üretilir, LLM-yazımı DEĞİL; conformance = aile-sahibi SPEC). "Yeşil" tanımı ikisinde de "contract-türevli
assertion geçti" → divergence yüzeyi yok. Eğer xUnit ASSERT'i LLM'e bırakılırsa coupling riski geri gelir → **YASAK**.

#### [MEDIUM] Orphan-op duplikasyon maliyeti (Faz 3) — boşa throughput
18 orphan-op testi conformance per-construct ile örtüşüyor → bakım maliyeti. Süreç-testi yalnız **ön-gereksinim
state ek değeri** olan op'lar için (reaktif/admin) üretilmeli; saf CRUD orphan-op'lar conformance'a bırakılmalı
(§9 Q2). Aksi = 18 düşük-değerli duplicate.

#### [LOW] Model-parse hatası izolasyonu (Faz 4 blast radius)
processes/flows parse'ı **toleranslı + non-fatal** olmalı: bozuk bir process kaydı op-emisyonunu (asıl ürün)
düşürmemeli. Additive + `try/skip+report` ile izole; test üretimi opsiyonel bir pass'tir.

## Sonuç

Tasarım mevcut Generation Gap + Kapı 0 access-authority işine **temiz oturuyor**; circularity ARRANGE/ASSERT
ayrımıyla **çözülü**. Fizibilite üretici-kaynağı erişimine bağlıydı — **erişim var (teyitli)**. Mimari inceleme
4 HIGH bulgu çıkardı (edge-case robustluk · determinizm/topo-sort · stale-seam delta-sync · ikinci-harness) —
hepsi mevcut invariant'lara/mekaniklere bağlanarak çözülebilir, blocker yok. **5 açık karar kilitlendi (§9);**
field-seviyesi ASSERT `effects`→`ExprBuild` ile, çoklu/eksik-creator DUR ile, iki katman tam-bağımsız xUnit ile.

**Sonraki adım:** tasarım sabit → `atomic-task-spec` ile implementasyon task'larına böl. Önerilen milestone sırası:
**M1** model+TestPlan IR (Gen.Core, deterministik, edge-case'ler) → **M2** DotnetEmitter test-pass (owned iskelet +
field-ASSERT via ExprBuild + seam) → **M3** build-report/provenance + DUR-marker → **M4** base-dotnet-rest `test-arrange`
seam-fill + delta-sync stale-seam reconciliation → **M5** verify (xUnit build+run yeşil) + eval'ler.
