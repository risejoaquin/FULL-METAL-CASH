using Microsoft.Extensions.Configuration;
using Npgsql;
using SolidPOS.PosServer.Infrastructure.PostgreSql;
using Xunit;

namespace SolidPOS.PosServer.UnitTests;

public sealed class PostgreSqlConnectionHardeningTests
{
    [Fact]
    public void Resolver_applies_bounded_pool_and_timeout_policy()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=solidpos;Username=solidpos;Password=test",
                ["PostgreSql:Pool:MaxSize"] = "24",
                ["PostgreSql:Timeouts:ConnectSeconds"] = "8",
                ["PostgreSql:Timeouts:CommandSeconds"] = "12"
            })
            .Build();

        PostgreSqlConnectionStringResolution result = PostgreSqlConnectionStringResolver.Resolve(configuration);

        Assert.True(result.IsValid);
        var builder = new NpgsqlConnectionStringBuilder(result.ConnectionString);
        Assert.True(builder.Pooling);
        Assert.Equal(24, builder.MaxPoolSize);
        Assert.Equal(8, builder.Timeout);
        Assert.Equal(12, builder.CommandTimeout);
        Assert.False(builder.NoResetOnClose);
        Assert.True(builder.ConnectionIdleLifetime >= 30);
    }

    [Fact]
    public void Resolver_clamps_pool_and_timeout_configuration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=solidpos;Username=solidpos;Password=test",
                ["PostgreSql:Pool:MinSize"] = "500",
                ["PostgreSql:Pool:MaxSize"] = "500",
                ["PostgreSql:Timeouts:CommandSeconds"] = "900"
            })
            .Build();

        PostgreSqlConnectionStringResolution result = PostgreSqlConnectionStringResolver.Resolve(configuration);
        var builder = new NpgsqlConnectionStringBuilder(result.ConnectionString);

        Assert.Equal(200, builder.MaxPoolSize);
        Assert.Equal(200, builder.MinPoolSize);
        Assert.Equal(120, builder.CommandTimeout);
    }
}
