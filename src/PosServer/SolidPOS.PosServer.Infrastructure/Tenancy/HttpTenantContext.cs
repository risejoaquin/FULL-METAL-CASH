using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;

namespace SolidPOS.PosServer.Infrastructure.Tenancy;

public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId => ReadGuidClaim("tenant_id");

    public Guid? UserId => ReadGuidClaim("user_id");

    public Guid? TerminalId => ReadGuidClaim("terminal_id");

    public Guid? StoreId => ReadGuidClaim("store_id");

    public bool IsAuthenticatedTenantRequest => TenantId.HasValue && (UserId.HasValue || TerminalId.HasValue);

    private Guid? ReadGuidClaim(string claimType)
    {
        ClaimsPrincipal? user = _httpContextAccessor.HttpContext?.User;
        string? value = user?.FindFirstValue(claimType);

        return Guid.TryParse(value, out Guid parsed) ? parsed : null;
    }
}
