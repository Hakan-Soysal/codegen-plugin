using System.Diagnostics;
using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Go;

namespace Gen.Tests;

public class GoEmitTests
{
    static Gen.Core.Gm.GenerationModel Gm() => GmBuilder.Build(
        Json.Parse<ManifestJson>(Fixtures.Read("manifest.json")),
        Json.Parse<ContractFile>(Fixtures.Read("operations.json")));

    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "gen-go-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static (int code, string output) Go(string args, string cwd)
    {
        var psi = new ProcessStartInfo("go", args) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd };
        var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o);
    }

    [Fact]
    public void Emitted_go_compiles()
    {
        var dir = TempDir();
        try
        {
            GoEmitter.Emit(Gm(), dir, new BuildReport());
            var (code, output) = Go("build ./...", dir);
            Assert.True(code == 0, "Üretilen Go derlenmedi:\n" + output);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Result_is_value_error_pair_not_sum_type()
    {
        var dir = TempDir();
        try
        {
            GoEmitter.Emit(Gm(), dir, new BuildReport());
            var logic = File.ReadAllText(Path.Combine(dir, "createinvoice_logic.go"));
            Assert.Contains("(Invoice, error)", logic);   // (T, error) — Go idiom
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Predicates_are_typed_and_realized_no_dynamic()
    {
        var dir = TempDir();
        try
        {
            var report = new BuildReport();
            GoEmitter.Emit(Gm(), dir, report);
            // tipli predicate: stub değil, gerçek tipli Go + input struct
            var guards = File.ReadAllText(Path.Combine(dir, "createinvoice_guards.g.go"));
            Assert.Contains("input.Amount > 0", guards);
            Assert.Contains("type CreateInvoiceValidation0Input struct", guards);
            Assert.Contains(report.Entries, e => e.Construct == "validation" && e.Status == ConstructStatus.Realized);
        }
        finally { Directory.Delete(dir, true); }
    }
}
