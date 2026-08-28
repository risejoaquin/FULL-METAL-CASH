using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Application.Terminals;

public interface ITerminalEnrollmentService
{
    Task<TerminalEnrollmentTokenResponse?> CreateEnrollmentTokenAsync(CreateTerminalEnrollmentTokenRequest request, CancellationToken cancellationToken);

    Task<TerminalSessionResponse?> RegisterTerminalAsync(RegisterTerminalRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TerminalResponse>> ListTerminalsAsync(CancellationToken cancellationToken);

    Task<bool> RevokeTerminalAsync(Guid terminalId, CancellationToken cancellationToken);
}
