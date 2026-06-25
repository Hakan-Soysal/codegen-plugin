using ConformanceAdapter;

// BUNDLED CONSOLE CONFORMANCE RUNNER (install-only; runs via `dotnet Conformance.dll` on the user's
// .NET runtime — no SDK build, no clone). This is the console-Main embodiment of the verified
// ConformanceRunEntrypoint: it loads the family's language-neutral SPECs (T3.3), runs them through the
// VERIFIED SpecRunner against a built generated App.dll, and prints per-spec PASS/FAIL.
//
// A3 invariant: the ASSERTION LIVES IN THE SPEC, not here. This Main only loads specs, runs the dumb
// SpecRunner, prints results, and maps pass/fail → exit code. It embeds no expected resultType/code.
//
// Usage: dotnet Conformance.dll <appDllPath> <specsPath>
//   <appDllPath>  = path to the built generated App.dll under test
//   <specsPath>   = a directory of *.json specs (recursive) OR a single *.json spec file (each file = one Spec)
// Exit: 0 if all non-skipped specs PASS; 1 if any FAIL (or on usage/loading error).

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet Conformance.dll <appDllPath> <specsPath>");
    Console.Error.WriteLine("  <appDllPath>  built generated App.dll under test");
    Console.Error.WriteLine("  <specsPath>   dir of *.json specs (recursive) OR a single specs *.json file");
    return 2;
}

var appDll = args[0];
var specsPath = args[1];

if (!File.Exists(appDll))
{
    Console.Error.WriteLine($"ERROR: app assembly not found: {appDll}");
    return 2;
}

// Load specs: single file → one Spec; directory → every *.json (recursive), each = one Spec.
// Mirrors ConformanceRunEntrypoint (SpecJson has no array parser; each file is ONE Spec object).
List<string> specJsons;
if (File.Exists(specsPath))
{
    specJsons = new List<string> { File.ReadAllText(specsPath) };
}
else if (Directory.Exists(specsPath))
{
    specJsons = Directory.EnumerateFiles(specsPath, "*.json", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal)
        .Select(File.ReadAllText)
        .ToList();
}
else
{
    Console.Error.WriteLine($"ERROR: specsPath is neither a file nor a directory: {specsPath}");
    return 2;
}

if (specJsons.Count == 0)
{
    Console.Error.WriteLine($"ERROR: no *.json specs found under: {specsPath}");
    return 2;
}

IReadOnlyList<Spec> specs;
try
{
    specs = SpecJson.ParseMany(specJsons);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: failed to parse specs: {ex.GetType().Name}: {ex.Message}");
    return 2;
}

IReadOnlyList<SpecResult> results;
try
{
    using var app = GeneratedApp.Load(appDll);
    results = await new SpecRunner().RunAsync(specs, app).ConfigureAwait(false);
}
catch (Exception ex)
{
    var inner = ex.InnerException ?? ex;
    Console.Error.WriteLine($"ERROR: failed to load/run app '{appDll}': {inner.GetType().Name}: {inner.Message}");
    return 2;
}

// Per-spec human-readable PASS/FAIL/SKIP. The Detail string already carries the spec-vs-observed
// evidence on FAIL (e.g. "resultType beklenen='NotProcessable' (spec), gözlenen='ServerError'") —
// that comes from the SPEC, proving the assertion is not embedded in this runner (A3).
var pass = 0;
var fail = 0;
var skip = 0;
foreach (var r in results)
{
    var tag = r.Status switch
    {
        SpecStatus.Pass => "PASS",
        SpecStatus.Fail => "FAIL",
        _ => "SKIP"
    };
    if (r.Status == SpecStatus.Pass) pass++;
    else if (r.Status == SpecStatus.Fail) fail++;
    else skip++;

    Console.WriteLine($"[{tag}] {r.Spec.Construct}/{r.Spec.OpId}: {r.Detail}");
}

Console.WriteLine($"---\nconformance: {pass} pass, {fail} fail, {skip} skip ({results.Count} specs)");

return fail == 0 ? 0 : 1;
