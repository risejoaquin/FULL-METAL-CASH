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

    private static AtomicCashCheckoutRequest Request(Guid tenant, Guid store, Guid terminal, Guid saleId, int amount)
    {
        var sale = new OfflineSaleDraft(saleId, tenant, store, terminal, DateTimeOffset.UtcNow,
            new[] { new OfflineSaleLineDraft(Guid.NewGuid(), null, "TEST", "Test", 1, amount) },
            new[] { new OfflineSalePaymentDraft("cash", amount, Guid.NewGuid()) }, "MXN", Guid.NewGuid());
        return new AtomicCashCheckoutRequest(sale, Guid.NewGuid(), Guid.NewGuid(), amount,
            Guid.NewGuid(), Guid.NewGuid());
    }
}
