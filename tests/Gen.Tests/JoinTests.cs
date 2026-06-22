using Gen.Core;
using Gen.Core.Model;
using Gen.Core.Pipeline;

namespace Gen.Tests;

public class JoinTests
{
    static ManifestJson Manifest() => Json.Parse<ManifestJson>(Fixtures.Read("manifest.json"));
    static ContractFile Contract() => Json.Parse<ContractFile>(Fixtures.Read("operations.json"));

    [Fact]
    public void Loader_resolves_contract_relative_to_manifest()
    {
        var m = Loader.LoadManifest(Fixtures.At("manifest.json"));
        var c = Loader.LoadContract(Fixtures.At("manifest.json"), m.Contract);
        Assert.NotNull(c);
        Assert.Equal("biz.CreateInvoice", c!.Operations!.Single().Id);
    }

    [Fact]
    public void Linked_join_attaches_business_and_derives_command_query()
    {
        var gm = GmBuilder.Build(Manifest(), Contract());
        var create = gm.Operations.Single(o => o.Id == "CreateInvoice");
        var get = gm.Operations.Single(o => o.Id == "GetInvoice");

        Assert.True(create.IsCommand);                 // access.creates dolu
        Assert.NotNull(create.Business);               // realizes biz.CreateInvoice çözüldü
        Assert.Equal("create", create.Business!.Effects.Single().Kind);

        Assert.False(get.IsCommand);                   // yalnız reads → query
        Assert.Null(get.Business);                     // realizes null
    }

    [Fact]
    public void Operations_are_deterministically_ordered_by_id()
    {
        var gm = GmBuilder.Build(Manifest(), Contract());
        Assert.Equal(new[] { "CreateInvoice", "GetInvoice", "ListInvoices" }, gm.Operations.Select(o => o.Id));
    }

    [Fact]
    public void Standalone_skips_join_no_business_no_error()
    {
        var m = Manifest() with { Mode = "standalone", Contract = null };
        var gm = GmBuilder.Build(m, null);   // contract yokken bile patlamaz
        Assert.All(gm.Operations, o => Assert.Null(o.Business));
    }

    [Fact]
    public void Linked_unresolved_realizes_throws_JoinError()
    {
        var emptyContract = new ContractFile(new ContractMeta(2), new(), new(), new(), new());
        Assert.Throws<JoinError>(() => GmBuilder.Build(Manifest(), emptyContract));
    }

    [Fact]
    public void Linked_with_contract_path_but_missing_file_throws_JoinError()
    {
        var m = Manifest();   // mode linked, contract "./operations.json"
        Assert.Throws<JoinError>(() => GmBuilder.Build(m, null));
    }
}
