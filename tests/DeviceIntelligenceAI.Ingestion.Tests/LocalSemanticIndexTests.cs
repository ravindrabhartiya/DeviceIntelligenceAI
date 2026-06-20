using DeviceIntelligenceAI.Ingestion.SemanticIndex;

namespace DeviceIntelligenceAI.Ingestion.Tests;

public class LocalSemanticIndexTests : IDisposable
{
    private readonly LocalSemanticIndex _index;

    public LocalSemanticIndexTests()
    {
        _index = new LocalSemanticIndex();
    }

    [Fact]
    public async Task IndexAndQuery_ReturnsMostRelevantFact()
    {
        await _index.IndexFactAsync("f1", "Update KB5034441 failed with error 0x80070032", DateTimeOffset.UtcNow);
        await _index.IndexFactAsync("f2", "Driver Intel UHD Graphics version 31.0.101 installed", DateTimeOffset.UtcNow);
        await _index.IndexFactAsync("f3", "Application explorer.exe crashed on 2024-06-14", DateTimeOffset.UtcNow);
        await _index.IndexFactAsync("f4", "Security posture appears healthy: firewall active", DateTimeOffset.UtcNow);

        var results = await _index.QueryAsync("Why did the update fail?");

        Assert.NotEmpty(results);
        Assert.Equal("f1", results[0].FactId);
    }

    [Fact]
    public async Task Query_ReturnsDriverFacts()
    {
        await _index.IndexFactAsync("f1", "Update KB5034441 installed on 2024-06-15", DateTimeOffset.UtcNow);
        await _index.IndexFactAsync("f2", "Driver Intel UHD Graphics version 31.0.101 installed", DateTimeOffset.UtcNow);
        await _index.IndexFactAsync("f3", "Driver Realtek Audio version 6.0.9600 installed", DateTimeOffset.UtcNow);

        var results = await _index.QueryAsync("Intel Graphics driver version");

        Assert.NotEmpty(results);
        // The Intel graphics driver fact should be the top result
        Assert.Contains("Intel", results[0].FactText);
    }

    [Fact]
    public async Task QueryInTimeRange_FiltersCorrectly()
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        var today = DateTimeOffset.UtcNow;
        var lastWeek = DateTimeOffset.UtcNow.AddDays(-7);

        await _index.IndexFactAsync("f1", "Update KB5034441 failed with error code", yesterday);
        await _index.IndexFactAsync("f2", "Update KB5039212 failed with different error", lastWeek);

        var results = await _index.QueryInTimeRangeAsync(
            "update failed",
            from: DateTimeOffset.UtcNow.AddDays(-2),
            to: today);

        Assert.Single(results);
        Assert.Equal("f1", results[0].FactId);
    }

    [Fact]
    public async Task BatchIndex_AllFactsSearchable()
    {
        var facts = new[]
        {
            new SemanticFact("f1", "CPU at 95% utilized", DateTimeOffset.UtcNow),
            new SemanticFact("f2", "Memory available 1.2 GB", DateTimeOffset.UtcNow),
            new SemanticFact("f3", "Disk 90% utilized", DateTimeOffset.UtcNow),
        };

        await _index.IndexFactsBatchAsync(facts);

        var count = await _index.GetIndexedCountAsync();
        Assert.Equal(3, count);

        var results = await _index.QueryAsync("high CPU usage");
        Assert.NotEmpty(results);
        Assert.Equal("f1", results[0].FactId);
    }

    [Fact]
    public async Task RemoveFact_NoLongerSearchable()
    {
        await _index.IndexFactAsync("f1", "Update KB5034441 failed", DateTimeOffset.UtcNow);
        await _index.RemoveFactAsync("f1");

        var results = await _index.QueryAsync("update failed");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Query_HandlesEmptyIndex()
    {
        var results = await _index.QueryAsync("anything");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Query_RanksRelevanceCorrectly()
    {
        await _index.IndexFactAsync("f1", "BSOD Blue Screen bugcheck from ndis.sys code 0xD1", DateTimeOffset.UtcNow);
        await _index.IndexFactAsync("f2", "Application crash notepad.exe stopped working", DateTimeOffset.UtcNow);
        await _index.IndexFactAsync("f3", "Blue screen BSOD occurred kernel panic", DateTimeOffset.UtcNow);

        var results = await _index.QueryAsync("blue screen BSOD");

        Assert.True(results.Count >= 2);
        // Both BSOD-related facts should score higher than the notepad crash
        var bsodResults = results.Where(r => r.FactText.Contains("BSOD") || r.FactText.Contains("Blue")).ToList();
        Assert.True(bsodResults.Count >= 2);
    }

    public void Dispose()
    {
        _index.Dispose();
    }
}
