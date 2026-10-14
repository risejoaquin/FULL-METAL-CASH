using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SolidPOS.PosCore.Application.OfflineSales;

namespace SolidPOS.PosCore.Infrastructure.SQLite;

/// <summary>
/// A terminal-scoped, additive SQLite cache for centrally signed discount
/// rules. No production signing credentials or server network calls.
/// Every checkout independently re-verifies the signature after reading.
/// </summary>
public sealed class SQLiteSignedOfflineCartDiscountRuleCache : ISignedOfflineCartDiscountRuleCache
{
    private readonly SQLiteLocalDatabase _database;

    public SQLiteSignedOfflineCartDiscountRuleCache(SQLiteLocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
CREATE TABLE IF NOT EXISTS local_signed_cart_discount_rules (
 tenant_id TEXT NOT NULL,
 store_id TEXT NOT NULL,
 terminal_id TEXT NOT NULL,
 discount_id TEXT NOT NULL,
 revision INTEGER NOT NULL CHECK (revision > 0),
 issued_at_utc TEXT NOT NULL,
 valid_from_utc TEXT NOT NULL,
 expires_at_utc TEXT NOT NULL,
 kind TEXT NOT NULL,
 amount_bits_json TEXT NOT NULL,
 requires_approval INTEGER NOT NULL CHECK(requires_approval IN (0,1)),
 can_combine INTEGER NOT NULL CHECK(can_combine IN (0,1)),
 key_id TEXT NOT NULL,
 signature_base64 TEXT NOT NULL,
 PRIMARY KEY (tenant_id, store_id, terminal_id, discount_id)
);
CREATE INDEX IF NOT EXISTS idx_local_signed_cart_rules_expiry
 ON local_signed_cart_discount_rules(expires_at_utc);
""";
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task SaveAsync(SignedOfflineCartDiscountRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var binding = connection.CreateCommand())
        {
            binding.Transaction = transaction;
            binding.CommandText = """
SELECT COUNT(*) FROM terminal_binding WHERE id=1 AND tenant_id=$tenant
 AND store_id=$store AND terminal_id=$terminal;
""";
            binding.Parameters.AddWithValue("$tenant", rule.TenantId.ToString());
            binding.Parameters.AddWithValue("$store", rule.StoreId.ToString());
            binding.Parameters.AddWithValue("$terminal", rule.TerminalId.ToString());
            if (Convert.ToInt32(binding.ExecuteScalar()) != 1)
                throw new UnauthorizedAccessException("Signed discount cannot be cached for a foreign terminal.");
        }

        long? existingRevision = null;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
SELECT revision FROM local_signed_cart_discount_rules
WHERE tenant_id=$tenant AND store_id=$store AND terminal_id=$terminal AND discount_id=$discount;
""";
            read.Parameters.AddWithValue("$tenant", rule.TenantId.ToString());
            read.Parameters.AddWithValue("$store", rule.StoreId.ToString());
            read.Parameters.AddWithValue("$terminal", rule.TerminalId.ToString());
            read.Parameters.AddWithValue("$discount", rule.DiscountId.ToString());
            object? previous = read.ExecuteScalar();
            if (previous is not null) existingRevision = Convert.ToInt64(previous);
        }
        if (existingRevision.HasValue && rule.Revision <= existingRevision.Value)
            throw new InvalidOperationException("Cannot downgrade or replay an issued offline discount rule.");

        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
INSERT INTO local_signed_cart_discount_rules
 (tenant_id,store_id,terminal_id,discount_id,revision,issued_at_utc,
  valid_from_utc,expires_at_utc,kind,amount_bits_json,requires_approval,
  can_combine,key_id,signature_base64)
VALUES ($tenant,$store,$terminal,$discount,$revision,$issued,$from,$expires,
        $kind,$amount,$approval,$combine,$key,$signature)
ON CONFLICT(tenant_id,store_id,terminal_id,discount_id) DO UPDATE SET
 revision=excluded.revision,
 issued_at_utc=excluded.issued_at_utc,
 valid_from_utc=excluded.valid_from_utc,
 expires_at_utc=excluded.expires_at_utc,
 kind=excluded.kind,
 amount_bits_json=excluded.amount_bits_json,
 requires_approval=excluded.requires_approval,
 can_combine=excluded.can_combine,
 key_id=excluded.key_id,
 signature_base64=excluded.signature_base64;
""";
        upsert.Parameters.AddWithValue("$tenant", rule.TenantId.ToString());
        upsert.Parameters.AddWithValue("$store", rule.StoreId.ToString());
        upsert.Parameters.AddWithValue("$terminal", rule.TerminalId.ToString());
        upsert.Parameters.AddWithValue("$discount", rule.DiscountId.ToString());
        upsert.Parameters.AddWithValue("$revision", rule.Revision);
        upsert.Parameters.AddWithValue("$issued", rule.IssuedAtUtc.ToUniversalTime().ToString("O"));
        upsert.Parameters.AddWithValue("$from", rule.ValidFromUtc.ToUniversalTime().ToString("O"));
        upsert.Parameters.AddWithValue("$expires", rule.ExpiresAtUtc.ToUniversalTime().ToString("O"));
        upsert.Parameters.AddWithValue("$kind", rule.Kind);
        upsert.Parameters.AddWithValue("$amount", JsonSerializer.Serialize(decimal.GetBits(rule.Amount)));
        upsert.Parameters.AddWithValue("$approval", rule.RequiresApproval ? 1 : 0);
        upsert.Parameters.AddWithValue("$combine", rule.CanCombine ? 1 : 0);
        upsert.Parameters.AddWithValue("$key", rule.KeyId);
        upsert.Parameters.AddWithValue("$signature", rule.SignatureBase64);
        upsert.ExecuteNonQuery();
        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<SignedOfflineCartDiscountRule?> LoadAsync(
        Guid tenantId, Guid storeId, Guid terminalId, Guid discountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT revision,issued_at_utc,valid_from_utc,expires_at_utc,kind,
 amount_bits_json,requires_approval,can_combine,key_id,signature_base64
FROM local_signed_cart_discount_rules
WHERE tenant_id=$tenant AND store_id=$store AND terminal_id=$terminal AND discount_id=$discount;
""";
        command.Parameters.AddWithValue("$tenant", tenantId.ToString());
        command.Parameters.AddWithValue("$store", storeId.ToString());
        command.Parameters.AddWithValue("$terminal", terminalId.ToString());
        command.Parameters.AddWithValue("$discount", discountId.ToString());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<SignedOfflineCartDiscountRule?>(null);

        int[] bits = JsonSerializer.Deserialize<int[]>(reader.GetString(5))
            ?? throw new InvalidDataException("Missing signed discount cent representation.");
        if (bits.Length != 4)
            throw new InvalidDataException("Corrupt signed discount numeric representation.");

        SignedOfflineCartDiscountRule rule = new(
            tenantId, storeId, terminalId, discountId,
            reader.GetInt64(0),
            DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
            reader.GetString(4),
            new decimal(bits),
            reader.GetInt32(6) != 0,
            reader.GetInt32(7) != 0,
            reader.GetString(8),
            reader.GetString(9));
        return Task.FromResult<SignedOfflineCartDiscountRule?>(rule);
    }
}
