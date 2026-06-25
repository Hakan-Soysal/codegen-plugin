# Gap-protokol runtime — detection gate + çözüm kademesi + DUR/sor/kaydet (T5.2)

> **Bu dosya filler'ın Faz 3-4 mekaniğidir** (tasarım kaynağı `SEAM-DOLDURMA-SKILL-TASARIM.md` §B.4
> + §A.5). SKILL.md Faz 3 yalnız kapıyı **atfeder + sırasını sabitler**; mekanik **buradadır**.
>
> **Değişmez (her oturumda geçerli):** Bilinmeyen boşlukta **improvise YASAK** → **DUR + kullanıcıya
> sor**. **"Yorum ≠ STOP":** bir gap'i açıklayıcı bir yorumla işaretleyip gövde yazmaya devam etmek
> STOP **değildir** — gerçek STOP = yazımı durdurup gap'i kesin sunmaktır. (PoC hatası: `ResourceCreditLimit`
> bağlanamadı ama yorumlanıp geçildi = improvisation. Bu yasaktır.)

---

## 0. Neden bu protokol var (amaç-odaklı)

Kullanıcının en çok önemsediği gereksinim = **sessiz improvisation YOK**. Statik kat (`gen/**`) construct'ları
adlı üyelere ayrıştırdı; filler'ın işi bunları kanonik sırada bağlamak. Ama bir predicate-input'u hiçbir
kaynağa bağlanamıyorsa ya da bir failable'ın adlı-hatası yoksa → seam **sadık üretilemez**. O an iki yol
vardır: (a) "makul varsayım" yapıp uydurmak (PoC hatası — yasak), (b) **deterministik** olarak durup sormak.
Bu protokol (b)'yi zorunlu, (a)'yı imkânsız kılar.

**Detection DETERMİNİSTİKTİR — agent'ın "fark etmesi"ne bırakılmaz.** Faz 3 sonlu bir denetim kümesidir
(K1–K4); her seam **YAZILMADAN ÖNCE** koşar. Agent'ın dikkatine değil, mekanik bir kapıya dayanır.

---

## 0.5 — Fill-öncesi tüm-manifest feasibility ön-kapısı (Faz 0.5 — contract-only)  [fill-öncesi]

> **Konum dürüstlüğü:** statik üretim (`gen/**`) generator binary'since, bu skill çağrılmadan ÖNCE yapılır
> → §0.5 **fill-öncesidir, statik-üretim-öncesi DEĞİL** (binary-öncesi contract-soundness = `techgen-sync`).
> **Bu, Faz 3 (§1) detection gate'in contract-türevli, tüm-manifest, fill-ÖNCESİ alt-kümesidir.**
> Faz 3 seam-başına + post-gen (üretilmiş `.g.cs` + `build-report` okur); §0.5 manifest'in **tamamını**
> **bir kez**, **yalnız contract'tan** (`manifest.json`/`operations.json`), **`gen/**` ve build-report'a
> DOKUNMADAN** denetler. Amaç: defektli manifest'i fill+verify döngüsü başlamadan yakalayıp upstream'e
> yansıtmak. K1/K2'yi YENİDEN TANIMLAMAZ — aynı kuralların contract-only ön-koşumudur.

Dört denetim (geçmeyen → **DUR + `back-to-teknik-analiz`**; seam-fill çözüm-kademesi DEĞİL):

### P1 — Manifest sağlık
`meta.hasErrors == false` ∧ `coverage.unrealizedBusinessOps == []` ∧ `coverage.uncoveredEntities == []`.
Biri ihlal → manifest güvenilmez (üretilmiş construct denetimi anlamsız) → DUR.

