using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Contracts.Sync;
using SolidPOS.PosServer.Contracts.Terminals;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Sync;

public sealed class PostgreSqlSyncConflictRepository : ISyncConflictRepository
{
    private readonly string _connectionString;

    public PostgreSqlSyncConflictRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<IReadOnlyCollection<SyncConflictResponse>> ListAsync(
        Guid tenantId,
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, terminal_id, entity_type, entity_id, local_event_id,
                   local_version, server_version, local_payload::text, server_payload::text,
                   resolution_strategy, resolved_payload::text, status, created_at, resolved_at,
                   resolved_by_user_id, resolution_note
            FROM pos.sync_conflicts
            WHERE tenant_id = @tenant_id
              AND (@status IS NULL OR status = @status)
            ORDER BY created_at DESC, id DESC
            LIMIT @limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("limit", limit);
        NpgsqlParameter statusParameter = command.Parameters.Add("status", NpgsqlDbType.Text);
        statusParameter.Value = string.IsNullOrWhiteSpace(status) ? (object)DBNull.Value : status;

        List<SyncConflictResponse> conflicts = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            conflicts.Add(ReadConflict(reader));
        }

        return conflicts;
    }

    public async Task<SyncConflictResponse?> ResolveAsync(
        Guid tenantId,
        Guid conflictId,
        Guid? resolvedByUserId,
        ResolveSyncConflictRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pos.sync_conflicts
            SET status = 'resolved',
                resolution_strategy = @resolution_strategy,
                resolved_payload = COALESCE(CAST(@resolved_payload AS jsonb),
                  CASE
                    WHEN @resolution_strategy = 'use_server' THEN server_payload
                    WHEN @resolution_strategy = 'use_client' THEN local_payload
                    ELSE server_payload
                  END),
                resolved_at = now(),
                resolved_by_user_id = @resolved_by_user_id,
                resolution_note = @resolution_note,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @conflict_id
              AND status = 'pending'
            RETURNING id, tenant_id, terminal_id, entity_type, entity_id, local_event_id,
                      local_version, server_version, local_payload::text, server_payload::text,
                      resolution_strategy, resolved_payload::text, status, created_at, resolved_at,
                      resolved_by_user_id, resolution_note;
            """;

        const string markInboxSql = """
            UPDATE pos.sync_inbox_events inbox
            SET status = 'processed',
                result = jsonb_build_object('resolvedConflictId', @conflict_id, 'resolutionStrategy', @resolution_strategy),
                error_code = NULL,
                error_message = NULL,
                processed_at = now()
            FROM pos.sync_conflicts conflict
            WHERE conflict.tenant_id = inbox.tenant_id
              AND inbox.tenant_id = @tenant_id
              AND conflict.id = @conflict_id
              AND inbox.conflict_id = conflict.id
              AND inbox.status = 'conflict';
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        SyncConflictResponse? response = null;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("conflict_id", conflictId);
            command.Parameters.AddWithValue("resolution_strategy", request.ResolutionStrategy.Trim());
            AddNullableUuid(command, "resolved_by_user_id", resolvedByUserId);
            command.Parameters.AddWithValue("resolution_note", string.IsNullOrWhiteSpace(request.ResolutionNote) ? (object)DBNull.Value : request.ResolutionNote.Trim());
            AddNullableJson(command, "resolved_payload", request.ResolvedPayload);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                response = ReadConflict(reader);
            }
        }

        if (response is not null)
        {
            await using var markCommand = new NpgsqlCommand(markInboxSql, connection, transaction);
            markCommand.Parameters.AddWithValue("tenant_id", tenantId);
            markCommand.Parameters.AddWithValue("conflict_id", conflictId);
            markCommand.Parameters.AddWithValue("resolution_strategy", request.ResolutionStrategy.Trim());
            await markCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<Guid> CreateAsync(
        Guid tenantId,
        Guid? terminalId,
        Guid? inboxEventId,
        Guid? localEventId,
        string entityType,
        Guid entityId,
        long? localVersion,
        long? serverVersion,
        JsonElement localPayload,
        JsonElement serverPayload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.sync_conflicts (
              tenant_id, terminal_id, entity_type, entity_id, local_event_id,
              local_version, server_version, local_payload, server_payload, status
            )
            VALUES (
              @tenant_id, @terminal_id, @entity_type, @entity_id, @local_event_id,
              @local_version, @server_version, CAST(@local_payload AS jsonb), CAST(@server_payload AS jsonb), 'pending'
            )
            RETURNING id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "terminal_id", terminalId);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        AddNullableUuid(command, "local_event_id", localEventId);
        AddNullableLong(command, "local_version", localVersion);
        AddNullableLong(command, "server_version", serverVersion);
        command.Parameters.AddWithValue("local_payload", NpgsqlDbType.Jsonb, localPayload.GetRawText());
        command.Parameters.AddWithValue("server_payload", NpgsqlDbType.Jsonb, serverPayload.GetRawText());

        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Sync conflict was not created."));
    }

    public async Task<SyncBootstrapResponse> ReadBootstrapAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        DateTimeOffset serverTime,
        string initialCursor,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT jsonb_build_object(
              'users', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                  'id', u.id,
                  'email', u.email::text,
                  'name', u.full_name,
                  'status', u.status,
                  'roles', COALESCE((
                    SELECT jsonb_agg(r.code ORDER BY r.code)
                    FROM pos.user_roles ur
                    JOIN pos.roles r ON r.tenant_id = ur.tenant_id AND r.id = ur.role_id
                    WHERE ur.tenant_id = u.tenant_id AND ur.user_id = u.id
                  ), '[]'::jsonb),
                  'permissions', COALESCE((
                    SELECT jsonb_agg(DISTINCT p.code ORDER BY p.code)
                    FROM pos.user_roles ur
                    JOIN pos.role_permissions rp ON rp.tenant_id = ur.tenant_id AND rp.role_id = ur.role_id
                    JOIN pos.permissions p ON p.code = rp.permission_code
                    WHERE ur.tenant_id = u.tenant_id AND ur.user_id = u.id
                  ), '[]'::jsonb)
                ) ORDER BY u.email)
                FROM pos.users u
                WHERE u.tenant_id = @tenant_id AND u.deleted_at IS NULL
              ), '[]'::jsonb)
            )::text AS access_snapshot,
            jsonb_build_object(
              'products', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                  'id', p.id,
                  'sku', p.sku,
                  'name', p.name,
                  'categoryId', p.category_id,
                  'productType', p.product_type,
                  'isSellable', p.is_sellable,
                  'isStockTracked', p.is_stock_tracked,
                  'taxMode', p.tax_mode,
                  'status', p.status
                ) ORDER BY p.name)
                FROM pos.products p
                WHERE p.tenant_id = @tenant_id AND p.status = 'active'
              ), '[]'::jsonb),
              'modifiers', COALESCE((
                SELECT jsonb_agg(to_jsonb(m) ORDER BY m.name)
                FROM pos.modifiers m
                WHERE m.tenant_id = @tenant_id AND m.deleted_at IS NULL
              ), '[]'::jsonb),
              'categories', COALESCE((
                SELECT jsonb_agg(to_jsonb(c) ORDER BY c.sort_order, c.name)
                FROM pos.categories c
                WHERE c.tenant_id = @tenant_id AND c.status = 'active'
              ), '[]'::jsonb)
            )::text AS catalog_snapshot,
            COALESCE((
              SELECT to_jsonb(p)
              FROM pos.inventory_policies p
              WHERE p.tenant_id = @tenant_id AND p.store_id = @store_id
              LIMIT 1
            ), '{}'::jsonb)::text AS inventory_policy,
            COALESCE((
              SELECT jsonb_agg(to_jsonb(t) ORDER BY t.product_id)
              FROM pos.inventory_low_stock_thresholds t
              WHERE t.tenant_id = @tenant_id AND t.store_id = @store_id
            ), '[]'::jsonb)::text AS low_stock_thresholds;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Bootstrap snapshot query returned no data.");
        }

        JsonElement syncSettings = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            maxEventsPerBatch = 500,
            maxAttempts = 3,
            deadLetterAfterAttempts = 3,
            stuckProcessingLeaseMinutes = 15,
            supportedConflictStrategies = new[] { "use_server", "use_client", "merge", "compensate" }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new SyncBootstrapResponse(
            tenantId,
            storeId,
            terminalId,
            serverTime,
            initialCursor,
            new TerminalRuntimeContextResponse(tenantId, storeId, terminalId),
            JsonDocument.Parse(reader.GetString(0)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(1)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(2)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(3)).RootElement.Clone(),
            syncSettings);
    }

    private static SyncConflictResponse ReadConflict(NpgsqlDataReader reader)
    {
        JsonElement? resolvedPayload = reader.IsDBNull(11)
            ? null
            : JsonDocument.Parse(reader.GetString(11)).RootElement.Clone();

        return new SyncConflictResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            JsonDocument.Parse(reader.GetString(8)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(9)).RootElement.Clone(),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            resolvedPayload,
            reader.GetString(12),
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            reader.IsDBNull(15) ? null : reader.GetGuid(15),
            reader.IsDBNull(16) ? null : reader.GetString(16));
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }

    private static void AddNullableLong(NpgsqlCommand command, string name, long? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Bigint);
        parameter.Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }

    private static void AddNullableJson(NpgsqlCommand command, string name, JsonElement? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Jsonb);
        parameter.Value = value.HasValue ? value.Value.GetRawText() : (object)DBNull.Value;
    }
}
