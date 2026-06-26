# Implementasyon Planı — Süreç-yapılı test üretimi

> **Kaynak tasarım:** `tasks/process-test-generation-design.md` (kilitli 5 karar dahil).
> **Format:** atomic-task-spec hibrit — 🔴 kritik yol tam format, 🟢 düşük risk kısa format.
> **ID order ≠ execution order değil** — burada ID order = topological order (graf §2).

## 1. Nasıl okunmalı
Her task tek agent oturumunda biter. 🔴 task'lar tam format (Inputs/Pre-cond/Changes-anchor/Acceptance/
Out-of-scope/Anti-pattern/DoD/Self-check). Executor **Inputs'u okumadan** kod yazmaz; **Acceptance'ı
çalıştırmadan** "bitti" demez. Tüm üretici kodu **deterministik** (ordinal-order) — bu invariant her task'ta korunur.

> **FIXTURE (LIFT — uyumluluk raporu blocker #2):** Tüm acceptance/E2E bash blokları `$FIXTURE` =
> **`tests/fixtures/studyo.manifest.json`** + **`tests/fixtures/studyo.operations.json`** (repo-içi, **T-1.0 commit eder**)
> kullanır. `Silinecek1/cikti/...` referansları repo-DIŞI (kırılgan) — onun yerine committed fixture kullan.
> Beklenen sayılar: 4 process / 14 flow / 42 op → 4 process-test + 1 orphan-flow + 18 orphan-op.

## 2. Bağımlılık grafı (topological)

```
M1 (fixture):       T-1.0 (committed studyo fixture — tüm acceptance buna bağlı)
M1 (Gen.Core IR):   T-1.0 → T-1.1 → T-1.2 → T-1.3 → T-1.4 → T-1.5
M2 (emitter):                                  └→ T-2.1 → ├→ T-2.2
                                                          ├→ T-2.3
                                                          └→ T-2.4
M3 (report/prov):                          T-2.1 → ├→ T-3.1
                                                   ├→ T-3.2   (T-1.4 PrereqKind'e bağlı)
                                                   └→ T-3.3
M4 (skill):         (M2+M3 çıktısı) → T-4.1 → T-4.2 → T-4.3
M5 (verify):        (hepsi) → T-5.1 → T-5.2
```
Paralel pencereler: {T-2.2, T-2.3, T-2.4} bağımsız (hepsi T-2.1 sonrası). {T-3.1, T-3.3} bağımsız.

## 3. Symbol-Task tablosu (forward-dep guard)

| Symbol (fully-qualified) | Producing Task |
|---|---|
| `tests/fixtures/studyo.{manifest,operations}.json` (committed fixture) | T-1.0 |
| `Gen.Core.Model.ProcessJson` / `ProcessStage` / `FlowJson` / `FlowStep` | T-1.1 |
| `Gen.Core.Model.ContractFile.Processes` / `.Flows` | T-1.1 |
| `Gen.Core.Model.ContractEffect.Expr` / `.Text` (effect değeri — field-ASSERT girdisi) | T-1.1 |
| `Gen.Core.Gm.TestPlan` / `ProcessTest` / `ScenarioTest` / `PrereqStep` / `PrereqKind` | T-1.2 |
| `Gen.Core.Pipeline.TestPlanBuilder` (containment + orphan) | T-1.3 |
| `TestPlanBuilder.DerivePrerequisites` | T-1.4 |
| `Gen.Core.Gm.GenerationModel.TestPlan` (alan) | T-1.5 |
| `Gen.Dotnet.DotnetEmitter.EmitTests` / `TestSkeleton` | T-2.1 |
| `DotnetEmitter.AssertBlock` (effect→assert) | T-2.2 |
| `DotnetEmitter.TestArrangeSeam` | T-2.3 |
| `DotnetEmitter.TestsCsproj` | T-2.4 |
| build-report `test` construct kayıtları | T-3.1 |
| build-report `test-prereq` unsupported (DUR-marker) | T-3.2 |
| base-dotnet-rest `test-arrange` arketipi | T-4.1 |
| techgen-sync test-seam delta-sync | T-4.2 |

Standart kütüphane / BCL (`List`, `Dictionary`, `string`, xUnit `Fact`/`Assert`) external — dep değil.

---

# M1 — Model + TestPlan IR (Gen.Core, deterministik)

**Amaç:** `operations.json`'un process/flow STRUKTÜRÜNÜ parse et + deterministik TestPlan IR türet (containment,
orphan, ön-gereksinim). **Çıktı:** `GenerationModel.TestPlan` dolu. **Milestone DoD:** `dotnet build src` exit 0;
fixture'tan TestPlan türetilince 4 process + 1 orphan-flow + 18 orphan-op çıkıyor (unit test ile doğrulanır, T-1.3/1.4).

---

## 🔴 T-1.0 — Process/flow committed test fixture (`tests/fixtures/studyo.*`)

### 1. Goal
Process/flow STRÜKTÜRÜ içeren bir `operations.json` + eşleşen `manifest.json`'u **repo-içi** `tests/fixtures/`'a
commit et (`studyo.operations.json` + `studyo.manifest.json`). Tüm M1–M5 acceptance'ı buna (`$FIXTURE`) bağlı.

### 2. Why
Uyumluluk raporu blocker #2: mevcut committed fixture (`tests/fixtures/operations.json`) 1 op / 0 process / 0 flow —
TestPlan'ı doğrulayamaz. Gerçek process/flow verisi `Silinecek1/cikti/` (repo-DIŞI, "Silinecek" = silinecek → kırılgan).
Doğrulama runnable olması için repo-içi committed fixture şart.

