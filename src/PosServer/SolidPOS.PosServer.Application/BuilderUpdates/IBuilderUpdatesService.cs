using SolidPOS.PosServer.Contracts.BuilderUpdates;

namespace SolidPOS.PosServer.Application.BuilderUpdates;

public interface IBuilderUpdatesService
{
    Task<IReadOnlyCollection<BuilderProjectResponse>> ListProjectsAsync(CancellationToken cancellationToken);

    Task<BuilderProjectResponse?> CreateProjectAsync(CreateBuilderProjectRequest request, CancellationToken cancellationToken);

    Task<BuilderBuildResponse?> CreateBuildAsync(Guid projectId, CreateBuilderBuildRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UpdateChannelResponse>> ListChannelsAsync(CancellationToken cancellationToken);

    Task<UpdateReleaseResponse?> CreateReleaseAsync(CreateUpdateReleaseRequest request, CancellationToken cancellationToken);

    Task<UpdateCheckResponse?> CheckForUpdateAsync(string? currentVersion, string? channel, string? packageType, Guid? terminalId, CancellationToken cancellationToken);
}
