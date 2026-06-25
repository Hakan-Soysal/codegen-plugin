# Conformance execution adapter (T4.4)

Generic, language-specific (C#/DI) execution harness that **runs** the family's
language-neutral conformance SPECs (T3.3) against a generated app. **The adapter only RUNS specs;
it never authors them.** The assertion lives in the SPEC (`assert.resultType` / `assert.code`,
contract-derived). The package cannot fudge it (A3 invariant).

Shipped as a **bundled, framework-dependent console runner** — installs with the codegen plugin and
runs install-only via `dotnet Conformance.dll` on the user's .NET runtime (no SDK build, no clone),
exactly like the bundled generator at `plugins/codegen/skills/filler/techgen/`.

## Projects

- `Conformance.csproj` — the console runner (`OutputType=Exe`, net10, framework-dependent). Contains
  `Spec.cs`, `GeneratedApp.cs`, `SpecRunner.cs`, and `Program.cs`. Carries NO assertion and NO
  dependency on the generator (`Gen.Core`/`Gen.Dotnet`) — it consumes an already-built `App.dll` + spec
  JSON. `FrameworkReference Microsoft.AspNetCore.App` is kept so the host can load the EF/ASP generated
  app; DI + `System.Text.Json` are shared-framework, so there are NO PackageReferences and the publish
  is tiny (`Conformance.dll` ~36 KB + `.deps.json` + `.runtimeconfig.json`).
- `Tests/Conformance.Tests.csproj` — SEPARATE xUnit project that ProjectReferences the console project
  (for `SpecRunner`/`Spec`/`GeneratedApp`) and `Gen.Core`/`Gen.Dotnet` (ONLY for the `AppFixture` test
  scaffold). Verifies the runner itself: `Filled_correct_seam_all_specs_pass`,
  `Wrong_seam_throws_spec_fails`, `Invariant_property_generator_runs_and_holds`.

## Components

- `Spec.cs` — DTO for the neutral SPEC JSON `{construct, opId, arrange, act, assert}` (consumed, not produced).
- `GeneratedApp.cs` — loads a *built* generated `App.dll` in an isolated `AssemblyLoadContext`
  (resolves EF/ASP.NET via the app's `deps.json`), invokes `AddGenerated`, resolves the op handler,
  invokes `ExecuteAsync`, and reflects the returned `Result<T>` sub-type name + `Code`. Carries NO assertion.
- `SpecRunner.cs` — the ONE generic harness (no per-construct classes). For each spec:
  `arrange → act → assert`. The assert compares the observed `ResultShape` against
  `spec.assert.resultType` / `spec.assert.code` — **no expected value is embedded in the adapter**.
  Includes a deterministic random generator (`RandomDecimals`) for invariant property tests.
- `Program.cs` — the console `Main`. Loads specs, runs `SpecRunner` against the app, prints per-spec
  PASS/FAIL (with the spec-vs-observed detail on FAIL), and exits 0 if all pass, 1 if any fail.

## `conformance.run` — bundled invocation (consumed by the descriptor)

The family feeds the built app path + the neutral spec JSONs (T3.3 output) as **positional args**:

```
dotnet ${CLAUDE_SKILL_DIR}/conformance/Conformance.dll <appDllPath> <specsPath>
```

- `<appDllPath>` = path to the built generated `App.dll` under test.
- `<specsPath>`  = a directory of `*.json` specs (recursive) OR a single `*.json` spec file. Each file
  is ONE neutral `Spec` object (matching `SpecJson.Parse`).

The runner loads `<appDllPath>` via `GeneratedApp`, runs every spec through `SpecRunner`, prints
`[PASS]/[FAIL]/[SKIP] <construct>/<opId>: <detail>`, and **exits 1 if any spec is `IsFail`** (0 if all
pass; `Skipped`/stub is not a fail). Usage / load / parse errors exit 2.

## Building / publishing the bundle

```
dotnet publish conformance-adapter/Conformance.csproj -c Release -o /tmp/conf-pub
# then copy *.dll + *.json (exclude *.pdb + the native apphost) into:
#   plugins/codegen/skills/filler/conformance/
```

## NOT in `Gen.slnx`

These projects are intentionally kept OUT of `Gen.slnx` so the solution-level `dotnet build`/`dotnet test`
stays exactly 73/73. The acceptance suite runs as its own test:
`dotnet test conformance-adapter/Tests/Conformance.Tests.csproj`.
