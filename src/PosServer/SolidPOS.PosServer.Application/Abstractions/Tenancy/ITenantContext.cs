namespace SolidPOS.PosServer.Application.Abstractions.Tenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }

    Guid? UserId { get; }

    Guid? TerminalId { get; }

    Guid? StoreId { get; }

    bool IsAuthenticatedTenantRequest { get; }
}
