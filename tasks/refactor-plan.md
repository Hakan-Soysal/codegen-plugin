# Refactor Plan — Generator → "gen/ segregation + pure projection + provenance + thin shell"

> Amaç: Mevcut `techgen` üretecini, son 3 turda kararlaştırılan ilkelere göre elden
> geçirmek. İlkeler: (1) **fiziksel ayrım** — `gen/` %100 üreteç-sahibi (üzerine yaz +
> orphan prune), insan kodu ayrı ağaçta (yoksa-üret); (2) **saf projeksiyon ≠ apply** —
> emit diske yazmaz, `EmittedFileSet` döner; ayrı `Applier` diske basar; (3) **provenance
> manifesti**; (4) **thin Program.cs/csproj shell + generated fragment** (merge'i ortadan
> kaldırır); (5) **orphan'ı yapısal olarak yok et** (manifest-diff prune); (6) **skill =
> delta-sync orkestratörü**, bulk = boş-hedef özel hâli.
>
> Bu plan kod DEĞİŞTİRMEZ — sadece tarif eder. Her faz: build + test yeşil bitmeli.

---

## 0. Yer gerçeği (doğrulandı — agent keşifleri)

| Gerçek | Kanıt | Plan için anlamı |
|---|---|---|
| Gen.Core tamamen dil-nötr; hiç output/file/path abstraction'ı yok | `GenerationModel.cs`, emisyon yalnız Gen.Dotnet/Gen.Go'da | `EmittedFile`/`FileClass` abstraction'ı **yeni** — Gen.Core'a değil, paylaşılan bir yere konmalı |
| Emitter'da **tek** `WriteIfAbsent`: `{op}Handler.Logic.cs` (`DotnetEmitter.cs:72`); gerisi hep `WriteAlways` | satır 118-119 | İnsan seam'i tek dosya sınıfı; gerisi Generated |
| **App-bootstrap aggregation YOK** — `Program(gm)` (`DotnetEmitter.cs:888-926`) düz inline DI+maps üretir | satır 894-916 | Thin-shell split'i **sıfırdan kurulacak** (reuse edilecek yapı yok) |
| Per-handler partial fragmentasyonu **zaten var** (Guards/Auth/Idem/Ext/Throws/Consistency/Logic) | satır 71-97 | Fragment pattern'i emitter'da yerleşik; sadece app-level eksik |
| csproj `Microsoft.NET.Sdk.Web` default globbing kullanır, `<Compile>` yok; tek paket EF Core 10.0.9 | `Csproj()` 122-136 | `gen/**/*.cs` otomatik derlenir; **explicit `<Compile Include>` EKLEME** (NETSDK1022) |
| App.csproj + Program.cs `outDir` kökünde; `src/`, `src/{Module}/`, `src/Uncharted/` | satır 20-50 | Layout yeniden çizilecek |
| CLI: `techgen <manifest> <outDir>`, **global dotnet tool** (`Gen.Cli.csproj` PackAsTool, ToolCommandName=techgen); exit 0 ⟺ SilentDrops=0 | `Program.cs:19-23` | CLI sözleşmesi korunur; apply+prune buraya girer |
| Go emitter: paralel, bağımsız, **CLI'de değil, pakette değil**, kendi `_logic.go` seam'i var | `GoEmitTests` dışında çağrılmıyor | Go = spike; hizalama opsiyonel (D2) |
| Test: **golden-file YOK** — substring assert + gerçek `dotnet build` exit 0 + byte-determinism + Logic-preserved-on-regen + Completeness ratchet (KnownDebt boş) | `EmitTests.cs:40-204` | Refactor öncesi **characterization golden snapshot** eklenmeli (Faz 0) |
| INV-7 gate: `Completeness.Check` census ⊄ build-report → SilentDrop → Clean=false → test RED | `BuildReport.cs`, `fix-plan.md:13` | Refactor bu gate'i **bozmamalı**; ratchet'e madde EKLENMEZ |
| `gen/` vs insan-kodu ayrımına dair **önceki karar YOK** | history agent | Yeni tasarım kararı (D1) — kullanıcıya sorulacak |
| Local-only: gerçek grammar/manifest fix'leri `CommandDSL/` (working-dir dışı) | `audit-fix-todo.md:12` | Bu refactor yalnız bu repo içinde; CommandDSL'e dokunmaz |

---

## 1. Hedef mimari

