using SolidPOS.PosServer.Contracts.BuilderUpdates;

namespace SolidPOS.PosServer.Application.BuilderUpdates;

public interface IBuilderUpdatesRepository
{
    Task<IReadOnlyCollection<BuilderProjectResponse>> ListProjectsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<BuilderProjectResponse?> CreateProjectAsync(Guid tenantId, CreateBuilderProjectRequest request, CancellationToken cancellationToken);

    Task<BuilderBuildResponse?> CreateBuildAsync(Guid tenantId, Guid projectId, CreateBuilderBuildRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UpdateChannelResponse>> ListChannelsAsync(CancellationToken cancellationToken);

    Task<UpdateReleaseResponse?> CreateReleaseAsync(Guid tenantId, CreateUpdateReleaseRequest request, CancellationToken cancellationToken);

    Task<UpdateCheckResponse> CheckForUpdateAsync(Guid tenantId, string currentVersion, string channel, string packageType, CancellationToken cancellationToken);
}
