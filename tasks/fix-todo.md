# Fix Todo — denetim boşlukları (INV-7 + partial)

Sıra: risk-first. Gate önce (drop'ları RED'e çevirir), sonra fix → yeşile çek.

## Phase A — Completeness gate (kök-neden)
- [x] A1 · Manifest construct census + SilentDrop gate (M) → mevcut fixture RED
- [x] A2 · CLI/emit-sonu gate entegrasyonu + exit kodu (S)
- [ ] ▸ Checkpoint A: gate RED, Sınıf-1 drop'lar görünür

## Phase B — Sınıf-1 fix (fixture'da mevcut)
- [x] B1 · throws + named-error catalog → tip + result binding (M)
- [x] B2 · consistency {risk,mode} → tx/outbox seçim-iskeleti (S)
- [x] B3 · deployable → GMDeployable + compose/AppHost stub (M)
- [x] B4 · note → doc-comment (S)
- [ ] ▸ Checkpoint B: mevcut fixture'da gate YEŞİL

## Phase C — POCO completeness (latent)
- [x] C1 · tüm annotation-site'larına Ext (param/field/deployable/module/entity/type) (M)
- [x] C2 · uncharted tipli model + çağrı-adapter (M)
- [x] C3 · sourceOfTruth → cross-module FK (S)
- [x] C4 · for guard/guardRef kararı (S) — Open Q1
- [ ] ▸ Checkpoint C: latent drop'lar kapandı

## Phase D — Partial'lar
- [x] D1 · serving @grpc/@queue → UnsupportedConstruct+report (sessiz değil) (S)
- [x] D2 · external BoundaryOp serving+validation AST (INV-4) (S)
- [ ] D3 · on/subscriptions → consumer wiring (M)
- [ ] D4 · @http/@trigger → hedef-stub + policy (M) — Open Q2

## Phase E — Tam fixture + final gate
- [ ] E1 · full-coverage fixture (her keyword) (M)
- [ ] E2 · final gate YEŞİL + regression (.NET+Go compile, determinizm, tüm test) (S)
- [ ] ▸ Checkpoint E (Complete): INV-7 mekanik zorlanıyor
