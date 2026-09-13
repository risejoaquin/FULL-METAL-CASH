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

    public async Task<TerminalDetailResponse?> GetTerminalAsync(Guid terminalId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return null;
        }

        return await _repository.GetTerminalAsync(tenantId.Value, terminalId, cancellationToken);
    }

    public async Task<TerminalDetailResponse?> AssignStoreAsync(Guid terminalId, Guid newStoreId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("Terminal store assignment rejected: missing tenant context");
            return null;
        }

        bool storeExists = await _repository.StoreExistsAsync(tenantId.Value, newStoreId, cancellationToken);
        if (!storeExists)
        {
            _logger.LogWarning("Terminal store assignment rejected: store {StoreId} does not exist for tenant {TenantId}", newStoreId, tenantId.Value);
            return null;
        }

        bool updated = await _repository.AssignTerminalStoreAsync(tenantId.Value, terminalId, newStoreId, cancellationToken);
        if (!updated)
        {
            _logger.LogWarning("Terminal store assignment failed for terminal {TerminalId}", terminalId);
            return null;
        }

        var payload = new
        {
            terminalId,
            tenantId = tenantId.Value,
            storeId = newStoreId,
            reassignedAt = _clock.UtcNow
        };

        await _syncChangeWriter.AppendAsync(
            tenantId.Value,
            newStoreId,
            "terminal.updated",
            terminalId,
            "update",
            1,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            _tenantContext.TerminalId,
            cancellationToken);

        _logger.LogInformation("Terminal {TerminalId} reassigned to store {StoreId} for tenant {TenantId}; sync change produced",
            terminalId, newStoreId, tenantId.Value);

        return await _repository.GetTerminalAsync(tenantId.Value, terminalId, cancellationToken);
    }

    public async Task<bool> DisableTerminalAsync(Guid terminalId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return false;
        }

        bool disabled = await _repository.DisableTerminalAsync(tenantId.Value, terminalId, cancellationToken);
        if (disabled)
        {
            var payload = new
            {
                terminalId,
                tenantId = tenantId.Value,
                status = "blocked",
                hardLockReason = "disabled",
                disabledAt = _clock.UtcNow
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

            _logger.LogInformation("Terminal disabled for tenant {TenantId} terminal {TerminalId}; sync change produced", tenantId.Value, terminalId);
        }
        else
        {
            _logger.LogWarning("Terminal disable requested for unknown or already revoked terminal {TerminalId}", terminalId);
        }

        return disabled;
    }

    public async Task<bool> EnableTerminalAsync(Guid terminalId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return false;
        }

        bool enabled = await _repository.EnableTerminalAsync(tenantId.Value, terminalId, cancellationToken);
        if (enabled)
        {
            var payload = new
            {
                terminalId,
                tenantId = tenantId.Value,
                status = "active",
                hardLockReason = (string?)null,
                enabledAt = _clock.UtcNow
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

            _logger.LogInformation("Terminal enabled for tenant {TenantId} terminal {TerminalId}; sync change produced", tenantId.Value, terminalId);
        }
        else
        {
            _logger.LogWarning("Terminal enable failed for terminal {TerminalId} (may be revoked or not found)", terminalId);
        }

        return enabled;
    }

    public async Task<TerminalHeartbeatResponse?> RecordHeartbeatAsync(TerminalHeartbeatRequest request, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        Guid? terminalId = _tenantContext.TerminalId;

        if (!tenantId.HasValue || !terminalId.HasValue)
        {
            _logger.LogWarning("Heartbeat rejected: missing tenant or terminal context");
            return null;
        }

        string? deviceHealthJson = request.DeviceHealth is not null
            ? JsonSerializer.Serialize(request.DeviceHealth with { ReportedAtUtc = _clock.UtcNow }, JsonOptions)
            : null;

        TerminalHeartbeatResponse? response = await _repository.RecordHeartbeatAsync(
            tenantId.Value,
            terminalId.Value,
            request.AppVersion,
            request.LocalDbVersion,
            request.LastSyncCursor,
            deviceHealthJson,
            cancellationToken);

        if (response is not null)
        {
            _logger.LogInformation("Heartbeat recorded for tenant {TenantId} terminal {TerminalId}", tenantId.Value, terminalId.Value);
        }

        return response;
    }

    public async Task<TerminalDeviceHealthDto?> GetDeviceHealthAsync(Guid terminalId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return null;
        }

        return await _repository.GetDeviceHealthAsync(tenantId.Value, terminalId, cancellationToken);
    }

    public async Task<TerminalRemoteConfigMetadata?> GetRemoteConfigAsync(Guid terminalId, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return null;
        }

        return await _repository.GetRemoteConfigAsync(tenantId.Value, terminalId, cancellationToken);
    }

    public async Task<TerminalRemoteConfigMetadata?> UpdateRemoteConfigAsync(Guid terminalId, TerminalRemoteConfigMetadata metadata, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            return null;
        }

        TerminalRemoteConfigMetadata updatedMetadata = metadata with { UpdatedAtUtc = _clock.UtcNow };
        string json = JsonSerializer.Serialize(updatedMetadata, JsonOptions);

        bool updated = await _repository.UpdateRemoteConfigAsync(tenantId.Value, terminalId, json, cancellationToken);
        if (!updated)
        {
            return null;
        }

        _logger.LogInformation("Remote config updated for tenant {TenantId} terminal {TerminalId}", tenantId.Value, terminalId);
        return updatedMetadata;
    }
}
