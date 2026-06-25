# Arketip Playbook Reference (filler)

Bu referans, seam-doldurucu (filler) için **arketip-bazlı uzman doldurma** rehberidir (tasarım §B.2-3).
Her arketip için: (a) **kanonik orkestrasyon sırası**, (b) **tek-tip seam (A2)** notu, (c) bir **few-shot
doğru-doldurulmuş** örnek (`doldurulacak` marker gövdesinin gerçek in-place impl ile değişmesi).

> Amaç: seam = ORKESTRASYON (§B.1). Üreteç sözleşmeyi `{op}Handler`'ın kardeş `.g.cs` partial'larına
> çağrılabilir/adlı üyelere ayrıştırdı (`Validation_N`, `Rule_N`, adlı-hata fabrikaları + `ThrowableErrors`,
> `RequiredRoles`, `IdempotencyKeys`, kapalı `Result<T>`, `Page<T>` zarfı). Gövdenin işi bunları **kanonik
> sırada bağlamaktır** → halüsinasyon yüzeyi dar. En değerli agent-bağlamı = Conventions + Examples.

---

## A2 — TEK-TİP SEAM DESENİ (post-T4, GLOBAL — tüm arketipler için aynı)

**Tüm seam noktaları handler-gövdesiyle AYNI desendedir.** İki ayrı fill mekaniği YOKTUR; Boundary /
Trigger / Subscription özel-durum DEĞİLDİR — handler ile birebir aynı in-place deseni kullanırlar:

```
gen-owned partial class  +  partial method imzası (.g.cs, HER ZAMAN ezilir)
        └── insan/LLM gövdesi  →  src/.../{X}.Logic.cs  (WriteIfAbsent, ASLA ezilmez, marker taşır)
```

- Marker konvansiyonu: boş seam gövdesi `"...doldurulacak"` alt-string'ini taşır
  (`throw new NotImplementedException("...: doldurulacak")`). Filler bu marker gövdesini gerçek impl ile değiştirir.
- `.g.cs` partial'lar üreteç-sahibidir, her run ezilir → seam'in *imzasını* asla elle değiştirme.
- `{X}.Logic.cs` insan-sahibidir, `WriteIfAbsent` ile bir kez üretilir ve **donar** → filler buraya yazar.
- Doldurma `gen/**` altına ASLA yazmaz (emission-contract yazma-yasağı).

**Arketip → tek-tip seam giriş-noktası** (hepsi aynı in-place desen, yalnız partial-method imzası değişir):

| Arketip ailesi | Seam dosyası (insan, WriteIfAbsent) | Doldurulacak partial-method |
|---|---|---|
| Command / Query (+saga/+idem/+pagination) | `src/{Module}/{op}Handler.Logic.cs` | `Task<Result<T>> ExecuteAsync(req, ct)` |
| Trigger-inbound | `src/{Module}/{op}{T}Trigger.Logic.cs` | `Task StartAsync(ct)` |
| Subscription-consumer | `src/{Consumer.Module}/{Event}To{Op}Consumer.Logic.cs` | `Task HandleAsync(@event, ct)` |
| Boundary-client | `src/Boundary/{Ext}Client.Logic.cs` | `I{Ext}` üye(ler)i (transport impl) |

> Trigger/Subscription sınıfları unseal edilip `StartAsync`/`HandleAsync` partial-method'a çevrildi; gövde
> `.Logic.cs`'e indi. Boundary `I{Ext}` gen'de kalır, impl `{Ext}Client.Logic.cs` insan-seam'i olur.
> Sonuç: filler **hepsini in-place doldurur**, arketip ayrımı tek-mekaniğe iner.

---

## 1. Command

**Tespit:** request `*Command`. **Kanonik sıra:**
`validate → rule → entity + invariant → persist → emit → Result`

- `Validation_N(input)` (false → `NotValid` / 400) → `Rule_N(input)` (false → `NotProcessable` / 422)
- entity oluştur/yükle + `{Entity}Invariants` → persist (`AppDbContext`) → event emit (`IEventBus`)
- her başarısız-olabilen adım **adlı-hata** fabrikasına bağlanır (`ThrowableErrors`); başarı → `Success<T>`.

**Seam (A2):** `src/{Module}/{op}Handler.Logic.cs`, gen partial-method `ExecuteAsync`.

Üretilen boş stub (`LogicFile`):
```csharp
public partial class CreateInvoiceHandler
{
    public partial Task<Result<InvoiceId>> ExecuteAsync(CreateInvoiceCommand request, CancellationToken ct)
        => throw new NotImplementedException("CreateInvoice: iş mantığı doldurulacak");  // ← marker
}
```

