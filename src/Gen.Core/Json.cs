using System.Text.Encodings.Web;
using System.Text.Json;
using Gen.Core.Model;

namespace Gen.Core;

/// <summary>Merkezî JSON ayarları: camelCase + ExprNode polimorfik converter.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = Build();

    static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        o.Converters.Add(new ExprNodeConverter());
        return o;
    }

    public static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"null deserialize: {typeof(T).Name}");

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
