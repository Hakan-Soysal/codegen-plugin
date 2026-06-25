# Gap-registry — on-disk format + merge + sürüm-drift (T5.4 / K4)

> **Bu dosya kademe rung-2'nin (kayıtlı çözüm) TÜKETTİĞİ store'un dosya formatıdır.**
> Runtime (detection + kademe + DUR/sor/kaydet) = `gap-protocol.md` (T5.2). Bu dosya yalnız
> **registry'nin disk düzenini + kayıt formatını + merge'ünü + sürüm-drift yeniden-doğrulamasını**
> tanımlar. Runtime'ı **uygulamaz** — ona atfeder.
>
> Kaynak: tasarım §4 K4 + §B.4.3; kayıt-içeriği `gap-protocol.md` §3 (dört alan).

---

## 0. Neden bu store var (amaç-odaklı)

Kademe rung-2 = **kayıtlı çözüm** (`gap-protocol.md` §2). Bir gap daha önce kullanıcı tarafından
**öğretildiyse** (B.4.3 madde 4: tekrar-edilebilir → kaydı önerildi → onaylandı), aynı **gap-signature**
gelecekte yeniden çıktığında kademe bunu **otomatik uygular + rapora yazar** (sessiz değil). Bu store o
öğretilmiş çözümlerin **diske yazılmış, repoyla taşınan** halidir → ekip arkadaşının yeniden-üretimi aynı
kayıtlı çözümleri uygular (öğrenme paylaşılır).

**Kritik değişmez:** kayıt **otomatik-uygulama rızasıdır**. Bir kayıt yoksa kademe rung-2 boş döner →
(rung-3/rung-4'e geçer). Asla "makul varsayım" üretmez. Store = öğretilenin tekrarı, icadın kaynağı değil.

---

## 1. Disk düzeni (`.dsl/gap-policies/`)

Registry **proje-yanında** durur (öğretilen çözüm projeye aittir), **paket+sürüm scope'lu**:

```
<proje-kökü>/
  .dsl/
    generators/
      techgen-dotnet@0.2.0.json        # descriptor PIN'i (K2; bu dosyanın konusu DEĞİL)
    gap-policies/
      techgen-dotnet@0.2.0/            # <pkg>@<ver>/  — bir paket-sürüm = bir dizin
        rule-input-unbindable-credit-limit.json
        failable-unnamed-rule0.json
        ...                            # her kayıt = bir *.json dosyası (bir gap-signature)
```

- **Dizin adı = `<pkg>@<ver>`** — paket id'si + sürüm (descriptor `id`+`version`, ör. `techgen-dotnet@0.2.0`).
  Scope budur: bir sürümün altındaki kayıtlar **yalnız o sürüm** için geçerlidir (sürüm-drift güvenliği, §4).
- **Her kayıt ayrı `*.json` dosyası** — bir dosya = bir gap-signature → bir resolution. Dosya adı insan-okunur
  bir slug'dır (tetikten türetilir, ör. `rule-input-unbindable-credit-limit.json`); **eşleştirme dosya adına
  değil, içerikteki `gap-signature`'a göredir** (dosya adı yalnız okunabilirlik içindir).
- **`.dsl/` git'e commit'lenir** → kayıtlar repoyla taşınır, ekip paylaşır.

### Seed vs öğretili — iki kaynak
- **Paket-seed:** descriptor'dan gelen, üretecin **kendi bildirdiği** bilinen-boşluk çözümleri (üreteç
  paketler). Mantıksal olarak paket scope'una aittir.
- **Proje-öğretili:** kullanıcının B.4.3 ile öğrettiği, bu projeye özgü çözümler. `.dsl/gap-policies/`'e yazılır.

Merge bu ikisini birleştirir (§3).

---

## 2. Kayıt formatı (record = dört alan)

Her kayıt **dört üst-alandan** oluşur (içerik `gap-protocol.md` §3 ile bire-bir aynı; bu dosya onu **diske
serileştirir**):

| Alan | Amaç | İçerik |
|---|---|---|
| `gap-signature` | **Eşleştirme anahtarı** — kademe bunu çıkan gap'le karşılaştırır | `{ archetype, kural (K1\|K2\|K3), tetik }` |
| `resolution` | **Ne uygulanacak** — eşleşince otomatik uygulanan eylem | `{ method, params, template }` |
| `scope` | **Geçerlilik sınırı** — hangi paket+sürüm için | `{ package: "<pkg>@<ver>" }` |
| `provenance` | **Köken + güven** — kim öğretti, ne zaman, tekrar-edilebilir mi | `{ taught-by, date, repeatable }` |

### 2.1 `gap-signature` — eşleştirme anahtarı
- `archetype` — seam'in arketipi (ör. `Command`, `Query`). Aynı tetik farklı arketipte farklı anlam taşıyabilir.
- `kural` — hangi detection denetimi çıkardı: `K1` (predicate-input bağlanabilirliği) / `K2` (failable→named-error)
  / `K3` (dependency çözünürlüğü). *(K4 = unsupported; o rung-3'te ele alınır, signature'ı registry'de aranabilir
  ama tipik seed kaynağıdır.)*
