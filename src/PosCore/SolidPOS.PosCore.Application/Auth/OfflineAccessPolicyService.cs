using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Auth;

public sealed class OfflineAccessPolicyService
{
    public bool IsWithinOfflineWindow(LocalUser user, DateTimeOffset nowUtc)
    {
        if (!user.IsActive) return false;
        if (user.MaxOfflineHours <= 0) return false;
        return nowUtc - user.LastSyncedAtUtc <= TimeSpan.FromHours(user.MaxOfflineHours);
    }
}
