using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Application.Terminals;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Auth;

/// <summary>
/// The permission catalog registers these capabilities for explicit RBAC
/// administration. Registering a code MUST NOT make it a terminal default.
/// The actual PostgreSQL migration is reviewed separately and never run live in CI.
/// </summary>
public sealed class SyncRecoveryPermissionCatalogTests
{
    [Fact]
    public void Recovery_permission_codes_are_distinct_and_discoverable_by_role_administration()
    {
        Assert.Equal("sync.recovery.execute", PermissionCodes.SyncRecoveryExecute);
        Assert.Equal("sync.recovery.approve", PermissionCodes.SyncRecoveryApprove);
        Assert.NotEqual(PermissionCodes.SyncRecoveryExecute, PermissionCodes.SyncRecoveryApprove);
        Assert.Contains(PermissionCodes.SyncRecoveryExecute, PermissionCodes.All);
        Assert.Contains(PermissionCodes.SyncRecoveryApprove, PermissionCodes.All);
        Assert.Equal(PermissionCodes.All.Count, PermissionCodes.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void No_default_terminal_permission_grants_manual_recovery_or_approval()
    {
        Assert.DoesNotContain(PermissionCodes.SyncRecoveryExecute, TerminalPermissionSet.Default);
        Assert.DoesNotContain(PermissionCodes.SyncRecoveryApprove, TerminalPermissionSet.Default);
        Assert.Contains(PermissionCodes.SyncPush, TerminalPermissionSet.Default);
        Assert.Contains(PermissionCodes.SyncPull, TerminalPermissionSet.Default);
    }
}