### 1.1 Üretilen app layout'u (Karar **D1** — öneri)
```
outDir/
  App.csproj            ← HUMAN shell (yoksa-üret, asla ezilme). <Import gen/Generated.props Condition=Exists>
  Program.cs            ← HUMAN shell (yoksa-üret). AddGenerated()/MapGenerated() çağırır + Use* pipeline + app.Run()
  src/{Module}/
    {op}Handler.Logic.cs ← HUMAN seam (yoksa-üret). gen/ DIŞINDA kalır.
  gen/                  ← BINARY-owned. write-only-if-changed + orphan prune. Tüm .g.cs burada.
    Generated.props     ← PackageReference'lar (EF Core vb.)
    Bootstrap.g.cs      ← AddGenerated/MapGenerated aggregator (HER ZAMAN, boşsa bile)
    Result.g.cs, ResultHttp.g.cs, AppDbContext.g.cs, EventBus.g.cs, Subscriptions.g.cs, Idempotency.g.cs
    {Module}/
      Wiring.g.cs       ← AddXModule/MapXModule (eski inline DI+maps buradan)
      {op}.g.cs, {op}.Guards.g.cs, {op}.Auth.g.cs, ... (eski .g.cs'ler)
      Errors.g.cs, Types.g.cs, Entities.g.cs, Events.g.cs, {e}.Invariants.g.cs
    Uncharted/{u}.g.cs
    provenance.json     ← üretilen dosya manifesti (prune + skill için)
  build-report.json     ← INV-7 gate (yerinde kalır)
```
**Namespace değişmez** — fiziksel yol değişir ama `App.{Module}` namespace'i korunur (partial class `gen/{Module}/{op}.g.cs` ile `src/{Module}/{op}Handler.Logic.cs` arasında aynı namespace'te yayılır; default globbing ikisini de derler).

### 1.2 Yazma davranışı — LEAN (Karar **D6 = LEAN** ✓ kilitlendi)
> Validator + advisor: "binary = saf projeksiyon ≠ apply" abstraction'ı (turn 5'te konuşulan)
> bu **refactor** için YAGNI. Amaçları (drift tespiti, skill delta-sync, stateless binary)
> zaten **disk'teki `gen/` + `provenance.json`** ile karşılanıyor — skill provenance'ı diskten
> okur, in-process file set'e ihtiyaç YOK. In-memory projeksiyona ihtiyaç duyan tek şey
> opsiyonel `techgen plan`, o da commit'li `gen/` üzerinde `git diff` ile ikame edilebilir.

**Lean (öneri):** `DotnetEmitter.Emit(gm,outDir,report)` **public entry olarak KALIR** (testler onu doğrudan çağırıyor — bkz. §4 risk). İçeride iki yardımcı eklenir, abstraction katmanı YOK:
- `WriteAlways` → **write-only-if-changed**: dosya varsa ve içerik aynıysa yazma (mtime churn / `dotnet watch` döngüsü önlenir). Tek satırlık değişiklik.
- Bu run'da yazılan path'ler bir `List<string>`'te biriktirilir. Bitince önceki `provenance.json` path listesiyle diff: **sil = `önceki − yazılan`** (yalnız ÖNCEDEN üretilmiş, `Generated` sınıfı path'ler; insan dosyasına asla dokunma). Boş dizinleri temizle.
- `provenance.json` **en son, atomik** yazılır.

**Full (alternatif — D6=full seçilirse):** `Project(gm,report):EmittedFileSet` saf fonksiyon + ayrı `Applier`. `Emit` shim olarak korunur (`Emit => Applier.Apply(Project(...), outDir, prev)`). Yalnızca `techgen plan` GERÇEKTEN shipping ise veya in-process file set'e başka ihtiyaç doğarsa hak eder. Aksi hâlde her `WriteAlways` çağrı noktasına dokunan gereksiz churn.

