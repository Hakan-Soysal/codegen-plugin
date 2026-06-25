namespace ConformanceAdapter.Tests;

// `conformance.run` ENTRYPOINT (descriptor.conformance.run, T1.3 tüketir).
// Aile N spec + build edilmiş app yolunu ENV ile besler; bu fact onları gerçekten okur ve koşar:
//   CONFORMANCE_APP   = build edilmiş üretilen app App.dll yolu
//   CONFORMANCE_SPECS = dil-nötr spec JSON dosyalarının dizini (veya glob) — *.json
// ENV yoksa fact SKIP olur (referans-pakette acceptance test'leri self-supply eder, ENV gereksiz).
public sealed class ConformanceRunEntrypoint
{
    [SkippableFact]
    public async Task Run_specs_from_env_against_app()
    {
        var appDll = Environment.GetEnvironmentVariable("CONFORMANCE_APP");
        var specsDir = Environment.GetEnvironmentVariable("CONFORMANCE_SPECS");

        // ENV verilmemişse atla — referans-pakette self-supplied acceptance test'leri yeterli.
        Skip.If(string.IsNullOrWhiteSpace(appDll) || string.IsNullOrWhiteSpace(specsDir),
            "CONFORMANCE_APP / CONFORMANCE_SPECS verilmedi — entrypoint atlandı (self-supplied test'ler koşar).");

        Assert.True(File.Exists(appDll), $"CONFORMANCE_APP yok: {appDll}");
        Assert.True(Directory.Exists(specsDir), $"CONFORMANCE_SPECS dizini yok: {specsDir}");

        var specJsons = Directory.EnumerateFiles(specsDir!, "*.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();
        Assert.True(specJsons.Count > 0, $"CONFORMANCE_SPECS altında *.json spec yok: {specsDir}");

        var specs = SpecJson.ParseMany(specJsons);
        using var app = GeneratedApp.Load(appDll!);
        var results = await new SpecRunner().RunAsync(specs, app);

        var failures = results.Where(r => r.IsFail).ToList();
        Assert.True(failures.Count == 0,
            "conformance FAIL:\n" + string.Join("\n", failures.Select(r => "  " + r)));
    }
}
