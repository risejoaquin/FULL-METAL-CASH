using Npgsql;

namespace SolidPOS.PosServer.Infrastructure.PostgreSql;

internal static class PostgreSqlTenantSession
{
    public static async Task SetTenantAsync(
        NpgsqlConnection connection,
        Guid tenantId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant_id, false);", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