### 1.3 Program.cs / csproj split (merge'i yok eder)
- Generated `gen/Bootstrap.g.cs`: `AddGenerated(this IServiceCollection)` + `MapGenerated(this IEndpointRouteBuilder)` — **imzalar SONSUZA DEK donuk**, modül 0 olsa bile boş emit edilir (shell her zaman derlensin). `namespace Microsoft.Extensions.DependencyInjection` / `Microsoft.AspNetCore.Builder` (shell'e extra using gerekmez).
- **KRİTİK — global vs per-module ayrımı (validator BREAK #3):** Bugün `Program(gm)` içindeki kayıtların çoğu **modül-kapsamlı DEĞİL, `gm`-düzeyinde global**: tek bir `AppDbContext` (tüm modüller arası, `DbContextFile` 595-599), `IEventBus`, `IIdempotencyStore`, `gm.Externals` clients, `gm.Uncharted` clients, `gm.Subscriptions` consumers. Bunlar **`gen/Bootstrap.g.cs::AddGenerated`'a** girer (per-module Wiring'e DEĞİL).
  - `gen/Bootstrap.g.cs` `AddGenerated`: TÜM `gm`-düzeyi global kayıtlar (DbContext, EventBus, IdempotencyStore, external/uncharted clients, subscription consumers).
  - Per-module `gen/{Module}/Wiring.g.cs`: YALNIZ per-op `AddScoped<{op}Handler>`, per-trigger `AddHostedService`, ve o modülün `MapGroup` route'ları (`MapPost/MapGet` mantığı).
- **Kod taşırken birebir koru (advisor):** kayıt kodu taşınırken MEVCUT kod aynen korunur. EF provider/paket durumu (yalnız `Microsoft.EntityFrameworkCore`, provider yok) sadece build-exit-0 testiyle korunuyor — taşıma sırasında "iyileştirme" yapma, provider varsayımını bozma.
- Human `Program.cs` (yoksa-üret): host + **Use\* pipeline (sıra insan-sahipli)** + `AddGenerated()` + `var api = app.MapGroup("/api")` + `MapGenerated(api)` + `app.Run()`. Generated kod **asla `Use*` emit etmez**; yalnız endpoint + metadata (`RequireAuthorization("x")` vb. — backing middleware'i shell garanti eder).
- Human `App.csproj` (yoksa-üret): TargetFramework + `<Import Project="gen/Generated.props" Condition="Exists('gen/Generated.props')" />` (koşul ZORUNLU — fresh clone / mid-wipe'ta build kırılmasın).
- Generated `gen/Generated.props`: EF Core vb. `<PackageReference>`. **CPM uyarısı**: hedef repoda `Directory.Packages.props` varsa NU1008 (versionsuz ref + PackageVersion gerekir) — v1'de **kapsam dışı, belgelenir** (üretilen app standalone, CPM yok varsayımı).
- Config **referansla akar** (`IConfiguration`/connection string shell-sahipli), parametreyle değil → seam donuk kalır.

### 1.4 Provenance (Karar **D3** — öneri: hash dahil)
`gen/provenance.json`:
```json
{ "generator": "techgen", "version": "0.2.0",
  "files": [ {"path":"gen/Bootstrap.g.cs","class":"Generated","sha256":"..."}, ... ] }
```
- `files` ordinal sıralı. OpenAPI-Generator `FILES` modelinin JSON'lu hâli.
- **Hash dahil** → skill drift tespiti yapar (insan `.g.cs`'e dokunmuş mu); prune zaten path-diff ile çalışır.
- SHA-256, on-disk byte üzerinden (LF normalize edilmiş emit ile çakışır).
- **İlk run / bozuk manifest (validator RISKY #5):** önceki provenance yoksa `prev=∅` → hiçbir şey silme. Provenance **parse edilemezse** (bozuk/elle düzenlenmiş) → boş kabul et, **prune'u atla + uyar**. Parse edemediğin bir manifestten ASLA silme hesaplama (yoksa bozuk dosya canlı insan dosyasını silebilir).

### 1.5 Determinizm + marker'lar (araştırma önerisi)
- `EmittedFileSet` ordinal sıralı; write-only-if-changed; timestamp YOK (zaten yok); GmBuilder ordinal sıralama korunur.
- Her generated dosyaya: `// <auto-generated/>` header + (tip düzeyinde) `[GeneratedCode("techgen","<ver>")]` + gerekli yerde `#nullable enable`. NOT: header analyzer'ı susturur, **CS nullable uyarısını susturmaz** → `#nullable` direktifi şart.
- LF satır sonu sabit; `CultureInfo.InvariantCulture` (zaten `ExprBuild`'de var).

### 1.6 Eski-layout → yeni-layout geçişi (BAŞ KUSUR — advisor)
Plan iki hedef durumu ele alıyor: boş dizin (greenfield) ve zaten-yeni-layout. **Eksik olan:** mevcut (flat `src/`, her şey WriteAlways) üreteçle üretilmiş bir app'in yeni üretece *upgrade* edilmesi. O ilk upgrade run'ı sert kırar:
- `outDir/src/Result.g.cs` (eski) + `outDir/gen/Result.g.cs` (yeni) ikisi de default `**/*.cs` glob'una düşer → duplicate `App.Result<T>` → **CS0101**.
- Program.cs artık write-if-absent → yeni üreteç onu **atlar** → eski monolitik Program.cs hayatta kalır, `AddGenerated()/MapGenerated()` çağırmaz, çift-tanımlı tiplere referans verir.

**Çözüm (refleksle migration tool yapma — advisor):** üreteç **v0.1.0, yeni paketlendi, muhtemelen sıfır gerçek tüketici app**. Karar **D7**:
- Tüketici app YOKSA → kapsamı "yalnız yeni app'ler"e daralt + belgele: önceden üretilmiş app'ler temiz bir dizine yeniden üretilmeli. (Repo'da commit'li üretilmiş app yok; `out/` gitignore'lu → varsayım makul.)
- Tüketici app VARSA → tek seferlik temizlik adımı (eski `src/*.g.cs` + eski Program.cs sil) gerekir.
- **Varsayımı sessiz bırakma — açıkça doğrula/belgele.**

---

## 2. Faz planı (güvenlik-öncelikli; her faz build+test yeşil)

### Faz 0 — Güvenlik ağı (characterization)
- Mevcut testler yeşil mi doğrula.
- **Refactor ÖNCESİ** fixture'ın TÜM emit edilen ağacının golden snapshot'ını al (şu an substring-test var, golden yok). Refactor'ın generated **içeriğini** sessizce değiştirmediğini yakalamak için. (brownfield-design-first: characterization testing.)
- DoD: golden snapshot commit'li; tüm testler yeşil.

### Faz 1 — write-only-if-changed + provenance/prune iskeleti (davranış ~değişmez)
- **`DotnetEmitter.Emit` public imzası KORUNUR** (validator BREAK #6a — 9 EmitTests + CompletenessTests + LatentConstructTests onu doğrudan çağırıyor; kaldırmak TÜM test derlemesini kırar).
- `WriteAlways` → write-only-if-changed (tek satır). Yazılan path'leri `List<string>`'te biriktir.
- `provenance.json` yaz (henüz ESKİ konumlarda — gen/ taşıması Faz 2). Prune'u şimdilik no-op/iskelet bırak (önceki provenance yoksa zaten silmez).
- DoD: golden snapshot **değişmedi** (byte-aynı); ikinci run mtime değişmedi; `dotnet build` exit 0; tüm testler yeşil. ← Davranış-koruma kanıtı.

### Faz 2 — gen/ ayrımı + prune + provenance
- Generated dosyaları `gen/`'e taşı; Logic.cs insan ağacında kalsın.
- `Applier`: write-only-if-changed + manifest-diff prune + provenance yaz.
- Golden snapshot güncelle (yollar değişti, içerik aynı kalmalı).
- Yeni testler: (a) rename op → eski `.g.cs` prune'landı, Logic.cs korundu; (b) değişmemiş girdi → ikinci run byte-aynı + mtime değişmedi; (c) provenance doğru; (d) `dotnet build` exit 0.
- DoD: yukarıdaki testler + INV-7 ratchet yeşil.

### Faz 3 — Program.cs/csproj shell+fragment split
- Inline DI/maps → `gen/Bootstrap.g.cs` + per-module `Wiring.g.cs`.
- Thin `Program.cs` + `App.csproj` = HumanShell (yoksa-üret); `Generated.props` + koşullu Import.
- **Test taşıma (validator #6):** `LatentConstructTests` route string assert'leri `Program.cs` İÇİNDE okuyor — `Internal_visibility_suppresses_public_route` (170-172) ve `Get_with_non_route_query_param_binds_all_params` (252-256). Route'lar `gen/{Module}/Wiring.g.cs`'e taşınınca bu assert'ler kırılır → Wiring dosyasını okuyacak şekilde taşı.
- Test: Program.cs'i elle düzenle → regen'de **korunur**; sıfır-modül'de boş `AddGenerated`/`MapGenerated` derlenir; `dotnet build` exit 0.
- DoD: split sonrası emit edilen app derlenir; shell-survives-regen testi + taşınan route testleri yeşil.

### Faz 4 — Marker + determinizm cilası
- `<auto-generated/>` + `[GeneratedCode]` + `#nullable enable`.
- DoD: byte-determinizm testi + `dotnet build` (0 yeni uyarı) yeşil.

### Faz 5 — CLI + paketleme
- Apply prune-aware; opsiyonel `techgen plan <manifest> <outDir>` dry-run (ekle/değiş/sil farkını yazar — skill için). `dotnet pack` ile tool'u yeniden paketle.
- DoD: `techgen` ve `techgen plan` gerçek fixture'da çalışır; exit kodları doğru.

### Faz 6 — Skill (delta-sync orkestratörü) — Karar **D4**
- `SKILL.md` + küçük deterministik helper. Mekanik iş artık ÜRETEÇTE (gen/ prune + yoksa-üret shell üreteç tarafından yapılıyor) → skill inceldi:
  - **Mekanik:** `techgen` çağır; `build-report.json` + `provenance.json` oku; `dotnet build` koş.
  - **LLM muhakemesi (yalnız 2 yer):** (1) signature değişimi `.Logic.cs`'i kırarsa → derleme hatalarını yüzeyle, insan gövdesini uzlaştır; (2) op silinince `.g.cs` prune'lanır ama insan `Logic.cs` (yoksa-üret, asla auto-silinmez) orphan kalır → skill tespit eder (`gen/` karşılığı olmayan Logic.cs) ve sil/taşı diye **sorar**.
- **Eval (zorunlu):** evrilen fixture app — greenfield emit → build yeşil; op ekle → yalnız yeni dosyalar, build yeşil; op rename → orphan `.g.cs` prune, Logic korundu, build yeşil; signature değiştir → Logic seam'de build RED (yüksek sesle), provenance doğru.
- DoD: 4 senaryo da assert'lendi.

### Faz 7 — Go hizalama (Karar **D2** — öneri: ERTELE/belgele)
- Go emitter'ı `EmittedFileSet` abstraction'ına hizala (Applier'ı paylaşsın) VEYA spike olduğu için ertele + gerekçeyi belgele. Go CLI'de/pakette değil; One-Version-Rule sembolik olarak uygulanır ama efor düşük öncelik.

---

## 3. Kararlar (KİLİTLENDİ)

- **D6 — Yazma mimarisi = LEAN** ✓. `DotnetEmitter.Emit` public imzası korunur; içine write-only-if-changed + path-tracking; `Emit()` sonunda manifest-diff prune + provenance yaz. Projeksiyon/Applier abstraction'ı YOK. Gerekçe §1.2: amaçları disk'teki `gen/`+`provenance` zaten karşılıyor.
- **D7 — Mevcut tüketici app = YOK / yalnız-yeni** ✓. Kapsam "yeni app'ler"; önceden üretilmiş app'ler temiz dizine yeniden üretilmeli (§1.6). Migration tool yapılmaz; varsayım belgelenir.
- **D4 — Skill = DAHİL (Faz 6)** ✓.
- **D2 — Go = ERTELE + belgele** ✓ (Faz 7; spike, CLI/pakette değil).
- **D1 — gen/ layout** ✓: `outDir/gen/` (generated) + kök (human shell) + `src/{Module}/*.Logic.cs` (human seam).
- **D3 — Provenance hash = SHA-256 dahil** ✓ (drift tespiti).
- *D5 düştü (lean'de abstraction yok).*

## 4. Riskler / tuzaklar (araştırmadan, doğrulanmış)
- `<Import gen/Generated.props>` **koşulsuz olursa** fresh clone/mid-wipe build'i kırar → `Condition="Exists(...)"` zorunlu.
- Wipe+rewrite mtime churn → spurious rebuild + `dotnet watch` döngüsü → **write-only-if-changed** şart; gerekirse `<Compile Update="gen/**/*.cs" Watch="false"/>`.
- Explicit `<Compile Include="gen/**/*.cs"/>` → NETSDK1022 hard error; **ekleme** (default glob zaten kapsıyor).
- CPM repoda versiyonlu generated PackageReference → NU1008 (v1 kapsam dışı, belgele).
- Endpoint `Map*` sırası pipeline'ı ETKİLEMEZ (routing metadata) → fragment emit sırası güvenli; ama `RequireAuthorization` gibi metadata, shell'deki `UseAuthentication/UseAuthorization` olmadan **inert** → "fragment yetkinliği isimle ister, shell backing'i garanti eder" sözleşmesi.
- Provenance yarım yazılırsa sonraki prune gerçek dosya siler → **en son + atomik** yaz; crash'te yazma.
- INV-7 gate: **düşük risk (validator doğruladı)** — `Program()` hiç `report.*` çağırmaz, tüm `report.Realized` Emit döngüsünde; `Covers/Census` construct-id tabanlı, path değil. Yani gen/ taşıması ve Program split INV-7'yi yapısal olarak kıramaz. Yine de regression testiyle kapat.
- **Eski→yeni upgrade (BAŞ KUSUR §1.6):** CS0101 duplicate tip + hayatta kalan eski Program.cs. D7 ile kapsam/temizlik kararı alınmadan Faz 2-3'e geçme.
- **Global vs per-module DI (BREAK #3 §1.3):** `gm`-düzeyi global kayıtlar yanlışlıkla per-module Wiring'e konursa tek `AppDbContext`/`IEventBus` parçalanır. Bootstrap.g.cs'e koy.
