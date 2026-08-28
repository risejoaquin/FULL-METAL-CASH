using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using SolidPOS.PosServer.Application.Receipts;
using SolidPOS.PosServer.Infrastructure.PostgreSql;

namespace SolidPOS.PosServer.Infrastructure.Receipts;

public sealed class PostgreSqlDigitalReceiptRepository : IDigitalReceiptRepository
{
    private readonly string _connectionString;

    public PostgreSqlDigitalReceiptRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    }

    public async Task<DigitalReceiptRecord?> IssueAsync(
        Guid tenantId,
        Guid saleId,
        string receiptNumber,
        string publicTokenHash,
        string publicUrl,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        const string sql = """
            INSERT INTO pos.digital_receipts (
              tenant_id, sale_id, receipt_number, public_token_hash, public_url,
              status, expires_at, issued_at, metadata
            )
            SELECT
              @tenant_id, @sale_id, @receipt_number, @public_token_hash, @public_url,
              'active', @expires_at, now(), jsonb_build_object('channel', 'api')
            WHERE EXISTS (
              SELECT 1
              FROM pos.sales s
              WHERE s.tenant_id = @tenant_id
                AND s.id = @sale_id
                AND s.deleted_at IS NULL
            )
            ON CONFLICT (tenant_id, sale_id) DO UPDATE
            SET status = CASE
                    WHEN pos.digital_receipts.status IN ('revoked', 'expired') THEN 'active'
                    ELSE pos.digital_receipts.status
                END,
                expires_at = COALESCE(EXCLUDED.expires_at, pos.digital_receipts.expires_at),
                issued_at = COALESCE(pos.digital_receipts.issued_at, now()),
                revoked_at = CASE
                    WHEN pos.digital_receipts.status IN ('revoked', 'expired') THEN NULL
                    ELSE pos.digital_receipts.revoked_at
                END
            RETURNING id, tenant_id, sale_id, receipt_number, public_url, status,
                      expires_at, issued_at, created_at, revoked_at, last_sent_at,
                      last_sent_email, send_count;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("receipt_number", receiptNumber);
        command.Parameters.AddWithValue("public_token_hash", publicTokenHash);
        command.Parameters.AddWithValue("public_url", publicUrl);
        AddNullableTimestamp(command, "expires_at", expiresAt);

        DigitalReceiptRecord? record = null;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                record = MapRecord(reader);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<DigitalReceiptRecord?> GetBySaleIdAsync(Guid tenantId, Guid saleId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        const string sql = """
            SELECT id, tenant_id, sale_id, receipt_number, public_url, status,
                   expires_at, issued_at, created_at, revoked_at, last_sent_at,
                   last_sent_email, send_count
            FROM pos.digital_receipts
            WHERE tenant_id = @tenant_id
              AND sale_id = @sale_id;
            """;

        DigitalReceiptRecord? record = await ReadSingleAsync(connection, transaction, sql, command =>
        {
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("sale_id", saleId);
        }, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<DigitalReceiptRecord?> GetByPublicTokenHashAsync(string publicTokenHash, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, tenant_id, sale_id, receipt_number, public_url, status,
                   expires_at, issued_at, created_at, revoked_at, last_sent_at,
                   last_sent_email, send_count
            FROM pos.digital_receipts
            WHERE public_token_hash = @public_token_hash
              AND status = 'active'
              AND (expires_at IS NULL OR expires_at > now())
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("public_token_hash", publicTokenHash);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRecord(reader) : null;
    }

    public async Task<DigitalReceiptRecord?> MarkEmailStubSentAsync(
        Guid tenantId,
        Guid saleId,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgreSqlTenantSession.SetTenantAsync(connection, tenantId, cancellationToken, transaction);

        const string sql = """
            UPDATE pos.digital_receipts
            SET last_sent_at = now(),
                last_sent_email = @recipient_email,
                send_count = send_count + 1,
                metadata = metadata || jsonb_build_object(
                  'lastEmailStubStatus', 'queued',
                  'lastEmailStubRecipient', @recipient_email,
                  'lastEmailStubQueuedAt', CAST(now() AS text)
                )
            WHERE tenant_id = @tenant_id
              AND sale_id = @sale_id
              AND status = 'active'
            RETURNING id, tenant_id, sale_id, receipt_number, public_url, status,
                      expires_at, issued_at, created_at, revoked_at, last_sent_at,
                      last_sent_email, send_count;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("recipient_email", recipientEmail);

        DigitalReceiptRecord? record = null;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                record = MapRecord(reader);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    private static async Task<DigitalReceiptRecord?> ReadSingleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Action<NpgsqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        configure(command);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapRecord(reader) : null;
    }

    private static DigitalReceiptRecord MapRecord(NpgsqlDataReader reader)
    {
        return new DigitalReceiptRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetInt32(12));
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        command.Parameters.Add(name, NpgsqlDbType.TimestampTz).Value = value.HasValue ? value.Value : (object)DBNull.Value;
    }
}
