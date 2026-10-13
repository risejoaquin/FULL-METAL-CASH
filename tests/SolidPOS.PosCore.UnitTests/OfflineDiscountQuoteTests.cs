using Microsoft.Data.Sqlite;
using System.Text.Json;
using SolidPOS.DiscountEngine;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Infrastructure.SQLite;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class OfflineDiscountQuoteTests
{
    private static OfflineSaleDraft Draft(Guid saleId) =>
        new(saleId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            [
                new OfflineSaleLineDraft(Guid.NewGuid(), null, "SKU-ONE", "One", 2, 101),
                new OfflineSaleLineDraft(Guid.NewGuid(), null, "SKU-TWO", "Two", 1, 103)
            ],
            [new OfflineSalePaymentDraft("cash", 305)], "MXN",
            CashierUserId: Guid.NewGuid());

    [Fact]
    public void Offline_and_server_shared_quote_conserve_identical_financial_totals()
    {
        var draft = Draft(Guid.NewGuid());
        var cart = new DiscountRule(Guid.NewGuid(), "cart", "percentage", 15);
        var offline = OfflineDiscountQuoteService.Preview(draft, [cart], []);
        var raw = new DiscountQuoteRequest(
            draft.TenantId, draft.StoreId, draft.CashierUserId!.Value,
            draft.Lines.Select((line, index) => new DiscountLine(
                OfflineDiscountQuoteService.GetStableLineId(draft.LocalSaleId, index),
                (long)line.Quantity * line.UnitPriceCents)).ToArray(),
            [cart], []);
        var online = DiscountCalculator.Quote(raw);
        Assert.Equal(online.GrossCents, offline.GrossCents);
        Assert.Equal(online.DiscountCents, offline.DiscountCents);
        Assert.Equal(online.NetCents, offline.NetCents);
        Assert.Equal(online.Allocations.ToArray(), offline.Allocations.ToArray());
        Assert.Equal(305, offline.GrossCents);
        Assert.Equal(46, offline.DiscountCents);
        Assert.Equal(259, offline.NetCents);
    }

    [Fact]
    public void Stable_line_keys_survive_multiple_offline_restarts()
    {
        Guid sale = Guid.NewGuid();
        var first = OfflineDiscountQuoteService.GetStableLineId(sale, 0);
        var repeated = OfflineDiscountQuoteService.GetStableLineId(sale, 0);
        Assert.Equal(first, repeated);
        Assert.NotEqual(first, OfflineDiscountQuoteService.GetStableLineId(sale, 1));
        Assert.NotEqual(first, OfflineDiscountQuoteService.GetStableLineId(Guid.NewGuid(), 0));
    }

    [Fact]
    public void Offline_preview_does_not_mutate_sale_payments_or_sale_event()
    {
        var sale = Draft(Guid.NewGuid());
        long previous = sale.TotalCents;
        var quote = OfflineDiscountQuoteService.Preview(sale,
            [new DiscountRule(Guid.NewGuid(), "cart", "fixed_amount", 10)], []);
        Assert.Equal(295, quote.NetCents);
        Assert.Equal(305, sale.PaidCents);
        Assert.Equal(previous, sale.TotalCents);
        Assert.All(sale.Lines, line => Assert.Equal(0, line.DiscountCents));
    }

    [Fact]
    public void Cart_checkout_materializes_in_original_sync_v4_line_fields_without_extra_metadata()
    {
        var draft = Draft(Guid.NewGuid());
        var rule = new DiscountRule(Guid.NewGuid(), "cart", "percentage", 15);
        var adjusted = OfflineDiscountQuoteService.ApplyCartDiscount(draft, rule,
            [new OfflineSalePaymentDraft("cash", 259)]);
        Assert.Equal(305, adjusted.SubtotalCents);
        Assert.Equal(46, adjusted.DiscountCents);
        Assert.Equal(259, adjusted.TotalCents);
        Assert.Equal(259, adjusted.PaidCents);
        Assert.All(adjusted.Lines, line => Assert.Equal(rule.Id, line.DiscountId));
        Assert.Equal(adjusted.DiscountCents, adjusted.Lines.Sum(x => x.DiscountCents));
        Assert.All(draft.Lines, line => Assert.Null(line.DiscountId));
        OfflineSaleCalculator.Validate(adjusted);
    }

    [Fact]
    public async Task Cart_checkout_persists_identical_discount_allocations_in_sqlite_and_sync_v4_json()
    {
        string dir = Path.Combine(Path.GetTempPath(), "solidpos-discount-replay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = new SQLiteLocalDatabase(Path.Combine(dir, "checkout.sqlite"));
        try
        {
            var repository = new SQLiteLocalPosRepository(db);
            await repository.InitializeAsync();
            var original = Draft(Guid.NewGuid());
            var rule = new DiscountRule(Guid.NewGuid(), "cart", "percentage", 15);
            var applied = OfflineDiscountQuoteService.ApplyCartDiscount(
                original, rule, [new OfflineSalePaymentDraft("cash", 259)]);
            var service = new OfflineSaleService(repository, new SystemClock());
            var evt = await service.CreateOfflineSaleAsync(applied);

            Assert.Equal(4, evt.SchemaVersion);
            Assert.Equal("sale.completed", evt.EventType);
            using var data = JsonDocument.Parse(evt.PayloadJson);
            var lines = data.RootElement.GetProperty("lines").EnumerateArray().ToArray();
            Assert.Equal(original.Lines.Count, lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                Assert.Equal(applied.Lines[i].DiscountCents,
                    lines[i].GetProperty("discountCents").GetInt32());
                Assert.Equal(rule.Id, lines[i].GetProperty("discountId").GetGuid());
            }

            using var connection = db.OpenConnection();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT subtotal_cents, discount_cents, total_cents, paid_cents FROM offline_sales;";
            using var reader = query.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(305L, reader.GetInt64(0));
            Assert.Equal(46L, reader.GetInt64(1));
            Assert.Equal(259L, reader.GetInt64(2));
            Assert.Equal(259L, reader.GetInt64(3));
            Assert.False(reader.Read());
            Assert.Single(await repository.GetPendingOutboxEventsAsync(10));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Cart_checkout_cannot_finalize_self_approved_or_mismatched_payments()
    {
        var draft = Draft(Guid.NewGuid());
        var discount = new DiscountRule(Guid.NewGuid(), "cart", "fixed_amount", 10);
        Assert.Throws<InvalidOperationException>(() =>
            OfflineDiscountQuoteService.ApplyCartDiscount(draft, discount,
                [new OfflineSalePaymentDraft("cash", 305)]));
        Assert.Throws<UnauthorizedAccessException>(() =>
            OfflineDiscountQuoteService.ApplyCartDiscount(draft,
                discount with { RequiresApproval = true },
                [new OfflineSalePaymentDraft("cash", 295)]));
    }

    [Fact]
    public void Existing_line_discount_and_cart_discount_cannot_be_silently_stacked()
    {
        var draft = Draft(Guid.NewGuid());
        var discounted = draft with
        {
            Lines = [draft.Lines[0] with { DiscountCents = 10, DiscountId = Guid.NewGuid() },
                draft.Lines[1]]
        };
        Assert.Throws<InvalidOperationException>(() =>
            OfflineDiscountQuoteService.ApplyCartDiscount(discounted,
                new DiscountRule(Guid.NewGuid(), "cart", "percentage", 10),
                [new OfflineSalePaymentDraft("cash", 265)]));
    }

    [Fact]
    public void Offline_preview_rejects_pre_discounted_or_unidentified_cashier()
    {
        var sale = Draft(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() =>
            OfflineDiscountQuoteService.Preview(sale with { CashierUserId = null }, [], []));
        var bad = sale with
        {
            Lines = [sale.Lines[0] with { DiscountCents = 99 }, sale.Lines[1]]
        };
        Assert.Throws<InvalidOperationException>(() => OfflineDiscountQuoteService.Preview(bad, [], []));
    }
}
