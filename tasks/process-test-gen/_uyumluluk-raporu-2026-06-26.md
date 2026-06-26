# Uyumluluk Raporu — Süreç-yapılı test üretimi (plan ↔ tasarım)

> **Reviewer:** bağımsız Opus (plan'ı yazan agent DEĞİL).
> **Tarih:** 2026-06-26.
> **Kaynaklar:** `tasks/process-test-generation-design.md` (tasarım, read-only) · `tasks/process-test-gen/IMPLEMENTATION-PLAN.md` (plan) · kaynak kod `src/Gen.*` (anchor doğrulaması) · committed fixture `tests/fixtures/operations.json` + `tests/Gen.Tests/ContractParseTests.cs`.
> **Sonuç (özet):** Plan **mekanik düzeyde tasarıma sadık** — anchor'ların tamamı kod-stabil, forward-dep'ler tutarlı, locked kararlar doğru uygulanmış. Uygulanan mekanik fix = **0** (sapmaların hepsi kavramsal; tasarımın kendi doğrulanmamış varsayımına ya da eksik harici veriye dayanıyor). **2 execution-blocking kavramsal bulgu** kullanıcı kararı gerektiriyor (§4).

---

## 1. Doc mental model (8-12 satır)

- **Amaç:** Gen binary, her **süreç** (+ orphan-flow, + orphan-op) için deterministik bir xUnit test **iskeleti** emit eder; iskeletin içini (yalnız ARRANGE) LLM doldurur. Üretim 4 katmana dokunur: (a) Contract model genişlet, (b) TestPlan IR türet, (c) emitter test-pass, (d) build-report/provenance.
- **Sabit 3-faz:** temiz data → ön-gereksinim oluşturma → sürecin işletilmesi (process→flow→op çağrı sırası).
- **Kapsam (studyo):** 4 process-test + 1 orphan-flow-test (`DersTipiYonetimi`) + 18 orphan-op-test = 23 sınıf.
- **Anti-circularity (tasarımın kalbi, §3d):** **ASSERT üretilir (LLM değil)** — field-seviyesi, `operations[].effects[]`'ten; **LLM yalnız ARRANGE doldurur**. Seam marker yalnız ARRANGE bölgesinde. ASSERT'i LLM'e bırakmak → totoloji/yanlış-yeşil → YASAK.
- **Determinizm/ordinal (§4, §10, §11-HIGH):** tüm enumerasyon `OrderBy(Ordinal)`; ön-gereksinim topo-sort'unda eşitlikte entity-id ordinal tie-break. Sıra yayınlandı = sözleşme (Hyrum); byte-tekrar-üretimi provenance sha'yı sabitler.
- **Kilitli 5 karar (§9):** (1) runner = xUnit projesi; (2) conformance ⟂ süreç-testi = **tam-bağımsız iki katman**, dedupe YOK, 23 scope'un hepsi (18 orphan-op dahil); (3) ASSERT = field-seviyesi (`effects`); (4) Go pariteti = SONRA (IR dil-nötr); (5) belirsiz/eksik creator = **DUR** (`Ambiguous`≥2 / `Missing`=0 → unsupported marker, sessiz uydurma yok).
- **Scope sınırı:** TestPlan IR dil-nötr (Gen.Core); emit dil-özel (yalnız DotnetEmitter; GoEmitter sonra). Model-parse toleranslı + non-fatal (bozuk process op-emisyonunu düşürmez).

---

## 2. Alignment matrisi

| Task | Doc bölümü | Durum | Boyut | Özet |
|---|---|---|---|---|
| T-1.1 | §2, §3a | ✓ | a,c,e | `ProcessJson`/`FlowJson` additive; anchor `record ContractFile(` + `record ContractActor` kod-stabil. Plan StageKind/Items'ı nullable yapmış (design non-nullable) → §8/§11 toleransına **daha sadık**. |
| T-1.2 | §3b | ✓ | a,c | TestPlan/ProcessTest/ScenarioTest/PrereqStep/PrereqKind. Plan `CreatorOp`'u nullable yapmış (design non-nullable) → Ambiguous/Missing için **gerekli düzeltme** (design iç tutarsızlığını giderir). |
| T-1.3 | §3b, §11-HIGH | ✓ | a,c,h | containment + orphan; küme-farkı sırası (opsInFlow tüm flow'lar) §11 ile birebir; edge-case (boş/dangling/op≥2) ele alınmış. |
| T-1.4 | §3b, §5, §9-Q5 | ✓ | a,c,e,j | prereq = reads∪updates−creates; tek-creator topo-sort + entity-id tie-break; Ambiguous/Missing sınıflama. `Build` imzası `manifestOps` ile genişliyor (forward-dep). Kaynak = manifest.access (doğru). |
| T-1.5 | §3b | ✓ | a,e,j | GM `TestPlan` alanı (sona) + `GmBuilder.Build(contract, m.Operations)`. Anchor `TypeEnv Env)`, `return new GenerationModel(`, `Env: env` kod-stabil. T-1.4 imzasıyla **tutarlı** (`Build(contract, m.Operations)` ↔ `Build(ContractFile?, IReadOnlyList<OperationJson>)`). |
| T-2.1 | §3c, §3d | ✓ | a,e,i | EmitTests owned iskelet; anchor `WriteAlways(...,"Bootstrap.g.cs")` (satır 158) provenance/prune (satır 161) **ÖNCESİ** → owned track doğru. Ambiguous skip. Path layout ⚠️ (§4-C). |
| T-2.2 | §3d, §9-Q3 | ⚠️ | b,c | **KAVRAMSAL (§4-A):** `calculate`→`Assert.Equal(<value>, field)`'in **value kaynağı yok** — `ContractEffect(Kind, Target)` expr/text parse etmiyor; committed calculate sample'ı yalnız kind+target. Anchor'lar (ContractEffect, ContractOp.Effects, ExprBuild) var; ama veri yok. |
| T-2.3 | §3c, §3d | ✓ | a,e,h | ARRANGE seam `WriteIfAbsent`+marker; ASSERT seam'de değil. Anchor `LogicFile`/`WriteIfAbsent`/`MigrateSeamIfFlat` kod-stabil. |
| T-2.4 | §3c, §9-Q1 | ✓ | a,c | Tests.csproj HumanShell (WriteIfAbsent); xunit + runner + Test.SDK + App ref. 🟢 kısa format (düşük risk) uygun. |
| T-3.1 | §3e, §10 | ✓ | a,e | `report.Realized("test", id)`; id şeması `Process_/OrphanFlow_/OrphanOp_` §10 ile aynı. Anchor `Realized` (BuildReport satır 19) var. |
| T-3.2 | §5, §9-Q5 | ✓ | a,e,h | Ambiguous/Missing → `Unsupported("test-prereq",...)`, iskelet emit YOK. Anchor `Unsupported` (satır 20) var. Q5=DUR doğru. |
| T-3.3 | §3e, §11 | ✓ | a,e | owned `tests/gen/**` → provenance; seam `tests/src/**` girmez. **Teyit:** `WriteAlways` `_written`'a ekliyor (satır 207) → provenance/prune otomatik kapsar. Doğru. |
| T-4.1 | §8, §3d | ✓ | a,e,h | base-dotnet-rest `test-arrange` arketipi; anchor `| Boundary-client |` (satır 207) var. ARRANGE-only + "ASSERT'e dokunma" net. |
| T-4.2 | §11-HIGH (stale-seam) | ✓ | a,e | techgen-sync test-seam delta-sync; anchor (signature/Logic.cs uzlaştırma §3) var. stale→build-kırığı, orphan→sor. |
| T-4.3 | §8 | ✓ | a,f | evals test-arrange senaryosu (pozitif+negatif). 🟢 kısa format uygun. |
| T-5.1 | §Sonuç-M5 | ✓ / ⚠️ | b | E2E build doğrulama. Mantık doğru; ama acceptance `Silinecek1/cikti/manifest.json` **var olmayan** fixture'a bağlı (§4-B). |
| T-5.2 | §4, §11 | ✓ / ⚠️ | b,f | determinizm (diff -r) + yanlış-ARRANGE→fail (anti-circularity kanıtı). Mantık güçlü; aynı eksik-fixture bağımlılığı (§4-B). |

**Topological tutarlılık (§2 graf + §3 symbol-task + pre-cond'lar):** Tutarlı. Forward-dep'ler doğru: T-1.4 imza genişlemesi ↔ T-1.5 caller; her downstream task pre-cond grep'i upstream sembolü doğru hedefliyor (ör. T-3.2 `PrereqKind`'i T-1.2 dosyasında arıyor — symbol-table §3 ile uyumlu). Tek imprecision: §2 graf annotasyonu "(T-1.4 PrereqKind'e bağlı)" — PrereqKind **tipi** T-1.2'de üretilir; ama T-3.2'nin **davranışı** T-1.4'ün ürettiği Ambiguous/Missing değerlerine de bağlı olduğundan annotasyon savunulabilir (executable pre-cond grep zaten T-1.2'yi doğru gösteriyor). Load-bearing hata değil → düzeltme uygulanmadı.

---

## 3. Uygulanan mekanik fix'ler

**YOK (0 fix).** Plan, tasarımı mekanik düzeyde sadık biçimde transkript etmiş; anchor'ların tamamı kod-stabil, forward-dep'ler ve locked kararlar tutarlı. Tespit edilen sapmaların **hepsi kavramsal** (§4): ya tasarımın kendi doğrulanmamış varsayımına (effect-expr) ya da repo'da var olmayan harici veriye (studyo fixture) ya da bilinçli bir layout tercihi farkına (gen.tests vs tests/gen) dayanıyor. Hiçbiri "tasarımın net dediğini plan atlamış + düzeltme tek-anlamlı" kategorisine girmiyor → mekanik fix manufacture edilmedi (yapay edit yasak).

> Not: Plan'ın design'dan iki bilinçli **iyileştirici** sapması (T-1.1 nullable StageKind/Items, T-1.2 nullable CreatorOp) tasarımın §8/§11 toleransına ve §5 Ambiguous semantiğine **daha sadık** — bunlar düzeltme değil, plan'ın design'ın illüstratif kodundaki iç tutarsızlığı gidermesi. Dokunulmadı.

---

## 4. Kavramsal uyumsuzluklar (kullanıcı kararı gerekir)

### A. [BLOCKING] Field-ASSERT `calculate` → beklenen-değer kaynağı yok

- **Doc'un dediği (§3d):** "`calculate` (25 effect) → `target` (entity.field) `==` `expr` değeri → `Assert.Equal(<expr>, entity.Field)`. **`expr` = ExprNode → üretici `ExprBuild` machinery'sini yeniden kullanır**." Ek (§3d not): "modellenen `effects[].expr` tek literal değere çözülmüş (`'onaylandi'`)."
- **Plan'ın dediği (T-2.2 Step 5.1):** "`kind=="calculate"` + `Target=[Entity,field]` → `Assert.Equal(<value>, <entity>.<Field>);` — value, effect'in `expr`/`text`'inden (ExprNode varsa `ExprBuild`'in literal render'ı; düz `text` literal'i ise doğrudan)."
- **Gerçeklik (kod + committed veri):**
  - `ContractEffect` = `(string Kind, JsonElement? Target)` — **expr/text/value/Ast alanı YOK** (`src/Gen.Core/Model/Contract.cs:20`). Karşılaştırma: `ContractGuard`'ın `ExprNode? Ast` alanı VAR (satır 17) — effect'te yok.
  - Tek committed `calculate` sample (`tests/Gen.Tests/ContractParseTests.cs:22`): `{"kind":"calculate","target":["Appointment","status"]}` — yalnız kind+target, **beklenen değer (RHS) yok**.
  - Şema kaynağı `src/tech/contract.ts` (Contract.cs yorumunda adı geçen) bu repo'da **yok**; studyo verisi de yok (§4-B).