### 3. Inputs (TAM oku)
- `/Users/hakansoysal/Desktop/ClaudeCode Denemeler/Silinecek1/cikti/studyo.operations.json` — kaynak (4 process/14 flow/42 op).
- `/Users/hakansoysal/Desktop/ClaudeCode Denemeler/Silinecek1/cikti/manifest.json` — eşleşen manifest (linked; `contract` alanı operations.json'a işaret eder).
- `CoreTemplate1/tests/fixtures/` — mevcut fixture konumu/konvansiyonu.

### 4. Pre-conditions
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler"
test -f Silinecek1/cikti/studyo.operations.json && test -f Silinecek1/cikti/manifest.json   # kaynak var
python3 -c "import json;o=json.load(open('Silinecek1/cikti/studyo.operations.json'));print(len(o['processes']),len(o['flows']),len(o['operations']))"  # expected: 4 14 42
```
Kaynak yoksa → STOP + kullanıcıya bildir (fixture başka yoldan sağlanmalı).

### 5. Changes
#### Step 5.1 — Kopyala + manifest contract path düzelt
**Action:**
- `Silinecek1/cikti/studyo.operations.json` → `CoreTemplate1/tests/fixtures/studyo.operations.json`.
- `Silinecek1/cikti/manifest.json` → `CoreTemplate1/tests/fixtures/studyo.manifest.json`.
- `studyo.manifest.json` içindeki `"contract": "./studyo.operations.json"` (linked) path'inin kopyalanan konumda
  çözüldüğünü doğrula (gerekirse path'i `./studyo.operations.json` olarak düzelt).

### 6. Acceptance tests
#### 6.1 Fixture sayıları
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
python3 -c "import json;o=json.load(open('tests/fixtures/studyo.operations.json'));print(len(o['processes']),len(o['flows']),len(o['operations']))"  # expected: 4 14 42
test -f tests/fixtures/studyo.manifest.json   # var
```
#### 6.2 Pozitif — linked çözülür
`studyo.manifest.json` `mode=="linked"`, `contract` alanı `tests/fixtures/studyo.operations.json`'a çözülüyor.
#### 6.3 Negatif — mevcut fixture korunur
Eski `tests/fixtures/operations.json` (1 op) **silinmedi/değişmedi** (regression yok).

### 7. Out of scope
- Kod değişikliği YOK (yalnız fixture commit).
- Model/emitter YOK.

### 8. Anti-patterns
- Fixture'ı `Silinecek1`'den **referansla** bırakma (kopyalamadan) → repo-dışı kırılganlık devam eder; KOPYALA.
- Mevcut `operations.json` fixture'ını ezme/silme.

### 9. Definition of Done
- [ ] `tests/fixtures/studyo.operations.json` (4/14/42) + `studyo.manifest.json` commit edildi.
- [ ] linked `contract` path çözülüyor.
- [ ] eski fixture dokunulmadı.
- [ ] `git diff --stat` yalnız `tests/fixtures/studyo.*` (2 yeni dosya).

### 10. Self-check
1. Sayıları (4/14/42) gerçekten doğruladım mı? 2. Kopyaladım mı, referansla mı bıraktım? 3. manifest contract path çözülüyor mu? 4. Eski fixture'a dokundum mu?

---

## 🔴 T-1.1 — `ProcessJson`/`FlowJson` (+ `ContractEffect` değeri) kayıtlarını Contract.cs'e ekle (additive)

### 1. Goal
`Gen.Core/Model/Contract.cs`'e `ProcessJson`, `ProcessStage`, `FlowJson`, `FlowStep` record'larını ekle,
`ContractFile`'a **nullable** `Processes`/`Flows` alanları bağla, **ve `ContractEffect`'i effect-değeri
(`Expr`/`Text`) taşıyacak şekilde genişlet** (field-ASSERT girdisi — blocker #1).

### 2. Why
Tasarım §2 + §3a: Contract model şu an process/flow **strüktürünü parse etmiyor** (yalnız op'ta üyelik ref'i).
TestPlan bunu gerektiriyor. Additive + nullable → standalone mod (INV-9) ve eski contract'lar bozulmaz; bilinmeyen
JSON alanları zaten `System.Text.Json` tarafından ignore ediliyor (toleranslı).

### 3. Inputs (düzenlemeden önce TAM oku)
- `src/Gen.Core/Model/Contract.cs` — tüm dosya (~30 satır). Pattern: mevcut `ContractOp`/`ContractEntity` record stili.
- `tasks/process-test-generation-design.md` §3a — kayıt şemaları.
- **Pattern (gerçek veri):** `Silinecek1/cikti/studyo.operations.json` `processes[0]` (`items[].{type,name,stageKind,flow,by}`)
  ve `flows[0]` (`items[].{type,name,target,optional,repeat,using}`).

### 4. Pre-conditions (başlamadan çalıştır)
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
test -f tests/fixtures/studyo.operations.json   # T-1.0 done (effect-değeri acceptance'ı için)
dotnet build src/Gen.Core/Gen.Core.csproj   # expected: exit 0
```
Fail → STOP, raporla.

### 5. Changes

#### Step 5.1 — Yeni record'lar
**File:** `src/Gen.Core/Model/Contract.cs`
**Anchor:** dosya sonu (`ContractActor` record'undan sonra; search `public sealed record ContractActor`)
**Action:** Add
**Code:**
```csharp
// operations.json process/flow strüktürü (TestPlan IR girdisi). Toleranslı: bilinmeyen alan ignore.
public sealed record ProcessJson(string Id, string? Entity, string? Note, List<ProcessStage>? Items);
public sealed record ProcessStage(string Type, string Name, string? StageKind, string? Flow, string? By);
public sealed record FlowJson(string Id, string? Actor, string? Note, List<FlowStep>? Items);
public sealed record FlowStep(string Type, string Name, string? Target, bool Optional, bool Repeat, string? Using);
```

#### Step 5.2 — `ContractFile`'a alan ekle (additive, sona)
**File:** `src/Gen.Core/Model/Contract.cs`
**Anchor:** `public sealed record ContractFile(` (search `record ContractFile`)
**Action:** Modify — son parametre `List<JsonElement>? Relations`'tan sonra iki nullable alan ekle:
```csharp
public sealed record ContractFile(
    ContractMeta? Meta,
    List<ContractOp>? Operations,
    List<ContractEntity>? Entities,
    List<ContractActor>? Actors,
    List<JsonElement>? Relations,
    List<ProcessJson>? Processes,
    List<FlowJson>? Flows);
```

#### Step 5.3 — `ContractEffect`'i effect-değeri taşıyacak şekilde genişlet (field-ASSERT girdisi — blocker #1)
**File:** `src/Gen.Core/Model/Contract.cs`
**Anchor:** `public sealed record ContractEffect(` (search `record ContractEffect`)
**Action:** Modify — mevcut `(string Kind, JsonElement? Target)`'a **nullable** effect-değeri alanları ekle (sona):
```csharp
// Expr = calculate effect'in ExprNode değeri (varsa); Text = ham literal metni (ör. "'iptal'").
// İkisi de nullable → eski/değersiz effect'ler bozulmaz. Field-ASSERT (T-2.2) bunları okur.
public sealed record ContractEffect(string Kind, JsonElement? Target, ExprNode? Expr = null, string? Text = null);
```
> **NOT:** `ExprNode` parse'ı için `ExprNodeConverter` (Gen.Core/Model/ExprNode.cs) JSON options'a kayıtlı olmalı —
> `Loader`'ın deserializer'ı bunu zaten predicate'ler için kullanıyor (teyit et: `Loader.cs`). Değilse `Expr`'i
> `JsonElement?` olarak al ve T-2.2'de `ExprNodeConverter.FromElement` ile çöz (mevcut API).

### 6. Acceptance tests
#### 6.1 Compile
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
dotnet build src/Gen.Core/Gen.Core.csproj   # expected: exit 0
```
#### 6.1b Effect-değeri parse
`tests/fixtures/studyo.operations.json` (T-1.0) deserialize edilince `IptalRandevu` op'unun bir `calculate`
effect'inde `Target=[Appointment,status]` ve `Text`/`Expr` değeri (`'iptal'`) **dolu** (null değil). Beklenen: effect değeri korunuyor.
#### 6.2 Pozitif — parse round-trip
`/tmp/t11.csx` veya küçük bir test ile `Loader`'ın gerçek `studyo.operations.json`'u deserialize ettiğinde
`ContractFile.Processes.Count == 4` ve `Flows.Count == 14` olduğunu doğrula. (Loader yolu: `Gen.Core/Pipeline/Loader.cs`.)
Beklenen: `Processes=4, Flows=14`, exception yok.
#### 6.3 Negatif — eksik alanlar
`processes` alanı OLMAYAN bir contract (eski fixture `CoreTemplate1/tests/fixtures/operations.json` varsa onu kullan)
deserialize edilince `Processes == null` (exception YOK — additive/nullable). Beklenen: null, build/parse kırılmaz.

### 7. Out of scope (DO NOT)
- TestPlan türetme YOK — T-1.3/1.4.
- `GenerationModel`'e alan ekleme YOK — T-1.5.
- Emitter'a dokunma — M2.
- `Manifest.cs`'e dokunma (process/flow manifest'te yok; yalnız operations.json).

### 8. Anti-patterns
- Alanları **non-nullable** yapma → standalone mod + eski contract parse'ı kırılır.
- `ProcessStage.StageKind`/`Flow`'u **zorunlu** yapma → bazı item'lar farklı tip olabilir (toleranslı kal).
- `ContractFile`'ın mevcut parametre **sırasını değiştirme** → yeni alanlar yalnız **sona** eklenir (positional record).

### 9. Definition of Done
- [ ] Step 5.1: 4 record eklendi.
- [ ] Step 5.2: `ContractFile` 2 nullable alanla genişledi (sona).
- [ ] Step 5.3: `ContractEffect` `Expr`/`Text` nullable alanlarıyla genişledi.
- [ ] 6.1 build exit 0.
- [ ] 6.1b: studyo fixture'ta `IptalRandevu` calculate effect değeri (`'iptal'`) parse ediliyor (null değil).
- [ ] 6.2 Processes=4 / Flows=14.
- [ ] 6.3 eski contract → Processes=null, kırılma yok.
- [ ] `git diff --stat` yalnız `src/Gen.Core/Model/Contract.cs`.

### 10. Self-check
1. `dotnet build`'i gerçekten çalıştırıp exit 0'ı **gözümle** gördüm mü?
2. 6.2'de gerçek studyo dosyasını mı deserialize ettim, yoksa varsaydım mı?
3. Contract.cs dışında dosyaya dokundum mu?
4. Record parametre sırasını koruduğumdan emin miyim (positional)?
5. Negatif (eski contract) durumunu gerçekten test ettim mi?

---

## 🔴 T-1.2 — TestPlan IR record'larını ekle

### 1. Goal
`Gen.Core/Gm/` altında `TestPlan`, `ProcessTest`, `ScenarioTest`, `PrereqStep`, `PrereqKind` record/enum'larını
tanımla (yeni dosya `TestPlan.cs`).

### 2. Why
Tasarım §3b: deterministik test IR. Emitter (M2) bunu tüketir; builder (T-1.3/1.4) doldurur. Ayrı dosya →
GenerationModel.cs şişmez.

### 3. Inputs
- `src/Gen.Core/Gm/GenerationModel.cs` — record stili pattern (sealed record, IReadOnlyList).
- `tasks/process-test-generation-design.md` §3b — şema.

### 4. Pre-conditions
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
test -f src/Gen.Core/Model/Contract.cs && grep -q "record ProcessJson" src/Gen.Core/Model/Contract.cs   # T-1.1 done
dotnet build src/Gen.Core/Gen.Core.csproj   # expected: exit 0
```
Fail → STOP (T-1.1 önce bitmeli).

### 5. Changes
#### Step 5.1 — Yeni dosya
**File:** `src/Gen.Core/Gm/TestPlan.cs` (yeni)
**Action:** Create
**Code:**
```csharp
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
```

### 6. Acceptance tests
#### 6.1 Compile
```bash
dotnet build src/Gen.Core/Gen.Core.csproj   # expected: exit 0
```
#### 6.2 Pozitif — tip erişilebilir
`grep -q "record TestPlan" src/Gen.Core/Gm/TestPlan.cs` → match. `enum PrereqKind` 3 değer içerir.
#### 6.3 Negatif
Yok (saf tip tanımı; davranış dallanması yok — düşük risk ama downstream-shape kritik olduğu için tam format).

### 7. Out of scope
- Builder mantığı YOK — T-1.3/1.4.
- `GenerationModel`'e bağlama YOK — T-1.5.

### 8. Anti-patterns
- `List<T>` kullanma; `IReadOnlyList<T>` (mevcut GM stili, immutability).
- `ScenarioTest`/`ProcessTest`'i tek record'a birleştirme — ayrı kalsın (orphan'da Entity yok).

### 9. Definition of Done
- [ ] `src/Gen.Core/Gm/TestPlan.cs` oluştu, 4 record + 1 enum.
- [ ] 6.1 build exit 0.
- [ ] `git diff --stat` yalnız yeni dosya.

### 10. Self-check
1. Build'i çalıştırdım mı, gördüm mü? 2. IReadOnlyList mı kullandım? 3. Başka dosyaya dokundum mu? 4. enum 3 değer mi?

---

## 🔴 T-1.3 — `TestPlanBuilder`: containment + orphan (deterministik)

### 1. Goal
`Gen.Core/Pipeline/TestPlanBuilder.cs` oluştur; process→flow→op containment'ından RunSequence ve orphan-flow/
orphan-op kümelerini **ordinal-deterministik** türet (ön-gereksinim HARİÇ — o T-1.4).

### 2. Why
Tasarım §3b + §5: containment + orphan = saf küme-mantığı, deterministik. Edge-case'ler (boş/dangling/op≥2 flow,
küme-farkı sırası) §11 HIGH bulgusunda — bu task onları açıkça ele alır.

### 3. Inputs
- `src/Gen.Core/Model/Contract.cs` — `ProcessJson`/`FlowJson` (T-1.1).
- `src/Gen.Core/Gm/TestPlan.cs` — IR (T-1.2).
- `src/Gen.Core/Pipeline/GmBuilder.cs` — `OrderBy(StringComparer.Ordinal)` determinizm pattern'ı (BİREBİR uygula).
- `tasks/process-test-generation-design.md` §3b (türetme) + §11 (edge-case'ler).

### 4. Pre-conditions
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
grep -q "record TestPlan" src/Gen.Core/Gm/TestPlan.cs      # T-1.2 done
grep -q "record ProcessJson" src/Gen.Core/Model/Contract.cs # T-1.1 done
dotnet build src/Gen.Core/Gen.Core.csproj                   # exit 0
```

### 5. Changes
#### Step 5.1 — Builder iskeleti + containment
**File:** `src/Gen.Core/Pipeline/TestPlanBuilder.cs` (yeni)
**Action:** Create — `public static class TestPlanBuilder { public static TestPlan Build(ContractFile? c) {...} }`.
Mantık (deterministik, hepsi `OrderBy(Ordinal)`):
- `c` null veya `Processes`/`Flows` null → boş TestPlan döndür (standalone/eski contract; çökme YOK).
- `flowById = c.Flows.ToDictionary(f => f.Id)`.
- Her process için `RunSequence` = process.Items (stage) → her stage.Flow → flowById[flow].Items (step) →
  step.Target (op id), **stage sırası + step sırası korunur** (Items listesi sırası = contract sırası, ordinal değil — sıra anlamlı). **dangling flow ref** (flowById'de yok) → o stage atlanır + (T-3.2'de marker; burada şimdilik skip+devam).
- `flowsInProcess` = process'lerde geçen flow id'leri (set).
- `OrphanFlowTests` = `c.Flows` \ flowsInProcess → her biri ScenarioTest (RunSequence = flow.Items.Target sırası).
- `opsInFlow` = TÜM flow'larda geçen op target'ları (set).
- `OrphanOpTests` = (manifest/contract op'ları) \ opsInFlow → ScenarioTest (RunSequence = [op]).
- **op-id kaynağı:** `c.Operations` (ContractOp.Id) ordinal-sıralı.
- Bu task'ta `Prerequisites`=boş, `WriteSet`=boş bırak (T-1.4 + emitter doldurur). Sıralama: ProcessTests
  process.Id ordinal; Orphan*Tests id ordinal.

> **Edge-case kuralları (§11 HIGH):** boş process (Items null/0) → RunSequence boş, yine de ProcessTest üret
> (rapor T-3.1). op ≥2 flow'da → opsInFlow set olduğu için bir kez; orphan DEĞİL. Orphan-flow içindeki op →
> opsInFlow'a dahil (flow'da geçiyor) → orphan-op SAYILMAZ. Küme-farkı sırası: önce opsInFlow hesapla (tüm
> flow'lar, orphan dahil), sonra ops−opsInFlow.

### 6. Acceptance tests
#### 6.1 Compile
```bash
dotnet build src/Gen.Core/Gen.Core.csproj   # exit 0
```
#### 6.2 Pozitif — gerçek fixture sayıları
Bir mini-runner/unit test ile `studyo.operations.json`'u Loader ile yükle → `TestPlanBuilder.Build(contract)`:
- `ProcessTests.Count == 4`
- `OrphanFlowTests.Count == 1` ve tek elemanın Id'i `DersTipiYonetimi`
- `OrphanOpTests.Count == 18`
Beklenen: tam bu sayılar (tasarım §1 ile birebir).
#### 6.3 Negatif — boş/null contract
`TestPlanBuilder.Build(null)` → boş TestPlan (3 liste de boş), exception YOK. `Processes=null` olan contract → aynı.

### 7. Out of scope
- Ön-gereksinim türetme YOK — T-1.4.
- WriteSet doldurma YOK — emitter/T-1.4.
- DUR-marker / build-report YOK — T-3.2.
- GenerationModel bağlama YOK — T-1.5.

### 8. Anti-patterns
- `Dictionary`/`HashSet` üzerinde **sırasız iterasyon** ile liste üretme → determinizm kırılır; her çıktı listesi `OrderBy(Ordinal)`.
- op∈flow ve op∈process'i ayrı sayıp **çift-sayma** → opsInFlow tek set; küme farkı bir kez.
- dangling flow ref'te **exception fırlatma** → skip+devam (robustluk; marker T-3.2'de).

### 9. Definition of Done
- [ ] `TestPlanBuilder.Build` containment + orphan üretir.
- [ ] 6.2: 4 / 1(`DersTipiYonetimi`) / 18.
- [ ] 6.3: null/boş → boş plan, çökme yok.
- [ ] Tüm çıktı listeleri ordinal-sıralı (kod incelemesi + 6.2 stabil).
- [ ] `git diff --stat` yalnız yeni `TestPlanBuilder.cs`.

### 10. Self-check
1. 6.2'yi gerçek studyo dosyasıyla mı koştum? 2. Çıktı sayıları tam 4/1/18 mi, gözümle gördüm mü? 3. Determinizm: her liste OrderBy'lı mı? 4. Dangling/boş edge-case'i çökmeden mi geçti? 5. Out-of-scope dışına çıktım mı?

---

## 🔴 T-1.4 — Ön-gereksinim türetme (access-graph, topo-sort, DUR sınıflama)

### 1. Goal
`TestPlanBuilder`'a ön-gereksinim türetmesini ekle: her test'in op-kümesi için `manifest.access.{reads,updates}`−
`creates` → gereken entity'ler → tek-creator topolojik sıra; çoklu/sıfır creator → `PrereqKind.Ambiguous`/`Missing`.

### 2. Why
Tasarım §5 + Q5 (DUR): ön-gereksinim deterministik türetilir (8 entity'nin 7'si tek-creator). Belirsizlik
sessizce çözülmez → `PrereqKind` ile işaretlenir; DUR-marker T-3.2'de. **Kaynak = manifest.access (Kapı 0 authority),
operations.json DEĞİL.**

### 3. Inputs
- `src/Gen.Core/Pipeline/TestPlanBuilder.cs` (T-1.3).
- `src/Gen.Core/Model/Manifest.cs` — `AccessJson(Reads,Creates,Updates,Deletes)` (4-key, tech).
- `src/Gen.Core/Gm/GenerationModel.cs` — `TypeEnv.WriteTarget` (Access.Creates/Updates/Deletes pattern).
- `tasks/process-test-generation-design.md` §5 (kanıt tablosu: hangi process hangi prereq).

### 4. Pre-conditions
```bash
grep -q "class TestPlanBuilder" src/Gen.Core/Pipeline/TestPlanBuilder.cs   # T-1.3 done
dotnet build src/Gen.Core/Gen.Core.csproj   # exit 0
```
> **NOT:** Build, manifest erişimi gerektirir. `TestPlanBuilder.Build` imzası **`(ContractFile? c, ...)`**'ten
> manifest op-access'ine ihtiyaç duyar → **imza genişler**: `Build(ContractFile? c, IReadOnlyList<OperationJson> manifestOps)`.
> Bu T-1.5 wiring'ini etkiler (caller güncellenir).

### 5. Changes
#### Step 5.1 — Creator ters-index + prereq türetme
**File:** `src/Gen.Core/Pipeline/TestPlanBuilder.cs`
**Anchor:** `Build` metodu (T-1.3'te oluşturuldu)
**Action:** Modify — imzaya `IReadOnlyList<OperationJson> manifestOps` ekle; içine:
- `creators = entity → [op.Id where op.Access.Creates contains entity]` (manifestOps'tan; ordinal).
- `DerivePrerequisites(IReadOnlyList<string> runSeq)`:
  - `produced` = runSeq op'larının `Access.Creates` birleşimi.
  - `needed` = runSeq op'larının `Access.Reads ∪ Access.Updates` − produced (ordinal-sıralı distinct).
  - her needed entity için: creators[e].Count==1 → `PrereqStep(e, creators[e][0], Single)`;
    >1 → `PrereqStep(e, null, Ambiguous)`; 0 → `PrereqStep(e, null, Missing)`.
  - **Topolojik sıra:** Single prereq'leri creator-op bağımlılığına göre sırala; eşitlikte **entity-id ordinal**
    tie-break (determinizm — §11 HIGH). Ambiguous/Missing'ler sona, entity-id ordinal.
- `WriteSet` = runSeq op'larının `Access.{Creates,Updates,Deletes}` birleşimi (ordinal distinct) — Kapı 0 çapası.
- ProcessTest/ScenarioTest'leri Prerequisites + WriteSet ile doldur.

### 6. Acceptance tests
#### 6.1 Compile
```bash
dotnet build src/Gen.Core/Gen.Core.csproj   # exit 0
```
#### 6.2 Pozitif — kanıt tablosu (tasarım §5)
studyo ile:
- `TakvimSureci` prereq = [`ClassType`→`TanimlaDersTipi` (Single)]
- `RandevuKatilimSureci` prereq entity'leri = {ClassType, Package, Session}, hepsi Single
- `UyelikPaketSureci` prereq = [] (self-contained)
- `WriteSet(OlusturRandevu içeren test)` ⊇ {Appointment, Session, Package}
#### 6.3 Negatif — Ambiguous
`UsageRecord` (creators: `IsaretleGeldi`,`SeansaGiris`) gereken bir test'te → `PrereqStep(UsageRecord, null, Ambiguous)`.
Sıfır-creator uydurma entity → `Missing`. Beklenen: CreatorOp null, Kind doğru; **sessiz seçim YOK**.

### 7. Out of scope
- DUR-marker / build-report emisyonu YOK — T-3.2 (burada yalnız `PrereqKind` sınıflar).
- Emitter YOK — M2.
- `operations.json access` okuma YASAK — yalnız `manifest.access`.

### 8. Anti-patterns
- Çoklu-creator'da **birini seç** (ör. ilk/ordinal) → YASAK; `Ambiguous` işaretle (Q5=DUR).
- `operations.json` `{reads,writes}` access'ini kaynak alma → manifest 4-key.
- Topo-sort'ta tie-break'siz sıra → determinizm kırılır; entity-id ordinal tie-break şart.

### 9. Definition of Done
- [ ] Prereq türetme + PrereqKind sınıflama eklendi.
- [ ] `Build` imzası `manifestOps` aldı.
- [ ] 6.2 kanıt tablosu tutuyor (TakvimSureci/Randevu/UyelikPaket/WriteSet).
- [ ] 6.3 UsageRecord→Ambiguous, sıfır→Missing, CreatorOp=null.
- [ ] Topo-sort entity-id ordinal tie-break'li (stabil).
- [ ] `git diff --stat` yalnız `TestPlanBuilder.cs`.

### 10. Self-check
1. 6.2'yi gerçek veriyle koştum mu? 2. Çoklu-creator'ı gerçekten Ambiguous mu yaptım, yoksa birini mi seçtim? 3. Kaynak manifest.access mi (writes değil)? 4. Topo-sort iki kez koşunca aynı sıra mı? 5. WriteSet Package'ı içeriyor mu?

---

## 🔴 T-1.5 — `GenerationModel.TestPlan` alanı + `GmBuilder` wiring

### 1. Goal
`GenerationModel`'e `TestPlan TestPlan` alanı ekle; `GmBuilder.Build` içinde `TestPlanBuilder.Build(contract, m.Operations)`
çağrısıyla doldur.

### 2. Why
Tasarım §3b: TestPlan GM'in türevi. Emitter (M2) `gm.TestPlan`'ı tüketir.

### 3. Inputs
- `src/Gen.Core/Gm/GenerationModel.cs` — `GenerationModel` record (son param `TypeEnv Env`).
- `src/Gen.Core/Pipeline/GmBuilder.cs` — `Build` return-construction (satır 38-55) + `BuildOp`.
- `src/Gen.Core/Pipeline/TestPlanBuilder.cs` (T-1.3/1.4).

### 4. Pre-conditions
```bash
grep -q "DerivePrerequisites\|Prerequisites" src/Gen.Core/Pipeline/TestPlanBuilder.cs   # T-1.4 done
dotnet build src/Gen.Core/Gen.Core.csproj   # exit 0
```

### 5. Changes
#### Step 5.1 — GM alanı (additive, sona)
**File:** `src/Gen.Core/Gm/GenerationModel.cs`
**Anchor:** `GenerationModel(` record, son param `TypeEnv Env` (search `TypeEnv Env)`)
**Action:** Modify — `TypeEnv Env`'den sonra `, TestPlan TestPlan` ekle (positional, sona).
#### Step 5.2 — GmBuilder doldur
**File:** `src/Gen.Core/Pipeline/GmBuilder.cs`
**Anchor:** `return new GenerationModel(` (search) — `Env: env` argümanından sonra
**Action:** Modify — `TestPlan: TestPlanBuilder.Build(contract, m.Operations)` ekle (son argüman).

### 6. Acceptance tests
#### 6.1 Compile + tüm çözüm
```bash
dotnet build src/Gen.Core/Gen.Core.csproj   # exit 0
dotnet build Gen.slnx 2>/dev/null || dotnet build src   # tüm src exit 0 (caller'lar kırılmadı)
```
#### 6.2 Pozitif — uçtan uca
Gerçek manifest+operations ile `GmBuilder.Build` → `gm.TestPlan.ProcessTests.Count == 4`.
#### 6.3 Negatif — standalone
`contract == null` (standalone mode) → `gm.TestPlan` boş plan (3 liste boş), exception YOK.

### 7. Out of scope
- Emitter YOK — M2. Test üretimi henüz GÖRÜNMEZ (yalnız IR dolu).

### 8. Anti-patterns
- `GenerationModel` parametre sırasını ortadan değiştirme → yeni alan **sona** (positional record; tüm caller'lar bozulur).
- `TestPlanBuilder.Build`'i yanlış argümanla çağırma (contract + m.Operations sırası).

### 9. Definition of Done
- [ ] GM `TestPlan` alanı (sona).
- [ ] GmBuilder dolduruyor.
- [ ] 6.1 tüm src build exit 0 (caller regression yok).
- [ ] 6.2 ProcessTests=4. 6.3 standalone→boş.
- [ ] `git diff --stat` yalnız `GenerationModel.cs` + `GmBuilder.cs`.

### 10. Self-check
1. Tüm src build exit 0 mu (sadece Gen.Core değil)? 2. GM alanı sona mı eklendi? 3. Standalone null-contract çöküyor mu? 4. Caller'ları (DotnetEmitter/GoEmitter) grep'leyip kırılmadığını gördüm mü?

---

# M2 — DotnetEmitter test-pass (owned iskelet + field-ASSERT + seam)

**Amaç:** `gm.TestPlan`'dan xUnit test projesi emit et: owned iskelet (`tests/gen/`) + field-ASSERT + ARRANGE seam
(`tests/src/`) + `tests/Tests.csproj`. **Milestone DoD:** generator çalışınca `tests/` projesi oluşur, `dotnet build`
edilebilir (ARRANGE seam'leri stub; build geçer). **Anti-circularity:** ASSERT owned'da (üretilir), ARRANGE seam'de (LLM).

---

## 🔴 T-2.1 — `EmitTests`: owned test iskeleti (xUnit, 3-faz)

### 1. Goal
`DotnetEmitter`'a `EmitTests(gm, outDir, report)` ekle; her ProcessTest/ScenarioTest için owned xUnit test
sınıfı emit et (`tests/gen/{Scope}/{Name}.g.cs`): 3-faz iskelet (temiz-data hook · ön-gereksinim çağrıları ·
RunSequence) + ARRANGE seam çağrısı + ASSERT placeholder (T-2.2 doldurur).

### 2. Why
Tasarım §3c: Generation Gap — owned iskelet `WriteAlways` (provenance/prune), ARRANGE `WriteIfAbsent`. xUnit `[Fact]`.

### 3. Inputs
- `src/Gen.Dotnet/DotnetEmitter.cs` — `Emit` gövdesi (satır 17-162); özellikle `WriteAlways`/`WriteIfAbsent`/
  `_written` mekaniği + op feature-slice deseni (satır 96-105) + `WriteProvenanceAndPrune` çağrısı (satır 161).
- `src/Gen.Core/Gm/TestPlan.cs` (T-1.2).
- `tasks/process-test-generation-design.md` §3c, §3d (iskelet yapısı).

### 4. Pre-conditions
```bash
grep -q "TestPlan TestPlan" src/Gen.Core/Gm/GenerationModel.cs   # T-1.5 done
dotnet build src   # exit 0
```

### 5. Changes
#### Step 5.1 — EmitTests metodu + çağrı
**File:** `src/Gen.Dotnet/DotnetEmitter.cs`
**Anchor:** `WriteAlways(Path.Combine(gen, "Bootstrap.g.cs")...` (satır 158) **ÖNCESİ** — `EmitTests(gm, outDir, report);` çağrısı ekle (provenance/prune satır 161'den ÖNCE olmalı ki owned test dosyaları track edilsin).
**Action:** Add (çağrı) + Add (yeni private static method `EmitTests`).
Mantık:
- `if (gm.TestPlan is null) return;` (defansif).
- `testsGen = Path.Combine(outDir, "tests", "gen")`, `testsSrc = Path.Combine(outDir, "tests", "src")`.
- Her ProcessTest → owned dosya `tests/gen/Process/{ProcessId}.g.cs`: `public partial class {ProcessId}Tests` +
  bir `[Fact] public async Task Runs() {...}` metodu. Gövde 3-faz:
  1. `// temiz data` — `await Fixture.ResetAsync();` (Fixture hook; gen-owned helper, T-2.4'te csproj + base fixture).
  2. ön-gereksinim — her `Single` PrereqStep için `await {CreatorOp}Handler...` çağrısı + ARRANGE seam'den payload.
  3. RunSequence — sıralı op çağrıları.
  4. `AssertBlock` placeholder yorumu `// ASSERT: T-2.2` (T-2.2 doldurur).
  - ARRANGE değerleri için `partial` method çağrısı: `Arrange{ProcessId}(...)` → seam'de (T-2.3).
- OrphanFlow/OrphanOp → `tests/gen/OrphanFlow/{Id}.g.cs` / `tests/gen/OrphanOp/{Id}.g.cs`, aynı desen.
- `Ambiguous`/`Missing` prereq'li test → iskeleti **emit ETME** (T-3.2 DUR-marker); yalnız Single/boş prereq'liler.
- Hepsi `WriteAlways` (owned). Determinizm: TestPlan zaten ordinal; ek sıralama gerekmez.

### 6. Acceptance tests
#### 6.1 Üretim + build
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
dotnet build src   # exit 0
# generator'ı studyo fixture'ına çalıştır (CLI yolu: src/Gen.Cli):
dotnet run --project src/Gen.Cli -- <manifest> <out>   # exit 0; tests/gen/ oluşur
ls <out>/tests/gen/Process | wc -l   # expected: 4 dosya (Ambiguous'lar hariç olabilir)
```
#### 6.2 Pozitif — iskelet şekli
`<out>/tests/gen/Process/TakvimSureci.g.cs` içerir: `[Fact]`, `partial class TakvimSureciTests`, `ResetAsync`,
RunSequence op adları, `// ASSERT: T-2.2`.
#### 6.3 Negatif — Ambiguous emit edilmez
Ambiguous prereq'li bir test owned iskelet olarak **emit EDİLMEZ** (T-3.2'ye bırakılır). `ls` ile yokluğu doğrula.

### 7. Out of scope
- Field-ASSERT gövdesi YOK — T-2.2 (placeholder yorum yeter).
- ARRANGE seam dosyası YOK — T-2.3.
- Tests.csproj YOK — T-2.4 (bu task'tan sonra build için csproj gerekebilir → T-2.4 paralel; M2 DoD ikisini ister).
- DUR-marker/build-report YOK — T-3.x.

### 8. Anti-patterns
- Owned iskeleti `WriteIfAbsent` ile yazma → owned `WriteAlways` (her run yenilenir, prune'lu). Sadece ARRANGE seam WriteIfAbsent.
- Test dosyalarını `WriteProvenanceAndPrune` SONRASI yazma → track edilmez, prune onları bozar. ÖNCE yaz.
- Ambiguous/Missing'i sessizce iskeletle → T-3.2 DUR; emit etme.

### 9. Definition of Done
- [ ] `EmitTests` eklendi, provenance/prune ÖNCESİ çağrılıyor.
- [ ] 6.1 generator exit 0, `tests/gen/Process` 4 (veya Single-only) dosya.
- [ ] 6.2 iskelet şekli (Fact/partial/Reset/RunSequence/ASSERT-placeholder).
- [ ] 6.3 Ambiguous emit edilmiyor.
- [ ] owned dosyalar `WriteAlways` (provenance'a giriyor — T-3.3 doğrular).
- [ ] `git diff --stat` yalnız `DotnetEmitter.cs`.

### 10. Self-check
1. Generator'ı gerçekten çalıştırıp `tests/gen/` oluştuğunu gördüm mü? 2. EmitTests provenance ÖNCESİ mi çağrılıyor? 3. owned WriteAlways mı? 4. Ambiguous gerçekten atlandı mı? 5. Determinizm korundu mu?

---

## 🔴 T-2.2 — Field-ASSERT üretimi (`effects` → assert, ExprBuild reuse)

### 1. Goal
`EmitTests` iskeletindeki ASSERT placeholder'ını contract-türevli field-seviyesi assertion ile doldur:
`calculate` effect → `Assert.Equal(<expr>, entity.Field)` (ExprBuild reuse), `create`/WriteSet → varlık,
`throws` → negatif.

### 2. Why
Tasarım §3d + Q3: field-seviyesi ASSERT **üretilir** (LLM değil) → anti-circularity. 25 `calculate` effect düz
değer; ExprBuild zaten ExprNode→C# render ediyor.

### 3. Inputs
- `src/Gen.Core/Predicate/ExprBuild.cs` — `Build(ExprNode, resolveType)` → `(Expr, Paths)`; literal render (`Literal`).
- `src/Gen.Core/Model/ExprNode.cs` — `LiteralNode`/`PathNode`/`BinaryNode`.
- `src/Gen.Core/Model/Manifest.cs` — `OperationJson.Access`; **NOT:** `effects` manifest'te değil **operations.json**'da
  (`ContractOp.Effects` = `ContractEffect(Kind, Target, Expr, Text)` — **`Expr`/`Text` T-1.1 Step 5.3'te eklendi**).
  Field-ASSERT effects'i **contract'tan** (GmOperation.Business) okur; effect-değeri `Expr` (varsa) → ExprBuild,
  yoksa `Text` literal'i. **İkisi de null ise** (eski/değersiz effect) → o effect için field-assert ATLA, WriteSet-varlık tabanına düş (graceful).
- `src/Gen.Dotnet/DotnetEmitter.cs` — T-2.1 EmitTests + ASSERT placeholder.
- `tasks/process-test-generation-design.md` §3d (effect→assert tablosu).

### 4. Pre-conditions
```bash
grep -q "EmitTests" src/Gen.Dotnet/DotnetEmitter.cs   # T-2.1 done
grep -q "ASSERT: T-2.2" src/Gen.Dotnet/DotnetEmitter.cs # placeholder mevcut
grep -q "Expr.*Text\|Text.*Expr" src/Gen.Core/Model/Contract.cs  # T-1.1 Step 5.3 (effect değeri) done
dotnet build src   # exit 0
```
> **NOT:** `ContractEffect.Target` toleranslı `JsonElement?` (string VEYA path dizisi). `calculate` effect'te
> `target=[Entity,field]`, değer `Expr` (ExprNode) ya da `Text` ('iptal'). Field-ASSERT Target path'ini parse eder,
> değeri `Expr`→ExprBuild ya da `Text`'ten alır; ikisi de yoksa o effect'i atlar (WriteSet-varlık tabanı kalır).

### 5. Changes
#### Step 5.1 — `AssertBlock` helper
**File:** `src/Gen.Dotnet/DotnetEmitter.cs`
**Anchor:** `EmitTests` metodu (T-2.1)
**Action:** Add `static string AssertBlock(GmOperation op, ...)`:
- op.Business.Effects üzerinden:
  - `kind=="calculate"` + `Target=[Entity,field]` → `Assert.Equal(<value>, <entity>.<Field>);` — value, effect'in
    `expr`/`text`'inden (ExprNode varsa `ExprBuild`'in literal render'ı; düz `text` literal'i ise doğrudan).
  - `kind=="create"` → `Assert.NotNull(<created entity>);` (WriteSet ile örtüşür).
  - `kind=="send"` → event-emit assertion (`emits` üzerinden — davranışsal, basit "emitted" kontrolü).
  - `kind=="perform"` → downstream op-çağrı assertion (callEdge — basit kontrol).
- WriteSet entity'leri için varlık assertion'ı (taban).
- op.Op.Throws için ilgili negatif test (ayrı `[Fact]` veya not) — minimum: throws varsa yorum-anchor + temel negatif.
- ASSERT placeholder'ını bu blokla değiştir.

### 6. Acceptance tests
#### 6.1 Üretim + build
```bash
dotnet build src   # exit 0
dotnet run --project src/Gen.Cli -- <manifest> <out>   # exit 0
```
#### 6.2 Pozitif — field-assert mevcut
`<out>/tests/gen/OrphanOp/IptalRandevu.g.cs` (effect: `Appointment.status='iptal'`) içerir:
`Assert.Equal("iptal", ` ... `.Status)`. (calculate→field eşlemesi.)
#### 6.3 Negatif — LLM-assert YOK
Üretilen `.g.cs`'te ARRANGE bölgesi DIŞINDA `doldurulacak` marker'ı YOK — ASSERT tamamen üretilmiş (LLM'e bırakılmamış).
`grep -L "doldurulacak" <out>/tests/gen/**/*.g.cs` → tüm owned dosyalar (assert owned).

### 7. Out of scope
- ARRANGE seam YOK — T-2.3. Koşullu-effect [Theory] YOK (fixture'da 0 koşullu; gelecek).
- send/perform'u tam davranışsal harness'a bağlama YOK — minimum assertion yeter (derinlik sonra).

### 8. Anti-patterns
- ASSERT'i ARRANGE seam'e (LLM) koyma → anti-circularity ihlali; ASSERT owned `.g.cs`'te.
- `operations.json` text'ini ham string olarak C#'e gömerken kaçış/literal-tip hatası → ExprBuild literal render'ını kullan (decimal 'm' suffix vb.).
- Target path'i 2-eleman varsaymak yerine toleranslı parse (string VEYA dizi — `ContractEffect.Target` JsonElement).

### 9. Definition of Done
- [ ] `AssertBlock` calculate/create/send/perform + WriteSet + throws ele alıyor.
- [ ] 6.2 IptalRandevu field-assert (`Assert.Equal("iptal", ...Status)`).
- [ ] 6.3 owned `.g.cs`'te ASSERT marker'sız (LLM-assert yok).
- [ ] `git diff --stat` yalnız `DotnetEmitter.cs`.

### 10. Self-check
1. Generator çıktısında gerçek `Assert.Equal` gördüm mü? 2. ASSERT owned'da mı, seam'de mi? 3. ExprBuild literal render'ını mı kullandım (elle string mi)? 4. Target path'i toleranslı mı parse ettim?

---

## 🔴 T-2.3 — ARRANGE human-seam (`tests/src/`, WriteIfAbsent, marker)

### 1. Goal
Her test için ARRANGE gövdesini human-seam olarak emit et: `tests/src/{Scope}/{Name}.Logic.cs` — `partial`
metod(lar) `Arrange{Name}(...)` + `doldurulacak` marker (temiz-data + ön-gereksinim payload). `WriteIfAbsent`.

### 2. Why
Tasarım §3c/§3d: LLM yalnız ARRANGE doldurur; iskelet owned partial method'u çağırır, gövde seam'de. Mevcut
`{op}Handler.Logic.cs` deseninin birebir aynısı (WriteIfAbsent + marker).

### 3. Inputs
- `src/Gen.Dotnet/DotnetEmitter.cs` — `LogicFile` helper (satır 105 çağrılır) + `WriteIfAbsent` (satır 213) +
  `MigrateSeamIfFlat` deseni. T-2.1 EmitTests.
- Marker biçimi: base-dotnet-rest descriptor `emptyStubMarker` (`{op}: iş mantığı doldurulacak` ailesi). Test için
  `Arrange {Name}: doldurulacak`.

### 4. Pre-conditions
```bash
grep -q "EmitTests" src/Gen.Dotnet/DotnetEmitter.cs   # T-2.1
grep -q "AssertBlock" src/Gen.Dotnet/DotnetEmitter.cs # T-2.2
dotnet build src   # exit 0
```

### 5. Changes
#### Step 5.1 — ARRANGE seam emisyonu
**File:** `src/Gen.Dotnet/DotnetEmitter.cs`
**Anchor:** `EmitTests` metodu — owned `.g.cs` yazımından sonra
**Action:** Add — her test için:
- owned iskelet `partial class {Name}Tests` içinde `partial void Arrange{Name}(/*payload out params*/);` bildirir
  (ya da `partial Task<...> Arrange...`). Gövde seam'de.
- `WriteIfAbsent(Path.Combine(testsSrc, scope, $"{Name}.Logic.cs"), TestArrangeSeam(test))` — `TestArrangeSeam`
  içerik: `partial class {Name}Tests { partial void Arrange{Name}(...) { /* Arrange {Name}: doldurulacak */ } }`.
- Seam **yalnız ARRANGE** — ASSERT owned'da kalır (T-2.2).

### 6. Acceptance tests
#### 6.1 Üretim + build
```bash
dotnet run --project src/Gen.Cli -- <manifest> <out>   # exit 0
ls <out>/tests/src/Process/TakvimSureci.Logic.cs   # mevcut
```
#### 6.2 Pozitif — marker + WriteIfAbsent
`TakvimSureci.Logic.cs` `doldurulacak` marker içerir. Dosyayı elle değiştir, generator'ı **tekrar koş** → dosya
**EZİLMEZ** (WriteIfAbsent). owned `.g.cs` ise yenilenir.
#### 6.3 Negatif — ASSERT seam'de değil
`grep -c "Assert\." <out>/tests/src/**/*.Logic.cs` → **0** (ASSERT seam'de YOK; owned'da).

### 7. Out of scope
- ARRANGE gövdesini doldurma YOK — bu LLM'in işi (M4 skill `test-arrange`).
- ASSERT'e dokunma — owned (T-2.2).

### 8. Anti-patterns
- ARRANGE seam'i `WriteAlways` ile yazma → kullanıcı dolumu ezilir; `WriteIfAbsent` şart.
- Seam'e ASSERT koyma → anti-circularity.
- Marker substring'i `doldurulacak` taşımıyorsa base-dotnet-rest tespit edemez → marker ailesine uy.

### 9. Definition of Done
- [ ] ARRANGE seam `tests/src/{Scope}/{Name}.Logic.cs`, WriteIfAbsent, marker.
- [ ] 6.2 tekrar-üretimde seam ezilmiyor.
- [ ] 6.3 seam'de Assert yok.
- [ ] `git diff --stat` yalnız `DotnetEmitter.cs`.

### 10. Self-check
1. Seam WriteIfAbsent mı (tekrar koşunca ezilmiyor mu)? 2. Marker `doldurulacak` içeriyor mu? 3. ASSERT seam'de değil owned'da mı? 4. Generator'ı iki kez koşup davranışı gözledim mi?

---

## 🟢 T-2.4 — `Tests.csproj` (xUnit, HumanShell)

**Önkoşullar:** T-2.1
**Referans:** tasarım §3c (Q1=xUnit), §9.
**Yapılacaklar:**
- `EmitTests` içinde `WriteIfAbsent(Path.Combine(outDir,"tests","Tests.csproj"), TestsCsproj())` ekle.
- `TestsCsproj()` içerik: net10.0 test SDK + `xunit` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`
  PackageReference + `<ProjectReference Include="../App.csproj" />` + owned `tests/gen/**` ve seam `tests/src/**` Compile include.
- HumanShell: `WriteIfAbsent` (yoksa-üret, asla ezilmez — App.csproj/Program.cs deseni, satır 26/159).

**Dosyalar:** `src/Gen.Dotnet/DotnetEmitter.cs`

**DoD:**
- [ ] `dotnet run --project src/Gen.Cli -- <manifest> <out>` sonrası `<out>/tests/Tests.csproj` var.
- [ ] `dotnet build <out>/tests/Tests.csproj` exit 0 (seam stub'larla derlenir).
- [ ] WriteIfAbsent: tekrar üretimde ezilmiyor.
- [ ] Manuel: csproj App'i referans ediyor + xunit paketleri var.

---

# M3 — build-report / provenance + DUR-marker

**Amaç:** Test construct'larını build-report'a kaydet (family-gate görsün); Ambiguous/Missing → DUR-marker
(unsupported); owned test `.g.cs` → provenance. **Milestone DoD:** build-report'ta `test` construct'ları + (varsa)
`test-prereq` unsupported; `provenance.json` owned test dosyalarını içerir.

---

## 🔴 T-3.1 — build-report `test` construct kayıtları

### 1. Goal
`EmitTests` her emit edilen test için `report.Realized("test", testId)` çağırsın (family-gate completeness envanteri).

### 2. Why
Tasarım §3e: test = construct; family-gate manifest/plan'dan enumerate edip realized mı diye bakar. Kayıt olmazsa
sessiz-düşüş gibi görünür.

### 3. Inputs
- `src/Gen.Core/Report/BuildReport.cs` — `Realized(construct, id)` (satır 19).
- `src/Gen.Dotnet/DotnetEmitter.cs` — `EmitTests` (T-2.1), `report` parametresi (mevcut `Realized` çağrı deseni satır 154).

### 4. Pre-conditions
```bash
grep -q "EmitTests" src/Gen.Dotnet/DotnetEmitter.cs   # T-2.1 done
dotnet build src   # exit 0
```

### 5. Changes
#### Step 5.1 — Realized çağrısı
**File:** `src/Gen.Dotnet/DotnetEmitter.cs`
**Anchor:** `EmitTests` — her owned test `.g.cs` `WriteAlways`'inden sonra
**Action:** Add — `report.Realized("test", testId);` (testId = `Process_{ProcessId}` / `OrphanFlow_{Id}` / `OrphanOp_{Id}` — tasarım §10 id şeması).

### 6. Acceptance tests
#### 6.1 Üretim + rapor
```bash
dotnet run --project src/Gen.Cli -- <manifest> <out>   # exit 0
grep -c '"construct": "test"' <out>/<build-report.json yolu>   # expected: emit edilen test sayısı (Single/boş prereq)
```
#### 6.2 Pozitif
build-report'ta `Process_TakvimSureci` realized.
#### 6.3 Negatif
Ambiguous test (T-3.2 unsupported) `realized` DEĞİL (T-3.2 ile koordine).

### 7. Out of scope
- DUR-marker (unsupported) YOK — T-3.2.
- provenance YOK — T-3.3.

### 8. Anti-patterns
- Tüm 23 testi realized işaretleyip Ambiguous'ları da realized sayma → T-3.2 ile çelişir; yalnız gerçekten emit edilenler realized.
- id şemasını rastgele seçme → tasarım §10 (`Process_`/`OrphanFlow_`/`OrphanOp_`).

### 9. Definition of Done
- [ ] Her emit edilen test `report.Realized("test", id)`.
- [ ] 6.1/6.2 build-report'ta test construct'ları.
- [ ] `git diff --stat` yalnız `DotnetEmitter.cs`.

### 10. Self-check
1. build-report çıktısını gerçekten grep'leyip gördüm mü? 2. id şeması §10 ile aynı mı? 3. Yalnız emit edilenler mi realized?

---

## 🔴 T-3.2 — DUR-marker: Ambiguous/Missing prereq → unsupported

### 1. Goal
`EmitTests`, `PrereqKind.Ambiguous`/`Missing` içeren bir test için iskelet emit ETMEZ; bunun yerine
`report.Unsupported("test-prereq", testId, reason)` ile DUR-marker yazar.

### 2. Why
Tasarım §5 + Q5=DUR: belirsiz ön-gereksinim sessizce çözülmez; üretici unsupported işaretler → base-dotnet-rest
fill-time'da gap-protokol DUR+sor (K4).

### 3. Inputs
- `src/Gen.Core/Report/BuildReport.cs` — `Unsupported(construct, id, reason)` (satır 20).
- `src/Gen.Dotnet/DotnetEmitter.cs` — `EmitTests` (T-2.1) Ambiguous-skip mantığı.
- `src/Gen.Core/Gm/TestPlan.cs` — `PrereqKind`.

### 4. Pre-conditions
```bash
grep -q "PrereqKind" src/Gen.Core/Gm/TestPlan.cs   # T-1.2
grep -q "EmitTests" src/Gen.Dotnet/DotnetEmitter.cs # T-2.1
dotnet build src   # exit 0
```

### 5. Changes
#### Step 5.1 — DUR-marker
**File:** `src/Gen.Dotnet/DotnetEmitter.cs`
**Anchor:** `EmitTests` — test başına emit kararı (T-2.1'deki "Ambiguous emit etme" noktası)
**Action:** Modify — `if (test.Prerequisites.Any(p => p.Kind != PrereqKind.Single))` → iskelet/seam yazma; yerine
`report.Unsupported("test-prereq", testId, $"ön-gereksinim belirsiz/eksik: {entity} ({kind})");`. Reason, hangi entity + Ambiguous/Missing içersin.

### 6. Acceptance tests
#### 6.1 Üretim + rapor
```bash
dotnet run --project src/Gen.Cli -- <manifest> <out>   # exit 0
grep '"test-prereq"' <out>/<build-report>   # Ambiguous/Missing test'ler için unsupported kaydı
```
#### 6.2 Pozitif
`UsageRecord` Ambiguous'una bağlı bir test → `test-prereq` unsupported, reason `UsageRecord (Ambiguous)`.
#### 6.3 Negatif — sessiz seçim yok
Ambiguous test'in owned iskeleti `tests/gen/` altında **YOK** (emit edilmedi); üretici creator **seçmedi**.

### 7. Out of scope
- gap çözümü YOK (skill fill-time'ın işi — M4).
- realized işaretleme YOK (T-3.1; bu unsupported).

### 8. Anti-patterns
- Ambiguous'ta bir creator **seçip** iskelet üretme → Q5=DUR ihlali.
- Missing'i sessiz atlama → unsupported KAYDET (no-silent-loss).

### 9. Definition of Done
- [ ] Ambiguous/Missing → `Unsupported("test-prereq", ...)`, iskelet emit YOK.
- [ ] 6.2 UsageRecord unsupported kaydı + reason.
- [ ] 6.3 iskelet yok (seçim yapılmadı).
- [ ] `git diff --stat` yalnız `DotnetEmitter.cs`.

### 10. Self-check
1. Ambiguous gerçekten emit edilmeyip unsupported mu yazıldı? 2. Reason entity+kind içeriyor mu? 3. Üretici creator seçmedi, değil mi?

---

## 🔴 T-3.3 — Owned test `.g.cs` → provenance/prune

### 1. Goal
owned test iskeletlerinin (`tests/gen/**`) `WriteAlways` ile yazıldığını ve `WriteProvenanceAndPrune`'a dahil
olduğunu **doğrula/garanti et** (FileClass.Generated; sha + orphan-prune).

### 2. Why
Tasarım §3e + §11: owned-tree dokunulmazlık denetimi (family-gate) test dosyalarını da kapsamalı; contract'tan
kalkan test (op süreçten çıktı) bir sonraki run'da prune edilmeli.

### 3. Inputs
- `src/Gen.Dotnet/DotnetEmitter.cs` — `WriteAlways` (`_written`'a ekler) + `WriteProvenanceAndPrune` (satır 161-191).
- `src/Gen.Dotnet/Provenance.cs` — `FileClass.Generated`, `ProvenanceEntry`.

### 4. Pre-conditions
```bash
grep -q "EmitTests" src/Gen.Dotnet/DotnetEmitter.cs   # T-2.1
dotnet build src   # exit 0
```

### 5. Changes
#### Step 5.1 — owned test'lerin WriteAlways'le yazıldığını teyit
**File:** `src/Gen.Dotnet/DotnetEmitter.cs`
**Anchor:** `EmitTests` owned yazımları
**Action:** Verify/Modify — owned test `.g.cs` yazımları **`WriteAlways`** (T-2.1'de öyle olmalı). `WriteAlways`
zaten `_written`'a ekliyorsa (kontrol et) provenance/prune otomatik kapsar. ARRANGE seam (`tests/src`) **WriteIfAbsent**
→ provenance'a girmez (HumanSeam; doğru). Ek kod gerekmeyebilir — bu task **doğrulama + (gerekirse) düzeltme**.

### 6. Acceptance tests
#### 6.1 provenance içerik
```bash
dotnet run --project src/Gen.Cli -- <manifest> <out>   # exit 0
grep "tests/gen/" <out>/provenance.json   # owned test dosyaları listeli, class "Generated"
grep -c "tests/src/" <out>/provenance.json   # expected: 0 (seam provenance'a girmez)
```
#### 6.2 Pozitif — prune
Bir process'i contract'tan çıkar (mock), generator'ı tekrar koş → ilgili `tests/gen/Process/{eski}.g.cs` **silinir** (orphan prune); seam (`tests/src`) **kalır** (insan-sahibi).
#### 6.3 Negatif — seam dokunulmaz
Prune, `tests/src/**` ARRANGE seam'lerini ASLA silmez (FileClass.Generated değil).

### 7. Out of scope
- family-gate skill tarafı YOK (kapı zaten manifest'ten enumerate eder — kesif tarafı, bu repo değil).

### 8. Anti-patterns
- owned test'i WriteIfAbsent yapma → prune/provenance kapsamaz, stale owned kalır.
- seam'i provenance'a sokma → insan dosyası prune edilir (felaket).

### 9. Definition of Done
- [ ] owned `tests/gen/**` provenance.json'da (Generated).
- [ ] seam `tests/src/**` provenance'a girmiyor (0).
- [ ] 6.2 prune owned orphan testi siliyor; seam kalıyor.
- [ ] `git diff --stat` yalnız `DotnetEmitter.cs` (veya değişiklik gerekmezse 0).

### 10. Self-check
1. provenance.json'da tests/gen var, tests/src yok — gözümle gördüm mü? 2. Prune owned'ı silip seam'i koruyor mu? 3. WriteAlways/WriteIfAbsent ayrımı doğru mu?

---

# M4 — base-dotnet-rest `test-arrange` + techgen-sync delta-sync

**Amaç:** Skill tarafı — LLM ARRANGE seam'i doldurur (ASSERT'e dokunmaz); contract değişince stale test-seam
delta-sync ile uzlaşır. **Milestone DoD:** base-dotnet-rest `test-arrange` arketipini tanımlar; techgen-sync
test-seam'leri reconcile eder; eval'ler eklendi.

---

## 🔴 T-4.1 — base-dotnet-rest: `test-arrange` arketipi (ARRANGE-only fill)

### 1. Goal
`base-dotnet-rest/SKILL.md`'ye yeni arketip `test-arrange`: `tests/src/**/*.Logic.cs` seam'lerini **yalnız ARRANGE**
doldur (temiz-data + ön-gereksinim payload); **ASSERT'e (owned `.g.cs`) DOKUNMA**.

### 2. Why
Tasarım §8 + §3d: anti-circularity — LLM ASSERT yazarsa totoloji. Mevcut seam-fill mekaniği (WriteIfAbsent+marker)
aynen; yeni arketip yalnız scope'u (ARRANGE) sınırlar.

### 3. Inputs
- `plugins/codegen/skills/base-dotnet-rest/SKILL.md` — Faz 2 arketip tablosu (`archetypeRules`) + Faz 4 fill + Kapı 0 (Faz 5).
- `plugins/codegen/skills/base-dotnet-rest/references/gap-protocol.md` — K-checks.
- `tasks/process-test-generation-design.md` §8.

### 4. Pre-conditions
```bash
test -f plugins/codegen/skills/base-dotnet-rest/SKILL.md   # mevcut
```

### 5. Changes
#### Step 5.1 — Arketip tablosu + Faz 4 not
**File:** `plugins/codegen/skills/base-dotnet-rest/SKILL.md`
**Anchor:** Faz 2 arketip tablosu (search `| Boundary-client |`) — yeni satır ekle:
`| Test-arrange | tests/src/**/*.Logic.cs seam (Arrange marker) |`
**Anchor 2:** Faz 4 sonrası — yeni alt-bölüm: `test-arrange` doldurma kuralı:
- Yalnız `Arrange{Name}` partial gövdesini doldur (temiz-data + Single prereq payload'ları).
- **ASSERT owned `.g.cs`'tedir — ASLA dokunma/yazma** (anti-circularity, A3).
- Kapı 0 (access-coverage) burada da geçerli: ARRANGE, manifest.access write-set'iyle tutarlı state kurar.
- Ön-gereksinim payload'ları: yalnız `Single` (üretici Ambiguous/Missing'i zaten DUR-marker'ladı; o test'e seam yok).

### 6. Acceptance tests (markdown skill — yapısal)
#### 6.1 Yapısal
`grep -c "test-arrange" SKILL.md` ≥ 2; `grep -c "ASSERT.*dokunma\|ASSERT.*owned" SKILL.md` ≥ 1.
#### 6.2 Pozitif
Arketip tablosunda `Test-arrange` satırı + tespit kuralı (`tests/src` + Arrange marker).
#### 6.3 Negatif
"ASSERT'i doldur" gibi bir ifade YOK (grep ile teyit: ASSERT yalnız "dokunma" bağlamında).

### 7. Out of scope
- techgen-sync delta-sync YOK — T-4.2.
- eval YOK — T-4.3.
- Üretici (C#) tarafı YOK — M2/M3.

### 8. Anti-patterns
- "LLM testi baştan yazar" deme → yalnız ARRANGE seam; iskelet+ASSERT üretici-owned.
- Kapı 0'ı test-arrange'da devre dışı bırakma → ARRANGE da manifest.access-tutarlı.

### 9. Definition of Done
- [ ] Arketip tablosunda `Test-arrange`.
- [ ] Faz 4 `test-arrange` kuralı (ARRANGE-only, ASSERT'e dokunma).
- [ ] 6.1/6.3 grep'leri geçer.
- [ ] `git diff --stat` yalnız `base-dotnet-rest/SKILL.md`.

### 10. Self-check
1. Arketip eklendi mi? 2. "ASSERT'e dokunma" açıkça yazılı mı? 3. ARRANGE-only scope net mi? 4. Üretici tarafına dokunmadım, değil mi?

---

## 🔴 T-4.2 — techgen-sync: test-seam stale reconciliation (delta-sync)

### 1. Goal
`techgen-sync/SKILL.md`'ye test-seam'lerini delta-sync'e dahil et: contract değişip owned test `.g.cs` yenilenince,
stale ARRANGE seam (eski imza) → **build-kırığı olarak yüzeyle** (handler seam'leriyle aynı mekanik); sessiz geçme yok.

### 2. Why
Tasarım §11 [HIGH] stale-seam: owned iskelet `WriteAlways` yenilenir, ARRANGE `WriteIfAbsent` bayatlar → derleme
kırığı. techgen-sync zaten handler seam'lerini reconcile ediyor; test-seam aynı sınıf.

### 3. Inputs
- `.claude/skills/techgen-sync/SKILL.md` — delta-sync + signature-uzlaştırma + orphan Logic.cs mantığı.
- `tasks/process-test-generation-design.md` §11 (stale-seam).

### 4. Pre-conditions
```bash
test -f .claude/skills/techgen-sync/SKILL.md   # mevcut
```

### 5. Changes
#### Step 5.1 — test-seam reconciliation notu
**File:** `.claude/skills/techgen-sync/SKILL.md`
**Anchor:** signature-değişimi/seam-uzlaştırma bölümü (search `Logic.cs` veya `signature`/`imza`)
**Action:** Add — test-seam'leri (`tests/src/**/*.Logic.cs`) handler seam'leriyle **aynı** delta-sync'e dahil:
- owned test `.g.cs` (yeni `Arrange{Name}` imzası) ↔ stale ARRANGE seam → `dotnet build` kırığı yüzeye çıkar (sessiz değil).
- Süreçten çıkan test → owned prune (T-3.3) + orphan ARRANGE seam'i tespit et → kullanıcıya sil/taşı sor (handler orphan deseni).

### 6. Acceptance tests (yapısal)
#### 6.1 `grep -c "tests/src\|test-seam" .claude/skills/techgen-sync/SKILL.md` ≥ 1.
#### 6.2 Stale test-seam'in build-kırığı olarak yüzeyleneceği yazılı.
#### 6.3 Orphan test-seam dispozisyonu (sil/taşı sor) yazılı.

### 7. Out of scope
- base-dotnet-rest arketip YOK — T-4.1.
- Üretici tarafı YOK.

### 8. Anti-patterns
- Stale test-seam'i sessiz geçme → build-kırığı yüzeyle.
- Orphan ARRANGE seam'i otomatik silme → kullanıcıya sor (insan-sahibi).

### 9. Definition of Done
- [ ] test-seam delta-sync'e dahil edildi.
- [ ] stale → build-kırığı; orphan → sor.
- [ ] `git diff --stat` yalnız `techgen-sync/SKILL.md`.

### 10. Self-check
1. test-seam handler seam'iyle aynı mekanikte mi? 2. Stale sessiz geçmiyor mu? 3. Orphan'a sor deniyor mu?

---

## 🟢 T-4.3 — base-dotnet-rest evals: test-arrange senaryosu

**Önkoşullar:** T-4.1
**Referans:** tasarım §8; mevcut `base-dotnet-rest/evals/evals.json` deseni.
**Yapılacaklar:**
- `evals.json`'a yeni senaryo: bir `tests/src/.../X.Logic.cs` ARRANGE seam'i doldurulur; ASSERT (owned) doldurulmaz;
  ön-gereksinim Single payload kurulur; Kapı 0 ile manifest.access-tutarlı. expected_output: yalnız ARRANGE doldu, ASSERT'e dokunulmadı.
- (negatif) ASSERT'e dokunma girişimi → YASAK (anti-circularity).

**Dosyalar:** `plugins/codegen/skills/base-dotnet-rest/evals/evals.json`

**DoD:**
- [ ] `python3 -c "import json;json.load(open('.../evals.json'))"` geçerli.
- [ ] Yeni eval id eklendi (test-arrange, pozitif + negatif).
- [ ] Manuel: senaryo ARRANGE-only + ASSERT-dokunma-yasağını içeriyor.

---

# M5 — Verify (xUnit build + run yeşil) + determinizm

**Amaç:** Uçtan uca: generator → xUnit projesi build + (ARRANGE dolu) run yeşil; üretim deterministik (byte-stabil).
**Milestone DoD:** studyo fixture'tan üretilen test projesi derlenir; determinizm provenance sha ile sabit.

---

## 🔴 T-5.1 — E2E: generator → `tests/` projesi derlenir

### 1. Goal
studyo manifest+operations'tan generator'ı çalıştır; üretilen `tests/Tests.csproj` (owned iskelet + ASSERT +
stub ARRANGE seam) **`dotnet build` exit 0**.

### 2. Why
Tasarım §Sonuç M5: iskelet+ASSERT contract-türevli; ARRANGE stub → build geçer (davranış M4 dolumundan sonra).

### 3. Inputs
- `src/Gen.Cli/Program.cs` — CLI giriş (manifest+out argümanları).
- studyo fixture: `Silinecek1/cikti/manifest.json` + `studyo.operations.json` (ya da repo fixture).
- Tüm M1–M3 çıktısı.

### 4. Pre-conditions
```bash
dotnet build src   # exit 0
grep -q "EmitTests" src/Gen.Dotnet/DotnetEmitter.cs && grep -q "TestsCsproj" src/Gen.Dotnet/DotnetEmitter.cs
```

### 5. Changes
(Kod değişikliği yok beklenir — bu bir **entegrasyon doğrulama** task'ı. Build kırılırsa kök-neden M1–M3'e geri besle.)
#### Step 5.1 — Çalıştır + derle
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
OUT=$(mktemp -d)
dotnet run --project src/Gen.Cli -- "Silinecek1/cikti/manifest.json" "$OUT"   # exit 0
dotnet build "$OUT/tests/Tests.csproj"   # expected: exit 0
```

### 6. Acceptance tests
#### 6.1 `dotnet build $OUT/tests/Tests.csproj` exit 0.
#### 6.2 Pozitif: `$OUT/tests/gen/Process/` 4 (veya Single-only) owned `.g.cs`; her biri `[Fact]` + `Assert.`.
#### 6.3 Negatif: ARRANGE seam stub'ları `doldurulacak` içerir ama yine de **derlenir** (boş partial gövde geçerli C#).

### 7. Out of scope
- ARRANGE doldurma (davranışsal yeşil) YOK — T-5.2 (M4 sonrası).
- Yeni özellik YOK — yalnız doğrula; kırılırsa upstream task fix.

### 8. Anti-patterns
- Build kırığını "beklenen" sayma → kök-neden M1–M3'e dön, düzelt (CLAUDE.md yarım-bırakma).
- Eksik csproj/paket → T-2.4'e geri besle.

### 9. Definition of Done
- [ ] generator exit 0, `tests/` üretildi.
- [ ] `dotnet build tests/Tests.csproj` exit 0.
- [ ] owned `.g.cs`'lerde Fact+Assert.
- [ ] Build kırığı varsa ilgili upstream task'a issue olarak yazıldı + düzeltildi.

### 10. Self-check
1. Build exit 0'ı gözümle gördüm mü? 2. tests/gen owned dosyalar Fact+Assert mı? 3. Kırık varsa normalize mi ettim yoksa fix mi?

---

## 🔴 T-5.2 — E2E: determinizm + (ARRANGE dolu) test run yeşil

### 1. Goal
(a) Generator'ı **iki kez** çalıştır → owned `tests/gen/**` **byte-aynı** (provenance sha sabit). (b) Bir process
test'inin ARRANGE'ını elle doldurup `dotnet test` → **yeşil** (ASSERT contract-türevli geçer).

### 2. Why
Tasarım §4/§11: determinizm invariant'ı (ordinal); ASSERT contract-çapalı olduğundan ARRANGE doğru kurulunca yeşil
**totoloji değil** (LLM assertion yazmadı).

### 3. Inputs
- T-5.1 çıktısı (`$OUT`).
- `src/Gen.Dotnet/Provenance.cs` — sha.

### 4. Pre-conditions
```bash
# T-5.1 geçti: tests/Tests.csproj build exit 0
```

### 5. Changes
#### Step 5.1 — Determinizm
```bash
OUT1=$(mktemp -d); OUT2=$(mktemp -d)
dotnet run --project src/Gen.Cli -- "Silinecek1/cikti/manifest.json" "$OUT1"
dotnet run --project src/Gen.Cli -- "Silinecek1/cikti/manifest.json" "$OUT2"
diff -r "$OUT1/tests/gen" "$OUT2/tests/gen"   # expected: fark YOK
```
#### Step 5.2 — Bir test'i yeşille
Bir process test'inin `tests/src/.../X.Logic.cs` ARRANGE'ını **elle** (temsili) doldur (temiz-data + Single prereq);
`dotnet test "$OUT1/tests/Tests.csproj" --filter <TestAdı>` → **passed**.

### 6. Acceptance tests
#### 6.1 `diff -r` owned test ağacı **fark yok** (determinizm).
#### 6.2 Pozitif: doldurulan test `dotnet test` → 1 passed.
#### 6.3 Negatif: ARRANGE'ı **yanlış** (manifest.access write-set'ini ihlal eden — Package'ı kurmayan) doldur →
test **fail** (ASSERT contract-çapalı olduğu için yakalar). Bu, anti-circularity'nin kanıt-noktası.

### 7. Out of scope
- Tüm 23 testi doldurma YOK — tek temsili test yeterli (kanıt).
- CI entegrasyonu YOK.

### 8. Anti-patterns
- Determinizm farkını görmezden gelme → ordinal-order ihlali; kök-neden M1/M2'ye dön.
- 6.3'te testi geçirmek için ASSERT'i gevşetme → ASSERT contract-türevli, dokunulmaz (anti-circularity).

### 9. Definition of Done
- [ ] 6.1 owned test ağacı iki run'da byte-aynı.
- [ ] 6.2 doldurulan test yeşil.
- [ ] 6.3 yanlış-ARRANGE test fail (ASSERT yakalıyor).
- [ ] Determinizm/yeşil/negatif üçü de kanıtlandı.

### 10. Self-check
1. `diff -r` çıktısını gerçekten boş gördüm mü? 2. Yeşil test'in ASSERT'i contract-türevli mi (ben mi yazdım)? 3. 6.3 negatifte fail'i gördüm mü, yoksa varsaydım mı? 4. ASSERT'i geçmek için gevşettim mi (YASAK)?

---

## 4. Genel doğrulama (her milestone sonrası)
```bash
cd "/Users/hakansoysal/Desktop/ClaudeCode Denemeler/CoreTemplate1"
dotnet build src   # exit 0
git diff --stat     # yalnız ilgili milestone dosyaları
```

## 5. Doküman bakımı
- Bir task spec'i gerçekle çelişirse (anchor kaymış, imza farklı) → önce spec'i düzelt, sonra uygula.
- Kilitli 5 karar (tasarım §9) değişmez referans; sapma olursa kullanıcıya sor.
