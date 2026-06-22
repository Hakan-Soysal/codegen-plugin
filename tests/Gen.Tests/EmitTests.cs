using System.Diagnostics;
using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Dotnet;

namespace Gen.Tests;

public class EmitTests
{
    static GenerationModelHolder Gm() => new(GmBuilder.Build(
        Json.Parse<ManifestJson>(Fixtures.Read("manifest.json")),
        Json.Parse<ContractFile>(Fixtures.Read("operations.json"))));

    sealed record GenerationModelHolder(Gen.Core.Gm.GenerationModel Value);

    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "gen-emit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static (int code, string output) Dotnet(string args, string cwd)
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

    [Fact]
    public void Emitted_app_compiles_and_report_is_clean()
    {
        var dir = TempDir();
        try
        {
            var report = new BuildReport();
            DotnetEmitter.Emit(Gm().Value, dir, report);

            Assert.True(File.Exists(Path.Combine(dir, "App.csproj")));
            Assert.True(report.Clean);

            var (code, output) = Dotnet("build App.csproj -v q --nologo", dir);
            Assert.True(code == 0, "Üretilen app derlenmedi:\n" + output);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Operation_file_has_request_record_and_partial_handler()
    {
        var dir = TempDir();
        try
        {
            DotnetEmitter.Emit(Gm().Value, dir, new BuildReport());
            var f = File.ReadAllText(Path.Combine(dir, "src", "Billing", "CreateInvoice.g.cs"));
            Assert.Contains("public sealed record CreateInvoiceCommand(string CustomerId, Money Amount);", f);
            Assert.Contains("public partial Task<Result<Invoice>> ExecuteAsync(CreateInvoiceCommand request, CancellationToken ct);", f);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Logic_file_is_preserved_on_regeneration()
    {
        var dir = TempDir();
        try
        {
            var gm = Gm().Value;
            DotnetEmitter.Emit(gm, dir, new BuildReport());
            var logic = Path.Combine(dir, "src", "Billing", "CreateInvoiceHandler.Logic.cs");
            File.WriteAllText(logic, "// HUMAN BODY\n");

            DotnetEmitter.Emit(gm, dir, new BuildReport());   // regen
            Assert.Equal("// HUMAN BODY\n", File.ReadAllText(logic));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Generated_g_files_are_byte_deterministic()
    {
        var (a, b) = (TempDir(), TempDir());
        try
        {
            var gm = Gm().Value;
            DotnetEmitter.Emit(gm, a, new BuildReport());
            DotnetEmitter.Emit(gm, b, new BuildReport());
            var fa = File.ReadAllText(Path.Combine(a, "src", "Billing", "CreateInvoice.g.cs"));
            var fb = File.ReadAllText(Path.Combine(b, "src", "Billing", "CreateInvoice.g.cs"));
            Assert.Equal(fa, fb);
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }
}
