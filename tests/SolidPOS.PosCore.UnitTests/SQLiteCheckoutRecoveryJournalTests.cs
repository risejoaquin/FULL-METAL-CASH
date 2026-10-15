using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class SQLiteCheckoutRecoveryJournalTests
{
    [Fact]
    public void Restart_preserves_uncertain_checkout_and_blocks_new_sale_id()
    {
        string directory = Path.Combine(Path.GetTempPath(), "solidpos-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = new SQLiteLocalDatabase(Path.Combine(directory, "poscore.sqlite"));
            var journal = new SQLiteCheckoutRecoveryJournal(database);
            Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
            var first = Request(tenant, store, terminal, Guid.NewGuid(), 4500);
            journal.Register(first);

            var restarted = new SQLiteCheckoutRecoveryJournal(new SQLiteLocalDatabase(database.DatabasePath));
            Assert.Equal(first.Sale.LocalSaleId, restarted.FindUnresolved(tenant, store, terminal));
            Assert.False(restarted.ReconcileCommitted(first.Sale.LocalSaleId));
            Assert.Throws<InvalidOperationException>(() =>
                restarted.Register(Request(tenant, store, terminal, Guid.NewGuid(), 4500)));
            Assert.Equal(first.Sale.LocalSaleId, restarted.FindUnresolved(tenant, store, terminal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Same_sale_id_cannot_change_amount_on_replay()
    {
        string directory = Path.Combine(Path.GetTempPath(), "solidpos-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var journal = new SQLiteCheckoutRecoveryJournal(
                new SQLiteLocalDatabase(Path.Combine(directory, "poscore.sqlite")));
            Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid(), saleId = Guid.NewGuid();
            journal.Register(Request(tenant, store, terminal, saleId, 4500));
            Assert.Throws<InvalidOperationException>(() =>
                journal.Register(Request(tenant, store, terminal, saleId, 5500)));
            Assert.Equal(saleId, journal.FindUnresolved(tenant, store, terminal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Commit_marker_without_complete_financial_evidence_does_not_unlock_terminal()
    {
        string directory = Path.Combine(Path.GetTempPath(), "solidpos-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = new SQLiteLocalDatabase(Path.Combine(directory, "poscore.sqlite"));
            var journal = new SQLiteCheckoutRecoveryJournal(database);
            Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
            var request = Request(tenant, store, terminal, Guid.NewGuid(), 4500);
            journal.Register(request);
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
CREATE TABLE local_checkout_commits (local_sale_id TEXT PRIMARY KEY);
INSERT INTO local_checkout_commits (local_sale_id) VALUES ($sale);
""";
                command.Parameters.AddWithValue("$sale", request.Sale.LocalSaleId.ToString());
                command.ExecuteNonQuery();
            }
            var restarted = new SQLiteCheckoutRecoveryJournal(new SQLiteLocalDatabase(database.DatabasePath));
            Assert.False(restarted.ReconcileCommitted(request.Sale.LocalSaleId));
            Assert.Equal(request.Sale.LocalSaleId, restarted.FindUnresolved(tenant, store, terminal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Pending_intents_remain_visible_and_block_new_sale_after_restart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "solidpos-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = new SQLiteLocalDatabase(Path.Combine(directory, "poscore.sqlite"));
            var journal = new SQLiteCheckoutRecoveryJournal(database);
            Guid tenant = Guid.NewGuid(), store = Guid.NewGuid(), terminal = Guid.NewGuid();
            var first = Request(tenant, store, terminal, Guid.NewGuid(), 4500);
            var second = Request(tenant, store, terminal, Guid.NewGuid(), 5000);
            journal.Register(first);
            // Simulate an inconsistent legacy/runtime state with two pending intents:
            // normal Register already rejects this, but restart must fail closed.
            using (var connection = database.OpenConnection())
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
INSERT INTO local_checkout_recovery
(local_sale_id, tenant_id, store_id, terminal_id, request_json, state, created_at_utc)
VALUES ($sale,$tenant,$store,$terminal,$request,'pending',$created);
""";
                cmd.Parameters.AddWithValue("$sale", second.Sale.LocalSaleId.ToString());
                cmd.Parameters.AddWithValue("$tenant", tenant.ToString());
                cmd.Parameters.AddWithValue("$store", store.ToString());
                cmd.Parameters.AddWithValue("$terminal", terminal.ToString());
                cmd.Parameters.AddWithValue("$request", System.Text.Json.JsonSerializer.Serialize(second));
                cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
                cmd.ExecuteNonQuery();
            }
            var restarted = new SQLiteCheckoutRecoveryJournal(new SQLiteLocalDatabase(database.DatabasePath));
            Assert.Equal(first.Sale.LocalSaleId, restarted.FindUnresolved(tenant, store, terminal));
            Assert.False(restarted.ReconcileCommitted(first.Sale.LocalSaleId));
            Assert.Throws<InvalidOperationException>(() =>
                restarted.Register(Request(tenant, store, terminal, Guid.NewGuid(), 2000)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static AtomicCashCheckoutRequest Request(Guid tenant, Guid store, Guid terminal, Guid saleId, int amount)
    {
        var sale = new OfflineSaleDraft(saleId, tenant, store, terminal, DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(Guid.NewGuid(), null, "TEST", "Test", 1, amount) },
            new[] { new OfflineSalePaymentDraft("cash", amount, Guid.NewGuid()) }, "MXN", Guid.NewGuid());
        return new AtomicCashCheckoutRequest(sale, Guid.NewGuid(), Guid.NewGuid(), amount,
            Guid.NewGuid(), Guid.NewGuid());
    }
}
