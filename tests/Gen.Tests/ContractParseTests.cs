using System.Text.Json;
using Gen.Core;
using Gen.Core.Model;

namespace Gen.Tests;

public class ContractParseTests
{
    // Gerçek CommandDSL contract'ında effect.target hem string ("biz.Invoice") hem path
    // dizisi (["Appointment","status"]) olabilir. Fixture yalnız string içerdiği için bu
    // hiç test edilmemişti → dizi target System.Text.Json'da throw ediyordu (JoinError).
    [Fact]
    public void Effect_target_accepts_string_or_path_array()
    {
        var json = """
        {"meta":{"schemaVersion":2},
         "operations":[
           {"id":"A","kind":"command","signature":{"actor":"x","verb":"v","ownership":"any","resource":"R"},
            "guards":[],"effects":[{"kind":"create","target":"biz.Invoice"}],"access":{"writes":[],"reads":[]},
            "flows":[],"processes":[],"domain":"D"},
           {"id":"B","kind":"command","signature":{"actor":"x","verb":"v","ownership":"any","resource":"R"},
            "guards":[],"effects":[{"kind":"calculate","target":["Appointment","status"]}],"access":{"writes":[],"reads":[]},
            "flows":[],"processes":[],"domain":"D"}],
         "entities":[],"actors":[],"relations":[]}
        """;
        var c = Json.Parse<ContractFile>(json);   // dizi target ARTIK throw etmemeli
        Assert.Equal(2, c.Operations!.Count);
        Assert.Equal(JsonValueKind.String, c.Operations[0].Effects[0].Target!.Value.ValueKind);
        Assert.Equal(JsonValueKind.Array, c.Operations[1].Effects[0].Target!.Value.ValueKind);
    }
}
