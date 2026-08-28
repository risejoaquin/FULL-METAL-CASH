using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class PostgreSqlSyncChangeWriter : ISyncChangeWriter
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlSyncChangeWriter> _logger;

    public PostgreSqlSyncChangeWriter(IConfiguration configuration, ILogger<PostgreSqlSyncChangeWriter> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        _logger = logger;
    }

    public async Task AppendAsync(
        Guid tenantId,
        Guid? storeId,
        string entityType,
        Guid entityId,
        string operation,
        long entityVersion,
        JsonElement payload,
        Guid? sourceTerminalId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.sync_changes (
              tenant_id,
              store_id,
              entity_type,
              entity_id,
              operation,
              entity_version,
              payload,
              source_terminal_id
            )
            VALUES (
              @tenant_id,
              @store_id,
              @entity_type,
              @entity_id,
              @operation,
              @entity_version,
              CAST(@payload AS jsonb),
              @source_terminal_id
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "store_id", storeId);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("operation", operation);
        command.Parameters.AddWithValue("entity_version", entityVersion);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payload.GetRawText());
        AddNullableUuid(command, "source_terminal_id", sourceTerminalId);

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "Sync change appended for tenant {TenantId} store {StoreId} sourceTerminal {SourceTerminalId}: {EntityType} {Operation} {EntityId} v{EntityVersion}",
            tenantId,
            storeId,
            sourceTerminalId,
            entityType,
            operation,
            entityId,
            entityVersion);
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