Few-shot — marker değiştirilmiş (kanonik sıra):
```csharp
public partial class CreateInvoiceHandler
{
    public partial async Task<Result<InvoiceId>> ExecuteAsync(CreateInvoiceCommand request, CancellationToken ct)
    {
        // 1) validate → NotValid (gen Validation_N + adlı-hata fabrikası)
        if (!Validation_0(new CreateInvoiceValidation0Input(request.Amount)))
            return InvalidAmount(new Dictionary<string, string> { ["amount"] = "must be > 0" });

        // 2) rule → NotProcessable
        if (!Rule_0(new CreateInvoiceRule0Input(request.CustomerId)))
            return CustomerNotEligible("customer not eligible for invoicing");

        // 3) entity + invariant
        var invoice = Invoice.Create(request.CustomerId, request.Amount);

        // 4) persist
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        // 5) emit
        await _bus.PublishAsync(new InvoiceCreated(invoice.Id), ct);

        // 6) Result
        return new Success<InvoiceId>(invoice.Id);
    }
}
```

---

## 2. Query

**Tespit:** request `*Query`, mutasyon yok. **Kanonik sıra:**
`yetki → sorgu → projeksiyon → Result`

- `RequiredRoles` / authz kontrolü (false → `NotAuthorized` / 403) → read-only sorgu (no `SaveChanges`)
- DTO projeksiyonu → `Success<T>`. Mutasyon/emit YOK.

**Seam (A2):** `src/{Module}/{op}Handler.Logic.cs`, gen partial-method `ExecuteAsync`.

Few-shot — marker değiştirilmiş:
```csharp
public partial class GetInvoiceHandler
{
    public partial async Task<Result<InvoiceDto>> ExecuteAsync(GetInvoiceQuery request, CancellationToken ct)
    {
        // 1) yetki
        if (!RequiredRoles.Contains(_ctx.Role))
            return Forbidden("invoice:read role required");

        // 2) sorgu (read-only)
        var invoice = await _db.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct);
        if (invoice is null)
            return InvoiceNotFound($"invoice {request.InvoiceId} not found");

        // 3) projeksiyon → 4) Result
        return new Success<InvoiceDto>(new InvoiceDto(invoice.Id, invoice.Amount, invoice.Status));
    }
}
```

---

## 3. saga (Command+saga)

**Tespit:** `Boundary`'de `// saga:` compensate kenarı. **Kanonik sıra:**
base Command sırası **+** dış-çağrı sırası **+** hata → **ters-sıra compensate**.

- `saga-orchestration-state` (generator-policy, in-memory) ile committed-adımları izle;
- bir adım fail → o ana kadar başarılı adımları **ters sırada** compensate çağır; sonra adlı-hata döndür.

**Seam (A2):** yine `src/{Module}/{op}Handler.Logic.cs` — saga *ayrı seam değildir*, aynı in-place
`ExecuteAsync` gövdesidir. Üreteç saga kenarını ext-partial'da yorum-marker olarak emit eder:
```csharp
// saga: Order → Payment.Charge (compensate: Payment.Refund)
// ponytail: committed-adımları izle; hata → ters-sıra Refund çağır (orchestration-state seam).
```

Few-shot — marker değiştirilmiş (ileri sıra + ters-sıra compensate):
```csharp
public partial class PlaceOrderHandler
{
    public partial async Task<Result<OrderId>> ExecuteAsync(PlaceOrderCommand request, CancellationToken ct)
    {
        if (!Validation_0(new PlaceOrderValidation0Input(request.Lines))) return EmptyOrder(/*...*/);

        var order = Order.Create(request.CustomerId, request.Lines);
        _db.Orders.Add(order);

        // ── saga ileri-sıra: committed adımları izle ──
        var committed = new Stack<Func<CancellationToken, Task>>();
        try
        {
            await _payment.ChargeAsync(order.Id, order.Total, ct);
            committed.Push(c => _payment.RefundAsync(order.Id, c));   // compensate kaydı

            await _inventory.ReserveAsync(order.Id, request.Lines, ct);
            committed.Push(c => _inventory.ReleaseAsync(order.Id, c));

            await _db.SaveChangesAsync(ct);
            return new Success<OrderId>(order.Id);
        }
        catch (Exception)
        {
            // ── hata → ters-sıra compensate (Stack zaten LIFO) ──
            while (committed.Count > 0) await committed.Pop()(ct);
            return PaymentOrReservationFailed("saga rolled back");
        }
    }
}
```

---

## 4. idempotency (Idempotent)

