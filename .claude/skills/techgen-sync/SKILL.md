---
name: techgen-sync
description: >-
  Bir hedef .NET uygulamasını manifest.json'a göre yeniden üretir (delta-sync) ve
  derlenebilir tutar. techgen üretecini çalıştırır, gen/ ağacını günceller + orphan
  prune eder, HumanShell/HumanSeam dosyalarına dokunmaz, sonra `dotnet build` ile
  doğrular. Build kırılırsa (signature değişimi Logic.cs'i bozdu) hataları yüzeyler ve
  insan gövdesini uzlaştırır; op silinince orphan Logic.cs'i tespit edip sil/taşı diye
  sorar. Şu durumlarda kullan: "manifest'i app'e uygula", "techgen sync", "üretilen
  kodu güncelle", "manifest değişti app'i yenile", "regenerate app from manifest",
  "delta-sync", "gen klasörünü güncelle".
---

# techgen-sync — manifest → hedef app delta-sync

`techgen` deterministik bir üreteçtir: `manifest.json` → `gen/` (üreteç-sahibi, her run
üzerine yazılır + orphan'lar prune edilir) + `Program.cs`/`App.csproj` (HumanShell,
yoksa-üret) + `src/{Module}/*Handler.Logic.cs` (HumanSeam, yoksa-üret, **asla ezilmez**).

Mekanik işin çoğu ARTIK ÜRETEÇTE (prune, write-if-changed, shell-if-absent). Bu skill'in
işi: üreteci doğru çağırmak, **doğrulamak**, ve üretecin çözemediği iki noktada (kırık
seam, orphan Logic) muhakeme uygulamak. "Bulk" = boş hedefe sync; aynı akış.

## Sözleşme (değişmez)
- `gen/**` ASLA elle düzenlenme. Üreteç %100 sahibi; her run yeniden üretir/prune eder.
- `*.Logic.cs` (iş gövdesi) ve `Program.cs`/`App.csproj` (shell) İNSAN sahibi; üreteç
  yoksa-üret, asla ezmez. Tüm el-emeği buralarda + gen/ dışı dosyalarda.
- "In god we trust, others we validate": `dotnet build` exit 0 görmeden iş bitmiş sayılmaz.

## Akış

### 1. Çalıştır (mekanik)
```
techgen <manifest.json> <targetDir>          # paketli tool
# veya repo içinden: dotnet run --project src/Gen.Cli -- <manifest.json> <targetDir>
```
Çıktılar: `<targetDir>/gen/` güncellenir, orphan `.g.cs`'ler prune edilir,
`<targetDir>/provenance.json` (üretilen dosyalar) ve `<targetDir>/build-report.json` yazılır.
Exit kodu: 0 ⟺ SilentDrop yok (INV-7). Exit 1 ise build-report'taki SilentDrop'ları oku
ve kullanıcıya bildir — manifest/üreteç düzeyinde bir kapsama açığı vardır.

### 2. Doğrula (mekanik)
```
dotnet build <targetDir>/App.csproj
```
- **exit 0** → sync sağlıklı. Değişimi özetle (git diff <targetDir>/gen, provenance farkı).
- **exit ≠ 0** → 3'e geç.

### 3. Kırık seam'i uzlaştır (LLM muhakemesi)
Build hatası genelde şu demektir: bir op'un signature'ı değişti → üreteç `gen/{M}/{op}.g.cs`'i
yeni imzayla yeniden üretti, ama insan `src/{M}/{op}Handler.Logic.cs` hâlâ eski imzayı
implement ediyor (partial method eşleşmiyor → CS hatası). Bu **kasıtlı yüksek-sesli** bir
sinyaldir, bug değil.
- Derleme hatalarını oku (hangi handler, hangi imza).
- İlgili `gen/{M}/{op}.g.cs`'teki yeni `partial ... ExecuteAsync(...)` imzasını referans al.
- `src/{M}/{op}Handler.Logic.cs`'teki insan gövdesini yeni imzaya uyarla — **mantığı koru**,
  yalnız imzayı/parametre erişimini güncelle. Belirsizse kullanıcıya sor.
- `dotnet build` tekrar; 0 olana kadar (CLAUDE.md: yarım bırakma).

> **Test-seam'ler (`tests/src/**/*.Logic.cs`) AYNI delta-sync'e dahil — handler seam'iyle aynı sınıf.**
> Owned test iskeleti `gen/tests/.../{Name}.g.cs` (`WriteAlways`) her run yenilenir; ARRANGE gövdesi
> `tests/src/{Scope}/{Name}.Logic.cs` (HumanSeam, `WriteIfAbsent`) bayatlayabilir. Contract değişip üreteç
> owned test `.g.cs`'i **yeni `Arrange{Name}` imzasıyla** yeniden üretince, eski imzayı implement eden ARRANGE
> seam partial method'u eşleşmez → **`dotnet build` kırığı yüzeye çıkar (sessiz geçilmez)**. Handler seam'iyle
> birebir aynı şekilde uzlaştır: yeni `gen/tests/.../{Name}.g.cs`'teki `partial ... Arrange{Name}(...)` imzasını
> referans al, `tests/src/{Scope}/{Name}.Logic.cs`'teki insan ARRANGE gövdesini yeni imzaya uyarla (mantığı koru),
> `dotnet build` 0 olana kadar tekrarla. Belirsizse kullanıcıya sor.

### 4. Orphan Logic.cs (LLM muhakemesi + kullanıcı onayı)
Bir op manifest'ten kaldırılınca/yeniden adlandırılınca üreteç `gen/{M}/{op}.g.cs`'i prune
eder, ama insan `{op}Handler.Logic.cs`'i (HumanSeam) **asla auto-silmez**. Sonuç: gen/
karşılığı olmayan orphan Logic.cs.
Tespit (mekanik):
```
# gen/ altında karşılık partial'ı OLMAYAN Logic.cs'ler:
for f in $(find <targetDir>/src -name '*Handler.Logic.cs'); do
  base=$(basename "$f" Handler.Logic.cs)              # ör. CreateInvoice
  mod=$(basename "$(dirname "$f")")                   # ör. Billing
  [ -f "<targetDir>/gen/$mod/$base.g.cs" ] || echo "ORPHAN: $f"
done
```
Her orphan için **kullanıcıya sor**: (a) sil (op gerçekten kaldırıldı), (b) taşı/yeniden
adlandır (op rename edildi → gövdeyi yeni op'un Logic.cs'ine taşı). Onaysız silme YOK
(local-only + geri-alınamaz iş kuralı).

> **Orphan test-seam (`tests/src/**/*.Logic.cs`) — handler orphan deseniyle AYNI.**
> Bir test contract'tan çıkınca (bir op süreçten ayrıldı / süreç tümüyle kaldırıldı) üreteç owned test
> `gen/tests/.../{Name}.g.cs`'i prune eder (T-3.3), ama ARRANGE seam `tests/src/{Scope}/{Name}.Logic.cs`'i
> **asla auto-silmez** (HumanSeam). Sonuç: gen tarafında karşılığı kalmayan orphan ARRANGE seam. Tespit:
> handler orphan taramasının aynısı — `tests/src` altındaki her `*.Logic.cs` için `gen/tests/.../{Name}.g.cs`
> yoksa ORPHAN. Her orphan test-seam için **kullanıcıya sor**: (a) sil (test gerçekten kaldırıldı), (b) taşı/
> yeniden adlandır (test rename edildi). **Onaysız otomatik silme YOK — insan-sahibi**, handler orphan ile aynı.

### 5. Özetle
Tamamlanınca: hangi op'lar eklendi/değişti/silindi (provenance diff), kaç dosya prune edildi,
kaç seam uzlaştırıldı, kalan orphan/karar. `dotnet build` exit 0 teyidi.

## Yapma
- `gen/`'i elle düzenleme; bir sonraki sync ezer.
- Kırık seam'i "beklenen hata" diye normalize etme — ya uzlaştır ya kullanıcıya sor.
- Build görmeden "sync tamam" deme.
- Orphan Logic.cs'i onaysız silme.
