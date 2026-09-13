using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Application.Terminals;

public interface ITerminalRepository
{
    Task<bool> StoreExistsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);

    Task StoreEnrollmentTokenAsync(Guid tenantId, Guid storeId, string tokenHash, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<AuthenticatedTerminal?> RegisterTerminalAsync(string enrollmentTokenHash, string name, string fingerprint, string? appVersion, CancellationToken cancellationToken);

    Task UpdateTerminalTokenHashAsync(Guid tenantId, Guid terminalId, string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TerminalResponse>> ListTerminalsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> RevokeTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken);

    Task<bool> IsTerminalActiveAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken);

    Task<TerminalDetailResponse?> GetTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken);

    Task<bool> AssignTerminalStoreAsync(Guid tenantId, Guid terminalId, Guid storeId, CancellationToken cancellationToken);

    Task<bool> DisableTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken);

    Task<bool> EnableTerminalAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken);

    Task<TerminalHeartbeatResponse?> RecordHeartbeatAsync(
        Guid tenantId,
        Guid terminalId,
        string? appVersion,
        int? localDbVersion,
        string? lastSyncCursor,
        string? deviceHealthJson,
        CancellationToken cancellationToken);

    Task<TerminalDeviceHealthDto?> GetDeviceHealthAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken);

    Task<TerminalRemoteConfigMetadata?> GetRemoteConfigAsync(Guid tenantId, Guid terminalId, CancellationToken cancellationToken);

    Task<bool> UpdateRemoteConfigAsync(Guid tenantId, Guid terminalId, string remoteConfigJson, CancellationToken cancellationToken);
}
