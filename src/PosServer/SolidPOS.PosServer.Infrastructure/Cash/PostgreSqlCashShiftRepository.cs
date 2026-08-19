using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Contracts.Cash;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Cash;

public sealed class PostgreSqlCashShiftRepository : ICashShiftRepository
{
    private const string UniqueViolation = "23505";
    private readonly string _connectionString;

    public PostgreSqlCashShiftRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<CashShiftResponse?> GetCurrentOpenShiftAsync(Guid tenantId, Guid storeId, Guid terminalId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, tenant_id, store_id, terminal_id, opened_by_user_id, closed_by_user_id,
                   status, opening_amount_cents, expected_cash_cents, counted_cash_cents,
                   difference_cents, opened_at, closed_at
            FROM pos.cash_shifts
            WHERE tenant_id = @tenant_id
              AND store_id = @store_id
              AND terminal_id = @terminal_id
              AND status = 'open'
            ORDER BY opened_at DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCashShift(reader) : null;
    }


    public async Task<CashShiftOperationalSummaryResponse?> GetOperationalSummaryAsync(Guid tenantId, Guid shiftId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
              cs.id,
              cs.tenant_id,
              cs.store_id,
              cs.terminal_id,
              cs.status,
              cs.opening_amount_cents,
              cs.expected_cash_cents,
              cs.counted_cash_cents,
              cs.difference_cents,
              COALESCE(SUM(p.amount_cents) FILTER (WHERE p.status = 'approved' AND pm.method_type = 'cash' AND s.status <> 'voided'), 0)::bigint AS cash_sales_cents,
              COALESCE(SUM(p.amount_cents) FILTER (WHERE p.status = 'approved' AND pm.method_type <> 'cash' AND s.status <> 'voided'), 0)::bigint AS non_cash_sales_cents,
              COALESCE((
                SELECT SUM(rr.amount_cents)
                FROM pos.returns r
                JOIN pos.return_refunds rr ON rr.tenant_id = r.tenant_id AND rr.return_id = r.id
                WHERE r.tenant_id = cs.tenant_id
                  AND r.cash_shift_id = cs.id
                  AND r.status = 'completed'
                  AND rr.status = 'approved'
                  AND rr.method_type = 'cash'
              ), 0)::bigint AS cash_refunds_cents,
              COALESCE((
                SELECT SUM(rr.amount_cents)
                FROM pos.returns r
                JOIN pos.return_refunds rr ON rr.tenant_id = r.tenant_id AND rr.return_id = r.id
                WHERE r.tenant_id = cs.tenant_id
                  AND r.cash_shift_id = cs.id
                  AND r.status = 'completed'
                  AND rr.status = 'approved'
                  AND rr.method_type <> 'cash'
              ), 0)::bigint AS non_cash_refunds_cents,
              COALESCE((
                SELECT SUM(cm.amount_cents)
                FROM pos.cash_movements cm
                WHERE cm.tenant_id = cs.tenant_id
                  AND cm.cash_shift_id = cs.id
                  AND cm.movement_type = 'cash_in'
              ), 0)::bigint AS cash_in_cents,
              COALESCE((
                SELECT SUM(cm.amount_cents)
                FROM pos.cash_movements cm
                WHERE cm.tenant_id = cs.tenant_id
                  AND cm.cash_shift_id = cs.id
                  AND cm.movement_type = 'cash_out'
              ), 0)::bigint AS cash_out_cents,
              COALESCE((
                SELECT COUNT(*)
                FROM pos.cash_movements cm
                WHERE cm.tenant_id = cs.tenant_id
                  AND cm.cash_shift_id = cs.id
                  AND cm.movement_type = 'drawer_open_no_sale'
              ), 0)::integer AS no_sale_drawer_open_count,
              COUNT(DISTINCT s.id) FILTER (WHERE s.status <> 'voided')::integer AS sales_count,
              COALESCE((
                SELECT COUNT(*)
                FROM pos.returns r
                WHERE r.tenant_id = cs.tenant_id
                  AND r.cash_shift_id = cs.id
                  AND r.status = 'completed'
              ), 0)::integer AS returns_count,
              COALESCE((
                SELECT COUNT(*)
                FROM pos.cash_movements cm
                WHERE cm.tenant_id = cs.tenant_id
                  AND cm.cash_shift_id = cs.id
              ), 0)::integer AS movement_count,
              cs.opened_at,
              cs.closed_at
            FROM pos.cash_shifts cs
            LEFT JOIN pos.sales s ON s.tenant_id = cs.tenant_id AND s.cash_shift_id = cs.id
            LEFT JOIN pos.payments p ON p.tenant_id = s.tenant_id AND p.sale_id = s.id
            LEFT JOIN pos.payment_methods pm ON pm.tenant_id = p.tenant_id AND pm.id = p.payment_method_id
            WHERE cs.tenant_id = @tenant_id
              AND cs.id = @shift_id
            GROUP BY cs.id, cs.tenant_id, cs.store_id, cs.terminal_id, cs.status,
                     cs.opening_amount_cents, cs.expected_cash_cents, cs.counted_cash_cents,
                     cs.difference_cents, cs.opened_at, cs.closed_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("shift_id", shiftId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOperationalSummary(reader) : null;
    }

    public async Task<CashShiftResponse?> OpenAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid openedByUserId,
        long openingAmountCents,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pos.cash_shifts (
              tenant_id,
              store_id,
              terminal_id,
              opened_by_user_id,
              status,
              opening_amount_cents,
              expected_cash_cents
            )
            SELECT @tenant_id, @store_id, @terminal_id, @opened_by_user_id, 'open', @opening_amount_cents, @opening_amount_cents
            WHERE EXISTS (
              SELECT 1 FROM pos.terminals
              WHERE tenant_id = @tenant_id
                AND id = @terminal_id
                AND store_id = @store_id
                AND status = 'active'
                AND hard_locked_at IS NULL
                AND deleted_at IS NULL
            )
            AND EXISTS (
              SELECT 1 FROM pos.users
              WHERE tenant_id = @tenant_id
                AND id = @opened_by_user_id
                AND status = 'active'
                AND deleted_at IS NULL
            )
            RETURNING id, tenant_id, store_id, terminal_id, opened_by_user_id, closed_by_user_id,
                      status, opening_amount_cents, expected_cash_cents, counted_cash_cents,
                      difference_cents, opened_at, closed_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("store_id", storeId);
        command.Parameters.AddWithValue("terminal_id", terminalId);
        command.Parameters.AddWithValue("opened_by_user_id", openedByUserId);
        command.Parameters.AddWithValue("opening_amount_cents", openingAmountCents);

        try
        {
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadCashShift(reader) : null;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return null;
        }
    }

