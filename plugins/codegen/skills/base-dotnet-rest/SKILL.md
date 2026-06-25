---
name: base-dotnet-rest
description: >-
  [dsl-generator] .NET/C# REST üreticisinin (techgen-dotnet) referans paketinin LLM-doldurucu (seam) yarısı. Statik üretilmiş
  (`gen/**`) bir .NET projesindeki BOŞ insan-seam'lerini (`{op}Handler.Logic.cs` ve T4-sonrası
  tek-tip in-place trigger/subscription/boundary seam'leri) arketip-bazlı uzman playbook'larla,
  kanonik sırada, contract'a SADIK biçimde doldurur; her seam'i `dotnet build` + conformance
  oracle ile doğrular. Bağlam-derleme → arketip → gap-gate → doldur → verify → rapor fazlarını
  yürütür. `describe` argümanıyla çağrılırsa YALNIZ kendi `capability.json` descriptor'ını döndürür
  (keşfin self-describe sözleşmesi; üretim YAPMAZ). Şu durumlarda kullan: keşif/aile bu paketi
  seçip devrettiğinde — "seam doldur", "Logic.cs doldur", "iş mantığını yaz", "statik üretilmiş
  projeyi tamamla", "generator çıktısını doldur" — ya da `describe` modunda capability sorgusu
  geldiğinde. Statik üretimi (`gen/**`) bu skill YAPMAZ (generator binary'sinin işi); yalnız
  insan-seam'i doldurur. `[dsl-generator]`
---

# base-dotnet-rest — .NET/C# REST projesindeki insan-seam'lerini doldur

Bu skill, eşli generator'ın **statik kattı** (`gen/**`) ürettikten sonra geriye kalan **boş
insan-seam'lerini** (iş mantığı = orkestrasyon gövdesi) doldurur. Statik kat zaten üyeleri (adlı
validation/rule/invariant, hata fabrikaları, idempotency anahtarları, saga client'ları, kapalı
`Result<T>`) emit etti; senin işin bunları **kanonik sırada bağlamak** — halüsinasyon yüzeyi dar
(Generation Gap deseni). Tasarım kaynağı: `SEAM-DOLDURMA-SKILL-TASARIM.md` §B.

> **Bu skill paket-içi mekaniğin LLM yarısıdır (§B).** Aile (`is-analizi → teknik-analiz → kesif`)
> üretmez/doldurmaz; **seçer + devreder + doğrular** (kapı, §A). Sen yalnız §A sözleşmelerine
> (capability beyanı, gap-protokol uyumu, owned-tree dokunulmazlığı) uyduğun sürece keşif seni seçer
> ve aile kapısı çıktını geçirir.

---

## 0. `describe` MODU — HER ŞEYDEN ÖNCE (self-describe sözleşmesi)

**Bu kapı, fazlardan ÖNCE çalışır.** Çağrı argümanı `describe` ise:

1. **YALNIZCA** `${CLAUDE_SKILL_DIR}/capability.json` dosyasını oku ve içeriğini **birebir** döndür.
2. **DUR.** Başka **hiçbir şey** yapma: statik üretim yok, seam doldurma yok, build yok, faz
   yürütme yok. `describe` üretim modunun yan-kapısı **değildir**.
3. Yalnız **kendi** `${CLAUDE_SKILL_DIR}` dizinini okursun (sanctioned alan); başka paketin/projenin
   dosyasına uzanmazsın.

Bu, keşfin `protocol/self-describe.md` sözleşmesidir: keşif kurulu-skill listesinde
`description`'ında **`[dsl-generator]`** token'ını taşıyan filler-skill'leri **aday** sayar, sonra
her adayı `describe` modunda çağırıp descriptor'ını (`capability.json`) alır. `describe` çağrısı
ASLA kod/dosya üretmez.

> Argüman `describe` **değilse** → aşağıdaki 6-faz doldurma akışını yürüt.

---

## Neyi neden böyle yapıyoruz (özü kavra)

Amaç "field eşleşiyor mu" değil; **"üretilen + doldurulan kod contract'a sadık derleniyor ve
davranışsal kapsanıyor mu"**. Üç değişmez tüm tasarımı dayatır:

