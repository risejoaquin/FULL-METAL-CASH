using Microsoft.AspNetCore.Authorization;

namespace SolidPOS.PosServer.Infrastructure.Security;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
