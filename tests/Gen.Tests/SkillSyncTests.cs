using System.Diagnostics;
using Gen.Core;
using Gen.Core.Gm;
using Gen.Core.Model;
using Gen.Core.Pipeline;
using Gen.Core.Report;
using Gen.Dotnet;

namespace Gen.Tests;

/// <summary>
/// techgen-sync skill'inin dayandığı substrat sözleşmeleri (Faz 6 eval).
/// Skill'in 3. adımı (kırık seam → uzlaştır) ve 4. adımı (orphan Logic tespiti) burada
/// gerçek `dotnet build` ile doğrulanır.
/// </summary>
public class SkillSyncTests
{
    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "gen-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static int Build(string cwd)
    {
        var psi = new ProcessStartInfo("dotnet", "build App.csproj -v q --nologo")
        { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd };
        var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    static GenerationModel Gm() => GmBuilder.Build(
        Json.Parse<ManifestJson>(Fixtures.Read("manifest.json")),
        Json.Parse<ContractFile>(Fixtures.Read("operations.json")));

    [Fact]
    public void Stale_human_logic_seam_fails_build_loudly()   // skill adım 3 tetikleyici
    {
        var dir = TempDir();
        try
        {
            DotnetEmitter.Emit(Gm(), dir, new BuildReport());
            Assert.Equal(0, Build(dir));   // ön koşul: temiz seam derlenir

            // signature değişimini simüle et: insan Logic.cs'i artık üretilen .g.cs partial'ıyla
            // eşleşmeyen bir imza implement ediyor (eski imza). Üreteç regen'de Logic'i EZMEZ.
            var logic = Path.Combine(dir, "src", "Billing", "CreateInvoiceHandler.Logic.cs");
            File.WriteAllText(logic,
                """
                using App;
                namespace App.Billing;
                public partial class CreateInvoiceHandler
                {
                    // stale: gerçek partial bildirimi (CreateInvoiceCommand) ile eşleşmiyor
                    public partial Task<Result<Invoice>> ExecuteAsync(string staleParam, CancellationToken ct)
                        => throw new NotImplementedException();
                }
                """);

            DotnetEmitter.Emit(Gm(), dir, new BuildReport());   // regen: .g.cs imzası yeniden üretilir, Logic korunur

            Assert.NotEqual(0, Build(dir));   // kırık seam YÜKSEK SESLE başarısız (sessiz kayma yok)
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Orphan_human_logic_is_detectable_after_op_removed()   // skill adım 4 tespiti
    {
        var dir = TempDir();
        try
        {
            var gm = Gm();
            DotnetEmitter.Emit(gm, dir, new BuildReport());

            var genFile = Path.Combine(dir, "gen", "Billing", "GetInvoice.g.cs");
            var logic = Path.Combine(dir, "src", "Billing", "GetInvoiceHandler.Logic.cs");
            Assert.True(File.Exists(genFile) && File.Exists(logic));

            // GetInvoice manifest'ten çıkarıldı → regen
            var trimmed = gm with { Operations = gm.Operations.Where(o => o.Id != "GetInvoice").ToList() };
            DotnetEmitter.Emit(trimmed, dir, new BuildReport());

            // SKILL.md adım-4 tespit mantığı: gen/ karşılığı olmayan Logic.cs = orphan
            Assert.False(File.Exists(genFile), ".g.cs prune edildi");
            Assert.True(File.Exists(logic), "Logic.cs orphan olarak DURUR (auto-silinmez) → skill sorar");
            var orphan = !File.Exists(Path.Combine(dir, "gen", "Billing", "GetInvoice.g.cs"))
                         && File.Exists(logic);
            Assert.True(orphan, "orphan tespit edilebilir (gen partial yok + Logic var)");
        }
        finally { Directory.Delete(dir, true); }
    }
}
