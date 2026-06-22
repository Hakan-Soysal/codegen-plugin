using System.IO;

namespace Gen.Tests;

/// <summary>Test fixture dosyalarına çıktı-dizini-göreli erişim.</summary>
public static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures");
    public static string At(string name) => Path.Combine(Dir, name);
    public static string Read(string name) => File.ReadAllText(At(name));
}
