# Audit Fix Todo — bağımsız denetim bulgularını kapatma

5 bağımsız denetçinin bulduğu açıklar. TDD + per-task commit; gate yeşil + emit derlenir kalmalı.

- [x] F1 · Uncharted BoundaryOp alt-ağacı (serving/validation/param-ext) — census + emitter; owned-entity concurrency realize (🔴 gerçek silent-drop, gate kör)
- [ ] F2 · Decimal/non-int literal tip-uyumu — ExprBuild .NET-only type-aware literal (🔴 CS0019, INV-4)
- [ ] F3 · `paginated by` strategy + size fidelity (offset≠cursor, size default) (🟠)
- [ ] F4 · GET route-token olmayan param binding (🟠 CS7036)
- [ ] F5 · Gate `Covers` substring → sınır-duyarlı eşleşme (🟡 soundness)
- [ ] F6 · Manifest-layer N/A belgeleme (@internal, ext@external/uncharted/error/event) + minör (permit comment) (🟡/⚪)

Not: @internal ve ext@external/error/event'in GERÇEK fix'i CommandDSL/manifest.ts'te (working-dir dışı, local-only kuralı → kullanıcı onayı gerekir). Bu repo'da yalnız belgeleme + savunma.
