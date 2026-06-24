# Flexibility Phase 1 — Seam-fix (parametresiz)

> Hedef-app esnetme, Faz 1. Amaç: 🔴/🟡 persistence & contract eksenlerini **seam** ile 🟢'ye
> çek — parametre/skill makinesi YOK (o Faz 2). Hepsi `src/Gen.Dotnet/DotnetEmitter.cs`
> template değişikliği. Gerekçe + Faz 2: hafıza `flexibility-two-phase-plan`.
> Sıra: değer-first. Her task → golden-file güncelle + emit edilen app `dotnet build` exit 0
> (kağıt-üstü "çalışıyor" yasak, SPEC §5) + regen `.Logic.cs` ezmez + determinizm (byte-aynı).

## Invariant'lar (her task'ta korunur)
- Default emit (insan partial'ı YOKKEN) derlenmeli — eklenen hook'lar boşken no-op.
- Değişiklik yalnız `WriteAlways` (gen/) template'i; `WriteIfAbsent` (Program.cs/csproj) ŞEKLİ değişmez.
- SPEC sınırları: framework icadı yok, REST-only, no-silent-loss.

## Tasks

- [x] **P1.1 · `AppDbContext` partial + `OnModelCreatingPartial` hook (M)** — 🔴→🟢 tablo-mapping seam
  - `DbContextFile` (L640): `public class AppDbContext` → `public partial class AppDbContext`.
  - Gövdeye EF-scaffolding konvansiyonu ekle:
    `protected override void OnModelCreating(ModelBuilder b) { OnModelCreatingPartial(b); }`
    `partial void OnModelCreatingPartial(ModelBuilder b);`
  - Kabul: insan partial'ı yokken build yeşil (partial void no-op elide); insan ayrı `partial class AppDbContext`'te `OnModelCreatingPartial` yazıp convention-dışı mapping (kolon tipi/index/tablo adı) verebiliyor → build yeşil. **İnsan stub'ı EMIT ETME** (YAGNI; partial void boşken çalışır).

- [x] **P1.2 · Entity'leri `partial class` emit (S)** — 🔴→🟢 entity genişletme seam
  - `EntitiesFile` (L252): `public class {e.Id}` → `public partial class {e.Id}`.
  - Kabul: build yeşil; insan ayrı partial'da audit-kolon/soft-delete/computed/navigation ekleyebiliyor.

- [x] **P1.4 · `ResultHttp` override edilebilir (M)** — 🔴→🟢 özel hata zarfı (RFC7807)
  - `ResultHttp` (L208) `static class` kalır; üreteç-call-site (`MapLine`) değişmez.
  - Mekanizma (öneri, minimal): mutable static delegate hook —
    `public static Func<object, IResult>? Override;` ve `ToHttp<T>` başında `if (Override is {} o) return o(r!);` sonra mevcut switch.
  - İnsan Program.cs (shell) içinde `ResultHttp.Override = r => ...;` ile özel zarf bağlar.
  - Kabul: `Override` null iken çıktı **byte-aynı** (mevcut golden bozulmaz); set edilince özel zarf dönüyor. Partial-method DEĞİL (non-void partial insan impl'ini zorlar → default build kırılır).

- [ ] **P1.3 · External (+uncharted) client stub'larını unseal (S, OPSİYONEL/düşük değer)**
  - `BoundaryFile` (L418) `public sealed class {ext}Client` → `sealed` kaldır; tutarlılık için Uncharted (L527) `{u}Client` aynı.
  - **Not:** Değer marjinal — impl swap zaten DI override ile çalışıyor (`AddSingleton<IPushService, MyImpl>`), subclass gerekmez. Tek faydası: insan stub'ı extend etmek isterse blok kalmasın. Tek-token, sıfır-risk → dahil ama önceliksiz. Atlanırsa Faz 1 yine tamam.

- [ ] **▸ Checkpoint P1 (Complete):** tüm golden-file'lar güncel + emit edilen fixture app `dotnet build` exit 0 + Go seam etkilenmedi (varsa) + determinizm testi yeşil + regen `.Logic.cs` ezmiyor. → 4 eksen 🟢, parametre yüzeyi açılmadı.
