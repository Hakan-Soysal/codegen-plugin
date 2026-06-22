namespace Gen.Dotnet;

/// <summary>Manifest tip/ad → C# idiom eşlemesi. ponytail: bilinen skalerler map'lenir,
/// composite/enum/entity adları olduğu gibi geçer (üretilen tipe çözülür).</summary>
public static class Naming
{
    public static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Type(string type, bool collection)
    {
        var baseType = type switch
        {
            "ID" => "string",
            "String" => "string",
            "Decimal" => "decimal",
            "Int" => "int",
            "Bool" or "Boolean" => "bool",
            "DateTime" => "DateTime",
            _ => type   // Money / InvoiceStatus / Invoice → üretilen tip
        };
        return collection ? $"List<{baseType}>" : baseType;
    }

    public static string HttpVerb(string method) => method.ToUpperInvariant() switch
    {
        "POST" => "MapPost",
        "PUT" => "MapPut",
        "PATCH" => "MapPatch",
        "DELETE" => "MapDelete",
        _ => "MapGet"
    };

    public static bool BindsBody(string method) =>
        method.ToUpperInvariant() is "POST" or "PUT" or "PATCH";
}
