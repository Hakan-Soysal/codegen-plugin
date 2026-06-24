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

// manifest yanındaki gen.config.json (.NET üreteç parametreleri; yoksa null → mevcut davranış).
var config = GenConfig.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, "gen.config.json"));

var report = new BuildReport();
DotnetEmitter.Emit(gm, outDir, report, config);
Completeness.Check(manifest, report);   // INV-7 gate: census ⊄ report → SilentDrop
report.WriteTo(Path.Combine(outDir, "build-report.json"));

var drops = report.SilentDrops;
// Gate exit kodu = SilentDrop yokluğu (INV-7). Unsupported (ör. grpc/queue serving) AÇIK rapordur, drop değil.
Console.WriteLine($"emit → {outDir}  (clean={report.Clean}, constructs={report.Entries.Count}, silentDrops={drops.Count})");
foreach (var d in drops) Console.WriteLine($"  ⚠ SESSİZ DROP: {d.Construct} / {d.Id}");
return drops.Count == 0 ? 0 : 1;
