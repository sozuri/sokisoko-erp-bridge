using System.Text.Json;
using SokiSoko.SapBridge.Sync;
using Xunit;

namespace SokiSoko.SapBridge.Tests;

/// <summary>
/// The wire shape must match the server's ingestBatch struct exactly; a renamed field is
/// silently dropped by encoding/json rather than rejected.
/// </summary>
public class IngestContractTests
{
    private static JsonElement Serialize(IngestBatch b) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(b));

    [Fact]
    public void Batch_CarriesEveryFieldTheServerReads()
    {
        var json = Serialize(new IngestBatch());
        foreach (var field in new[] { "products", "stock", "prices", "customers", "codes", "cursors", "agent" })
            Assert.True(json.TryGetProperty(field, out _), $"missing '{field}'");
    }

    [Fact]
    public void AgentReport_UsesTheServersFieldNames()
    {
        var json = Serialize(new IngestBatch { Agent = new AgentReport { Version = "0.3.0", Error = "boom" } });
        var agent = json.GetProperty("agent");
        Assert.Equal("0.3.0", agent.GetProperty("version").GetString());
        Assert.Equal("boom", agent.GetProperty("error").GetString());
    }

    [Fact]
    public void Codes_UseTheServersExportedGoFieldNames()
    {
        // InboundCode has no json tags server-side, so encoding/json matches the Go field
        // names Kind/Code/Name verbatim - not camelCase.
        var json = Serialize(new IngestBatch
        {
            Codes = { new InboundCode { Kind = CodeKinds.Warehouse, Code = "101", Name = "First Floor Warehouse" } },
        });
        var code = json.GetProperty("codes")[0];
        Assert.Equal("warehouse", code.GetProperty("Kind").GetString());
        Assert.Equal("101", code.GetProperty("Code").GetString());
        Assert.Equal("First Floor Warehouse", code.GetProperty("Name").GetString());
    }

    [Fact]
    public void CodeKinds_MatchTheServerConstants()
    {
        Assert.Equal("warehouse", CodeKinds.Warehouse);
        Assert.Equal("item_group", CodeKinds.ItemGroup);
        Assert.Equal("manufacturer", CodeKinds.Manufacturer);
        Assert.Equal("supplier", CodeKinds.Supplier);
    }
}
