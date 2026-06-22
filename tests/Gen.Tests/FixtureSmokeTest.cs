using System.Text.Json;

namespace Gen.Tests;

/// <summary>Task 1: fixture'lar mevcut + geçerli JSON + beklenen kökler var.
/// Aşağı akışın tüm temeli; bozuk/eksik fixture her şeyi kırar.</summary>
public class FixtureSmokeTest
{
    [Fact]
    public void Manifest_fixture_parses_with_expected_roots()
    {
        using var doc = JsonDocument.Parse(Fixtures.Read("manifest.json"));
        var root = doc.RootElement;
        Assert.Equal("linked", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("operations").GetArrayLength() >= 1);
        Assert.True(root.GetProperty("entities").GetArrayLength() >= 1);
    }

    [Fact]
    public void Operations_fixture_parses_with_expected_roots()
    {
        using var doc = JsonDocument.Parse(Fixtures.Read("operations.json"));
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("meta").GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("operations").GetArrayLength() >= 1);
    }
}
