using Gen.Core;
using Gen.Dotnet;

namespace Gen.Tests;

public class GenConfigTests
{
    [Fact]
    public void Parses_db_provider_from_json()
        => Assert.Equal("sqlite", Json.Parse<GenConfig>("{\"dbProvider\":\"sqlite\"}").DbProvider);

    [Fact]
    public void Missing_provider_is_null()
        => Assert.Null(Json.Parse<GenConfig>("{}").DbProvider);

    [Fact]
    public void Load_returns_null_when_file_absent()
        => Assert.Null(GenConfig.Load(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".json")));
}
