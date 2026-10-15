using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SolidPOS.DiscountEngine;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.SQLite;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

/// <summary>
/// Development-only signed fixtures use throwaway P-256 test keys.
/// No private signing key is included in production artifacts.
/// </summary>
public sealed class SignedOfflineCartDiscountTests
{
    [Fact]
    public async Task Signed_rule_survives_restart_and_checkout_preserves_v4_sale_event()
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var published = fixture.Sign(fixture.Rule(1, amount: 15.0m));
        await fixture.Service().PublishAsync(published);

        var restartedCache = new SQLiteSignedOfflineCartDiscountRuleCache(
            new SQLiteLocalDatabase(fixture.Path));
        var restarted = new SignedOfflineCartDiscountCheckout(
            restartedCache, fixture.Verifier(), new SystemClock());
        var signed = await restartedCache.LoadAsync(
            fixture.TenantId, fixture.StoreId, fixture.TerminalId, published.DiscountId);
        Assert.NotNull(signed);
        Assert.Equal(decimal.GetBits(15.0m), decimal.GetBits(signed.Amount));

        var adjusted = await restarted.ApplyAsync(
            fixture.Sale(), published.DiscountId, [new OfflineSalePaymentDraft("cash", 259)]);
        Assert.Equal(46, adjusted.DiscountCents);
        Assert.Equal(259, adjusted.TotalCents);
        Assert.All(adjusted.Lines, line => Assert.Equal(published.DiscountId, line.DiscountId));