1. **`gen/**` paket-sahibi, dokunulmaz; `*.Logic.cs` insan-sahibi, doldurulan.** Statik kat
   deterministik (id-sıralı, byte-aynı) ve yeniden-üretimde ezilir; seam katı LLM-üretilir.
2. **Determinizm = STATİK kat.** "Aynı girdi → aynı çıktı" yalnız `gen/**` için geçerli. Seam
   deterministik DEĞİL — ama **bir kez doldurulur** (`WriteIfAbsent`, yeniden-roll YOK) ve
   **commit'lenir → o andan sonra DONMUŞ insan-kodu** gibi davranır; yeniden-üretim onu ezmez
   (INV-C). Tekrar-üretilebilirlik **commit ile** sağlanır, LLM ile değil.
3. **Bilinmeyen boşlukta improvise YASAK → DUR + sor.** "Yorum ≠ STOP."

---

## Altın kurallar (her oturumda geçerli — değişmezler)

- **`gen/**` ağacına ASLA yazma — SERT YASAK.** `gen/**` generator-sahibidir (descriptor
  `emissionContract.ownedTree`). Tek bir satır bile düzenleme; bir sonraki yeniden-üretim ezer ve
  aile kapısı `provenance` ile dokunulmuşluğu yakalayıp **RED** verir. Yalnız **human-seam**'e yaz:
  `{op}Handler.Logic.cs` + T4-sonrası tek-tip in-place boundary/trigger/subscription seam'leri
  (hepsi `descriptor.emissionContract.humanTree` altında).
- **Seam tespiti = BOŞ-marker substring'i** (descriptor-tahrikli, üreteç-nötr). Boş insan-seam'i,
  `descriptor.emissionContract.seamPath` + `emptyStubMarker`'dan bulunur. Marker **tek bir literal
  string'e hardcode DEĞİL** — descriptor'dan gelir; tespit **substring `doldurulacak`** ile yapılır.
  Geçerli marker biçimleri (hepsi `doldurulacak` substring'ini taşır):
  `{op}: iş mantığı doldurulacak`, `{Ext}.{op}: doldurulacak`, `{cls}.HandleAsync: doldurulacak`.
  Bu substring'i taşıyan seam = doldurulacak boş seam; taşımayan = dolu (dokunma — v1 bayat-seam
  fill yapmaz, §B.6).
- **Bilinmeyen gap → DUR. improvise YASAK.** Contract'ta karşılığı bulunamayan, tanımlı çözümü
  (üreteç-policy / kayıtlı çözüm) olmayan her boşlukta **dur ve kullanıcıya sor** — uydurma, tahmin,
  "makul varsayım" YOK. **"Yorum ≠ STOP":** bir şeyi açıklayıcı yorumla geçiştirmek STOP değildir;
  gerçek STOP = yazımı durdurup gap'i kesin sunmak. (PoC hatası: `ResourceCreditLimit` bağlanamadı
  ama STOP edilmedi → improvisation. Bu yasak.)
- **Codebase-grounded KESİN inference → kaydet + devam (dar istisna).** Contract'ta açık olmayan bir
  eşlemeyi (ör. iş-dili "Müşteri" → hangi entity; Customer↔User bağı) mevcut codebase **tek-aday-
  deterministik** çözüyorsa (tam **bir** FK/nav/tip), varsayımı app-kökündeki **`ASSUMPTIONS.md`**'ye
  (insan-tree; `gen/**` DEĞİL) **NE + NEDEN-kanıt** olarak yaz ve DEVAM et. **0 ya da ≥2 aday → yine
  DUR + sor.** Bu "makul varsayım"a kapı DEĞİL — yalnız somut kod-kanıtlı tek-aday geçer. Mekanik +
  format: `references/gap-protocol.md` §2b.
- **Tek-tip in-place seam varsayımı (T4-sonrası).** Trigger / subscription / boundary'yi "ayrı
  dosya / farklı mekanik" diye işleme. T4-sonrası HEPSİ handler-gövdesiyle **aynı** desende:
  gen-owned `partial` + human `{X}.Logic.cs` (WriteIfAbsent, marker). Hepsini **in-place** doldur.
