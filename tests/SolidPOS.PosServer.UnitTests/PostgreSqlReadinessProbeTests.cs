using SolidPOS.PosServer.Infrastructure.PostgreSql;
using Xunit;

namespace SolidPOS.PosServer.UnitTests;

public sealed class PostgreSqlReadinessProbeTests
{
    [Fact]
    public async Task Missing_configuration_returns_unavailable_dependency_breakdown()
    {
        var probe = new PostgreSqlReadinessProbe(PostgreSqlConnectionStringResolution.Missing());

        PostgreSqlReadinessResult result = await probe.CheckAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal("unavailable", result.Status);
        Assert.Equal("not_configured", result.Database);
        Assert.Equal("unknown", result.SchemaCompatibility);
        Assert.Equal("unknown", result.SyncReadiness);
        Assert.NotNull(result.Dependencies);
        Assert.Contains(result.Dependencies!, dependency => dependency.Name == "database" && dependency.Status == "unavailable");
        Assert.Contains(result.Dependencies!, dependency => dependency.Name == "schema" && dependency.Status == "unavailable");
        Assert.Contains(result.Dependencies!, dependency => dependency.Name == "sync" && dependency.Status == "unavailable");
        Assert.Contains(result.Dependencies!, dependency => dependency.Name == "storage");
    }

    [Fact]
    public void Expected_schema_and_sync_contract_remain_version_four()
    {
        Assert.Equal(4, PostgreSqlReadinessProbe.ExpectedSchemaVersion);
        Assert.Equal("schema_version_4", PostgreSqlReadinessProbe.ExpectedSyncContract);
    }
}