### P2 — Referential integrity (alanlar var mı / doğru mu)
Her op-referansı manifest koleksiyonlarında çözülmeli; "havada" referans → defekt.
**KRİTİK — koleksiyonlar `id` ile anahtarlanır, `name` ile DEĞİL.** Çözüm kümeleri:
`{operations[].id}`, `{entities[].id}`, `{types[].id}`, `{errors[].id}`, `{events[].id}`. (`name` alanı
yalnız entity/type/event **field**'larındadır — `fields[].name` + `fields[].type` —, üst-koleksiyon kimliği
değildir. `.name` ile çözmeye kalkmak HER tip-referansını yanlış "havada" sayar.) Denetimler:
- `operations[].realizes` (+ `entities[].realizes`) → bir business-op karşılığı (`biz.<X>` biçimi).
- `signature.returns` + `signature.params[].type` → **`{types[].id} ∪ {entities[].id} ∪ skaler-küme}`**.
  **Skaler-küme = manifest'in kendi `ref:"scalar"` işaretiyle tanımlı tip-uzayı** (üreteç-tanıdığı skalerler:
  `ID`, `String`, `Int`, `Decimal`, `Bool`, `Date`, `DateTime`, `Duration`, `Unit`). Bu kümeyi BEN
  uydurmam — manifest field'larında `ref` markörü skaler-mi-referans-mı ground-truth'unu taşır.
