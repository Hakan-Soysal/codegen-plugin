# Verify-loop & oracle — iki-kapılı doğrulama + retry/fresh-start + halüsinasyon kapısı (T5.5)

> **Bu dosya filler'ın Faz 5 mekaniğidir** (tasarım kaynağı `SEAM-DOLDURMA-SKILL-TASARIM.md` §B.5
> + §1.5). SKILL.md Faz 5 yalnız iki-kapılı oracle'ı **atfeder + sırasını sabitler**; mekanik
> **buradadır**.
>
> **Değişmez (her oturumda geçerli):** **build gerekli ama yetersiz.** `dotnet build` exit 0 bir
> seam'in contract'a SADIK olduğunu söylemez — PoC seam'i hem derlendi hem kısmen icat edilmişti
> (reward-hacking). Bu yüzden conformance ikincil oracle ZORUNLUDUR ve oracle **deterministiktir**:
> gerçek execution + assert. **LLM-judge ASLA** ("seam doğru mu?" diye LLM'e sordurmak yasak).

---

## 0. Neden bu loop var (amaç-odaklı)

Kullanıcının gereksinimi "derlendi" değil, **"her Tech DSL construct'ı davranışsal kapsandı"**.
Statik kat (`gen/**`) construct'ları adlı üyelere ayrıştırdı; filler onları kanonik sırada bağladı.
Ama gövde **derlenip yine de yanlış davranabilir** (PoC kanıtı: seam derlendi ama `DuplicateInvoice`
yerine generic hata döndüren bir kol icat edilebilirdi). İki olası yol var: (a) "derlendi → bitti"
deyip reward-hack'lemek (yasak), (b) build'i **gerekli ama yetersiz** sayıp davranışı **deterministik**
bir oracle ile doğrulamak. Bu loop (b)'yi zorunlu, (a)'yı imkânsız kılar.

**Oracle DETERMİNİSTİKTİR — "LLM'e sor" değil.** Conformance, gerçek execution + assert ile çalışır
(adapter bir op'u çağırır + dönen `Result<T>`'yi inceler; assertion contract-türevli SPEC'tedir).
**LLM-judge YASAK** — bir seam'in doğruluğu LLM'in yargısına bırakılmaz; deterministik testin
PASS/FAIL'ine bırakılır.

---

## 1. İki-kapılı oracle  [§B.5]

Her doldurulan seam **iki kapıdan** geçer. Birincisi geçmeden ikinciye geçilmez; her ikisi de
geçmeden seam "bitti" SAYILMAZ.

### Kapı 1 — Birincil: BUILD (zorunlu, ama yetersiz)

- **Komut:** `descriptor.build.command` (referans paket: `dotnet build {targetDir}/App.csproj`).
- **Başarı:** `descriptor.build.success` = **exit 0**.
- **Yetersizlik:** build-pass **contract-sadakatini söylemez** — derlenen kod yanlış iş-mantığı
  taşıyabilir (reward-hacking). Bu yüzden build geçse bile **Kapı 2 zorunludur**. **build gerekli
  ama yetersiz.**

### Kapı 2 — İkincil: CONFORMANCE (deterministik, zorunlu)

- **Ne koşar:** seam'in ilgili construct'larının **conformance spec'lerini** (T3.3, aile-sahibi,
  `manifest.json`/`operations.json`-türevli) **adapter** (T4.4) ile koşar → **hepsi PASS** olmalı.
- **Construct → test eşlemesi (T3.3 SPEC'ten türer):** `throws` → tipli-hata negatif testi;
  `validation` → sınır (NotValid/400); `rule` → ihlal (NotProcessable/422); `invariant` → property
  test; `idempotent` → replay (2. çağrı dedup); `saga` → failure-injection + compensate (v1 stub);
  `pagination` → `Page<T>` şekli; `roles`/`ownership`/`scopes` → 403.
- **Oracle = gerçek execution + assert.** Adapter generic execution harness'tır: built `App.dll`'i
  izole `AssemblyLoadContext`'te yükler, `AddGenerated` + op handler'ı çözer, `ExecuteAsync` çağırır,
  dönen `Result<T>` alt-tip adı + `Code`'u yansıtır. **Assertion ADAPTER'da DEĞİL** — SPEC'tedir
  (`assert.resultType` / `assert.code`, contract-türevli). Paket assertion'ı fudge edemez (A3).
- **LLM-judge ASLA.** Conformance, LLM'in "seam doğru görünüyor mu" yargısı DEĞİLDİR; deterministik
  bir testin PASS/FAIL'idir. Aynı seam + aynı spec → aynı sonuç.

#### `conformance.run` — somut çağrı (descriptor'dan; skill'e gömülü console runner)

Conformance runner skill bundle'ında **gömülü** gelir (`${CLAUDE_SKILL_DIR}/conformance/`) — kurulum
dışında klon/build/install GEREKMEZ, kullanıcının .NET runtime'ıyla koşar (jeneratör `techgen/` ile
simetrik). Çağrı (descriptor `conformance.run`):

```
dotnet ${CLAUDE_SKILL_DIR}/conformance/Conformance.dll <built App.dll yolu> <ailenin nötr spec JSON dizini/dosyası (T3.3 çıktısı)>
```

Runner spec'leri enumerate eder, `App.dll`'i `GeneratedApp` (AssemblyLoadContext) ile yükler, her spec'i
`SpecRunner`'dan geçirir, her spec için PASS/FAIL yazdırır. **Çıkış kodu: 0 = tüm spec PASS; ≠0 = FAIL**
(1 = en az bir spec `IsFail`; 2 = yükleme/argüman hatası). Loop için: **nonzero ⟹ conformance FAIL**.

> **Görev bölümü (A3):** SPEC = **aile** (T3.3, assertion contract-türevli); ADAPTER = **paket**
> (T4.4, "op çağır + Result incele" harness'ı); koşum + doğrulama = aile. Filler bu loop'ta yalnız
> **build'i koşar + conformance'ın PASS olduğunu doğrular**; spec/adapter YAZMAZ (out of scope).

---

## 2. Retry + fresh-start (debugging-decay sınırı)  [§B.5]

Build veya conformance düşerse loop **sınırlı** düzeltme dener — **sonsuz retry YOK** (kalite düşer,
debugging-decay). Sıralama deterministik:

| Adım | Bütçe | Davranış |
|---|---|---|
| **1. Build-fix iterasyonu** | seam başına **≤3** | Build/conformance FAIL → düzelt → yeniden koş. En fazla **3 retry**. |
| **2. Fresh-start** | **1 kez** | ≤3 iterasyon hâlâ geçmiyorsa → stub'ı geri-üret, seam'i **sıfırdan** bir kez doldur, yeniden koş. |
| **3. Gap → DUR** | — | Fresh-start da geçmiyorsa → bu bir **gap**'tir → **T5.2 gap-protocol.md §B.4.3** (Faz 4b: DUR + kullanıcıya sor). **improvise YOK.** |

- **≤3 + fresh-start sınırı KESİNDİR.** 3 build-fix + 1 fresh-start'tan sonra debugging durur; loop
  kendini "bir daha denerim" diye uzatmaz.
- **Retry-bitince → gap.** Çözüm bu dosyada DEĞİL: retry-exhausted hand-off **T5.2**'ye devredilir
  (`${CLAUDE_SKILL_DIR}/references/gap-protocol.md` §B.4.3 — Bilinmeyen gap → DUR, sor, kaydet). Bu
  dosya yalnız "ne zaman gap'e düşülür"ü tanımlar; DUR/sor/kayıt mekaniği T5.2'dir.

---

## 3. Halüsinasyon kapısı (slopsquatting kes)  [§B.5]

Seam **yalnız contract/gen'de geçen tip/paket** kullanır — icat edilmiş tip/paket ad'ı YASAK:

- **Tip/paket kaynağı:** seam yalnız contract (`manifest.json`/`operations.json`) veya üretilmiş
  yüzey (`gen/**` partial'ları — `handlerSurfaceMap` üyeleri) içinde **var olan** tipleri/paketleri
  çağırır. Bağlamda olmayan bir tip/paket = halüsinasyon.
- **İki katmanlı kesim:** (1) **build** — var olmayan tip/paket derlenmez (exit≠0 → Kapı 1 düşer);
  (2) **paket-allowlist** (varsa) — yalnız izinli NuGet paketleri; allowlist-dışı paket adı (ör.
  yazım-benzeri **slopsquatting**) reddedilir. Bu kapı, "derlenir ama uydurma bağımlılık çeker"
  riskini build + allowlist ile keser.

---

## 4. Yapısal kabul senaryoları (bağlayıcı mantık)

Aşağıdaki iki senaryo loop mantığını bağlayıcı biçimde tanımlar. **Bu senaryolar referans adapter'ın
gerçek acceptance testlerinde (T4.4) deterministik olarak koşulur ve KANIT'lanmıştır** (aşağı bkz.).

### 4.1 (6.2) Pozitif — golden seam GEÇER (1 iterasyon)

Senaryo: PoC `CreateInvoice` **doğru** doldurulmuş seam (dup → `DuplicateInvoice`/NotProcessable,
`amount<=0` → NotValid, else Success). **Kapı 1:** `dotnet build` exit 0. **Kapı 2:** CreateInvoice
conformance spec'leri (throws + validation) → **hepsi PASS**. → Loop **biter (1 iterasyon)**, retry'a
gerek yok. Bu, "build0 + conformance PASS → done" pozitif kanıtıdır.

> **KANIT (gerçek execution):** `ConformanceAcceptanceTests.Filled_correct_seam_all_specs_pass` —
> app emit edilir, doğru seam doldurulur, **build edilir**, 2 spec koşulur → 2 PASS.

### 4.2 (6.3) Negatif — icat edilen seam CONFORMANCE'tan düşer (build0 AMA FAIL)

Senaryo: seam **derlenir** ama `DuplicateInvoice` yerine generic **`ServerError`** döndürür.
**Kapı 1:** `dotnet build` exit 0 (seam SÖZDİZİMSEL olarak geçerli — build geçer). **Kapı 2:** throws
conformance spec'i beklenen `NotProcessable`/`DuplicateInvoice` yerine `ServerError` gözler →
**FAIL**. → Loop "bitti" DEMEZ; düzeltmeye zorlar (≤3 retry → fresh-start → gerekirse gap). Bu,
**build-pass tek başına "bitti" demez** kanıt-noktasıdır: assertion SPEC'te (contract-türevli)
olduğundan paket onu gizleyemez (A3) — icat edilmiş davranış conformance'tan **düşer**.

> **KANIT (gerçek execution):** `ConformanceAcceptanceTests.Wrong_seam_throws_spec_fails` — app emit
> edilir, **YANLIŞ** seam (ServerError) doldurulur, **build edilir (exit 0)**, throws spec koşulur →
> `IsFail`; FAIL nedeni gözlenen `ServerError`'ın beklenen `NotProcessable`'la uyuşmaması.
> Aynı koşumda validation spec'i hâlâ PASS (yanlış-seam yalnız dup kolunu bozdu → runner seçici).
>
> **Bu test gerçekten koşuldu:** bundled runner `dotnet ${CLAUDE_SKILL_DIR}/conformance/Conformance.dll
> <App.dll> <specs>` → doğru-seam: 2 pass, **exit 0**; yanlış-seam (ServerError): throws spec FAIL
> (beklenen `NotProcessable` ≠ gözlenen `ServerError`), **exit 1** (validation hâlâ PASS → runner seçici).
> Yani icat-seam'in build0 olmasına rağmen conformance'tan düştüğü **empirik doğrulandı** — "build gerekli
> ama yetersiz" değişmezinin somut kanıtı. (Runner'ın kendi acceptance suite'i ayrıca 3/3 geçer.)

---

## 5. Anti-patterns (yapma)

- **build-pass'i "bitti" SAYMA** → build gerekli ama YETERSİZ; conformance (Kapı 2) zorunlu (Bulgu #3).
- **LLM'e "seam doğru mu?" DİYE SORDURMA** → oracle **deterministik** (gerçek execution + assert);
  **LLM-judge ASLA**. Doğruluk testin PASS/FAIL'idir, LLM'in yargısı değil.
- **SONSUZ RETRY YAPMA** → seam başına **≤3** build-fix + **1** fresh-start; sonra **gap → DUR**
  (T5.2). Loop kendini uzatmaz (debugging-decay).
- **Conformance assertion'ını PAKETTE kurma** → assertion SPEC'tedir (T3.3, contract-türevli); paket
  yalnız adapter'ı (T4.4) sağlar, beklentiyi fudge edemez (A3).
- **İcat edilmiş tip/paket KULLANMA** → yalnız contract/gen'de var olan; build + paket-allowlist
  slopsquatting'i keser.
- **Retry-bitince gap'i BURADA çözme** → DUR/sor/kayıt mekaniği T5.2 (gap-protocol.md §B.4.3); bu
  dosya yalnız "ne zaman gap'e düşülür"ü tanımlar.

---

## 6. Out of scope (bu dosya)

- **Conformance SPEC yazma** (construct → spec eşleme, assertion contract-türevliği) → **T3.3**
  (`kesif/.../conformance-spec.md`).
- **Conformance ADAPTER yazma** (`GeneratedApp`/`SpecRunner` console runner'ı) → **T4.4** (kaynak
  `conformance-adapter/`, skill'e gömülü `${CLAUDE_SKILL_DIR}/conformance/`). Filler yalnız `conformance.run`'ı koşar.
- **Gap çözme** (retry-bitince DUR/sor/kayıt) → **T5.2** (`gap-protocol.md` §B.4.3). Bu dosya yalnız
  retry-exhausted → gap **devrini** tanımlar.
- **Aile kapısı** (paket-bağımsız yeniden-doğrulama, K1/K2 contract-vs-yüzey + conformance koşumu) →
  **M3 / T3.2** (`family-gate.md`). Bu loop **paket-içi** verify'dır; aile bağımsız tekrar doğrular.
