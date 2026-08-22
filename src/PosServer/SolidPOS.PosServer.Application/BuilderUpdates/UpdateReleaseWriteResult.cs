using SolidPOS.PosServer.Contracts.BuilderUpdates;

namespace SolidPOS.PosServer.Application.BuilderUpdates;

public sealed record UpdateReleaseWriteResult(
    UpdateReleaseResponse Release,
    bool WasCreated,
    int InsertedTargetCount);
