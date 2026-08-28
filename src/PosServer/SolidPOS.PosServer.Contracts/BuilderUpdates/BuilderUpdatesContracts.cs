using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.BuilderUpdates;

public sealed record BuilderProjectResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    Guid? TargetStoreId,
    string PackageType,
    JsonElement Config,
    JsonElement Branding,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateBuilderProjectRequest(
    string Name,
    Guid? TargetStoreId,
    string PackageType,
    JsonElement? Config,
    JsonElement? Branding);

public sealed record BuilderBuildResponse(
    Guid Id,
    Guid TenantId,
    Guid ProjectId,
    string Status,
    string AppVersion,
    string Channel,
    bool UniversalInstaller,
    string? ArtifactUrl,
    string? ArtifactHash,
    string? Signature,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);

public sealed record CreateBuilderBuildRequest(
    string AppVersion,
    string Channel,
    bool UniversalInstaller,
    string? ArtifactUrl,
    string? ArtifactHash,
    string? Signature);

public sealed record UpdateChannelResponse(
    string Code,
    string Name,
    string Stability,
    bool AllowsTenantScopedRelease,
    bool AllowsMandatoryRelease);

public sealed record UpdateReleaseResponse(
    Guid Id,
    Guid? TenantId,
    string Version,
    string Channel,
    string PackageType,
    string ArtifactUrl,
    string ArtifactHash,
    string Signature,
    string? RollbackVersion,
    bool Mandatory,
    bool UniversalInstaller,
    DateTimeOffset PublishedAt,
    DateTimeOffset? RevokedAt);

public sealed record CreateUpdateReleaseRequest(
    string Version,
    string Channel,
    string PackageType,
    string ArtifactUrl,
    string ArtifactHash,
    string Signature,
    string? RollbackVersion,
    bool Mandatory,
    bool UniversalInstaller,
    bool TenantScoped,
    IReadOnlyCollection<Guid>? TargetTerminalIds = null);

public sealed record UpdateCheckResponse(
    bool UpdateAvailable,
    string CurrentVersion,
    string Channel,
    string PackageType,
    UpdateReleaseResponse? Release,
    string Decision,
    string BrandingPolicy);