- **Neden kavramsal:** Tasarımın anti-circularity kalbi (25/38 effect = ASSERT'lerin çoğunluğu) "effect bir beklenen-değer taşır" varsayımına dayanıyor; mevcut model + tek görünür örnek bunu **çürütüyor**. Hiçbir task `ContractEffect`'i genişletmiyor (T-1.1 yalnız `ContractFile`/process/flow ekliyor). Doğru JSON şekli buradan bilinemez → mekanik fix imkânsız.
- **Olası yorumlar:**
  - **(A)** Studyo `operations.json`'unda effect'in gerçekten bir `expr`/value alanı VAR; hem `ContractEffect` C# modeli hem committed test fixture'ı onu düşürüyor → fix: `ContractEffect`'e additive `ExprNode? Expr` (ya da değer alanı) ekle (ContractGuard.Ast deseni) **+ bu adımı T-1.1'e/yeni task'a ekle** (plan'da eksik). T-2.2 ancak ondan sonra uygulanabilir.
  - **(B)** Effect'ler gerçekten yalnız kind+target; tasarımın `calculate→Assert.Equal(<value>)` premisi temelsiz → T-2.2 yeniden-kapsamlandırılmalı: field-Assert.Equal yerine yalnız varlık/WriteSet assertion'ı (Target'ın non-null olduğu / yazıldığı). Bu, §9-Q3 "ASSERT = field-seviyesi" locked kararını **gevşetir** → kullanıcı onayı şart.
- **Reviewer önerisi:** Kullanıcıya sor: *gerçek `operations.json` effect kaydı beklenen-değeri hangi alanda taşıyor (varsa şekli: ExprNode AST mı, literal text mi)?* Cevap (A) ise → `ContractEffect` genişletme task'ı ekle (T-1.1 kapsamı veya yeni T-1.1b), sonra T-2.2. Cevap (B) ise → T-2.2'yi rescope + §9-Q3'ü revize et. Çözülene kadar **M2 anti-circularity hedefi doğrulanamaz**.

### B. [BLOCKING] Doğrulama fixture'ı (`Silinecek1` / `studyo`) repo'da yok

- **Doc'un dediği:** §1/§5/§11 boyunca "gerçek fixture (studyo) ile doğrulandı"; sayılar 4 process / 14 flow / 42 op → 4 process-test + 1 orphan-flow + 18 orphan-op.
- **Plan'ın dediği:** Her 🔴 task'ın 6.2 acceptance'ı ve T-5.1/T-5.2 bash blokları **hardcoded** `Silinecek1/cikti/manifest.json` + `studyo.operations.json`'a (named entity'ler: TakvimSureci, IptalRandevu, UsageRecord, DersTipiYonetimi…) bağlı.
- **Gerçeklik:** `Silinecek1/` dizini **yok**, `.gitignore`'da değil, git history'de yok (silinmiş scratch dizini — "Silinecek" = "to-be-deleted"). Repo'daki tek committed fixture `tests/fixtures/operations.json`: **1 op, 0 process, 0 flow**. Sayılar (4/1/18), named entity'ler, IptalRandevu field-assert örneği — **hiçbiri runnable değil**.
- **Neden kavramsal (design-plan misalignment DEĞİL ama execution-blocking):** Design ve plan **aynı** (artık eksik) veriye atıf yapıyor — aralarında çelişki yok; ama plan'ın kabul kriterlerinin tamamı çalıştırılamaz. Mandate "task'ları gerçeğe göre doğrula + studyo sayıları acceptance'ta tutarlı mı" diyor; burada gerçeklik = fixture yok.
- **Reviewer önerisi:** Kullanıcı: process/flow taşıyan gerçek bir fixture'ı (studyo eşdeğeri) repo'ya **commit et** (ör. `tests/fixtures/` altında) ve tüm acceptance bloklarını + T-5.x bash yollarını ona **repoint et**. Bu yapılmadan M1-M5 doğrulaması mümkün değil. (Auto-fix edilemez: doğru yol bilinmiyor.)

### C. [LOW] Test dosya layout'u: design `gen.tests/`+`tests/` ↔ plan `tests/gen/`+`tests/src/`

- **Doc'un dediği (§3c tablo):** owned = `gen.tests/{Scope}/{Name}.g.cs`; seam = `tests/{Scope}/{Name}.Logic.cs`.
- **Plan'ın dediği (T-2.1/2.3/2.4 boyunca tutarlı):** owned = `tests/gen/{Scope}/{Name}.g.cs`; seam = `tests/src/{Scope}/{Name}.Logic.cs` (tek `Tests.csproj` ikisini de Compile-include eder).
- **Neden kavramsal:** Plan, mevcut emitter konvansiyonunu (`outDir/gen` owned + `outDir/src` seam) `tests/` altına taşıyor — tasarımın "**feature-slice deseniyle aynı**" ifadesine design'ın kendi örnek yolundan **daha sadık**. Provenance/prune outDir-relative çalıştığından her iki layout da fonksiyonel; fark yalnız isimlendirme + tek-csproj netliği.
- **Reviewer önerisi:** Plan'ın layout'u tercih edilmeli (codebase konvansiyonuyla tutarlı); **tasarım §3c plan'a göre güncellensin** (plan design'a göre değil). Düşük öncelik, blocker değil. Plan'ı design'ın literal yoluna **çekme** (auto-fix yapılmadı — yanlış yön olurdu).

---

## 5. Self-check

1. Tasarımı tamamen okudum mu? Evet (255 satır) + plan tamamen (1150 satır, iki sayfada).
2. Her anchor string'ini gerçek kaynağı açıp teyit ettim mi (satır no'ya değil)? Evet — `record ContractFile(`, `record ContractActor`, `TypeEnv Env)`, `return new GenerationModel(`, `WriteAlways(...,"Bootstrap.g.cs")`, satır-161-öncesi prune, `_written?.Add` (satır 207), `Realized`/`Unsupported` (19/20), `ContractEffect(Kind,Target)`, `ContractOp.Effects`, `ExprBuild.Build`, `FileClass.Generated`, `| Boundary-client |`, techgen-sync seam §3 — hepsi VAR.
3. Headline bulguyu (effect-expr) tek örnekle değil, model + committed sample + şema-kaynağı yokluğu ile mi doğruladım? Evet (advisor önerisi üzerine `ContractParseTests.cs:22` + `src/tech` yokluğu).
4. Mekanik fix manufacture ettim mi? Hayır — net mekanik misalignment yok; yapay edit yapmadım.
5. Locked kararları doğru okudum mu? Evet — §9-Q2 (23 scope hepsi) §11-MEDIUM (orphan-op filtrele) önerisini **override** ediyor; plan Q2'yi takip ediyor → bu doğru, plan §11-MEDIUM'u atlamadı, locked karar uyarınca yok saydı.
6. Tasarım dokümanını / kaynak kodu değiştirdim mi? Hayır (read-only).
7. Kavramsal soruları kendim kapattım mı? Hayır — §4'e taşıdım, kullanıcı kararına bıraktım.

---

## 6. Sayısal özet

- **Task sayısı:** 17 (T-1.1..1.5, T-2.1..2.4, T-3.1..3.3, T-4.1..4.3, T-5.1..5.2).
- **✓ uyumlu:** 13 · **⚠️ kavramsal-bağımlı:** 4 (T-2.2 tam ⚠️; T-5.1/T-5.2 fixture-bağımlı; T-2.1 layout-not).
- **Uygulanan mekanik fix:** **0** (plan mekanik düzeyde sadık).
- **Kavramsal bulgu:** **3** (A: effect-expr kaynağı — BLOCKING; B: eksik fixture — BLOCKING; C: layout farkı — LOW).
- **Anchor doğrulama:** 14/14 kod-stabil (tümü mevcut kaynakta teyitli).
- **En kritik:** A + B her ikisi de **execution-blocking** ve reviewer tarafından çözülemez — kullanıcı kararı şart.