    public async Task<CashMovementResponse?> CreateMovementAsync(
        Guid tenantId,
        Guid shiftId,
        string movementType,
        long amountCents,
        string reason,
        Guid createdByUserId,
        Guid? authorizedByUserId,
        CancellationToken cancellationToken)
    {
        const string shiftSql = """
            SELECT expected_cash_cents
            FROM pos.cash_shifts
            WHERE tenant_id = @tenant_id
              AND id = @shift_id
              AND status = 'open'
            FOR UPDATE;
            """;

        const string usersSql = """
            SELECT
              EXISTS (
                SELECT 1 FROM pos.users
                WHERE tenant_id = @tenant_id
                  AND id = @created_by_user_id
                  AND status = 'active'
                  AND deleted_at IS NULL
              ),
              @authorized_by_user_id IS NULL OR EXISTS (
                SELECT 1 FROM pos.users
                WHERE tenant_id = @tenant_id
                  AND id = @authorized_by_user_id
                  AND status = 'active'
                  AND deleted_at IS NULL
              );
            """;

        const string insertSql = """
            INSERT INTO pos.cash_movements (
              tenant_id,
              cash_shift_id,
              movement_type,
              amount_cents,
              reason,
              authorized_by_user_id,
              created_by_user_id
            )
            VALUES (
              @tenant_id,
              @shift_id,
              @movement_type,
              @amount_cents,
              @reason,
              @authorized_by_user_id,
              @created_by_user_id
            )
            RETURNING id, tenant_id, cash_shift_id, movement_type, amount_cents,
                      reason, authorized_by_user_id, created_by_user_id, created_at;
            """;

        const string updateShiftSql = """
            UPDATE pos.cash_shifts
            SET expected_cash_cents = @expected_cash_cents,
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @shift_id
              AND status = 'open';
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        long expectedCashCents;
        await using (var shiftCommand = new NpgsqlCommand(shiftSql, connection, transaction))
        {
            shiftCommand.Parameters.AddWithValue("tenant_id", tenantId);
            shiftCommand.Parameters.AddWithValue("shift_id", shiftId);
            object? result = await shiftCommand.ExecuteScalarAsync(cancellationToken);
            if (result is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            expectedCashCents = (long)result;
        }

        if (!await UsersAreValidAsync(connection, transaction, usersSql, tenantId, createdByUserId, authorizedByUserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        long updatedExpectedCashCents = movementType switch
        {
            "cash_in" => expectedCashCents + amountCents,
            "cash_out" => expectedCashCents - amountCents,
            _ => expectedCashCents
        };

        CashMovementResponse movement;
        await using (var insertCommand = new NpgsqlCommand(insertSql, connection, transaction))
        {
            AddMovementParameters(insertCommand, tenantId, shiftId, movementType, amountCents, reason, createdByUserId, authorizedByUserId);
            await using NpgsqlDataReader reader = await insertCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            movement = ReadCashMovement(reader);
        }

        await using (var updateCommand = new NpgsqlCommand(updateShiftSql, connection, transaction))
        {
            updateCommand.Parameters.AddWithValue("tenant_id", tenantId);
            updateCommand.Parameters.AddWithValue("shift_id", shiftId);
            updateCommand.Parameters.AddWithValue("expected_cash_cents", updatedExpectedCashCents);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return movement;
    }

    public async Task<CashShiftResponse?> CloseAsync(
        Guid tenantId,
        Guid shiftId,
        Guid closedByUserId,
        long countedCashCents,
        CancellationToken cancellationToken)
    {
        const string shiftSql = """
            SELECT expected_cash_cents
            FROM pos.cash_shifts
            WHERE tenant_id = @tenant_id
              AND id = @shift_id
              AND status = 'open'
            FOR UPDATE;
            """;

        const string userSql = """
            SELECT EXISTS (
              SELECT 1 FROM pos.users
              WHERE tenant_id = @tenant_id
                AND id = @closed_by_user_id
                AND status = 'active'
                AND deleted_at IS NULL
            );
            """;

        const string closeSql = """
            UPDATE pos.cash_shifts
            SET status = 'closed',
                closed_by_user_id = @closed_by_user_id,
                counted_cash_cents = @counted_cash_cents,
                difference_cents = @difference_cents,
                closed_at = now(),
                updated_at = now()
            WHERE tenant_id = @tenant_id
              AND id = @shift_id
              AND status = 'open'
            RETURNING id, tenant_id, store_id, terminal_id, opened_by_user_id, closed_by_user_id,
                      status, opening_amount_cents, expected_cash_cents, counted_cash_cents,
                      difference_cents, opened_at, closed_at;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        long expectedCashCents;
        await using (var shiftCommand = new NpgsqlCommand(shiftSql, connection, transaction))
        {
            shiftCommand.Parameters.AddWithValue("tenant_id", tenantId);
            shiftCommand.Parameters.AddWithValue("shift_id", shiftId);
            object? result = await shiftCommand.ExecuteScalarAsync(cancellationToken);
            if (result is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            expectedCashCents = (long)result;
        }

        await using (var userCommand = new NpgsqlCommand(userSql, connection, transaction))
        {
            userCommand.Parameters.AddWithValue("tenant_id", tenantId);
            userCommand.Parameters.AddWithValue("closed_by_user_id", closedByUserId);
            object? userResult = await userCommand.ExecuteScalarAsync(cancellationToken);
            if (userResult is not true)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        CashShiftResponse shift;
        await using (var closeCommand = new NpgsqlCommand(closeSql, connection, transaction))
        {
            closeCommand.Parameters.AddWithValue("tenant_id", tenantId);
            closeCommand.Parameters.AddWithValue("shift_id", shiftId);
            closeCommand.Parameters.AddWithValue("closed_by_user_id", closedByUserId);
            closeCommand.Parameters.AddWithValue("counted_cash_cents", countedCashCents);
            closeCommand.Parameters.AddWithValue("difference_cents", countedCashCents - expectedCashCents);

            await using NpgsqlDataReader reader = await closeCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            shift = ReadCashShift(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return shift;
    }

    private static async Task<bool> UsersAreValidAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid tenantId,
        Guid createdByUserId,
        Guid? authorizedByUserId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);
        AddNullableGuid(command, "authorized_by_user_id", authorizedByUserId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && reader.GetBoolean(0) && reader.GetBoolean(1);
    }

    private static void AddMovementParameters(
        NpgsqlCommand command,
        Guid tenantId,
        Guid shiftId,
        string movementType,
        long amountCents,
        string reason,
        Guid createdByUserId,
        Guid? authorizedByUserId)
    {
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("shift_id", shiftId);
        command.Parameters.AddWithValue("movement_type", movementType);
        command.Parameters.AddWithValue("amount_cents", amountCents);
        command.Parameters.AddWithValue("reason", reason);
        AddNullableGuid(command, "authorized_by_user_id", authorizedByUserId);
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }


    private static CashShiftOperationalSummaryResponse ReadOperationalSummary(NpgsqlDataReader reader)
    {
        return new CashShiftOperationalSummaryResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetInt64(11),
            reader.GetInt64(12),
            reader.GetInt64(13),
            reader.GetInt64(14),
            reader.GetInt32(15),
            reader.GetInt32(16),
            reader.GetInt32(17),
            reader.GetInt32(18),
            reader.GetFieldValue<DateTimeOffset>(19),
            reader.IsDBNull(20) ? null : reader.GetFieldValue<DateTimeOffset>(20));
    }

    private static CashShiftResponse ReadCashShift(NpgsqlDataReader reader)
    {
        return new CashShiftResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
    }

    private static CashMovementResponse ReadCashMovement(NpgsqlDataReader reader)
    {
        return new CashMovementResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.GetGuid(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }
}
