using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SolidPOS.PosServer.Infrastructure.Security;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Auth;

public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task Required_permission_succeeds_when_claim_is_present()
    {
        var requirement = new PermissionRequirement("reports.read");
        ClaimsPrincipal user = new(new ClaimsIdentity(
            [new Claim("permissions", "reports.read")],
            authenticationType: "test"));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Required_permission_is_denied_when_claim_is_missing()
    {
        var requirement = new PermissionRequirement("reports.read");
        ClaimsPrincipal user = new(new ClaimsIdentity(
            [new Claim("permissions", "sales.read")],
            authenticationType: "test"));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
