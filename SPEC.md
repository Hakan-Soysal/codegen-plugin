# SPEC — Tech DSL → Kod Üreteci (.NET-first)

> Kaynak: bu konuşmada kilitlenen kararlar + `tasks/plan.md`. `/spec` interaktif
> sorularının altı alanı zaten kararlı olduğundan konsolide edildi (uydurma yok).

## 1. Objective & users
`manifest.json` (+ opsiyonel `operations.json`) girdisini alıp **derlenen, method
gövdeleri `NotImplemented` olan bir .NET uygulaması** üreten deterministik araç.
Üretilen iskeleti sonradan **LLM veya insan developer** tek tek doldurur.
Nihai hedef 4 dil (.NET, Java, Node, Go); bu spec **.NET-first + Go seam-doğrulama**
kapsamındadır.

## 2. Core features (kabul kriterleri `tasks/plan.md`'de task-bazında)
- Load: manifest + operations.json → tipli model (System.Text.Json).
- Join: `realizes` ile op/entity ↔ ContractOp/Entity; linked çözülmezse `JoinError`; standalone'da join atla.
- GM: in-memory nötr IR; tüm koleksiyonlar `id`'ye göre sıralı (determinizm).
- Emit (.NET): operation→Command/Query+Handler, entity→EF, type/enum→record/enum, event→record+pub/sub stub, Result<T> 6'lı taksonomi, ExprNode→C# predicate (validation/rule/permit/invariant).
- Stub seam: iş gövdesi `NotImplementedException`, **ayrı `partial` dosyada** (`{X}.Logic.cs`, yoksa-üret), `{X}.g.cs` her zaman ezilir.
- build-report.json: her construct `realized | unsupported(reason)` (no-silent-loss).
- Go spike (Phase 5): aynı GM'den derlenen Go → seam doğrulama gate'i.

## 3. Tech stack & constraints
- Üreteç: **.NET 10 / C#** (SDK 10.0.300 mevcut).
- Üretilen app şekli: **Minimal API + düz CQRS handler + EF Core + küçük `Result<T>`**. Framework icat YOK.
- Tek transport: REST/Minimal API.
- Input kontrat tek sürüm: 4 dil de aynı `manifest.json`; dile-özel varyant yasak.

## 4. Project structure
```
Gen.sln
src/Gen.Core/      # load + join + GM + build-report (dil-nötr)
src/Gen.Dotnet/    # .NET emitter (ince)
src/Gen.Go/        # Go emitter (Phase 5)
tests/Gen.Tests/   # xUnit: golden-file + ExprNode unit + derleme doğrulama
tests/fixtures/    # manifest.json + operations.json
```

## 5. Testing strategy
- Her emit task'ı: golden-file eşleşir **+ emit edilen proje `dotnet build`/`go build` exit 0** (kağıt üstü "çalışıyor" yasak).
- ExprNode→predicate (non-trivial logic): zorunlu assert-tabanlı unit test + "unknown node → unsupported" testi.
- Regen: ikinci çalıştırma `.Logic.cs`'i ezmez.
- Determinizm: aynı girdi → byte-aynı `.g.cs`.

## 6. Boundaries
- **Always:** determinizm (id-sıralı emit), build-report, gerçek-derleme doğrulaması, ayrı-dosya stub.
- **Ask first:** üretilen app'in hedef şeklini değiştirmek; yeni dış bağımlılık; git history'yi değiştiren işlemler.
- **Never (`ponytail:` silindi):** drift/regeneration engine, protocol-binding pluggability, GM JSON Schema (Phase 6'ya ertelendi), portable conformance suite, framework icadı, dile-özel manifest varyantı, sessiz construct düşürme.
