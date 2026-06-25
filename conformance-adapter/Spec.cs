using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConformanceAdapter;

// Dil-nötr conformance SPEC (T3.3 aile-sahibi). Adapter bu JSON'u TÜKETİR, üretmez.
// Şekil: {construct, opId, arrange, act, assert}. assert alanı contract-türevli — adapter
// hiçbir beklenen değeri (resultType/code) gömmez; hepsi spec'ten okunur (A3 değişmezi).
public sealed record Spec(
    [property: JsonPropertyName("construct")] string Construct,
    [property: JsonPropertyName("opId")] string OpId,
    [property: JsonPropertyName("arrange")] JsonElement Arrange,
    [property: JsonPropertyName("act")] SpecAct Act,
    [property: JsonPropertyName("assert")] SpecAssert Assert);

public sealed record SpecAct(
    [property: JsonPropertyName("call")] string Call,
    [property: JsonPropertyName("with")] JsonElement With);

// assert: beklenti SPEC'ten gelir (paketten DEĞİL). resultType = Result<T> alt-tip adı;
// code = NotProcessable kodu (throws). stub=true → saga-stub (v1, koşulmaz). source = provenance.
public sealed record SpecAssert(
    [property: JsonPropertyName("resultType")] string? ResultType,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("violated")] string? Violated,
    [property: JsonPropertyName("stub")] bool Stub,
    [property: JsonPropertyName("expected")] string? Expected,
    [property: JsonPropertyName("source")] string? Source,
    // invariant (property test): persist edilen entity alanı + predicate (op/bound). SPEC-türevli.
    [property: JsonPropertyName("field")] string? Field = null,
    [property: JsonPropertyName("op")] string? Op = null,
    [property: JsonPropertyName("bound")] decimal? Bound = null);

public static class SpecJson
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Spec Parse(string json) =>
        JsonSerializer.Deserialize<Spec>(json, Opts)
        ?? throw new InvalidOperationException("spec JSON null deserialize oldu");

    public static IReadOnlyList<Spec> ParseMany(IEnumerable<string> jsons) =>
        jsons.Select(Parse).ToList();
}
