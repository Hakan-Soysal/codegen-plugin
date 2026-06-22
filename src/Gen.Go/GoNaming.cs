namespace Gen.Go;

/// <summary>Manifest tip/ad → Go idiom. Exported adlar PascalCase (Go görünürlük kuralı).</summary>
public static class GoNaming
{
    public static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Type(string type, bool collection)
    {
        var baseType = type switch
        {
            "ID" => "string",
            "String" => "string",
            "Decimal" => "float64",   // ponytail: float64; decimal lib gerekirse swap
            "Int" => "int",
            "Bool" or "Boolean" => "bool",
            "DateTime" => "time.Time",
            _ => type
        };
        return collection ? $"[]{baseType}" : baseType;
    }

    public static bool UsesTime(string type) => type == "DateTime";
}
