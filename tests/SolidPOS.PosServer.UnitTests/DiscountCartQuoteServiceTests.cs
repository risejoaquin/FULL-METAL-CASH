using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Discounts;
using SolidPOS.PosServer.Contracts.Discounts;
using SolidPOS.PosServer.Infrastructure.Discounts;
using Xunit;

namespace SolidPOS.PosServer.UnitTests;

public sealed class DiscountCartQuoteServiceTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _store = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _product = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IDiscountsRepository> _discounts = new();
    private readonly Mock<IAuditEventWriter> _audit = new();
    private readonly Mock<IClock> _clock = new();

    private DiscountsService Service()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenant);
        _tenantContext.SetupGet(x => x.StoreId).Returns(_store);
        _tenantContext.SetupGet(x => x.UserId).Returns(_user);
        _clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);
        _discounts.Setup(x => x.ProductExistsAsync(
            _tenant, _product, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new DiscountsService(
            _tenantContext.Object, _discounts.Object, _audit.Object,
            _clock.Object, NullLogger<DiscountsService>.Instance);
    }

    private DiscountResponse Stored(Guid id, string type, decimal value,
        Guid? productId = null, Guid? storeId = null, Guid? categoryId = null,
        string status = "active")
    {
        var now = DateTimeOffset.UtcNow;
        return new DiscountResponse(id, _tenant, null, "Fixture Rule", type, value,
            storeId, categoryId, productId, null, null, status, now, now);
    }

    [Fact]
    public async Task Server_quotes_two_real_product_lines_with_verified_discount_scope()
    {
        var lineId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var lineDiscount = Stored(Guid.NewGuid(), "fixed_amount", 30, _product, _store);
        var cartDiscount = Stored(Guid.NewGuid(), "percentage", 10, storeId: _store);
        var mocks = new[] { lineDiscount, cartDiscount };
        _discounts.Setup(x => x.GetByIdAsync(_tenant, It.IsAny<Guid>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(
                (Guid tenant, Guid id, CancellationToken _) => mocks.SingleOrDefault(y => y.Id == id));

        var service = Service();
        var request = new DiscountCartQuoteRequest(_store,
            [new(lineId, _product, 200, lineDiscount.Id),
             new(other, _product, 100)],
            cartDiscount.Id);

        var quote = await service.QuoteCartAsync(request, CancellationToken.None);
        Assert.NotNull(quote);
        Assert.True(quote.PreviewOnly);
        Assert.Equal(300, quote.GrossCents);
        Assert.Equal(57, quote.DiscountCents); // 30 line + 10% * 270 = 27
        Assert.Equal(243, quote.NetCents);
        Assert.Equal(quote.DiscountCents, quote.Allocations.Sum(x => x.Cents));
        Assert.Equal(quote.NetCents, quote.Lines.Sum(x => x.NetCents));
        _audit.VerifyNoOtherCalls(); // preview is not a sale or a settlement
    }

    [Fact]
    public async Task Tenant_and_store_scope_mismatches_are_rejected_without_calling_repository()
    {
        var svc = Service();
        var quote = await svc.QuoteCartAsync(new DiscountCartQuoteRequest(Guid.NewGuid(),
            [new(Guid.NewGuid(), _product, 100)]), CancellationToken.None);
        Assert.Null(quote);
        _discounts.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Foreign_product_discount_and_client_claimed_category_are_rejected()
    {
        var bad = Stored(Guid.NewGuid(), "fixed_amount", 20, Guid.NewGuid(), _store);
        _discounts.Setup(x => x.GetByIdAsync(_tenant, bad.Id,
            It.IsAny<CancellationToken>())).ReturnsAsync(bad);
        var svc = Service();
        var line = new DiscountCartQuoteLineRequest(Guid.NewGuid(), _product, 100, bad.Id);
        Assert.Null(await svc.QuoteCartAsync(new DiscountCartQuoteRequest(
            _store, [line]), CancellationToken.None));

        var claimedCategory = Stored(Guid.NewGuid(), "percentage", 10, _product, _store, Guid.NewGuid());
        _discounts.Setup(x => x.GetByIdAsync(_tenant, claimedCategory.Id,
            It.IsAny<CancellationToken>())).ReturnsAsync(claimedCategory);
        Assert.Null(await svc.QuoteCartAsync(new DiscountCartQuoteRequest(
            _store, [line with { LineDiscountId = claimedCategory.Id }]), CancellationToken.None));
    }

    [Fact]
    public async Task Inactive_or_expired_rules_never_generate_pricing_quotes()
    {
        var stale = Stored(Guid.NewGuid(), "percentage", 50, _product, _store,
            status: "inactive");
        _discounts.Setup(x => x.GetByIdAsync(_tenant, stale.Id,
            It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        var svc = Service();
        Assert.Null(await svc.QuoteCartAsync(new DiscountCartQuoteRequest(
            _store, [new(Guid.NewGuid(), _product, 100, stale.Id)]), CancellationToken.None));
    }

    [Fact]
    public async Task Partial_update_cannot_introduce_fractional_fixed_amount_when_type_is_omitted()
    {
        var original = Stored(Guid.NewGuid(), "fixed_amount", 100, storeId: _store);
        _discounts.Setup(x => x.GetByIdAsync(_tenant, original.Id,
            It.IsAny<CancellationToken>())).ReturnsAsync(original);
        var service = Service();
        var updated = await service.UpdateAsync(original.Id, new UpdateDiscountRequest(
            null, null, null, 1.5m, null, null, null, null, null, null),
            CancellationToken.None);
        Assert.Null(updated);
        _discounts.Verify(x => x.UpdateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateDiscountRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Repeated_rules_across_lines_are_rejected_not_double_counted()
    {
        var rule = Stored(Guid.NewGuid(), "percentage", 10, _product, _store);
        _discounts.Setup(x => x.GetByIdAsync(_tenant, rule.Id,
            It.IsAny<CancellationToken>())).ReturnsAsync(rule);
        var svc = Service();
        Assert.Null(await svc.QuoteCartAsync(new DiscountCartQuoteRequest(_store,
            [new(Guid.NewGuid(), _product, 100, rule.Id),
             new(Guid.NewGuid(), _product, 100, rule.Id)]), CancellationToken.None));
    }
}
