using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Infrastructure.Terminals;

public sealed class TerminalEnrollmentService : ITerminalEnrollmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly ITerminalRepository _repository;
    private readonly ITokenService _tokenService;
    private readonly ISyncChangeWriter _syncChangeWriter;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<TerminalEnrollmentService> _logger;

    public TerminalEnrollmentService(
        ITenantContext tenantContext,
        ITerminalRepository repository,
        ITokenService tokenService,
        ISyncChangeWriter syncChangeWriter,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        ILogger<TerminalEnrollmentService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _tokenService = tokenService;
        _syncChangeWriter = syncChangeWriter;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<TerminalEnrollmentTokenResponse?> CreateEnrollmentTokenAsync(
        CreateTerminalEnrollmentTokenRequest request,
        CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("Terminal enrollment token creation rejected because tenant context is missing");
            return null;
        }

        int expiresInMinutes = Math.Clamp(request.ExpiresInMinutes, 5, 1440);
        bool storeExists = await _repository.StoreExistsAsync(tenantId.Value, request.StoreId, cancellationToken);
        if (!storeExists)
        {
            _logger.LogWarning("Terminal enrollment token creation rejected because store is invalid for tenant {TenantId}", tenantId.Value);
            return null;
        }

        string token = _tokenService.CreateRefreshToken();
        string tokenHash = _tokenService.HashToken(token);
        DateTimeOffset expiresAt = _clock.UtcNow.AddMinutes(expiresInMinutes);

        await _repository.StoreEnrollmentTokenAsync(tenantId.Value, request.StoreId, tokenHash, expiresAt, cancellationToken);

        _logger.LogInformation(
            "Terminal enrollment token created for tenant {TenantId} store {StoreId}",
            tenantId.Value,
            request.StoreId);

        return new TerminalEnrollmentTokenResponse(tenantId.Value, request.StoreId, token, expiresAt);
    }

    public async Task<TerminalSessionResponse?> RegisterTerminalAsync(RegisterTerminalRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentToken)
            || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Fingerprint))
        {
            _logger.LogWarning("Terminal registration failed");
            return null;
        }

        string tokenHash = _tokenService.HashToken(request.EnrollmentToken);
        AuthenticatedTerminal? terminal = await _repository.RegisterTerminalAsync(
            tokenHash,
            request.Name.Trim(),
            request.Fingerprint.Trim(),
            request.AppVersion?.Trim(),
            cancellationToken);

        if (terminal is null)
        {
            _logger.LogWarning("Terminal registration failed");
            return null;
        }

        DateTimeOffset expiresAt = _clock.UtcNow.AddDays(_jwtOptions.TerminalAccessTokenDays);
        string accessToken = _tokenService.CreateTerminalAccessToken(terminal, TerminalPermissionSet.Default, expiresAt);
        await _repository.UpdateTerminalTokenHashAsync(terminal.TenantId, terminal.TerminalId, _tokenService.HashToken(accessToken), cancellationToken);

        TerminalResponse terminalResponse = new(
            terminal.TerminalId,
            terminal.TenantId,
            terminal.StoreId,
            terminal.Name,
            request.Fingerprint.Trim(),
            terminal.Status,
            request.AppVersion?.Trim(),
            _clock.UtcNow);

        await _syncChangeWriter.AppendAsync(
            terminal.TenantId,
            terminal.StoreId,
            "terminal.updated",
            terminal.TerminalId,
            "update",
            1,
            JsonSerializer.SerializeToElement(terminalResponse, JsonOptions),
            terminal.TerminalId,
            cancellationToken);

        _logger.LogInformation(
            "Terminal registered for tenant {TenantId} store {StoreId} terminal {TerminalId}; sync change produced",
            terminal.TenantId,
            terminal.StoreId,
            terminal.TerminalId);

        return new TerminalSessionResponse(
            accessToken,
            expiresAt,
            terminalResponse);
    }

    public async Task<IReadOnlyCollection<TerminalResponse>> ListTerminalsAsync(CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return [];
        }

        return await _repository.ListTerminalsAsync(tenantId.Value, cancellationToken);
    }

    public async Task<bool> RevokeTerminalAsync(Guid terminalId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return false;
        }

        bool revoked = await _repository.RevokeTerminalAsync(tenantId.Value, terminalId, cancellationToken);
        if (revoked)
        {
            var payload = new
            {
                terminalId,
                tenantId = tenantId.Value,
                status = "blocked",
                hardLockReason = "revoked",
                revokedAt = _clock.UtcNow
            };

            await _syncChangeWriter.AppendAsync(
                tenantId.Value,
                null,
                "terminal.updated",
                terminalId,
                "update",
                1,
                JsonSerializer.SerializeToElement(payload, JsonOptions),
                _tenantContext.TerminalId,
                cancellationToken);

            _logger.LogInformation("Terminal revoked for tenant {TenantId} terminal {TerminalId}; sync change produced", tenantId.Value, terminalId);
        }
        else
        {
            _logger.LogWarning("Terminal revoke requested for unknown terminal {TerminalId}", terminalId);
        }

        return revoked;
    }
}
