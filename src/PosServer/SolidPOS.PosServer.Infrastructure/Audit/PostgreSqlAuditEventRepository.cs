using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Contracts.Audit;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Audit;

public sealed class PostgreSqlAuditEventRepository : IAuditEventRepository
{
    private readonly string _connectionString;

    public PostgreSqlAuditEventRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<AuditEventPageResponse> ListAsync(Guid tenantId, AuditEventFilters filters, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              id,
              tenant_id,
              actor_user_id,
              terminal_id,
              action,
              entity_type,
              entity_id,
              before_data::text,
              after_data::text,
              ip_address::text,
              user_agent,
              trace_id,
              occurred_at,
              COUNT(*) OVER() AS total_count
            FROM pos.audit_events
            WHERE tenant_id = @tenant_id
              AND (@action IS NULL OR action = @action)
              AND (@entity_type IS NULL OR entity_type = @entity_type)
              AND (@entity_id IS NULL OR entity_id = @entity_id)
              AND (@actor_user_id IS NULL OR actor_user_id = @actor_user_id)
              AND (@terminal_id IS NULL OR terminal_id = @terminal_id)
              AND (@from IS NULL OR occurred_at >= @from)
              AND (@to IS NULL OR occurred_at <= @to)
            ORDER BY occurred_at DESC, id DESC
            LIMIT @limit
            OFFSET @offset;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableText(command, "action", filters.Action);
        AddNullableText(command, "entity_type", filters.EntityType);
        AddNullableUuid(command, "entity_id", filters.EntityId);
        AddNullableUuid(command, "actor_user_id", filters.ActorUserId);
        AddNullableUuid(command, "terminal_id", filters.TerminalId);
        AddNullableTimestamp(command, "from", filters.From);
        AddNullableTimestamp(command, "to", filters.To);
        command.Parameters.AddWithValue("limit", filters.PageSize);
        command.Parameters.AddWithValue("offset", (filters.Page - 1) * filters.PageSize);

        List<AuditEventResponse> items = [];
        long total = 0;

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (total == 0)
            {
                total = reader.GetInt64(13);
            }

            items.Add(new AuditEventResponse(
                reader.GetGuid(0),
                reader.GetGuid(1),
                NullableGuid(reader, 2),
                NullableGuid(reader, 3),
                reader.GetString(4),
                reader.GetString(5),
                NullableGuid(reader, 6),
                NullableJson(reader, 7),
                NullableJson(reader, 8),
                NullableString(reader, 9),
                NullableString(reader, 10),
                NullableString(reader, 11),
                ReadDateTimeOffset(reader, 12)));
        }

        return new AuditEventPageResponse(items, new AuditEventPageMetaResponse(filters.Page, filters.PageSize, total));
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? NullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static JsonElement? NullableJson(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        string rawJson = reader.GetString(ordinal);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        try
        {
            return reader.GetFieldValue<DateTimeOffset>(ordinal);
        }
        catch (InvalidCastException)
        {
            DateTime value = reader.GetDateTime(ordinal);
            DateTime utcValue = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();

            return new DateTimeOffset(utcValue);
        }
    }
}