- `tetik` — gap'i benzersizleştiren somut sebep (ör. `"rule-input-unbindable:credit-limit"`).

İki kayıt **aynı `gap-signature`'a** sahipse (archetype+kural+tetik üçlüsü eşit) = **aynı gap** → merge'de
çakışma (§3).

### 2.2 `resolution` — uygulanacak eylem
- `method` — sabit sözlük (B.4.3 madde 3 ile **aynı**): `use-generator-policy:X` / `inject-human-interface` /
  `map-to-error:Y` / `back-to-teknik-analiz`.
- `params` — method'a özgü parametreler (ör. policy adı, hata tipi, store adı).
- `template` — (opsiyonel) uygulanacak gövde-şablonu veya yönerge metni.

### 2.3 `scope` — geçerlilik sınırı
- `package` — `"<pkg>@<ver>"` (ör. `"techgen-dotnet@0.2.0"`). Dizin adıyla **tutarlı olmalıdır**. Sürüm
  değişince eski kayıt **oto-uygulanmaz, yeniden-doğrulanır** (§4).

### 2.4 `provenance` — köken + güven
- `taught-by` — `user` (B.4.3 öğretili) veya `generator-seed` (descriptor seed).
- `date` — kaydın oluşturulduğu tarih (ISO `YYYY-MM-DD`).
- `repeatable` — `true` ise gelecekte otomatik-uygulanabilir (kayıt nedeni). `false` tek-seferliktir →
  **kaydedilmez** (B.4.3: tek-seferlik yalnız uygulanır). Yani diskteki her kayıtta `repeatable: true` beklenir.

Örnek: `examples/gap-policy.örnek.json`.

---

## 3. Merge (paket-seed ⊕ proje-öğretili; çakışmada proje kazanır)

Kademe rung-2'nin gördüğü **etkin registry** = iki kaynağın birleşimidir:

```
etkin-registry = paket-seed  ⊕  proje-öğretili
```

- **⊕ = union by `gap-signature`** — iki kaynaktaki kayıtlar `gap-signature` (archetype+kural+tetik) anahtarına
  göre birleştirilir.
- **Çakışma (aynı `gap-signature` her iki kaynakta) → PROJE KAZANIR.** Proje-öğretili kayıt paket-seed'i
  **gölgeler** (override). Gerekçe: kullanıcı bu projede bilinçli olarak farklı bir çözüm öğretti; bu, üretecin
  genel seed'inden daha spesifik + daha günceldir.
- **Çakışma yoksa** her iki kaynaktan gelen kayıt da etkin-registry'de yer alır (toplama).

> **Sıra (kademe içi):** merge `gap-protocol.md` §2'deki rung sırasını **değiştirmez**. Önce rung-1
> (üreteç-policy), sonra rung-2 (bu birleşik registry). Registry **içinde** çakışma çözümü = proje > seed.

---

## 4. Sürüm uyuşmazlığı → yeniden-doğrula (drift güvenliği)

Kayıt **paket+sürüm scope'ludur** (`scope.package = "<pkg>@<ver>"`). Aktif üreteç sürümü kaydın sürümünden
**farklıysa**:

- **Eski kayıt OTO-UYGULANMAZ.** Bir sürümde geçerli bir çözüm (ör. belirli bir `map-to-error:Y`), üreteç yeni
  sürümde throws-kataloğunu / policy'leri / yüzeyi değiştirmiş olabileceği için yeni sürümde **sadık olmayabilir**.
- **Yeniden-doğrulanır.** Sürüm-drift'inde kayıt körü körüne uygulanmaz; detection (K1–K4) **yeni sürümün**
  contract'ı + yüzeyi karşısında **yeniden koşar**. Çözüm hâlâ geçerliyse uygulanır (ve istenirse yeni sürüm
  scope'una taşınır/kopyalanır); değilse normal kademeye düşer (rung-3/rung-4 → gerekirse DUR + sor).

> **Net kural:** `scope.package`'ın sürümü ≠ aktif üreteç sürümü → kayıt **kademe rung-2 eşleşmesi sayılmaz**;
> yeniden-doğrulama gerekir. "Bir sürüm için öğretilmiş çözümü başka sürüme sessiz taşıma" = **yasak** (drift
> = sessiz improvisation riski).

---

## 5. Out of scope (bu dosya)

- **Runtime** (detection gate + kademe + DUR/sor/kaydet mekaniği) → **T5.2** (`gap-protocol.md`). Bu dosya
  yalnız store'un **formatını** tanımlar; rung-2'nin nasıl koştuğunu değil.
- **Verify-loop** (build + conformance oracle + retry/fresh-start) → **T5.5** (`verify-loop.md`).
- **Descriptor şeması** (seed'in geldiği `id`+`version`+`source`) → aile A.2 + keşif (kapsam dışı).
- **Aile-kapısı** (A.4 bağımsız zorlama) → T3.2.