- **Onaylanmamış/üretilmiş katı kabul etmeden doldurma.** Statik kat eksikse (gen yok) DUR —
  statik üretim senin işin değil (generator binary'si; K5 fold = filler binary'yi çağırır, ama
  doldurma akışı üretilmiş gen'i varsayar).

---

## Başlamadan

- **Descriptor'ı çöz.** `${CLAUDE_SKILL_DIR}/capability.json` → `emissionContract` (seamPath,
  emptyStubMarker, ownedTree, humanTree, handlerSurfaceMap, canonicalOrder), `archetypeRules`,
  `gapProtocol`, `build`, `conformance`. **Tüm yol/marker/sıra değerleri buradan; hardcode etme.**
- **Devretme paketini doğrula** (§A.3): `manifest.json`/`operations.json` (domain niyeti + linked
  contract), `gen.config.json` (paket-spesifik profil: dbProvider vb.), `targetDir`, üretilmiş
  `gen/**` + `build-report`. Biri yoksa DUR + bildir.

Sonra altı fazı **sırayla** yürüt.

---

## Faz 1 — Bağlam-derleme (deterministik)

**Amaç:** Her seam için sınırlı, kayıtlı bağlam paketi kur (agent'ın "tahmin etmesi"ne bırakma).
**Derle:**
- **`{op}` prefix → handler partial ailesi.** O op'un feature-slice klasöründen (`gen/{Module}/{op}/`)
  `{op}` kardeş `.g.cs` partial'larını topla
  (`{op}.g.cs`, `.Endpoint.g.cs`, `.Idem.g.cs`, `.Page.g.cs`, `.Trigger.g.cs` + module kökündeki `Subscriptions.g.cs` vb.) →
  doldurulacak gövdenin çağırabileceği üye yüzeyi (`Validation_N`, `Rule_N`,
  `{Entity}Invariants.Invariant_N`, hata fabrikaları, `RequiredRoles`, `IdempotencyKeys`,
  `I{External}` client).
- **`realizes` link → niyet.** Seam'in `realizes`'ı üzerinden `operations.json`/`manifest.json`
  iş niyetini bağla (bu op ne yapmalı, hangi construct'ları taşıyor).
- **`build-report` policy/construct.** `build-report.policies` (consistency-mode, dedup-store,
  saga-orchestration-state…) + `build-report.constructs[].status` → seam'i değiştiren parametrik
  bağlam. Görmezsen "derlenir ama yanlış profil" riski.
- **`config`.** `gen.config.json` alt-detayları (dbProvider, transport…).

> Bu paket **deterministik + sınırlıdır** — keyfi dosya gezme değil, descriptor-yönlü sabit küme.

---

## Faz 2 — Arketip sınıflandır (B.3)

**Amaç:** Seam'i doğru uzman-playbook'a yönlendir. `descriptor.archetypeRules` tespit kurallarıyla
arketipi belirle (taksonomi §B.3):

| Arketip | Tespit (descriptor `archetypeRules`'tan) |
|---|---|
| Command | request `*Command` |
| Command+saga | Boundary'de `// saga:` compensate kenarı |
| Idempotent | `.Idem.g.cs` var |
| Query | request `*Query`, mutasyon yok |
| Query+pagination | `.Page.g.cs` var |
| Trigger-inbound | `.Trigger.g.cs` (`IHostedService`) |
| Subscription-consumer | `Subscriptions.g.cs` consumer |
| Boundary-client | `I{External}Client` stub |

Arketipler birleşebilir (ör. Command + saga + idempotent). Birden çok arketip imzası varsa
hepsini uygula (kanonik sıra çakışmayı çözer).

> **Playbook İÇERİĞİ burada DEĞİL** — arketip→playbook eşlemesi + few-shot doğru-doldurulmuş
> örnekler **T5.3**'tür (referans dosyaları). Bu faz yalnız **sınıflandırır**.

---

## Faz 3 — Gap-detection gate (doldurma-ÖNCESİ, deterministik)

**Amaç:** Gövde yazılmadan ÖNCE sonlu bağlanabilirlik denetimi koş — improvisation'ı kapıda kes.
Doldurma-öncesi dört denetim (K1–K4); geçmeyen → **GAP → STOP** (Faz 4'e geçme):

- **K1 — predicate-input bağlanabilirliği:** her `Validation_N`/`Rule_N`/`Invariant_N` input alanı
  request-param / entity-field / boundary-dönüş / `build-report.policy`'ye bağlanmalı.
- **K2 — failable→named-error:** başarısız olabilen her validation/rule, throws kataloğunda
  adlı-hataya eşlenmeli.
- **K3 — dependency çözünürlüğü:** seam'in ihtiyacı her bağımlılık DI'da (`Bootstrap.g.cs`) ya da
  boundary olmalı.
- **K4 — unsupported:** `build-report.constructs[status=unsupported]` bu op'a değiyorsa → bilinen
  boşluk (bildirilmiş).

> **Gate RUNTIME DETAYLARI burada DEĞİL** — K1–K4'ün mekanik denetim algoritması + çözüm-kademesi
> (üreteç-policy → kayıtlı çözüm → unsupported → DUR+sor) + kayıt içeriği **T5.2**'dir:
> `${CLAUDE_SKILL_DIR}/references/gap-protocol.md`. Bu faz yalnız **kapıyı atfeder + sırasını sabitler**
> (fill-öncesi). Dual-layer: filler K1–K4'ü pakette erken-DUR koşar; aile **yalnız K1/K2'yi** A.4
> kapısında bağımsız yeniden koşar (öz-beyana güvenmez — T3.2).

---

## Faz 4 — Çözüm kademesi / doldur (in-place seam)

**Amaç:** Gate geçen seam'in gövdesini **kanonik sırada** yaz — yerinde (in-place).
- **Çözüm kademesi (T5.2):** gap varsa ilk eşleşen otomatik uygulanır (rapora yazılır, sessiz
  değil): (1) üreteç-policy → (2) kayıtlı çözüm → (3) unsupported-bilinen → (3b) codebase-grounded
  KESİN inference → (4) hiçbiri ⇒ DUR+sor. Bu kademenin mekaniği **T5.2**; buraya atıf yeter.
- **Codebase-grounded eşleme → `ASSUMPTIONS.md` (rung-3b):** contract'ta açık olmayan bir eşlemeyi
  mevcut codebase **tek-aday-deterministik** çözdüyse, gövdeyi yazmadan önce app-kökündeki
  `ASSUMPTIONS.md`'ye ilgili op başlığı altına **NE + NEDEN-kanıt (`dosya:sembol`) + GÜVEN** maddesini
  yaz, sonra devam et. **0/≥2 aday → DUR+sor** (ledger'a yazma). Format + iki-bant ayrımı: gap-protocol §2b.
- **In-place fill:** arketip playbook'unun (T5.3) kanonik sırasında üyeleri bağla; gövdeyi
  **`{X}.Logic.cs` human-seam'ine** yaz (`WriteIfAbsent` — boş-marker substring'i `doldurulacak`
  taşıyan seam'e bir kez). `gen/**`'e ASLA dokunma. Kanonik sıra descriptor'dan
  (`emissionContract.canonicalOrder`, ör. idempotency→authz→validation→external-input→rule→
  entity+invariant→persist→emit→return).
- **Tek-tip:** trigger/subscription/boundary aynı in-place desenle doldurulur (ayrı dosya muamelesi
  yok). **v1 kapsam (B.6):** yalnız IN-PLACE seam'i olan arketipler (Command/Query ailesi);
  trigger/subscription/boundary techgen temiz human-seam emit edene kadar **açıkça ertelenmiş**
  (sessiz değil).

---

## Faz 5 — Verify-loop (T5.5)

**Amaç:** Her doldurulan seam'i doğrula — "derlendi" yetmez (build-pass contract-sadakatini
söylemez; reward-hacking riski).
- **Birincil (zorunlu):** `descriptor.build.command` → exit 0.
- **İkincil (conformance):** deterministik oracle (gerçek execution+assert; LLM-judge ASLA) —
  throws→negatif, invariant→property, validation/rule→sınır, idempotent→replay, saga→
  failure-injection+compensate, pagination→`Page<T>`, roles→403.
- **Retry:** seam başına ≤3 build-fix → sonra fresh-start → o da olmazsa gap → DUR+sor.

> **Verify-loop DETAYLARI burada DEĞİL** — iki-kapılı oracle mekaniği (build + conformance) +
> retry/fresh-start + halüsinasyon kapısı **T5.5**'tir:
> `${CLAUDE_SKILL_DIR}/references/verify-loop.md`. Bu faz yalnız **atfeder + sırasını sabitler**.
> **build gerekli ama yetersiz** — conformance (deterministik oracle, LLM-judge ASLA) zorunlu.

---

## Faz 6 — Rapor

**Amaç:** Ne yapıldığını **sessiz olmayan** biçimde bildir.
- Doldurulan seam'ler (arketip + uygulanan kanonik sıra).
- Gate sonuçları (K1–K4: geçti/STOP), uygulanan çözüm-kademesi kararları (üreteç-policy / kayıtlı
  çözüm — her biri açıkça, sessiz değil).
- **`ASSUMPTIONS.md` varsayımları (rung-3b):** codebase-grounded eşlemeler (NE + NEDEN-kanıt) — varsa
  madde madde özetle; insanın denetlemesi için ledger dosyasına işaret et.
- Bilinmeyen gap → DUR olan seam'ler + kullanıcıya sorulan + (tekrar-edilebilirse) kayıt önerisi.
- Verify sonuçları (build exit, conformance oracle).
- v1 kapsam-dışı ertelenen seam'ler (trigger/subscription/boundary) — açıkça.

---

## CreateInvoice (Command+saga+idempotent) — 6 fazda izlenebilir akış (PoC)

Referans PoC, fill akışının fazlardan nasıl geçtiğini somutlaştırır:

1. **Bağlam-derleme:** `CreateInvoice` prefix → `CreateInvoiceHandler.g.cs` + `.Idem.g.cs` +
   saga client `IPaymentGateway`; `realizes CreateInvoice` → operations.json niyeti;
   `build-report.policies`: `saga-orchestration-state`, `dedup-store`.
2. **Arketip:** request `*Command` + `.Idem.g.cs` + Boundary'de `// saga:` → **Command + saga +
   idempotent** (üç imza birden).
3. **Gap-gate (K1–K4):** validation/rule input'ları request/entity'ye bağlı mı (K1); başarısız
   rule'lar adlı-hataya eşli mi (K2 — PoC'de `Rule_0` adsızdı → STOP olmalıydı); saga client DI'da
   mı (K3); unsupported değiyor mu (K4). Geçerse Faz 4.
4. **Doldur (kanonik sıra):** `IdempotencyKeys` ile `TryBeginAsync` (idempotent başta) → authz →
   `Validation_N` → external-input → `Rule_N` → entity+`Invariant_N` → persist → saga: dış-çağrı
   sırası + hata→ters-sıra compensate → emit → `Result<T>`. Gövde **`CreateInvoiceHandler.Logic.cs`**
   human-seam'ine yazılır (`doldurulacak` marker'lı), `gen/**` dokunulmaz.
5. **Verify:** `dotnet build` exit 0 + conformance: idempotent replay, saga failure-injection→
   compensate, throws→negatif, invariant→property.
6. **Rapor:** doldurulan seam + gate/kademe kararları + verify sonuçları.

---

## Referans dosyaları (gerektiğinde oku — İÇERİK ayrı task'larda)

- `${CLAUDE_SKILL_DIR}/capability.json` — descriptor (seamPath/marker/sıra/arketip kuralları). **T1.3.**
- `${CLAUDE_SKILL_DIR}/references/gap-protocol.md` — gap-runtime (K1–K4 detection gate + çözüm-kademesi + DUR/sor/kayıt içeriği). **T5.2.**
- Arketip playbook'ları + few-shot doğru-doldurulmuş örnekler — **T5.3.**
- `${CLAUDE_SKILL_DIR}/references/verify-loop.md` — iki-kapılı oracle (build + conformance, deterministik/LLM-judge yasak) + retry/fresh-start + halüsinasyon kapısı. **T5.5.**