**Tespit:** `.Idem.g.cs` (gen `IdempotencyKeys` üyesi). **Kanonik sıra:**
**başta** `IIdempotencyStore.TryBeginAsync` (key = `IdempotencyKeys`'ten türetilmiş) **+** normal Command sırası.

- `TryBeginAsync` false → işlem daha önce yapılmış → idempotent yanıt döndür (yeniden çalıştırma);
- true → tek-seferlik mutasyona devam. `dedup-store` = generator-policy (in-memory).

**Seam (A2):** yine `src/{Module}/{op}Handler.Logic.cs` — idempotency ön-kontrolü aynı in-place
`ExecuteAsync` gövdesinin başına eklenir. Üreteç `IdempotencyKeys`'i `.Idem.g.cs` partial'ında emit eder:
```csharp
public partial class SubmitPaymentHandler
{
    public static readonly string[] IdempotencyKeys = ["request.PaymentRef"];
}
```

Few-shot — marker değiştirilmiş (TryBeginAsync başta):
```csharp
public partial class SubmitPaymentHandler
{
    public partial async Task<Result<PaymentId>> ExecuteAsync(SubmitPaymentCommand request, CancellationToken ct)
    {
        // 0) idempotency gate — EN BAŞTA (key = IdempotencyKeys)
        var key = request.PaymentRef;
        if (!await _idem.TryBeginAsync(key, ct))
            return new Success<PaymentId>(request.ExistingPaymentId);  // replay → aynı sonuç, yan-etki yok

        // 1..n) normal Command sırası
        if (!Validation_0(new SubmitPaymentValidation0Input(request.Amount))) return InvalidAmount(/*...*/);
        var payment = Payment.Create(request.PaymentRef, request.Amount);
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return new Success<PaymentId>(payment.Id);
    }
}
```

---

## 5. pagination (Query+pagination)

**Tespit:** `.Page.g.cs`. **Kanonik sıra:**
base Query sırası **+** strategy (cursor / offset) **+** `Page<T>` zarfı.

- yetki → sorgu (cursor/offset stratejisine göre `Take`/`Skip` veya `WHERE id > cursor`) → projeksiyon
- `Page<T>(Items, NextCursor)` zarfına sar; `cursor-token` kodlaması = generator-policy (opaque).

**Seam (A2):** yine `src/{Module}/{op}Handler.Logic.cs`, `ExecuteAsync`, dönüş tipi `Result<Page<T>>`.
Üreteç `Page<T>` zarfını çekirdekte kapalı record olarak emit eder:
```csharp
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
```

Few-shot — marker değiştirilmiş (cursor strategy + Page<T> zarfı):
```csharp
public partial class ListInvoicesHandler
{
    public partial async Task<Result<Page<InvoiceDto>>> ExecuteAsync(ListInvoicesQuery request, CancellationToken ct)
    {
        // 1) yetki
        if (!RequiredRoles.Contains(_ctx.Role)) return Forbidden("invoice:list role required");

        // 2) sorgu + strategy (cursor: id > decode(cursor))
        var afterId = request.Cursor is null ? 0 : DecodeCursor(request.Cursor);
        var rows = await _db.Invoices.AsNoTracking()
            .Where(i => i.Id > afterId)
            .OrderBy(i => i.Id)
            .Take(request.PageSize + 1)   // +1 → sonraki sayfa var mı?
            .ToListAsync(ct);

        // 3) projeksiyon + Page<T> zarfı
        var hasMore = rows.Count > request.PageSize;
        var items = rows.Take(request.PageSize)
            .Select(i => new InvoiceDto(i.Id, i.Amount, i.Status)).ToList();
        var next = hasMore ? EncodeCursor(items[^1].Id) : null;

        return new Success<Page<InvoiceDto>>(new Page<InvoiceDto>(items, next));
    }
}
```

---

## 6. Trigger-inbound

**Tespit:** `.Trigger.g.cs` (`IHostedService`, unseal + partial-method). **Kanonik sıra:**
`StartAsync → kaynaktan oku/dinle → request kur → _handler.ExecuteAsync → (gerekirse) ack/commit`

- inbound wiring (scheduler/queue/webhook/file/stream) kaynağını bağla; her olayda request kur,
  `_handler.ExecuteAsync` çağır. Trigger **iş mantığını içermez**, yalnız wiring yapar.

**Seam (A2):** `src/{Module}/{op}{T}Trigger.Logic.cs`, gen partial-method `StartAsync` — handler ile
**aynı in-place desen** (gen partial + partial-method imza + insan `.Logic.cs` gövdesi).

Üretilen boş stub (`TriggerLogic`):
```csharp
public partial class CreateInvoiceCronTrigger
{
    public partial Task StartAsync(CancellationToken ct)
        => throw new NotImplementedException("CreateInvoiceCronTrigger.StartAsync: doldurulacak");  // ← marker
}
```

Few-shot — marker değiştirilmiş (kaynak → _handler.ExecuteAsync):
```csharp
public partial class CreateInvoiceCronTrigger
{
    public partial Task StartAsync(CancellationToken ct)
    {
        // @trigger.cron inbound wiring: zamanlayıcı → request kur → _handler.ExecuteAsync
        _timer = new PeriodicTimer(TimeSpan.FromHours(1));
        _ = Task.Run(async () =>
        {
            while (await _timer.WaitForNextTickAsync(ct))
            {
                foreach (var due in await _source.DueInvoicesAsync(ct))
                {
                    var request = new CreateInvoiceCommand(due.CustomerId, due.Amount);
                    await _handler.ExecuteAsync(request, ct);   // gen partial'dan _handler enjekte edildi
                }
            }
        }, ct);
        return Task.CompletedTask;
    }
}
```

---

## 7. Subscription-consumer

**Tespit:** `Subscriptions.g.cs` consumer (unseal + partial-method). **Kanonik sıra:**
`HandleAsync → event → request eşle → _handler.ExecuteAsync`

- gelen `@event`'i hedef op'un request tipine eşle; `_handler.ExecuteAsync` çağır.
  Consumer **iş mantığı içermez**, yalnız event→request mapping + dispatch.

**Seam (A2):** `src/{Consumer.Module}/{Event}To{Op}Consumer.Logic.cs`, gen partial-method `HandleAsync` —
handler ile **aynı in-place desen**.

Üretilen boş stub (`SubscriptionLogic`):
```csharp
public partial class OrderPaidToCreateInvoiceConsumer
{
    public partial Task HandleAsync(App.Sales.OrderPaid @event, CancellationToken ct)
        => throw new NotImplementedException("OrderPaidToCreateInvoiceConsumer.HandleAsync: doldurulacak");  // ← marker
}
```

Few-shot — marker değiştirilmiş (event→request → _handler.ExecuteAsync):
```csharp
public partial class OrderPaidToCreateInvoiceConsumer
{
    public partial async Task HandleAsync(App.Sales.OrderPaid @event, CancellationToken ct)
    {
        // event → request eşle
        var request = new CreateInvoiceCommand(@event.CustomerId, @event.AmountDue);

        // dispatch (consumer iş mantığı içermez)
        await _handler.ExecuteAsync(request, ct);
    }
}
```

---

## 8. Boundary-client

**Tespit:** `I{External}Client` stub (gen'de `I{Ext}` arayüzü). **Kanonik sıra (üye başına):**
`request kur (transport DTO) → dış-çağrı (HTTP/gRPC/SDK) → yanıt eşle → tipli dönüş`

- dış adapter: arayüz üyesini transport impl ile gerçekle. Her üye = bir transport çağrısı + map.
  Hatalar transport-istisnasından domain'e çevrilir (saga compensate'i bunu çağırır).

**Seam (A2):** `src/Boundary/{Ext}Client.Logic.cs`, `I{Ext}` impl'i — handler ile **aynı in-place desen**
(gen `I{Ext}` arayüzü kalır; impl insan-seam'i `WriteIfAbsent`, ezilmez; DI `AddSingleton<I{Ext},{Ext}Client>`).

Üretilen boş stub (`BoundaryClientLogic`):
```csharp
public class PaymentGatewayClient : IPaymentGateway
{
    public Task<ChargeResult> ChargeAsync(string orderId, decimal amount, CancellationToken ct)
        => throw new NotImplementedException("PaymentGateway.Charge: doldurulacak");  // ← marker
}
```

Few-shot — marker değiştirilmiş (transport impl):
```csharp
public class PaymentGatewayClient : IPaymentGateway
{
    public async Task<ChargeResult> ChargeAsync(string orderId, decimal amount, CancellationToken ct)
    {
        // 1) request kur (transport DTO)
        var body = new { order_id = orderId, amount_cents = (int)(amount * 100) };

        // 2) dış-çağrı
        var resp = await _http.PostAsJsonAsync("/v1/charges", body, ct);
        resp.EnsureSuccessStatusCode();

        // 3) yanıt eşle → 4) tipli dönüş
        var dto = await resp.Content.ReadFromJsonAsync<ChargeResponse>(ct);
        return new ChargeResult(dto!.ChargeId, dto.Status == "succeeded");
    }
}
```

---

## Doldurma kuralları (özet)

1. **Yalnız contract/gen'de geçen tip/paket** kullan (halüsinasyon kapısı; paket-allowlist + build).
2. **Kanonik sırayı** ihlal etme — her arketibin sırası yukarıda kesindir.
3. Gövde gen üyelerini **bağlar** (icat etmez): `Validation_N`/`Rule_N`/`Invariants`/`RequiredRoles`/
   `IdempotencyKeys`/`ThrowableErrors` fabrikaları + kapalı `Result<T>` / `Page<T>`.
4. **Marker** (`doldurulacak`) gövdesini gerçek impl ile değiştir; imzaya dokunma.
5. `gen/**` altına ASLA yazma; yalnız `{X}.Logic.cs` insan-seam'lerine yaz (WriteIfAbsent, donar).
