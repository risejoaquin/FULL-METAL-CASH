using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Contracts.Tenants;

namespace SolidPOS.PosServer.Infrastructure.Tenants;

public sealed class TenantConfigService : ITenantConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly ITenantConfigRepository _repository;
    private readonly ISyncChangeWriter _syncChangeWriter;
    private readonly ILogger<TenantConfigService> _logger;

    public TenantConfigService(
        ITenantContext tenantContext,
        ITenantConfigRepository repository,
        ISyncChangeWriter syncChangeWriter,
        ILogger<TenantConfigService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _syncChangeWriter = syncChangeWriter;
        _logger = logger;
    }

    public async Task<TenantConfigResponse?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("Tenant config read rejected because tenant context is missing");
            return null;
        }

        TenantConfigResponse? config = await _repository.GetAsync(tenantId.Value, cancellationToken);
        if (config is null)
        {
            _logger.LogWarning("Tenant config not found for tenant {TenantId}", tenantId.Value);
            return null;
        }

        _logger.LogInformation("Tenant config read for tenant {TenantId} version {Version}", tenantId.Value, config.Version);
        return config;
    }

    public async Task<TenantConfigResponse?> UpdateCurrentAsync(UpdateTenantConfigRequest request, CancellationToken cancellationToken)
    {
        Guid? tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("Tenant config update rejected because tenant context is missing");
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.BusinessVertical) || string.IsNullOrWhiteSpace(request.UiLayout))
        {
            _logger.LogWarning("Tenant config update rejected because required fields are missing for tenant {TenantId}", tenantId.Value);
            return null;
        }

        TenantConfigResponse? updated = await _repository.UpsertAsync(tenantId.Value, request, cancellationToken);
        if (updated is null)
        {
            _logger.LogWarning("Tenant config update rejected by version check for tenant {TenantId}", tenantId.Value);
            return null;
        }

        await _syncChangeWriter.AppendAsync(
            updated.TenantId,
            null,
            "tenant.config",
            updated.TenantId,
            "update",
            updated.Version,
            JsonSerializer.SerializeToElement(updated, JsonOptions),
            _tenantContext.TerminalId,
            cancellationToken);

        _logger.LogInformation(
            "Tenant config updated for tenant {TenantId} version {Version}; sync change produced",
            tenantId.Value,
            updated.Version);

        return updated;
    }
}
