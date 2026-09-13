using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Application.Terminals;

public interface ITerminalEnrollmentService
{
    Task<TerminalEnrollmentTokenResponse?> CreateEnrollmentTokenAsync(CreateTerminalEnrollmentTokenRequest request, CancellationToken cancellationToken);

    Task<TerminalSessionResponse?> RegisterTerminalAsync(RegisterTerminalRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TerminalResponse>> ListTerminalsAsync(CancellationToken cancellationToken);

    Task<TerminalDetailResponse?> GetTerminalAsync(Guid terminalId, CancellationToken cancellationToken);

    Task<TerminalDetailResponse?> AssignStoreAsync(Guid terminalId, Guid newStoreId, CancellationToken cancellationToken);

    Task<bool> RevokeTerminalAsync(Guid terminalId, CancellationToken cancellationToken);

    Task<bool> DisableTerminalAsync(Guid terminalId, CancellationToken cancellationToken);

    Task<bool> EnableTerminalAsync(Guid terminalId, CancellationToken cancellationToken);

    Task<TerminalHeartbeatResponse?> RecordHeartbeatAsync(TerminalHeartbeatRequest request, CancellationToken cancellationToken);

    Task<TerminalDeviceHealthDto?> GetDeviceHealthAsync(Guid terminalId, CancellationToken cancellationToken);

    Task<TerminalRemoteConfigMetadata?> GetRemoteConfigAsync(Guid terminalId, CancellationToken cancellationToken);

    Task<TerminalRemoteConfigMetadata?> UpdateRemoteConfigAsync(Guid terminalId, TerminalRemoteConfigMetadata metadata, CancellationToken cancellationToken);
}
