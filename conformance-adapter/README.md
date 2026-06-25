# Conformance execution adapter (T4.4)

Generic, language-specific (C#/xUnit/DI) execution harness that **runs** the family's
language-neutral conformance SPECs (T3.3) against a generated app. **The adapter only RUNS specs;
it never authors them.** The assertion lives in the SPEC (`assert.resultType` / `assert.code`,
contract-derived). The package cannot fudge it (A3 invariant).

## Components

- `Spec.cs` — DTO for the neutral SPEC JSON `{construct, opId, arrange, act, assert}` (consumed, not produced).
- `GeneratedApp.cs` — loads a *built* generated `App.dll` in an isolated `AssemblyLoadContext`
  (resolves EF/ASP.NET via the app's `deps.json`), invokes `AddGenerated`, resolves the op handler,
  invokes `ExecuteAsync`, and reflects the returned `Result<T>` sub-type name + `Code`. Carries NO assertion.
- `SpecRunner.cs` — the ONE generic harness (no per-construct classes). For each spec:
  `arrange → act → assert`. The assert compares the observed `ResultShape` against
  `spec.assert.resultType` / `spec.assert.code` — **no expected value is embedded in the adapter**.
  Includes a deterministic random generator (`RandomDecimals`) for invariant property tests (Step 5.2).
- `Tests/` — test-fixture scaffolding (emits the app from `tests/fixtures/manifest.json`, fills the
  CreateInvoice seam with a throwaway impl, builds it) + the §6 acceptance tests. This scaffolding is
  NOT part of `SpecRunner` (which consumes an already-built assembly path, matching how T1.3 invokes it).

## `conformance.run` — descriptor command string (Step 5.3, consumed by T1.3)

T1.3 writes this verbatim into the reference descriptor's `descriptor.conformance.run` field. The family
feeds N specs + the built app path via environment variables that the runner **actually reads**
(`ConformanceRunEntrypoint.Run_specs_from_env_against_app`):

```
CONFORMANCE_APP=<path to the built App.dll of the generated app under test> \
CONFORMANCE_SPECS=<dir of the family's neutral spec JSON files (*.json, T3.3 output)> \
dotnet test CoreTemplate1/conformance-adapter/ConformanceAdapter.csproj -c Debug
```

The entrypoint enumerates `CONFORMANCE_SPECS/**/*.json`, loads `CONFORMANCE_APP` via `GeneratedApp`, runs
every spec through `SpecRunner`, and fails the run if any spec is `IsFail`. When the two env vars are
absent the entrypoint SKIPs (so plain `dotnet test` still works for the self-supplied acceptance suite,
which emits + builds the app and inlines the T3.3 CreateInvoice specs itself).

## NOT in `Gen.slnx`

This project is intentionally kept OUT of `Gen.slnx` so the solution-level `dotnet build`/`dotnet test`
stays exactly 73/73. Run the adapter as its own `dotnet test` (the command above).
