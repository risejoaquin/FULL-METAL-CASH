using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Reports;
using SolidPOS.PosServer.Contracts.Reports;
using SolidPOS.PosServer.Infrastructure.Reports;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Reports;

public sealed class ReportsServiceTests
{
    [Fact]
    public async Task Sales_range_rejects_request_without_tenant_context()
    {
        Mock<ITenantContext> tenantContext = new();
        Mock<IReportsRepository> repository = new();
        ReportsService service = CreateService(tenantContext.Object, repository.Object);

        SalesRangeReportResponse? result = await service.GetSalesRangeAsync(new ReportDateRangeFilters(null, null, null), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.GetSalesRangeAsync(It.IsAny<Guid>(), It.IsAny<ReportDateRangeFilters>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Sales_range_rejects_invalid_date_range()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = CreateTenantContext(tenantId);
        Mock<IReportsRepository> repository = new();
        ReportsService service = CreateService(tenantContext.Object, repository.Object);

        SalesRangeReportResponse? result = await service.GetSalesRangeAsync(
            new ReportDateRangeFilters(null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.GetSalesRangeAsync(It.IsAny<Guid>(), It.IsAny<ReportDateRangeFilters>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Sales_range_defaults_to_last_24_hours()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = CreateTenantContext(tenantId);
        Mock<IReportsRepository> repository = new();
        repository
            .Setup(x => x.GetSalesRangeAsync(tenantId, It.IsAny<ReportDateRangeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid repositoryTenantId, ReportDateRangeFilters filters, CancellationToken cancellationToken) =>
                new SalesRangeReportResponse(repositoryTenantId, filters.StoreId, filters.From!.Value, filters.To!.Value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        ReportsService service = CreateService(tenantContext.Object, repository.Object);

        SalesRangeReportResponse? result = await service.GetSalesRangeAsync(new ReportDateRangeFilters(null, null, null), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.To > result.From);
        Assert.InRange((result.To - result.From).TotalHours, 23.99, 24.01);
    }

    [Fact]
    public async Task Top_products_normalizes_limit_to_max_200()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = CreateTenantContext(tenantId);
        Mock<IReportsRepository> repository = new();
        repository
            .Setup(x => x.GetTopProductsAsync(tenantId, It.IsAny<ReportListFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TopProductReportItemResponse>());

        ReportsService service = CreateService(tenantContext.Object, repository.Object);

        IReadOnlyCollection<TopProductReportItemResponse>? result = await service.GetTopProductsAsync(
            new ReportListFilters(null, null, null, 999),
            CancellationToken.None);

        Assert.NotNull(result);
        repository.Verify(
            x => x.GetTopProductsAsync(
                tenantId,
                It.Is<ReportListFilters>(filters => filters.Limit == 200 && filters.From.HasValue && filters.To.HasValue),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Sales_range_rejects_store_not_owned_by_tenant()
    {
        Guid tenantId = Guid.NewGuid();
        Guid foreignStoreId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = CreateTenantContext(tenantId);
        Mock<IReportsRepository> repository = new();
        repository.Setup(x => x.StoreExistsAsync(tenantId, foreignStoreId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ReportsService service = CreateService(tenantContext.Object, repository.Object);

        SalesRangeReportResponse? result = await service.GetSalesRangeAsync(
            new ReportDateRangeFilters(foreignStoreId, null, null),
            CancellationToken.None);

        Assert.Null(result);
        repository.Verify(x => x.GetSalesRangeAsync(It.IsAny<Guid>(), It.IsAny<ReportDateRangeFilters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dashboard_overview_normalizes_limit_and_auto_selects_hour_bucket_for_24_hours()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = CreateTenantContext(tenantId);
        Mock<IReportsRepository> repository = new();
        repository.Setup(x => x.GetSalesRangeAsync(tenantId, It.IsAny<ReportDateRangeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, ReportDateRangeFilters f, CancellationToken _) => new SalesRangeReportResponse(id, f.StoreId, f.From!.Value, f.To!.Value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        repository.Setup(x => x.GetSalesByPaymentMethodAsync(tenantId, It.IsAny<ReportDateRangeFilters>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PaymentMethodReportItemResponse>());
        repository.Setup(x => x.GetTopProductsAsync(tenantId, It.IsAny<ReportListFilters>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TopProductReportItemResponse>());
        repository.Setup(x => x.GetNegativeInventoryAsync(tenantId, null, 200, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<NegativeInventoryItemResponse>());
        repository.Setup(x => x.GetCashShiftsAsync(tenantId, It.IsAny<ReportListFilters>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CashShiftReportItemResponse>());
        repository.Setup(x => x.GetDashboardInventorySummaryAsync(tenantId, It.IsAny<ReportDateRangeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardInventorySummaryResponse(0, 0, 0, 0, 0));
        repository.Setup(x => x.GetSalesTrendAsync(tenantId, It.IsAny<ReportDateRangeFilters>(), "hour", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DashboardSalesTrendPointResponse>());
        ReportsService service = CreateService(tenantContext.Object, repository.Object);

        DashboardOverviewResponse? result = await service.GetDashboardOverviewAsync(
            new DashboardReportFilters(null, null, null, 999, null), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("hour", result.TrendBucket);
        repository.Verify(x => x.GetTopProductsAsync(tenantId, It.Is<ReportListFilters>(f => f.Limit == 200), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ReportsService CreateService(ITenantContext tenantContext, IReportsRepository repository)
    {
        return new ReportsService(tenantContext, repository, Mock.Of<ILogger<ReportsService>>());
    }

    private static Mock<ITenantContext> CreateTenantContext(Guid tenantId)
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        return tenantContext;
    }
}
