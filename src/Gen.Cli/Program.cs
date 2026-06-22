using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Dotnet;

// Üreteç CLI: manifest.json (+operations.json) → .NET uygulaması + build-report.json.
// Kullanım: gen <manifest.json> <outDir>
var manifestPath = args.Length > 0 ? args[0] : "tests/fixtures/manifest.json";
var outDir = args.Length > 1 ? args[1] : "out";

var manifest = Loader.LoadManifest(manifestPath);
var contract = Loader.LoadContract(manifestPath, manifest.Contract);
var gm = GmBuilder.Build(manifest, contract);

var report = new BuildReport();
DotnetEmitter.Emit(gm, outDir, report);
report.WriteTo(Path.Combine(outDir, "build-report.json"));

Console.WriteLine($"emit → {outDir}  (clean={report.Clean}, constructs={report.Entries.Count})");
return report.Clean ? 0 : 1;
