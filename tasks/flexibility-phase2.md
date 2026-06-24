# Flexibility Phase 2 — Tek parametre: DB provider (tool tarafı)

> Hedef-app esnetme, Faz 2. Amaç: DB-provider'ı gerçek çalışır parametre yap. Skill tarafı
> (config reconciliation: yok→oluştur+onay / farklı→bildir / aynı→geç, generator-registry)
> GELECEK iş — bugün sadece tool, basit. Gerekçe: hafıza `flexibility-two-phase-plan`.
> Karar: config-by-reference (sp,o)→IConfiguration (call-site blast-radius yok) + Emit'e
> opsiyonel `GenConfig?` param (mevcut callers/golden bozulmaz; config yokken byte-aynı).

## Invariant'lar
- `config == null` → mevcut davranış (boş lambda, provider paketi yok) → **golden değişmez**.
- Bilinmeyen provider → build-report `unsupported(reason)` + boş lambda fallback (no-silent-loss).
- Provider = enum-whitelist; connstring değeri runtime (appsettings), emit-zamanı DEĞİL.
- Provider paket versiyonu MUTLAKA doğrulanır (Context7/EFCore 10.0.9 ile uyum) — kağıt-üstü yasak.

## Provider whitelist → emit eşlemesi
| dbProvider | Use{X} | NuGet paketi (Generated.props) |
|---|---|---|
| postgres | `UseNpgsql(...GetConnectionString("Default"))` | Npgsql.EntityFrameworkCore.PostgreSQL (versiyon doğrula) |
| sqlite | `UseSqlite(...GetConnectionString("Default"))` | Microsoft.EntityFrameworkCore.Sqlite (=10.0.9) |
| sqlserver | `UseSqlServer(...GetConnectionString("Default"))` | Microsoft.EntityFrameworkCore.SqlServer (=10.0.9) |
| inmemory | `UseInMemoryDatabase("AppDb")` | Microsoft.EntityFrameworkCore.InMemory (=10.0.9) |
| (yok/null) | `o => { /* seam */ }` (mevcut) | — |

## Tasks

- [x] **P2.1 · `GenConfig` modeli + JSON load (S)**
  - Gen.Dotnet'te `record GenConfig(string? DbProvider)` + dosyadan deserialize (System.Text.Json, mevcut `Json` infra).
  - Kabul: örnek `{"dbProvider":"sqlite"}` → DbProvider="sqlite"; dosya yok → null; bozuk JSON → net hata.

- [x] **P2.2 · Bootstrap config-by-reference provider emit (M)**
  - `GeneratedBootstrap` config alır; whitelist → `(sp,o)=>o.Use{X}(...)`; null → mevcut boş lambda; bilinmeyen → `report.Unsupported("dbProvider", v, ...)` + boş lambda.
  - Mevcut `using Microsoft.EntityFrameworkCore;` UseX'leri kapsar; GetRequiredService/GetConnectionString Web SDK implicit-using.
  - Kabul: her provider string → doğru UseX satırı; null → byte-aynı; bilinmeyen → build-report unsupported.

- [x] **P2.3 · Generated.props provider paketi (S)**
  - `GeneratedProps` config alır; provider → eşleşen `<PackageReference>` ekler. **Versiyonları doğrula (Context7).**
  - Kabul: provider=sqlite → props'ta Sqlite paketi; null → yalnız EFCore (mevcut).

- [x] **P2.4 · `Emit` `config` param + CLI wiring (M)**
  - `Emit(gm, outDir, report, GenConfig? config = null)`; CLI manifest yanındaki `gen.config.json`'ı yükler (yoksa null) → Emit'e geçer.
  - Kabul: gen.config `{"dbProvider":"sqlite"}` ile emit → emit edilen app **gerçek `dotnet build` exit 0** (sqlite = en hafif, server gerektirmez) + determinizm (byte-aynı) + mevcut fixture (config'siz) golden değişmedi.

- [x] **▸ Checkpoint P2 (Complete):** tüm test yeşil + config'siz emit pre-P2 ile byte-aynı + sqlite-config app derleniyor + build-report provider'ı kaydediyor. → DB-provider gerçek parametre, tek knob, SPEC sınırları korundu.

## Kapsam dışı (gelecek)
- Skill: hedef app analizi + config seed/reconcile (yok→onayla, farklı→bildir, aynı→geç) + output'a human-owned kopya.
- Generator-registry (hexagonal/DDD/VSA/3-tier/actor üreteçleri); skill en uygununu seçer.
