using Moq;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Contracts.Audit;
using SolidPOS.PosServer.Infrastructure.Audit;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Audit;

public sealed class AuditEventServiceTests
{
    [Fact]
    public async Task List_rejects_request_without_tenant_context()
    {
        Mock<ITenantContext> tenantContext = new();
        Mock<IAuditEventRepository> repository = new();
        AuditEventService service = CreateService(tenantContext.Object, repository.Object);

        AuditEventPageResponse? result = await service.ListAsync(CreateFilters(), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<AuditEventFilters>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task List_rejects_invalid_date_range()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = CreateTenantContext(tenantId);
        Mock<IAuditEventRepository> repository = new();
        AuditEventService service = CreateService(tenantContext.Object, repository.Object);

        AuditEventPageResponse? result = await service.ListAsync(
            CreateFilters() with
            {
                From = DateTimeOffset.UtcNow,
                To = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<AuditEventFilters>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task List_normalizes_pagination_and_text_filters()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = CreateTenantContext(tenantId);
        Mock<IAuditEventRepository> repository = new();
        repository
            .Setup(x => x.ListAsync(tenantId, It.IsAny<AuditEventFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid repositoryTenantId, AuditEventFilters filters, CancellationToken cancellationToken) =>
                new AuditEventPageResponse([], new AuditEventPageMetaResponse(filters.Page, filters.PageSize, 0)));

        AuditEventService service = CreateService(tenantContext.Object, repository.Object);

        AuditEventPageResponse? result = await service.ListAsync(
            CreateFilters() with
            {
                Action = " admin.catalog.category.soft_delete ",
                EntityType = " category ",
                Page = -10,
                PageSize = 999
            },
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Meta.Page);
        Assert.Equal(200, result.Meta.PageSize);
        repository.Verify(
            x => x.ListAsync(
                tenantId,
                It.Is<AuditEventFilters>(filters =>
                    filters.Action == "admin.catalog.category.soft_delete" &&
                    filters.EntityType == "category" &&
                    filters.Page == 1 &&
                    filters.PageSize == 200),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static AuditEventService CreateService(ITenantContext tenantContext, IAuditEventRepository repository)
    {
        return new AuditEventService(tenantContext, repository, Mock.Of<ILogger<AuditEventService>>());
    }

    private static Mock<ITenantContext> CreateTenantContext(Guid tenantId)
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        return tenantContext;
    }

    private static AuditEventFilters CreateFilters()
    {
        return new AuditEventFilters(null, null, null, null, null, null, null, 1, 50);
    }
}
