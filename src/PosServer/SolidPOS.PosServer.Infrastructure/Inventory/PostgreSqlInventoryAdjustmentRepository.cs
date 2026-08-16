using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Contracts.Inventory;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Inventory;

public sealed class PostgreSqlInventoryAdjustmentRepository : IInventoryAdjustmentRepository
{
    private readonly string _connectionString;

    public PostgreSqlInventoryAdjustmentRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<InventoryAdjustmentResponse?> CreateAsync(
        Guid tenantId,
        Guid storeId,
        Guid? terminalId,
        CreateInventoryAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        InventoryAdjustmentResponse? existing = await ReadAdjustmentByLocalIdAsync(connection, transaction, tenantId, request.LocalAdjustmentId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        if (!await RuntimeContextIsValidAsync(connection, transaction, tenantId, storeId, request.CreatedByUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Guid adjustmentId = Guid.NewGuid();
        foreach (CreateInventoryAdjustmentLineRequest line in request.Lines)
        {
            if (!await InventoryLineIsValidAsync(connection, transaction, tenantId, line, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await InsertLedgerLineAsync(
                connection,
                transaction,
                tenantId,
                storeId,
                terminalId,
                adjustmentId,
                request,
                line,
                cancellationToken);
        }

        InventoryAdjustmentResponse? created = await ReadAdjustmentByIdAsync(connection, transaction, tenantId, adjustmentId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    private static async Task<bool> RuntimeContextIsValidAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid createdByUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.tenants t
              JOIN pos.stores s
                ON s.tenant_id = t.id
               AND s.id = @store_id
               AND s.status = 'active'
               AND s.deleted_at IS NULL
              JOIN pos.users u
                ON u.tenant_id = t.id
               AND u.id = @created_by_user_id
               AND u.status = 'active'
               AND u.deleted_at IS NULL
              WHERE t.id = @tenant_id
                AND t.status = 'active'
                AND t.deleted_at IS NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<bool> InventoryLineIsValidAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CreateInventoryAdjustmentLineRequest line,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1
              FROM pos.products p
              JOIN pos.units u
                ON u.tenant_id = p.tenant_id
               AND u.id = @unit_id
              WHERE p.tenant_id = @tenant_id
                AND p.id = @product_id
                AND p.is_stock_tracked = true
                AND p.status = 'active'
                AND p.deleted_at IS NULL
                AND (
                  @variant_id IS NULL OR EXISTS (
                    SELECT 1
                    FROM pos.product_variants v
                    WHERE v.tenant_id = p.tenant_id
                      AND v.product_id = p.id
                      AND v.id = @variant_id
                      AND v.status = 'active'
                      AND v.deleted_at IS NULL
                  )
                )
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_id", line.ProductId);
        AddNullableGuid(command, "variant_id", line.VariantId);
        command.Parameters.AddWithValue("unit_id", line.UnitId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task InsertLedgerLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid storeId,
        Guid? terminalId,
        Guid adjustmentId,
        CreateInventoryAdjustmentRequest request,
        CreateInventoryAdjustmentLineRequest line,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.inventory_ledger (
              tenant_id, store_id, terminal_id, product_id, variant_id,
              movement_type, quantity_delta, unit_id, cost_cents,
              reference_type, reference_id, source_event_id, local_occurred_at, metadata
            )
            VALUES (
              @tenant_id, @store_id, @terminal_id, @product_id, @variant_id,
              @movement_type, @quantity_delta, @unit_id, @cost_cents,
              'inventory_adjustment', @reference_id, @source_event_id, @local_occurred_at, CAST(@metadata AS jsonb)
            );
            """;

        string movementType = request.AdjustmentType == "waste" ? "waste" : "adjustment";
        decimal quantityDelta = decimal.Parse(line.QuantityDelta, CultureInfo.InvariantCulture);

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        AddNullableGuid(command, "terminal_id", terminalId);
        command.Parameters.AddWithValue("product_id", line.ProductId);
        AddNullableGuid(command, "variant_id", line.VariantId);
        command.Parameters.AddWithValue("movement_type", movementType);
        command.Parameters.AddWithValue("quantity_delta", quantityDelta);
        command.Parameters.AddWithValue("unit_id", line.UnitId);
        command.Parameters.Add("cost_cents", NpgsqlDbType.Bigint).Value = line.CostCents.HasValue ? line.CostCents.Value : (object)DBNull.Value;
        command.Parameters.AddWithValue("reference_id", adjustmentId);
        command.Parameters.AddWithValue("source_event_id", request.LocalAdjustmentId);
        command.Parameters.AddWithValue("local_occurred_at", request.OccurredAt);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
        {
            localAdjustmentId = request.LocalAdjustmentId,
            adjustmentType = request.AdjustmentType,
            reason = request.Reason,
            createdByUserId = request.CreatedByUserId,
            effect = "manual_inventory_adjustment"
        }));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<InventoryAdjustmentResponse?> ReadAdjustmentByLocalIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid localAdjustmentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT reference_id
            FROM pos.inventory_ledger
            WHERE tenant_id = @tenant_id
              AND reference_type = 'inventory_adjustment'
              AND source_event_id = @source_event_id
            ORDER BY created_at
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("source_event_id", localAdjustmentId);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid adjustmentId
            ? await ReadAdjustmentByIdAsync(connection, transaction, tenantId, adjustmentId, cancellationToken)
            : null;
    }

    private static async Task<InventoryAdjustmentResponse?> ReadAdjustmentByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid adjustmentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              id,
              tenant_id,
              store_id,
              terminal_id,
              product_id,
              variant_id,
              movement_type,
              quantity_delta,
              unit_id,
              cost_cents,
              source_event_id,
              local_occurred_at,
              created_at,
              metadata->>'adjustmentType' AS adjustment_type,
              metadata->>'reason' AS reason,
              metadata->>'createdByUserId' AS created_by_user_id
            FROM pos.inventory_ledger
            WHERE tenant_id = @tenant_id
              AND reference_type = 'inventory_adjustment'
              AND reference_id = @reference_id
            ORDER BY created_at, id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("reference_id", adjustmentId);

        List<InventoryAdjustmentLineResponse> lines = [];
        AdjustmentHeader? header = null;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            header ??= new AdjustmentHeader(
                adjustmentId,
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetGuid(10),
                reader.GetString(13),
                reader.GetString(14),
                Guid.Parse(reader.GetString(15)),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetFieldValue<DateTimeOffset>(12));

            lines.Add(new InventoryAdjustmentLineResponse(
                reader.GetGuid(0),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6),
                reader.GetFieldValue<decimal>(7).ToString(CultureInfo.InvariantCulture),
                reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9)));
        }

        return header is null
            ? null
            : new InventoryAdjustmentResponse(
                header.Id,
                header.TenantId,
                header.StoreId,
                header.TerminalId,
                header.LocalAdjustmentId,
                header.AdjustmentType,
                header.Reason,
                header.CreatedByUserId,
                header.OccurredAt,
                header.CreatedAt,
                lines);
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }

    private sealed record AdjustmentHeader(
        Guid Id,
        Guid TenantId,
        Guid StoreId,
        Guid? TerminalId,
        Guid LocalAdjustmentId,
        string AdjustmentType,
        string Reason,
        Guid CreatedByUserId,
        DateTimeOffset OccurredAt,
        DateTimeOffset CreatedAt);
}
