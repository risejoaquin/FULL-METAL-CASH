using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.BuilderUpdates;
using SolidPOS.PosServer.Contracts.BuilderUpdates;

namespace SolidPOS.PosServer.Infrastructure.BuilderUpdates;

public sealed class BuilderUpdatesService : IBuilderUpdatesService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Channels = ["stable", "beta", "internal"];
    private static readonly HashSet<string> PackageTypes = ["velopack"];

    private readonly ITenantContext _tenantContext;
    private readonly IBuilderUpdatesRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<BuilderUpdatesService> _logger;

    public BuilderUpdatesService(
        ITenantContext tenantContext,
        IBuilderUpdatesRepository repository,
        IAuditEventWriter auditEventWriter,
        ILogger<BuilderUpdatesService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<BuilderProjectResponse>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        return TryGetTenantId(out Guid tenantId)
            ? await _repository.ListProjectsAsync(tenantId, cancellationToken)
            : Array.Empty<BuilderProjectResponse>();
    }

    public async Task<BuilderProjectResponse?> CreateProjectAsync(CreateBuilderProjectRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Name)
            || !PackageTypes.Contains(Normalize(request.PackageType)))
        {
            _logger.LogWarning("Builder project creation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        BuilderProjectResponse? response = await _repository.CreateProjectAsync(
            tenantId,
            request with
            {
                Name = request.Name.Trim(),
                PackageType = Normalize(request.PackageType)
            },
            cancellationToken);

        if (response is not null)
        {
            await WriteAuditAsync(tenantId, "builder.project.created", "builder_project", response.Id, response, cancellationToken);
        }

        return response;
    }

    public async Task<BuilderBuildResponse?> CreateBuildAsync(Guid projectId, CreateBuilderBuildRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || projectId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.AppVersion)
            || !Channels.Contains(Normalize(request.Channel)))
        {
            _logger.LogWarning("Builder build creation rejected for tenant {TenantId} project {ProjectId}", _tenantContext.TenantId, projectId);
            return null;
        }

        BuilderBuildResponse? response = await _repository.CreateBuildAsync(
            tenantId,
            projectId,
            request with
            {
                AppVersion = request.AppVersion.Trim(),
                Channel = Normalize(request.Channel)
            },
            cancellationToken);

        if (response is not null)
        {
            await WriteAuditAsync(tenantId, "builder.build.created", "builder_build", response.Id, response, cancellationToken);
        }

        return response;
    }

    public Task<IReadOnlyCollection<UpdateChannelResponse>> ListChannelsAsync(CancellationToken cancellationToken)
    {
        return _repository.ListChannelsAsync(cancellationToken);
    }

    public async Task<UpdateReleaseResponse?> CreateReleaseAsync(CreateUpdateReleaseRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Version)
            || !Channels.Contains(Normalize(request.Channel))
            || !PackageTypes.Contains(Normalize(request.PackageType))
            || string.IsNullOrWhiteSpace(request.ArtifactUrl)
            || string.IsNullOrWhiteSpace(request.ArtifactHash)
            || string.IsNullOrWhiteSpace(request.Signature))
        {
            _logger.LogWarning("Update release creation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        UpdateReleaseResponse? response = await _repository.CreateReleaseAsync(
            tenantId,
            request with
            {
                Version = request.Version.Trim(),
                Channel = Normalize(request.Channel),
                PackageType = Normalize(request.PackageType),
                ArtifactUrl = request.ArtifactUrl.Trim(),
                ArtifactHash = request.ArtifactHash.Trim(),
                Signature = request.Signature.Trim(),
                RollbackVersion = string.IsNullOrWhiteSpace(request.RollbackVersion) ? null : request.RollbackVersion.Trim()
            },
            cancellationToken);

        if (response is not null)
        {
            await WriteAuditAsync(tenantId, "updates.release.created", "update_release", response.Id, response, cancellationToken);
        }

        return response;
    }

    public Task<UpdateCheckResponse?> CheckForUpdateAsync(string? currentVersion, string? channel, string? packageType, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(currentVersion)
            || !Channels.Contains(Normalize(channel ?? string.Empty))
            || !PackageTypes.Contains(Normalize(packageType ?? "velopack")))
        {
            return Task.FromResult<UpdateCheckResponse?>(null);
        }

        return CheckAsync();

        async Task<UpdateCheckResponse?> CheckAsync()
        {
            return await _repository.CheckForUpdateAsync(
                tenantId,
                currentVersion.Trim(),
                Normalize(channel!),
                Normalize(packageType ?? "velopack"),
                cancellationToken);
        }
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = _tenantContext.TenantId ?? Guid.Empty;
        return tenantId != Guid.Empty;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private async Task WriteAuditAsync(Guid tenantId, string action, string entityType, Guid entityId, object payload, CancellationToken cancellationToken)
    {
        await _auditEventWriter.AppendAsync(
            tenantId,
            action,
            entityType,
            entityId,
            null,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            cancellationToken);
    }
}
