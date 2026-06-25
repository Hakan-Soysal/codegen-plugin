using System.Diagnostics;
using System.Reflection;
using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Dotnet;

namespace ConformanceAdapter.Tests;

// TEST-FIXTURE SCAFFOLDING (production DEĞİL). Üretilen app'i fixture manifest'inden emit eder,
// CreateInvoice seam'ini THROWAWAY bir impl ile doldurur (M5 değil — sadece adapter'ı koşturmak için),
// build eder ve App.dll yolunu döndürür. SpecRunner bu scaffold'a BAĞIMLI DEĞİL: zaten-build edilmiş
// assembly + spec listesi tüketir (T1.3 onu gerçek üretilen app'e karşı böyle çağıracak).
public static class AppFixture
{
    static string FixturesDir()
    {
        // Test projesi konumu: conformance-adapter/Tests/ → bin/Debug/net10.0.
        // 3x ".." ile Tests proje köküne, +1 ".." ile conformance-adapter köküne, +1 ".." ile repo köküne çık.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var testsRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", ".."));
        var repoRoot = Path.GetFullPath(Path.Combine(testsRoot, "..", ".."));
        return Path.GetFullPath(Path.Combine(repoRoot, "tests", "fixtures"));
    }

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "conf-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // app'i emit et (seam'ler boş = NotImplementedException). dir'e yazar.
    public static void Emit(string dir)
    {
        var fx = FixturesDir();
        var manifest = Json.Parse<ManifestJson>(File.ReadAllText(Path.Combine(fx, "manifest.json")));
        var contract = Json.Parse<ContractFile>(File.ReadAllText(Path.Combine(fx, "operations.json")));
        var gm = GmBuilder.Build(manifest, contract);
        DotnetEmitter.Emit(gm, dir, new BuildReport());
    }

    // CreateInvoice seam'ini doldur (throwaway). impl = ExecuteAsync gövdesi (return ifadesi).
    // MARKER-convention: doldurulmuş seam 'doldurulacak' substring'i İÇERMEZ.
    public static void FillCreateInvoiceSeam(string dir, string executeBody)
    {
        var path = Path.Combine(dir, "src", "Billing", "CreateInvoice", "CreateInvoiceHandler.Logic.cs");
        var content =
            "using App;\n" +
            "using App.Billing;\n\n" +
            "namespace App.Billing;\n\n" +
            "public partial class CreateInvoiceHandler\n" +
            "{\n" +
            "    // throwaway test-fixture impl (M5 değil). Dedup için instance-state.\n" +
            "    private readonly HashSet<string> _seen = new();\n\n" +
            "    public partial Task<Result<Invoice>> ExecuteAsync(CreateInvoiceCommand request, CancellationToken ct)\n" +
            "    {\n" +
            executeBody + "\n" +
            "    }\n" +
            "}\n";
        File.WriteAllText(path, content);
    }

    // app'i build et → App.dll mutlak yolu. exit!=0 → exception (build kanıtı).
    public static string Build(string dir)
    {
        var (code, output) = RunDotnet("build App.csproj -v q --nologo", dir);
        if (code != 0)
            throw new InvalidOperationException("Üretilen app build edilmedi:\n" + output);
        var dll = Path.Combine(dir, "bin", "Debug", "net10.0", "App.dll");
        if (!File.Exists(dll))
            throw new InvalidOperationException("App.dll bulunamadı: " + dll);
        return dll;
    }

    static (int code, string output) RunDotnet(string args, string cwd)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = cwd
        };
        var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o);
    }
}
