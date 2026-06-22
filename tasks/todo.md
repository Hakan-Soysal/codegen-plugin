# Todo — Tech DSL Kod Üreteci (.NET-first)

Sıra: yukarıdan aşağı (dependency + risk-first). Her task bitince `dotnet build`/golden/build-report doğrula, sonra commit.

## Phase 1 — Foundation
- [x] Task 1 · Solution + manifest fixture (S)
- [x] Task 2 · POCO'lar + ExprNode polimorfik converter (M)
- [x] Task 3 · Load + realizes-join + GM (operations) (M)
- [x] Task 4 · build-report iskeleti (S)
- [x] ▸ Checkpoint: Foundation

## Phase 2 — .NET walking skeleton
- [x] Task 5a · Operation → Command/Query + Handler.g.cs + Handler.Logic.cs (M)
- [x] Task 5b · serving → Minimal API endpoint; command/query türevi (M)
- [x] Task 6 · Result<T> 6'lı taksonomi (S)
- [x] ▸ Checkpoint: Walking skeleton (üretilen app DERLENİYOR)

## Phase 3 — ExprNode → C# predicate
- [x] Task 7 · ExprNode → C# emitter + test (M)
- [x] Task 8 · validation/rule/permit/invariant → predicate gövdesi (S)

## Phase 4 — .NET core constructs
- [x] Task 9 · entity/field/sourceOfTruth/concurrency → EF entity (M)
- [x] Task 10 · type/enum → record/enum (S)
- [x] Task 11 · event/emits/on → record + pub/sub stub + outbox (M)
- [x] Task 12 · auth (roles/ownership/scopes) → guard iskeleti (S)
- [x] ▸ Checkpoint: .NET core (Go spike'a hazır)

## Phase 5 — Go spike (SEAM GATE)
- [ ] Task 13 · Go emitter, Phase 2–4 construct'ları, aynı GM (L→dilimle)
- [ ] Task 14 · Seam bulguları raporu (S)
- [ ] ▸ Checkpoint: SEAM GATE — structure ancak şimdi 'stabil'

## Phase 6 — GM sertleştirme
- [ ] Task 15 · GM'i Go bulgularına göre düzelt (M)
- [ ] Task 16 · generation-model.schema.json (S)

## Phase 7 — .NET long-tail
- [ ] Task 17 · pagination (S)
- [ ] Task 18 · calls + compensate → saga iskeleti (M)
- [ ] Task 19 · idempotent → dedup konvansiyonu (S)
- [ ] Task 20 · passthrough prelude'lar (@http/@trigger/@crypto/@audit/@sensitivity/@metric) (M)
- [ ] Task 21 · external/uncharted → çağrı-adapter stub (S)
- [ ] ▸ Checkpoint: Complete