- `throws[]` (hata id'leri) → `{errors[].id}`,
- `emits[]` (event id'leri) → `{events[].id}`,
- `subscriptions[]` → kaynak `{events[].id}` + hedef `{operations[].id}`,
- `access.{reads,creates,updates,deletes}` (entity id'leri) → `{entities[].id}`,
- `callEdges[]` / `externals[]` referansları (op/entity/system id'leri).

### P3 — K2-contract (failable → named-error, contract-only)
§1 K2 ile AYNI kural, yalnız contract'tan: her failable `validation`/`rule`, ihlalinde
`operations[].throws[]` → adlı `errors[]`'a eşlenebilmeli. Eşlenemeyen (adsız) → defekt.
(PoC `Rule_0` burada — fill'den ÖNCE — yakalanır.)

### P4 — K1-contract (kaynak 1-3, contract-only)
§1 K1 ile AYNI kural ama **yalnız ilk üç kaynak** (request-param / entity-field / boundary-dönüş);
hepsi contract'tan türetilebilir. **Kaynak-4 (`build-report.policy`) üretim-sonrasıdır → §0.5'te
DENETLENMEZ.** İlk üç kaynağa bağlanamayan ama policy-bağımlı görünen alan → "policy-bağımlı, Faz 3'e
ertelendi" işaretlenir, **sert-GAP sayılmaz** (yanlış infeasibility üretme; kaynak-4 yalnız post-gen Faz 3'te çözülür).

### Dispozisyon (§0.5'e özgü — kademe DEĞİL)
P1–P4'ten biri defekt çıkarırsa: **DUR — fill başlamaz.** Defekti KESİN sun (hangi P-denetimi ·
hangi construct · ne eksik/havada). Eylem = **`back-to-teknik-analiz`** (manifest upstream'de düzeltilir)
ya da kullanıcı contract'ı düzeltir. **Bu yapısal contract defekti seam-fill çözüm-kademesiyle
(rung-1/2/3b) çözülmez** — policy/registry/codebase-inference bir manifest defektini onarmaz; manifest
düzeltilir. Hepsi PASS → normal akış (Faz 1 → … → Faz 3 detection gate post-gen).

---

## 1. Faz 3 — Detection gate (doldurma-ÖNCESİ, deterministik)  [§B.4.1]

Her seam'in gövdesi **yazılmadan ÖNCE** dört sonlu denetim koşar. Çıktı = **gap-listesi** (bu seam için
hangi denetim hangi gap'i çıkardı). Denetim deterministik: contract (`operations.json`/`manifest.json`) +
üretilmiş yüzey (handler `.g.cs` partial'ları, `handlerSurfaceMap` rehberliğinde) + `build-report` okunur,
mekanik kıyaslanır. **Hiçbir denetim "agent buna dikkat etsin" değildir.**

### K1 — Predicate-input bağlanabilirliği
Her `Validation_N` / `Rule_N` / `Invariant_N` predicate'inin input-record'undaki **her alan**, şu **dört
kaynaktan TAM BİRİNE** bağlanabilir olmalı:
1. **request-param** — op'un signature param'ı (`operations[].signature.params[]`).
2. **entity-field** — op'un dokunduğu entity alanı (`entities[].fields[]`).
3. **boundary-dönüş** — bir dış-çağrının (`I{External}` client) dönüş değeri (`callEdges[]`/`externals[]`).
4. **`build-report.policy`** — üreteç-policy'sinin sağladığı parametrik bağlam (`build-report.policies`).

Bağlanamayan alan → **K1 GAP** (alan "havada"; predicate sadık üretilemez). Çözüm kademesine geç.
*(PoC GAP #1: `ResourceCreditLimit` dört kaynağın hiçbirine bağlanamadı → K1 GAP olmalıydı.)*

### K2 — Failable → named-error
Başarısız olabilen **her** `validation`/`rule`, ihlal edildiğinde op'un `operations.json` **throws
kataloğunda** (`operations[].throws[]` → adlı `errors[]`; üretilmiş yüzeyde `{op}.Throws.g.cs` fabrikası)
bir **adlı-hataya** eşlenmeli. Eşlenmeyen (adsız fırlatan) failable → **K2 GAP**.
*(PoC GAP #2: `Rule_0` ihlali adsız hata üretti, throws kataloğunda karşılığı yoktu → K2 GAP olmalıydı.)*

### K3 — Dependency çözünürlüğü
Seam'in ihtiyaç duyduğu **her bağımlılık** ya DI'da (`Bootstrap.g.cs`) kayıtlı ya da bir boundary
(`I{External}`) olmalı. Çözülemeyen bağımlılık → **K3 GAP**.

### K4 — Unsupported construct
`build-report.constructs[]` içinde `status == "unsupported"` olan bir kayıt bu op'a **değiyorsa** →
**bildirilen bilinen boşluk** (üreteç onu sessiz düşürmedi, raporladı). Bu bir K4 sinyalidir; çözüm
kademesi rung-3'te ele alınır.

> **NOT — şema gerçeği (ReportTests.cs ile doğrulandı):** `build-report` = `{ constructs: [{construct,
> id, status, reason?}], policies: {name: decision} }`. **`silentDrops` diye bir JSON ALANI YOKTUR** —
> silent-drop sinyali üreteç **exit≠0** ya da `constructs[].status == "unsupported"`. K4 bunu okur.

### DUAL-LAYER — K1/K2 İKİ KATMANDA koşar (INV-B)
- **Pakette (bu dosya):** filler K1–K4'ün **HEPSİNİ** fill-öncesi **erken-DUR** olarak koşar. Amaç:
  seam yazılmadan gap'i yakalayıp ya kademeyle çözmek ya da DUR etmek.
- **Ailede (T3.2 / A.4 kapısı):** aile **yalnız K1 ve K2'yi** (predicate-input bağlanabilirliği +
  failable→named-error) post-gen artefaktlardan (contract vs üretilmiş `.g.cs` yüzeyi) **bağımsız**
  yeniden koşar. Aile paketin `gapProtocol.compliant=true` öz-beyanına **GÜVENMEZ**; aynı yapısal
  denetimi **kendi** contract'ından yapar. İmprovise eden paket `compliant:true` yazsa bile kapıdan geçemez.

> **Net sınır:** K1/K2 iki yerde (paket erken-DUR + aile bağımsız zorlama). K3/K4 **yalnız pakette**
> (erken-DUR). Aile kapısı K3/K4'ü yeniden koşmaz — bunlar fill-öncesi paket-içi denetimlerdir.
> Bu dosya **paket-içi erken-DUR**'dur; aile-kapısı K1/K2'si T3.2 — bkz. `kesif/references/family-gate.md` §K.

---

## 2. Faz 4a — Çözüm kademesi (resolution ladder)  [§B.4.2]

Faz 3 bir gap çıkardıysa, gövde yazılmadan **çözüm kademesi** koşar. **İlk eşleşen otomatik uygulanır
ve RAPORA YAZILIR (sessiz değil).** Kademe, "tanımlı yöntem varsa onu kullan; yoksa DUR" ilkesinin
deterministik sıralamasıdır:

| Rung | Kaynak | Davranış |
|---|---|---|
| **1. Üreteç-policy** | `build-report.policies` (`{name: decision}`) + inline `ponytail:`/`§8` yönergeleri | Üretecin **tanımlı yöntemi**. Gap değil, yönerge. Eşleşen policy varsa **otomatik uygula + rapora yaz**. |
| **2. Kayıtlı çözüm** | proje-yanı registry (`.dsl/gap-policies/<pkg>@<ver>/`), paket+sürüm scope'lu | Daha önce öğretilmiş çözüm (gap-signature eşleşmesi). Varsa **otomatik uygula + rapora yaz**. |
| **3. Unsupported = bilinen boşluk** | K4 sinyali (`build-report.constructs[unsupported]`) | Üretecin **bildirdiği** bilinen boşluk. Registry'de karşılığı varsa uygula; yoksa dispozisyonlu bekletilir (sessiz geçilmez). |
| **3b. Codebase-grounded inference** | mevcut codebase yapısı (FK/nav/tip) — §2b | Eşleme contract'ta açık değil ama codebase **tek-aday-deterministik** çözüyorsa → `ASSUMPTIONS.md`'ye kaydet + **DEVAM**. 0/≥2 aday → rung-4. |
| **4. Hiçbiri eşleşmez** | — | **BİLİNMEYEN GAP → DUR + sor** (Faz 4b). **improvise YOK.** |

**Rung-1 örnek (üreteç-policy otomatik):** K1, bir validation input'unun "store"a bağlanmasını sorarsa ve
`build-report.policies.dedup-store == "in-memory"` ise → kademe rung-1 bunu okur, **mevcut in-memory store'u
kullan** kararını otomatik uygular, **rapora yazar**. Bu bir yönergedir, bilinmeyen gap **değildir**.

**Çakışma/öncelik:** rung-1 → rung-4 sırasıyla **ilk eşleşen** kazanır; alt rung'lara bakılmaz. Kademe
yukarıdan aşağı taranır; ilk eşleşmede durur. (Registry merge önceliği = proje-öğretili ⊕ paket-seed,
çakışmada **proje kazanır** — detay T5.4/K4.)

---

## 2b. Codebase-grounded KESİN inference → Varsayım Defteri (ASSUMPTIONS.md)  [iki-bant]

Bazı bağlamalar (özellikle K1 alan/kavram eşlemesi) contract'ta **açık değildir** ama **mevcut
codebase** yapısal cevabı verir — ör. iş-dili "Müşteri" hangi entity'ye düşer, Customer ile User
nasıl ilişkilenir. Bu, rung-1/2/3'ün **tanımlı yöntemi** değildir; ama körü körüne rung-4 DUR da
değildir. **Bu bant rung-4'ten ÖNCE denenir:** rung-1→3 eşleşmediyse, DUR'a düşmeden "codebase tek
bir somut yola indirgiyor mu?" bak. İki-bant kural:

- **KESİN (tek-aday, deterministik) → KAYDET + DEVAM.** Codebase eşlemeyi tam **bir** somut yapısal
  yola indirgiyorsa (tam **bir** FK / tam **bir** navigation property / tam **bir** aday tip),
  varsayımı **`ASSUMPTIONS.md`**'ye yaz (format aşağıda) ve gövdeyi yaz. Kanıt = somut kod artefaktı
  (`dosya:sembol`), his değil.
- **BELİRSİZ (0 veya ≥2 aday) → rung-4 DUR + sor.** Codebase **hiç** aday vermiyorsa (PoC `creditLimit`
  gibi — hiçbir yere bağlanmıyor) **ya da ≥2 aday** varsa (hangi yol doğru belirsiz) → **DUR + sor**.
  Ledger'a "varsayım" diye yazma — bu bir **SORU**'dur, varsayım değil.

> **"makul varsayım"a kapı DEĞİL.** Yalnız **tek-aday-deterministik** kanıt bu bandı geçer; sıfır-aday
> (bağlanamayan input) ve çok-aday (belirsiz) yine rung-4 DUR eder. PoC hatası (`creditLimit` sessiz
> "decimal varsay") bu bantta da engellenir: codebase hiç aday vermez → DUR.

> **Yalnız gate-tetikli DEĞİL — fill-time'da da geçerli.** Bu bant K1–K4 gate'inin gap çıkarmasını
> BEKLEMEZ. K1–K4 **mekanik** denetimlerdir; oysa asıl vaka (iş-dili "Müşteri" → hangi alan/entity)
> bir **semantik** kavram→alan eşlemesidir ve adlı `Rule_N` input'u olarak gate'e hiç yüzeye çıkmayabilir.
> Kural tek davranıştır: **Faz-4 fill sırasında contract'ta-açık-olmayan bir kavram→alan/entity
> eşlemesini codebase'den TÜRETTİĞİN HER AN** iki-bant uygula (tek-aday→kaydet+devam, 0/≥2→DUR). "rung-3b"
> yalnız kademedeki yeridir; tetik gate değil, **eşlemeyi yaptığın an**.

**Varsayım Defteri (`ASSUMPTIONS.md`) — konum + format:**
- **Konum:** target app **kökünde** `ASSUMPTIONS.md` (insan-tree; `gen/**`'e ASLA yazma — altın kural).
  Kalıcı insan-sahibi review artefaktı; her fill-run'da ilgili op başlığı altına madde eklenir.
- **Format (madde başına: NE + NEDEN-kanıt + GÜVEN):**
```
## {Op}
- NE: <ne varsayıldı/eşlendi — ör. "Müşteri → User entity, UserId ile">
  NEDEN: <somut kod kanıtı — ör. "Order.UserId FK (gen/Billing/Entities.g.cs); tek aday">
  GÜVEN: tek-aday (deterministik)
```

**Örnek (iki-bant ayrımı):** Op `CreateOrder`, kural "müşteri kendi siparişini görür". Contract
`customerId` demiyor; codebase'de **tek** yapısal yol `Order.UserId` FK → KESİN: `ASSUMPTIONS.md`'ye
"Müşteri → User (UserId FK, Entities.g.cs; tek aday)" yaz + filtreyi `UserId` ile kur. Eğer hem
`Order.UserId` hem `Order.CustomerId` olsaydı (≥2 aday) → ledger'a yazma, **DUR + sor**.

---

## 3. Faz 4b — Bilinmeyen gap → DUR, sor, (tekrar-edilebilirse) KAYDET  [§B.4.3]

Kademe rung-4'e düştüyse (hiçbir tanımlı yöntem yok) → **BİLİNMEYEN GAP**:

1. **DUR — gövdeyi YAZMA.** Seam'in `.Logic.cs` gövdesi **yazılmaz**. "Yorum ≠ STOP": gap'i yorumla
   işaretleyip devam etmek **yasak**; gerçek STOP = yazımı durdurmak.
2. **Gap'i KESİN sun.** Kullanıcıya tam olarak şunu göster: **contract ne diyor** (hangi construct,
   hangi predicate/throws), **ne eksik** (hangi denetim hangi gap'i çıkardı — ör. "K1: `creditLimit`
   alanı dört kaynağın hiçbirine bağlanamıyor"), **neden sadık ilerlenemez** (improvise = uydurma).
3. **Kullanıcıdan TANIM + EYLEM al.** Agent ancak kullanıcı tanım+eylem verdikten **sonra** yazar.
   Olası eylemler (kayıt `resolution.method` ile aynı sözlük): `use-generator-policy:X` /
   `inject-human-interface` / `map-to-error:Y` / `back-to-teknik-analiz`.
4. **Tekrar-edilebilirliği değerlendir + KAYDI ÖNER.** Çözüm genellenebilir (gap-signature gelecekte
   yeniden çıkabilir) ise → **kullanıcıyı bilgilendir** + registry'ye kaydı **ÖNER**. **Onay = gelecek
   otomatik-uygulama rızası** (bir sonraki aynı-signature gap'te kademe rung-2 bunu otomatik uygular).
   Tek-seferlik ise → yalnız uygula, kaydetme. **Kayıt asla sessiz değil — kullanıcıya önerilir.**

**Ne kaydedilir (kayıt İÇERİĞİ — dosya formatı T5.4/K4):**
```
gap-signature : { archetype, kural (K1/K2/K3), tetik (ör. "rule-input-unbindable:credit-limit") }
resolution    : { method: use-generator-policy:X | inject-human-interface | map-to-error:Y | back-to-teknik-analiz, params, template }
scope         : { package: <pkg>@<ver> }      // sürüm değişince yeniden-doğrula (drift güvenliği)
provenance    : { taught-by: user, date, repeatable: true }
```
> **Out of scope (atıf):** Registry **dosya formatı** + on-disk merge mekaniği + sürüm-drift yeniden-doğrulama
> = **T5.4 / K4** (`.dsl/gap-policies/<pkg>@<ver>/*.json`, proje-yanı, paket+sürüm scope, git'e commit →
> ekip paylaşır). Bu dosya yalnız kaydın **içeriğini** (yukarıdaki dört alan) tanımlar.

---

## 4. Rapora yazma (sessiz-olmama zorunluluğu)

Her kademe kararı **rapora yazılır** (Faz 6'da sunulur), sessizce uygulanmaz:
- **Rung-1 (üreteç-policy):** hangi policy, hangi karar (ör. `dedup-store: in-memory → mevcut store`).
- **Rung-2 (kayıtlı çözüm):** hangi gap-signature eşleşti, hangi resolution uygulandı.
- **Rung-3 (unsupported):** hangi construct bildirilen-bilinen boşluk olarak dispozisyonlandı.
- **Rung-3b (codebase-grounded inference):** hangi op'ta hangi eşleme `ASSUMPTIONS.md`'ye kaydedildi
  (NE + NEDEN-kanıt) — Faz 6 raporunda ledger maddeleri özetlenir.
- **Rung-4 (DUR):** hangi seam DUR oldu, kullanıcıya ne soruldu, (varsa) hangi kayıt önerildi.

---

## 5. Yapısal kabul senaryoları (bağlayıcı mantık)

Aşağıdaki üç senaryo runtime mantığını bağlayıcı biçimde tanımlar (markdown için yapısal kabul):

### 5.1 (6.1) Yapısal — denetim/kademe terimleri mevcut
`gap-protocol.md` K1–K4'ü, çözüm kademesini (`build-report.policies`/`policy`) ve DUR/improvise-yasağını
açıkça içerir (greps: `K1\|K2\|K3\|K4` ≥4; `DUR\|improvise` ≥1; `build-report.policies\|policy` ≥1).

### 5.2 (6.2) Pozitif — bilinen gap OTOMATİK (ladder rung-1)
Senaryo: `build-report.policies.dedup-store == "in-memory"` policy'li bir op. Detection bir store-bağlama
sorusu çıkarır → çözüm kademesi **rung-1** `policies.dedup-store`'u okur → **mevcut in-memory store'u kullan**
kararını **otomatik** uygular → **rapora yazar**. Bu bir üreteç-yönergesidir, bilinmeyen gap **değildir** →
DUR olmaz. Bu senaryo "tanımlı yöntem varsa onu kullan" ilkesinin pozitif kanıtıdır.

### 5.3 (6.3) Negatif — bilinmeyen gap → DUR (ladder rung-4)
Senaryo: PoC GAP #1 — `kredi-limiti` boundary'si yok; `creditLimit` rule-input'u K1'in **dört kaynağının
hiçbirine** (request-param / entity-field / boundary-dönüş / build-report.policy) bağlanamıyor → **K1 GAP**.
Çözüm kademesi taranır: rung-1 (policy) yok, rung-2 (kayıtlı çözüm) yok, rung-3 (unsupported) değmiyor →
**rung-4: BİLİNMEYEN GAP → DUR + kullanıcıya sor**. Gövde **yazılmaz**; improvise/yorum-işaretleme **YASAK**.
Bu senaryo PoC hatasının (sessiz "decimal varsay") tam olarak engellendiği kanıt-noktasıdır.

### 5.4 (§0.5) Yapısal — fill-öncesi ön-kapı terimleri mevcut
`gap-protocol.md` §0.5'i contract-only ön-kapı denetimlerini açıkça içerir
(greps: `P1\|P2\|P3\|P4` ≥4; `back-to-teknik-analiz` ≥1; `gen/\*\*.*DOKUNMADAN\|contract-only` ≥1).
§0.5 defekti **DUR + back-to-teknik-analiz**'e götürür (seam-fill kademesine DEĞİL); kaynak-4 §0.5'te denetlenmez.

---

## 6. Anti-patterns (yapma)

- **Detection'ı agent'ın "fark etmesine" BIRAKMA** → K1–K4 deterministik sonlu denetimdir; mekanik koşar.
- **Bilinmeyen gap'i yorumda işaretleyip DEVAM ETME** → "yorum ≠ STOP". Gerçek STOP = yazımı durdur, sun, sor.
- **Bilinmeyen boşlukta IMPROVISE ETME** ("makul varsayım" / "decimal varsay" / tip-uydur) → DUR + sor.
- **Kayıtlı/policy çözümü SESSİZ uygulama** → her kademe kararı rapora yazılır; yeni kaydı kullanıcıya öner.
- **Codebase ≥2 aday / 0 aday iken "tek-aday" diye `ASSUMPTIONS.md`'ye yazıp DEVAM etme** → bu, §2b
  iki-bant kuralını delip "makul varsayım"a döner. Tek-aday-deterministik DEĞİLse → rung-4 DUR + sor.
- **Codebase-grounded eşlemeyi SESSİZ yapma** → tek-aday olsa bile ledger'a yazılmadan geçme; kayıt zorunlu.
- **`silentDrops` JSON alanı ARAMA** → yoktur; silent-drop sinyali = exit≠0 / `status=="unsupported"`.
- **Aile-kapısı K1/K2'sini bu dosyaya gömme** → o T3.2 (bağımsız zorlama). Bu dosya paket-içi erken-DUR.
- **Kademe sırasını atlama** → rung-1→4 yukarıdan tara, ilk eşleşende dur; alt rung'a düşmeden DUR etme.
- **(§0.5) manifest defektini seam-fill kademesine ERTELEME** → contract referential-integrity / K2 /
  K1-1..3 defekti fill-ÖNCESİ §0.5'te yakalanır; "Faz 3 koşar, geçeyim" deme. Defekt → DUR + back-to-teknik-analiz.
- **(§0.5) yanlış infeasibility ÜRETME** → kaynak-4 (`build-report.policy`) post-gen'dir; policy-bağımlı alanı §0.5'te "bağlanamıyor" GAP'i sayma.

## 7. Out of scope (bu dosya)

- **Aile-kapısı K1/K2** (bağımsız zorlama, contract-vs-yüzey post-gen) → **T3.2** (`family-gate.md` §K).
  Bu dosya yalnız **paket-içi fill-öncesi erken-DUR**'u tanımlar (aynı K1/K2 kuralı, farklı katman).
- **Registry dosya formatı** + on-disk merge/drift → **T5.4 / K4**. Burada yalnız kayıt **içeriği**.
- **Verify-loop** (build + conformance oracle + retry/fresh-start) → **T5.5**.
- **Arketip playbook'ları + few-shot örnekler** → **T5.3**.
