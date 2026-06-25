# codegen-plugin — techgen-dotnet (üreteç + doldurucu)

"Statik C# kod üreteci + LLM-doldurucu" referans paketi. CommandDSL ailesinin **`kesif`** skill'i bu paketi **self-describe** ile keşfeder (`[dsl-generator]` işareti), `capability.json` descriptor'ıyla eşleştirir ve seam-doldurmayı **`base-dotnet-rest`** skill'ine devreder.

## Kurulum (Claude Code plugin)

| Adım | Komut |
|---|---|
| Marketplace'i ekle | `/plugin marketplace add Hakan-Soysal/codegen-plugin` |
| Plugin'i kur | `/plugin install codegen@codegen-tools` |
| Güncelle | `/plugin marketplace update codegen-tools` |

Kurulduktan sonra `base-dotnet-rest` skill'i kurulu-skill listesinde `[dsl-generator]` işaretiyle görünür; `kesif` onu `describe` modunda çağırıp capability'sini okur. (Marketplace adı **`codegen-tools`**, plugin adı **`codegen`**, repo **`codegen-plugin`** — üçü farklı, normaldir.)

## İçerik

| Yol | Ne |
|---|---|
| `plugins/codegen/skills/base-dotnet-rest/` | LLM-doldurucu skill: `SKILL.md` + `capability.json` (descriptor) + `references/` (gap-protocol, archetype-playbooks, gap-registry, verify-loop) + `evals/` |
| `src/Gen.Cli`, `src/Gen.Dotnet` | Statik C# üreteç (manifest → `gen/**` deterministik kod + boş human-seam'ler) |
| `conformance-adapter/` | Aile-üretimi dil-nötr SPEC'leri koşan generic harness (assertion spec'te) |
| `tests/`, `SPEC.md` | Üreteç testleri + spesifikasyon |

## Pair mimarisi

- **Üreteç** (statik): manifest'ten `gen/**`'i byte-deterministik üretir + boş seam (`*.Logic.cs`) iskeletleri (marker `…doldurulacak`).
- **Doldurucu** (LLM): seam'leri arketip-temelli **bir kez** doldurur (`gen/**`'e asla yazmaz), build + conformance ile doğrular.
- **Aile** (`command-dsl`/`kesif`): seçer → devreder → kapıda doğrular (manifest-türevli completeness + K1/K2 + conformance). Bu paket: üretir → doldurur → verify eder.