        var outbox = await new OfflineSaleService(fixture.Repository, new SystemClock())
            .CreateOfflineSaleAsync(adjusted);
        Assert.Equal(4, outbox.SchemaVersion);
        using var payload = JsonDocument.Parse(outbox.PayloadJson);
        foreach (var line in payload.RootElement.GetProperty("lines").EnumerateArray())
            Assert.Equal(published.DiscountId, line.GetProperty("discountId").GetGuid());
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM local_signed_cart_discount_rules;"));
    }

    [Fact]
    public async Task Invalid_signature_or_mutated_money_is_rejected_before_any_cache_write()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var signed = fx.Sign(fx.Rule(1));
        var invalid = new[]
        {
            signed with { Amount = 80m },
            signed with { Kind = "fixed_amount" },
            signed with { StoreId = Guid.NewGuid() },
            signed with { SignatureBase64 = Convert.ToBase64String(new byte[64]) },
            signed with { KeyId = "untrusted-key" }
        };
        foreach (var item in invalid)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => fx.Service().PublishAsync(item));
        }
        Assert.Equal(0, fx.Scalar("SELECT COUNT(*) FROM local_signed_cart_discount_rules;"));
    }

    [Fact]
    public async Task Other_terminal_or_store_cannot_cache_or_redeem_signed_rule()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var signed = fx.Sign(fx.Rule(1));
        await fx.Service().PublishAsync(signed);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().ApplyAsync(fx.Sale() with { TerminalId = Guid.NewGuid() },
                signed.DiscountId, [new OfflineSalePaymentDraft("cash", 259)]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().ApplyAsync(fx.Sale() with { StoreId = Guid.NewGuid() },
                signed.DiscountId, [new OfflineSalePaymentDraft("cash", 259)]));

        // A correctly signed rule for another terminal still cannot be cached locally.
        var foreign = fx.Sign(fx.Rule(2) with { TerminalId = Guid.NewGuid() });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().PublishAsync(foreign));
    }

    [Fact]
    public async Task Monotonic_revision_blocks_replay_after_a_discount_is_revoked()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var old = fx.Sign(fx.Rule(1, amount: 15m));
        var revoked = fx.Sign(fx.Rule(2, amount: 15m) with
        {
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
            IssuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            ValidFromUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        await fx.Service().PublishAsync(old);
        await fx.Service().PublishAsync(revoked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fx.Service().PublishAsync(old));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().ApplyAsync(fx.Sale(), revoked.DiscountId,
                [new OfflineSalePaymentDraft("cash", 259)]));
        Assert.Equal(2, fx.Scalar("SELECT revision FROM local_signed_cart_discount_rules;"));
    }

    [Fact]
    public async Task Expired_or_future_validity_window_refuses_checkout_even_if_signed()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var future = fx.Sign(fx.Rule(1) with
        {
            ValidFromUtc = now.AddMinutes(15),
            ExpiresAtUtc = now.AddHours(1)
        });
        await fx.Service().PublishAsync(future);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().ApplyAsync(fx.Sale(), future.DiscountId,
                [new OfflineSalePaymentDraft("cash", 259)]));
    }

    [Fact]
    public async Task Terminal_reenrollment_revokes_access_to_previous_signed_rule_cache()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var signed = fx.Sign(fx.Rule(1));
        await fx.Service().PublishAsync(signed);
        Assert.NotNull(await fx.Cache.LoadAsync(
            fx.TenantId, fx.StoreId, fx.TerminalId, signed.DiscountId));

        await fx.Repository.SaveTerminalBindingAsync(new TerminalBinding(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "new-test-machine", "new-test-token", DateTimeOffset.UtcNow, 4));

        Assert.Null(await fx.Cache.LoadAsync(
            fx.TenantId, fx.StoreId, fx.TerminalId, signed.DiscountId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().ApplyAsync(fx.Sale(), signed.DiscountId,
                [new OfflineSalePaymentDraft("cash", 259)]));
    }

    [Fact]
    public async Task Direct_sqlite_tampering_is_detected_when_rule_is_reloaded()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var signed = fx.Sign(fx.Rule(1));
        await fx.Service().PublishAsync(signed);
        fx.Execute("UPDATE local_signed_cart_discount_rules SET kind='fixed_amount';");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().ApplyAsync(fx.Sale(), signed.DiscountId,
                [new OfflineSalePaymentDraft("cash", 259)]));
    }

    [Fact]
    public async Task Supervisor_required_rule_always_fails_closed_on_offline_checkout()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var signed = fx.Sign(fx.Rule(1) with { RequiresApproval = true });
        await fx.Service().PublishAsync(signed);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fx.Service().ApplyAsync(fx.Sale(), signed.DiscountId,
                [new OfflineSalePaymentDraft("cash", 259)]));
    }

    [Fact]
    public async Task Downgrading_signature_key_or_using_untrusted_key_cannot_be_used_for_checkout()
    {
        using var fx = new Fixture();
        await fx.InitializeAsync();
        var signed = fx.Sign(fx.Rule(1));
        await fx.Service().PublishAsync(signed);
        using var another = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var untrustedVerifier = new PinnedOfflineDiscountSignatureVerifier(
            new Dictionary<string, byte[]> { ["ci-authority"] = another.ExportSubjectPublicKeyInfo() });
        var instance = new SignedOfflineCartDiscountCheckout(
            fx.Cache, untrustedVerifier, new SystemClock());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            instance.ApplyAsync(fx.Sale(), signed.DiscountId,
                [new OfflineSalePaymentDraft("cash", 259)]));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "solidpos-signed-discount-ci", Guid.NewGuid().ToString("N"));
        private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid StoreId = Guid.NewGuid();
        public readonly Guid TerminalId = Guid.NewGuid();
        public readonly Guid DiscountId = Guid.NewGuid();
        public string Path => System.IO.Path.Combine(_dir, "discounts.sqlite");
        private readonly SQLiteLocalDatabase _db;
        public SQLiteLocalPosRepository Repository { get; }
        public SQLiteSignedOfflineCartDiscountRuleCache Cache { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_dir);
            _db = new SQLiteLocalDatabase(Path);
            Repository = new SQLiteLocalPosRepository(_db);
            Cache = new SQLiteSignedOfflineCartDiscountRuleCache(_db);
        }
        public async Task InitializeAsync()
        {
            await Repository.InitializeAsync();
            await Repository.SaveTerminalBindingAsync(new TerminalBinding(
                TenantId, StoreId, TerminalId, "test-machine", "test-token",
                DateTimeOffset.UtcNow, 4));
            await Cache.InitializeAsync();
        }

        public SignedOfflineCartDiscountRule Rule(long revision, decimal amount = 15m)
        {
            var now = DateTimeOffset.UtcNow;
            return new SignedOfflineCartDiscountRule(TenantId, StoreId, TerminalId, DiscountId,
                revision, now, now.AddMinutes(-1), now.AddHours(1),
                "percentage", amount, false, false, "ci-authority", "");
        }
        public SignedOfflineCartDiscountRule Sign(SignedOfflineCartDiscountRule rule) =>
            rule with
            {
                SignatureBase64 = Convert.ToBase64String(_signer.SignData(
                    PinnedOfflineDiscountSignatureVerifier.EncodeUnsigned(rule), HashAlgorithmName.SHA256))
            };

        public PinnedOfflineDiscountSignatureVerifier Verifier() => new(
            new Dictionary<string, byte[]> { ["ci-authority"] = _signer.ExportSubjectPublicKeyInfo() });
        public SignedOfflineCartDiscountCheckout Service() =>
            new(Cache, Verifier(), new SystemClock());
        public OfflineSaleDraft Sale() => new(
            Guid.NewGuid(), TenantId, StoreId, TerminalId, DateTimeOffset.UtcNow,
            [
                new OfflineSaleLineDraft(Guid.NewGuid(), null, "SKU-1", "One", 2, 101),
                new OfflineSaleLineDraft(Guid.NewGuid(), null, "SKU-2", "Two", 1, 103)
            ], [new OfflineSalePaymentDraft("cash", 305)], "MXN",
            CashierUserId: Guid.NewGuid());

        public int Scalar(string query)
        {
            using var connection = _db.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = query;
            return Convert.ToInt32(command.ExecuteScalar());
        }
        public void Execute(string commandText)
        {
            using var connection = _db.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }
        public void Dispose()
        {
            _signer.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
    }
}
